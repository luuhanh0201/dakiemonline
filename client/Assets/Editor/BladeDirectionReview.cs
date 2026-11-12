using System;
using System.IO;
using System.Reflection;
using System.Text;
using Game.Rules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaKiem.Client.Editor
{
    // Validate the rendered blade, including mirrored characters and the clearance solver.
    public static class BladeDirectionReview
    {
        public static void Run()
        {
            var channels = typeof(CharacterView).GetField("current", BindingFlags.NonPublic | BindingFlags.Instance);
            var weaponChannel = typeof(CharacterView).GetField("WeaponTurn", BindingFlags.NonPublic | BindingFlags.Static);
            int angleIndex = (int)weaponChannel.GetRawConstantValue();
            var catalog = new EquipmentCatalog();
            // Only these slots have drawings on the character; the hat and the cape are icons only.
            var slots = new[] { GearSlot.Weapon, GearSlot.Armor, GearSlot.Pants };
            int combinations = 1;
            foreach (var slot in slots) combinations *= catalog.ItemsIn(slot).Count;
            int samples = 0, cuttingSamples = 0;
            float worstDirectionError = 0f, worstReverseSweep = 0f;
            var report = new StringBuilder("combo,facing,fps,scenario,hit,frame,angleError,sweep,edgeDot\n");
            for (int combo = 0; combo < combinations; combo++)
            foreach (int facing in new[] { 1, -1 })
            foreach (int fps in new[] { 30, 60, 120 })
            {
                var view = new CharacterView();
                var root = GameObject.Find("Character").transform;
                var torso = root.Find("Rig/hip/torso");
                var grip = torso.Find("hand_near/gear_grip");
                var blade = grip.Find("gear_weapon");
                int code = combo;
                foreach (var slot in slots)
                {
                    view.Wear(slot, code % catalog.ItemsIn(slot).Count);
                    code /= catalog.ItemsIn(slot).Count;
                }
                // Three basic hits, then the ultimate with its configured 0.3/0.3/0.4 s hits.
                for (int scenario = 0; scenario < 4; scenario++)
                {
                    float previousAngle = 0f;
                    CastPhase previousPhase = CastPhase.None;
                    bool multi = scenario == 3;
                    for (int f = 0; f < Mathf.RoundToInt(fps * (multi ? 1.5f : 0.6f)); f++)
                    {
                        float time = f / (float)fps;
                        CastPhase phase = time < 0.15f ? CastPhase.Windup
                            : time < 0.25f ? CastPhase.Active : CastPhase.Recovery;
                        int hit = scenario, hitIndex = 0;
                        float progress = 0f;
                        if (multi)
                        {
                            phase = time < 0.2f ? CastPhase.Windup : time < 1.2f ? CastPhase.Active : CastPhase.Recovery;
                            hitIndex = time < 0.5f ? 0 : time < 0.8f ? 1 : 2;
                            float start = hitIndex == 0 ? 0.2f : hitIndex == 1 ? 0.5f : 0.8f;
                            progress = Mathf.Clamp01((time - start) / (hitIndex == 2 ? 0.4f : 0.3f));
                            hit = hitIndex;
                        }
                        CastPhase renderedPhase = phase;
                        if (multi && phase == CastPhase.Active && hitIndex < 2 && progress >= 0.6f)
                        {
                            renderedPhase = CastPhase.Windup;
                            hit++;
                        }
                        if (renderedPhase == CastPhase.Active && previousPhase != CastPhase.Active)
                            view.ShowImpact(hit == 2);
                        view.Show(Vector2.zero, facing, true, 0f, false, phase, false, 1f / fps,
                            new SwingMoment(multi ? 0 : scenario, hitIndex, multi ? 3 : 1, progress));
                        Vector3 axis = torso.InverseTransformVector(grip.TransformVector(Vector3.up)).normalized;
                        float actual = Mathf.Atan2(-axis.x, axis.y) * Mathf.Rad2Deg;
                        float expected = ((float[])channels.GetValue(view))[angleIndex];
                        float error = Mathf.Abs(Mathf.DeltaAngle(expected, actual));
                        worstDirectionError = Mathf.Max(worstDirectionError, error);
                        if (error > 24.02f) throw new Exception("Clearance overturned blade direction: " + error);
                        float sweep = Mathf.DeltaAngle(previousAngle, actual);
                        float edgeDot = 0f;
                        if (renderedPhase == CastPhase.Active && f > 0 && Mathf.Abs(sweep) > 0.5f)
                        {
                            float reverse = hit == 1 ? -sweep : sweep;
                            worstReverseSweep = Mathf.Max(worstReverseSweep, reverse);
                            if (reverse > 0.5f) throw new Exception($"Backwards swing: hit {hit}, {fps} Hz, {sweep}");
                            // Asset's cutting edge is +X; a wrist roll must put that edge
                            // on the leading side of the blade's actual angular motion.
                            Vector3 edge = torso.InverseTransformVector(blade.TransformVector(Vector3.right)).normalized;
                            Vector3 tangent = Vector3.Cross(Vector3.forward, axis) * Mathf.Sign(sweep);
                            edgeDot = Vector3.Dot(edge, tangent);
                            if (edgeDot < 0.9f) throw new Exception($"Spine leads cut: hit {hit}, {fps} Hz, dot {edgeDot}");
                            cuttingSamples++;
                        }
                        report.AppendLine($"{combo},{facing},{fps},{scenario},{hit},{f},{error:F5},{sweep:F3},{edgeDot:F3}");
                        previousAngle = actual;
                        previousPhase = renderedPhase;
                        samples++;
                    }
                }
                Object.DestroyImmediate(root.gameObject);
                foreach (var obj in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (obj != null && obj.transform.parent == null) Object.DestroyImmediate(obj);
            }
            if (cuttingSamples == 0) throw new Exception("No moving blade samples checked");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/blade-direction.csv", report.ToString());
            string result = $"Blade review passed: {samples} frames, {cuttingSamples} moving cut samples; "
                + $"max direction error {worstDirectionError:F5} degrees; reverse sweep {worstReverseSweep:F5} degrees. "
                + $"{combinations} gear combinations, both facings, 30/60/120 Hz, basic combo and ultimate with hit stops.";
            File.WriteAllText("Logs/blade-direction-summary.txt", result);
            Debug.Log(result);
        }
    }
}
