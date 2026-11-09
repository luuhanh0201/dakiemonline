using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Rules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaKiem.Client.Editor
{
    // Independent fights prevent a previous cast/cooldown from swallowing the next review skill.
    public static class SkillFxReview
    {
        private static readonly SkillSlot[] Slots = { SkillSlot.Attack, SkillSlot.Ranged, SkillSlot.OptionA,
            SkillSlot.OptionB, SkillSlot.Support, SkillSlot.Ultimate };
        private static readonly float[] Times = { 0.12f, 0.25f, 0.36f, 0.52f, 0.70f, 0.93f, 1.18f };
        private const int Width = 400, Height = 280;

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-fxReviewOut");
            string output = at >= 0 ? args[at + 1] : "Logs/water-fx";
            bool quick = Array.IndexOf(args, "-fxReviewQuick") >= 0;
            var sheet = new Texture2D(Width * Times.Length, Height * Slots.Length, TextureFormat.RGB24, false);
            var report = new StringBuilder("fps,facing,skill,started,hits,healEvents,frames,peakEffects,pool\n");
            ClearScene();
            var camera = new GameObject("FX Review Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            var target = new RenderTexture(Width, Height, 24);
            camera.orthographic = true;
            camera.aspect = Width / (float)Height;
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            var world = new WorldView(ReadConfig().MapBoxes);
            foreach (var renderer in Object.FindObjectsByType<SpriteRenderer>()) renderer.enabled = false;
            camera.orthographicSize = 1.65f;
            camera.backgroundColor = new Color(0.035f, 0.065f, 0.12f);
            var character = new CharacterView();
            var boss = new BossView();
            var fx = new FxView(world);
            var warning = GameObject.Find("WarningZone").GetComponent<SpriteRenderer>();
            foreach (int fps in quick ? new[] { 60 } : new[] { 30, 60, 120 })
            foreach (int facing in quick ? new[] { 1 } : new[] { 1, -1 })
            for (int row = 0; row < Slots.Length; row++)
            {
                GameConfig config = ReadConfig();
                // Isolated visual fixture only: a stationary, nonlethal boss within reach.
                config.Boss.StartDistance = 1.8 * facing;
                config.Boss.MoveSpeed = 0;
                foreach (var attack in config.Boss.Cycle) { attack.WarningSec = 20; attack.Damage = 0; }
                var fight = new Simulation(config, 6);
                ResourceRules.TakeDamage(fight.Resources, 200);
                fight.Step(new MovementIntent(facing, 0, false), default);
                fight.ClearEvents();
                fx.Show(fight, config.Boss, 0f);
                if (fx.ActiveEffectCount != 0) throw new Exception("Effect carried into a new fight");
                for (int settle = 0; settle < 90; settle++)
                    character.Show(new Vector2((float)fight.Player.X, (float)fight.Player.Y), facing,
                        true, 0, false, CastPhase.None, false, 1f / 60f);
                int starts = 0, heals = 0, captures = 0, peakEffects = 0;
                bool sawBladeTrail = false;
                var bladeTrail = GameObject.Find("Water blade trail").GetComponent<MeshRenderer>();
                var hits = new HashSet<int>();
                float accumulator = 0f;
                bool pressed = false;
                int total = Mathf.CeilToInt(3f * fps);
                for (int frame = 0; frame < total; frame++)
                {
                    accumulator += 1f / fps;
                    while (accumulator + 0.000001f >= (float)Ticks.DeltaSeconds)
                    {
                        fight.Step(default, new CombatIntent(pressed ? (SkillSlot?)null : Slots[row]));
                        pressed = true;
                        accumulator -= (float)Ticks.DeltaSeconds;
                    }
                    foreach (var item in fight.CombatEvents)
                    {
                        if (item.Kind == CombatEventKind.SkillStarted && item.Slot == Slots[row]) starts++;
                        if (item.Kind == CombatEventKind.SkillHit) hits.Add(item.HitIndex);
                        if (item.Kind == CombatEventKind.SelfHealed) heals++;
                        if (item.Kind == CombatEventKind.SkillHit)
                            character.ReactToHit(item, config.Skill(item.Slot.Value), fight.Combat.ComboStep);
                    }
                    fx.Show(fight, config.Boss, 1f / fps);
                    peakEffects = Mathf.Max(peakEffects, fx.ActiveEffectCount);
                    var feet = new Vector2((float)fight.Player.X, (float)fight.Player.Y);
                    character.Show(feet, facing, fight.Player.Grounded, (float)fight.Player.VelocityY,
                        fight.Player.IsDashing, fight.Combat.Phase, false, 1f / fps, SwingMoment.Of(fight.Combat));
                    boss.Show((float)fight.Boss.X, fight);
                    sawBladeTrail |= bladeTrail.enabled;
                    fight.ClearEvents();
                    // Keep the enemy tell out of this close-up; the in-game review retains it.
                    warning.enabled = false;
                    camera.transform.position = new Vector3(feet.x + facing * 1.2f, 0.95f, -10f);
                    if (fps == 60 && facing == 1 && captures < Times.Length && frame >= Mathf.RoundToInt(Times[captures] * fps))
                    {
                        camera.Render(); camera.Render(); RenderTexture.active = target;
                        sheet.ReadPixels(new Rect(0, 0, Width, Height), captures * Width, (Slots.Length - 1 - row) * Height);
                        captures++;
                    }
                    if (frame % Mathf.Max(1, fps / 12) == 0)
                        foreach (var filter in Object.FindObjectsByType<MeshFilter>())
                            foreach (Vector3 v in filter.sharedMesh.vertices)
                                if (float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y))
                                    throw new Exception("Invalid FX mesh: " + filter.name);
                }
                if (starts != 1) throw new Exception("Skill not started exactly once: " + Slots[row]);
                if (Slots[row] == SkillSlot.Ultimate && hits.Count != 3) throw new Exception("Missing ultimate hit");
                if (Slots[row] == SkillSlot.Support && heals != 1) throw new Exception("Missing heal effect event");
                if (peakEffects == 0) throw new Exception("No skill effects shown: " + Slots[row]);
                if (Slots[row] == SkillSlot.Attack && !sawBladeTrail) throw new Exception("Short attack lost its blade trail");
                if (fx.ActiveEffectCount != 0) throw new Exception("Effects did not expire: " + Slots[row]);
                if (fx.PooledEffectCount > 32) throw new Exception("Unbounded effect pool");
                report.AppendLine($"{fps},{facing},{Slots[row]},{starts},{hits.Count},{heals},{total},{peakEffects},{fx.PooledEffectCount}");
            }
            sheet.Apply();
            File.WriteAllBytes(output + ".png", sheet.EncodeToPNG());
            File.WriteAllText(output + ".csv", report.ToString());
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Debug.Log("Skill FX review passed: " + output);
        }

        private static void ClearScene()
        {
            foreach (var obj in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (obj != null && obj.transform.parent == null) Object.DestroyImmediate(obj);
        }

        private static GameConfig ReadConfig()
        {
            var files = new Dictionary<string, string>();
            foreach (string name in GameConfig.FileNames)
                files[name] = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "config", name));
            return GameConfig.Parse(files);
        }
    }
}
