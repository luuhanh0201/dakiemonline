using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    /// <summary>
    /// Plays the three attack hits frame by frame and writes every third frame into one image,
    /// one row per hit, so the real easing and the trail can be reviewed. The last two rows are
    /// the ultimate, played by the real rules, with a frame by frame table beside the image.
    /// Run in batch mode with -executeMethod and -comboSheetOut followed by a file path. Adding
    /// -comboFps and 30 or 120 draws the ultimate rows at that frame rate instead of 60.
    /// </summary>
    public static class ComboReview
    {
        private const string OutputArgument = "-comboSheetOut";
        private const string FrameRateArgument = "-comboFps";
        private const int FramesPerSecond = 60;
        private const int FramesPerCell = 3;
        private const int Cells = 12;
        private const int Hits = 3;
        private const int UltimateRows = 2;
        private const int Rows = Hits + UltimateRows;
        private const int CellWidth = 170;
        private const int CellHeight = 190;
        private const float ViewHeight = 1.8f;
        private const float BackgroundShade = 0.12f;

        // The basic attack's phases, in frames.
        private const int WindupFrames = 9;
        private const int ActiveFrames = 6;

        // The ultimate.
        private const int UltimateFramesPerCell = 4;
        private const double PlayerStartX = 5d;
        private const double BossDistance = 2d;
        private const float SecondsToShow = 1.8f;
        private const int RestFrames = 30;
        private static readonly int[] FrameRates = { 30, 60, 120 };

        public static void Run()
        {
            string output = ReadOutputPath();
            var view = new CharacterView();
            var camera = new GameObject("ComboCamera").AddComponent<Camera>();
            var cell = new RenderTexture(CellWidth, CellHeight, 24);
            var sheet = new Texture2D(CellWidth * Cells, CellHeight * Rows, TextureFormat.RGB24, false);

            camera.orthographic = true;
            camera.orthographicSize = ViewHeight / 2f;
            camera.aspect = CellWidth / (float)CellHeight;
            camera.transform.position = new Vector3(0.2f, ViewHeight / 2f - 0.15f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(BackgroundShade, BackgroundShade, BackgroundShade * 2f);
            camera.targetTexture = cell;

            for (int hit = 0; hit < Hits; hit++)
            {
                bool sawTrail = false;
                var trail = GameObject.Find("Water blade trail").GetComponent<MeshRenderer>();
                for (int frame = 0; frame < Cells * FramesPerCell; frame++)
                {
                    CastPhase phase = frame < WindupFrames ? CastPhase.Windup
                        : frame < WindupFrames + ActiveFrames ? CastPhase.Active
                        : CastPhase.Recovery;

                    if (frame == WindupFrames)
                    {
                        view.ShowImpact(hit == Hits - 1);
                    }

                    view.Show(Vector2.zero, 1, true, 0f, false, phase, false, 1f / FramesPerSecond, new SwingMoment(hit, 0, 1, 0f));
                    sawTrail |= trail.enabled;

                    if (frame % FramesPerCell == 0)
                    {
                        camera.Render();
                        camera.Render();
                        RenderTexture.active = cell;
                        sheet.ReadPixels(new Rect(0, 0, CellWidth, CellHeight), frame / FramesPerCell * CellWidth, (Rows - 1 - hit) * CellHeight);
                    }
                }
                if (!sawTrail) throw new InvalidOperationException("Missing blade trail for combo hit " + hit);
            }

            PlayUltimate(view, camera, cell, sheet, output);

            RenderTexture.active = null;
            sheet.Apply();
            File.WriteAllBytes(output, sheet.EncodeToPNG());
            Debug.Log($"Combo sheet written to {output}");
        }

        /// <summary>The bottom rows and the table: the ultimate against the boss, as the game runs it.</summary>
        private static void PlayUltimate(CharacterView view, Camera camera, RenderTexture cell, Texture2D sheet, string output)
        {
            GameConfig config = ReadConfig();
            Transform weapon = GameObject.Find("Character").transform.Find("Rig/hip/torso/hand_near/gear_grip/gear_weapon");
            var table = new StringBuilder("fps,frame,seconds,phase,hit,progress,damage,finisher,bladeAngle,bladeTurn\n");

            int drawnRate = ReadDrawnFrameRate();

            foreach (int fps in FrameRates)
            {
                PlayUltimateAt(fps, fps == drawnRate, config, view, weapon, camera, cell, sheet, table);
            }

            File.WriteAllText(Path.ChangeExtension(output, ".csv"), table.ToString());
        }

        private static void PlayUltimateAt(
            int fps,
            bool draw,
            GameConfig config,
            CharacterView view,
            Transform weapon,
            Camera camera,
            RenderTexture cell,
            Texture2D sheet,
            StringBuilder table)
        {
            var fight = new Simulation(config, PlayerStartX);
            var walk = new MovementIntent(1d, 0d, false);
            float frameSeconds = 1f / fps;
            float accumulator = 0f;
            bool pressed = false;
            float angle = 0f;
            int framesPerCell = UltimateFramesPerCell * fps / FramesPerSecond;

            while (fight.Boss.X - fight.Player.X > BossDistance)
            {
                fight.Step(walk, default);
                fight.ClearEvents();
            }

            var feet = new Vector2((float)fight.Player.X, (float)fight.Player.Y);

            for (int frame = 0; frame < RestFrames; frame++)
            {
                view.Show(feet, 1, true, 0f, false, CastPhase.None, false, frameSeconds);
            }

            int frames = Mathf.RoundToInt(SecondsToShow * fps);

            for (int frame = 0; frame < frames; frame++)
            {
                accumulator += frameSeconds;

                while (accumulator >= (float)Ticks.DeltaSeconds)
                {
                    fight.Step(default, new CombatIntent(pressed ? (SkillSlot?)null : SkillSlot.Ultimate));
                    pressed = true;
                    accumulator -= (float)Ticks.DeltaSeconds;
                }

                int damage = 0;
                bool finisher = false;

                foreach (CombatEvent item in fight.CombatEvents)
                {
                    if (item.Kind == CombatEventKind.SkillHit)
                    {
                        damage += item.Amount;
                        finisher |= view.ReactToHit(item, config.Skill(item.Slot.Value), fight.Combat.ComboStep);
                    }
                }

                fight.ClearEvents();
                view.Show(feet, fight.Player.Facing, true, 0f, false, fight.Combat.Phase, false, frameSeconds, SwingMoment.Of(fight.Combat));

                float turn = frame == 0 ? 0f : Mathf.DeltaAngle(angle, weapon.eulerAngles.z);
                angle = weapon.eulerAngles.z;
                table.AppendLine(string.Join(",", fps, frame, F(frame * frameSeconds), fight.Combat.Phase, fight.Combat.ActiveHitIndex, F((float)fight.Combat.ActiveHitProgress), damage, finisher, F(angle), F(turn)));

                if (draw && frame % framesPerCell == 0 && frame / framesPerCell < Cells * UltimateRows)
                {
                    int place = frame / framesPerCell;
                    camera.transform.position = new Vector3(feet.x + 0.2f, feet.y + ViewHeight / 2f - 0.15f, -10f);
                    camera.Render();
                    camera.Render();
                    RenderTexture.active = cell;
                    sheet.ReadPixels(new Rect(0, 0, CellWidth, CellHeight), place % Cells * CellWidth, (UltimateRows - 1 - place / Cells) * CellHeight);
                }
            }
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static GameConfig ReadConfig()
        {
            var files = new Dictionary<string, string>();

            foreach (string fileName in GameConfig.FileNames)
            {
                files[fileName] = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "config", fileName));
            }

            return GameConfig.Parse(files);
        }

        private static int ReadDrawnFrameRate()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, FrameRateArgument);

            return index < 0 ? FramesPerSecond : int.Parse(arguments[index + 1], CultureInfo.InvariantCulture);
        }

        private static string ReadOutputPath()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, OutputArgument);

            if (index < 0 || index + 1 >= arguments.Length)
            {
                throw new ArgumentException($"{OutputArgument} needs a file path.");
            }

            return arguments[index + 1];
        }
    }
}
