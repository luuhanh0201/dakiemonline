using System;
using System.Collections.Generic;
using System.IO;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    /// <summary>
    /// Renders the map, character and boss as the game camera sees them, so the sample scene can
    /// be reviewed without playing. Run in batch mode with -executeMethod and -sceneShotOut
    /// followed by a file path.
    /// </summary>
    public static class SampleSceneReview
    {
        private const string OutputArgument = "-sceneShotOut";
        private const int ShotWidth = 1920;
        private const int ShotHeight = 1080;
        private const float FrameSeconds = 1f / 60f;
        private const double PlayerStartX = 5d;

        private const float PhoneHeightDp = 393f;
        private const int SheetCellWidth = 640;
        private const int SheetCellHeight = 360;
        private const int SheetColumns = 3;
        private const int SheetRows = 6;
        private const int TicksPerSkill = 36;
        private const int TicksPerCell = 12;

        private static readonly SkillSlot[] SheetSkills =
        {
            SkillSlot.Ranged,
            SkillSlot.OptionB,
            SkillSlot.Ultimate,
            SkillSlot.OptionA,
            SkillSlot.Support,
            SkillSlot.Attack,
        };

        // Camera centres that between them show the whole map.
        private static readonly float[] CameraStops = { 6.2f, 15f, 24f };

        public static void Run()
        {
            string output = ReadOutputPath();
            var cameraHost = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraHost.AddComponent<Camera>();
            var target = new RenderTexture(ShotWidth, ShotHeight, 24);

            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.aspect = ShotWidth / (float)ShotHeight;
            camera.targetTexture = target;

            GameConfig config = ReadConfig();
            var world = new WorldView(config.MapBoxes);
            var character = new CharacterView();
            var boss = new BossView();
            var hud = new HudView();
            var fx = new FxView(world);
            var fight = new Simulation(config, PlayerStartX);
            var shot = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);

            Vector2 feet = new Vector2((float)fight.Player.X, (float)fight.Player.Y);
            character.Show(feet, 1, true, 0f, false, CastPhase.None, false, FrameSeconds);
            boss.Show((float)PlayerStartX + 3.4f, fight);

            for (int view = 0; view < CameraStops.Length; view++)
            {
                world.Jump(CameraStops[view]);
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                shot.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);
                RenderTexture.active = null;
                shot.Apply();
                File.WriteAllBytes(Path.ChangeExtension(output, null) + "-" + view + ".png", shot.EncodeToPNG());
            }

            hud.DrawThrough(camera, ShotHeight / PhoneHeightDp);
            world.Jump(CameraStops[0]);
            WriteSkillSheet(output, config, world, character, boss, hud, fx, camera, target);
        }

        /// <summary>Plays each skill through the real rules and saves moments of its effect.</summary>
        private static void WriteSkillSheet(string output, GameConfig config, WorldView world, CharacterView character, BossView boss, HudView hud, FxView fx, Camera camera, RenderTexture target)
        {
            var sheet = new Texture2D(SheetCellWidth * SheetColumns, SheetCellHeight * SheetRows, TextureFormat.RGB24, false);
            var cell = new RenderTexture(SheetCellWidth, SheetCellHeight, 24);
            int place = 0;

            foreach (SkillSlot slot in SheetSkills)
            {
                var fight = new Simulation(config, PlayerStartX);
                ResourceRules.TakeDamage(fight.Resources, 200);
                for (int settle = 0; settle < 60; settle++)
                    character.Show(new Vector2((float)fight.Player.X, (float)fight.Player.Y), 1,
                        true, 0f, false, CastPhase.None, false, FrameSeconds);
                bool pressed = false;

                for (int tick = 0; tick < TicksPerSkill; tick++)
                {
                    fight.Step(default, new CombatIntent(pressed ? (SkillSlot?)null : slot));
                    pressed = true;
                    fx.Show(fight, config.Boss, (float)Ticks.DeltaSeconds);
                    var feet = new Vector2((float)fight.Player.X, (float)fight.Player.Y);
                    world.Jump(Mathf.Max(6f, feet.x + 1f));
                    character.Show(feet, fight.Player.Facing, fight.Player.Grounded, (float)fight.Player.VelocityY, fight.Player.IsDashing, fight.Combat.Phase, false, (float)Ticks.DeltaSeconds, SwingMoment.Of(fight.Combat));
                    boss.Show((float)fight.Boss.X, fight);
                    hud.ShowResources((float)fight.Resources.Hp / config.Character.MaxHp, (float)(fight.Resources.Energy / config.Character.MaxEnergy), (float)(fight.Player.Stamina / config.Character.MaxStamina));
                    hud.ShowBossHealth((float)fight.Boss.Hp / config.Boss.MaxHp);
                    hud.SetCooldown(HudButton.Ranged, 0.6f);
                    fight.ClearEvents();

                    if (tick % TicksPerCell == 7 && place < SheetColumns * SheetRows)
                    {
                        Canvas.ForceUpdateCanvases();
                        camera.Render();
                        Graphics.Blit(target, cell);
                        RenderTexture.active = cell;
                        sheet.ReadPixels(new Rect(0, 0, SheetCellWidth, SheetCellHeight), place % SheetColumns * SheetCellWidth, (SheetRows - 1 - place / SheetColumns) * SheetCellHeight);
                        RenderTexture.active = null;
                        place++;
                    }
                }
            }

            sheet.Apply();
            File.WriteAllBytes(Path.ChangeExtension(output, null) + "-skills.png", sheet.EncodeToPNG());
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

        private static string ReadOutputPath()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, OutputArgument);

            if (index < 0 || index + 1 >= arguments.Length)
            {
                throw new ArgumentException($"Missing {OutputArgument}");
            }

            return arguments[index + 1];
        }
    }
}
