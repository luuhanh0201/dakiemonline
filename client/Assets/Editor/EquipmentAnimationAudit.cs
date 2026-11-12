using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Rules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DaKiem.Client.Editor
{
    // Exhaustive ordered pairs of the supported visual states, not a gameplay simulation.
    public static class EquipmentAnimationAudit
    {
        private static readonly string[] States = { "idle", "walk", "run", "rise", "fall", "dash", "hurt",
            "wind1", "strike1", "recover1", "wind2", "strike2", "recover2", "wind3", "strike3", "recover3" };
        private static readonly Dictionary<Texture, Texture2D> Readable = new Dictionary<Texture, Texture2D>();
        private static readonly Dictionary<Texture, Color32[]> Masks = new Dictionary<Texture, Color32[]>();
        private static readonly StringBuilder overlaps = new StringBuilder("combo,facing,source,target,frame,part,pixels\n");
        private static readonly StringBuilder errors = new StringBuilder();
        private static int frames, pairs, overlapSamples, pictures, faults;
        private static Camera camera;
        private static RenderTexture cell;
        private const string Folder = "Logs/equipment-audit";

        public static void Replay()
        {
            camera = new GameObject("ReplayCamera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 0.95f;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.2f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            cell = new RenderTexture(300, 300, 24); camera.targetTexture = cell;
            int[] sources = { 10, 10, 11, 7, 6, 12, 13, 13 };
            int[] targets = { 2, 0, 2, 2, 15, 7, 9, 15 };
            var sheet = new Texture2D(1800, sources.Length * 300, TextureFormat.RGB24, false);
            for (int row = 0; row < sources.Length; row++)
            {
                var view = new CharacterView();
                float x = 0f;
                for (int f = 0; f < 24; f++) Step(view, sources[row], f, 60, 1, ref x);
                for (int f = 0; f < 9; f++)
                {
                    Step(view, targets[row], f, 60, 1, ref x);
                    if (f > 4 && f != 8) continue;
                    Render(x);
                    sheet.ReadPixels(new Rect(0, 0, 300, 300), (f == 8 ? 5 : f) * 300, (sources.Length - 1 - row) * 300);
                }
                foreach (var obj in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (obj != null && obj.transform.parent == null && obj != camera.gameObject) Object.DestroyImmediate(obj);
            }
            RenderTexture.active = null; sheet.Apply();
            File.WriteAllBytes(Folder + "/confirmed-transitions.png", sheet.EncodeToPNG());
        }

        /// <summary>
        /// The two tier 1 sets of config/items.json, dressed the way the game does it (an Appearance through
        /// CharacterView.ShowAppearance), in several poses facing both ways: Logs/equipment-audit/sets.png.
        /// </summary>
        public static void Sets()
        {
            Directory.CreateDirectory(Folder);
            string configDir = Path.Combine(Application.dataPath, "../../config");
            var files = new Dictionary<string, string>();
            foreach (string name in GameConfig.FileNames) files[name] = File.ReadAllText(Path.Combine(configDir, name));
            GameConfig config = GameConfig.Parse(files);
            int Item(string id) { for (int i = 0; i < config.Gear.Count; i++) if (config.Gear[i].Id == id) return i; throw new ArgumentException(id); }
            Appearance[] sets =
            {
                GearRules.Default(config),
                new Appearance(Item("gear.nodachi"), Item("gear.giap"), Item("gear.hakama")),
            };
            int[] poses = { 0, 2, 3, 5, 8, 11 };
            camera = new GameObject("SetsCamera").AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 0.95f;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.2f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            cell = new RenderTexture(240, 240, 24); camera.targetTexture = cell;
            int columns = poses.Length * 2;
            var sheet = new Texture2D(240 * columns, 240 * sets.Length, TextureFormat.RGB24, false);
            var names = new StringBuilder();
            // One character for every cell, changing clothes between sets as a player does. A second
            // CharacterView made after the first was destroyed draws its head wrongly in the editor
            // (also seen in the 10/11 sequence sheets); that is checked separately.
            var view = new CharacterView();
            float x = 0f;
            for (int row = 0; row < sets.Length; row++)
            {
                foreach (GearSlot slot in GearRules.ShownSlots) names.Append(config.Gear[sets[row][slot]].Art).Append(slot == GearSlot.Pants ? "\n" : ", ");
                view.ShowAppearance(config, sets[row]);
                int column = 0;
                foreach (int facing in new[] { 1, -1 })
                foreach (int pose in poses)
                {
                    for (int f = 0; f < 14; f++) Step(view, pose, f, 60, facing, ref x);
                    Render(x);
                    sheet.ReadPixels(new Rect(0, 0, 240, 240), column * 240, (sets.Length - 1 - row) * 240);
                    column++;
                }
            }
            RenderTexture.active = null; sheet.Apply();
            File.WriteAllBytes($"{Folder}/sets.png", sheet.EncodeToPNG());
            File.WriteAllText($"{Folder}/sets.txt", names.ToString());
            Debug.Log("Equipment sets sheet written");
        }

        public static void Run()
        {
            bool captureOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-auditCaptureOnly") >= 0;
            bool quick = Array.IndexOf(Environment.GetCommandLineArgs(), "-auditQuick") >= 0;
            Directory.CreateDirectory(Folder);
            var catalog = new EquipmentCatalog();
            // Only these slots have drawings on the character; the hat and the cape are icons only.
            var slots = new[] { GearSlot.Weapon, GearSlot.Armor, GearSlot.Pants };
            int combinations = 1;
            foreach (var slot in slots) combinations *= catalog.ItemsIn(slot).Count;
            // Four unique weapon/hat silhouettes; armour and cape do not alter these masks.
            if (quick) combinations = 4;
            camera = new GameObject("AuditCamera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 0.95f;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.2f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            cell = new RenderTexture(240, 240, 24);
            camera.targetTexture = cell;
            var manifest = new StringBuilder();
            for (int combo = 0; combo < combinations; combo++)
            foreach (int facing in new[] { 1, -1 })
            {
                var view = new CharacterView();
                Transform root = GameObject.Find("Character").transform;
                int code = combo;
                foreach (var slot in slots)
                {
                    int index = code % catalog.ItemsIn(slot).Count;
                    view.Wear(slot, index);
                    manifest.Append($"{combo},{facing},{slot},{catalog.ItemsIn(slot)[index]}\n");
                    code /= catalog.ItemsIn(slot).Count;
                }
                Transform[] joints = root.GetComponentsInChildren<Transform>(true);
                MeshFilter[] meshes = root.GetComponentsInChildren<MeshFilter>(true);
                var weapon = root.Find("Rig/hip/torso/hand_near/gear_grip/gear_weapon").GetComponent<SpriteRenderer>();
                var head = root.Find("Rig/hip/torso/head/head").GetComponent<SpriteRenderer>();
                // The hat slot no longer draws, so it may be missing from the rig.
                var hat = root.Find("Rig/hip/torso/head/gear_hat")?.GetComponent<SpriteRenderer>();
                float x = 0f;
                // All 256 ordered pairs, including self-transitions, at 60 Hz.
                for (int from = 0; from < (captureOnly ? 0 : States.Length); from++)
                for (int to = 0; to < States.Length; to++)
                {
                    pairs++;
                    for (int f = 0; f < 24; f++) Step(view, from, f, 60, facing, ref x);
                    for (int f = 0; f < 18; f++)
                    {
                        Step(view, to, f, 60, facing, ref x);
                        Check(joints, meshes, f % 6 == 0, $"{combo}/{facing}/{States[from]}->{States[to]}/{f}");
                        if (f == 2 || f == 8 || f == 17)
                        {
                            CheckOverlap(weapon, head, combo, facing, from, to, f, x);
                            CheckOverlap(weapon, hat, combo, facing, from, to, f, x);
                        }
                    }
                }
                // Continuous sequences at each frame rate, including reversals, gear changes and impacts.
                foreach (int fps in quick ? Array.Empty<int>() : captureOnly ? new[] { 60 } : new[] { 30, 60, 120 })
                {
                    var sheet = fps == 60 ? new Texture2D(240 * 8, 240 * 6, TextureFormat.RGB24, false) : null;
                    int sample = 0;
                    for (int state = 0; state < States.Length; state++)
                    for (int f = 0; f < fps / 2; f++)
                    {
                        int direction = state == 2 ? -facing : facing;
                        if (f == fps / 4 && state >= 7 && (state - 7) % 3 == 1) view.ShowImpact(state >= 13);
                        if (f == 0) view.WearNext(GearSlot.Weapon);
                        Step(view, state, f, fps, direction, ref x);
                        Check(joints, meshes, f % 6 == 0, $"sequence/{combo}/{facing}/{fps}/{state}/{f}");
                        if (sheet != null && (f == 0 || f == fps / 4 || f == fps / 2 - 1))
                        {
                            Render(x);
                            sheet.ReadPixels(new Rect(0, 0, 240, 240), sample % 8 * 240, (5 - sample / 8) * 240);
                            sample++;
                        }
                    }
                    // A multi-hit cast uses another selection path from the basic combo.
                    for (int f = 0; f < fps * 2; f++)
                    {
                        float time = f / (float)fps;
                        int hit = Mathf.Clamp((int)((time - 0.2f) / 0.4f), 0, 2);
                        var phase = time < 0.2f ? CastPhase.Windup : time < 1.4f ? CastPhase.Active : CastPhase.Recovery;
                        view.Show(new Vector2(x, 0), facing, true, 0, false, phase, false, 1f / fps,
                            new SwingMoment(0, hit, 3, Mathf.Repeat((time - 0.2f) / 0.4f, 1f)));
                        frames++;
                        Check(joints, meshes, f % 6 == 0, $"ultimate/{combo}/{facing}/{fps}/{f}");
                    }
                    if (sheet != null)
                    {
                        sheet.Apply();
                        File.WriteAllBytes($"{Folder}/sequence-{combo:D2}-{facing}.png", sheet.EncodeToPNG());
                        Object.DestroyImmediate(sheet);
                    }
                }
                // Dispose scene instances so other combinations cannot appear in the capture.
                Object.DestroyImmediate(root.gameObject);
                foreach (var obj in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (obj != null && obj.transform.parent == null && obj != camera.gameObject) Object.DestroyImmediate(obj);
                Debug.Log($"Equipment audit completed combination {combo}, facing {facing}");
            }
            RenderTexture.active = null;
            if (captureOnly) return;
            File.WriteAllText($"{Folder}/combinations.csv", manifest.ToString());
            File.WriteAllText($"{Folder}/overlap-candidates.csv", overlaps.ToString());
            File.WriteAllText($"{Folder}/errors.txt", errors.ToString());
            string summary = $"Combinations={combinations}; pairs={pairs}; frames={frames}; nonfinite/missing={faults}; overlapCandidates={overlapSamples}; screenshots={pictures}. Overlaps are visual candidates, not confirmed failures. Pair tests at 60 Hz; "
                + (quick ? "quick mode skips continuous sequences.\n" : "sequences at 30/60/120 Hz.\n");
            File.WriteAllText($"{Folder}/summary.txt", summary);
            Debug.Log(summary);
        }

        private static void Step(CharacterView view, int state, int frame, int fps, int facing, ref float x)
        {
            float dt = 1f / fps;
            bool air = state == 3 || state == 4;
            x += facing * (state == 1 ? 2f : state == 2 ? 4f : state == 5 ? 15f : 0f) * dt;
            float y = state == 3 ? 0.2f + frame * dt * 2f : state == 4 ? Mathf.Max(0.05f, 1f - frame * dt * 2f) : 0f;
            var phase = state < 7 ? CastPhase.None : (CastPhase)(1 + (state - 7) % 3);
            view.Show(new Vector2(x, y), facing, !air, state == 3 ? 5f : state == 4 ? -5f : 0f,
                state == 5, phase, state == 6, dt, new SwingMoment(Mathf.Max(0, (state - 7) / 3), 0, 1, 0f));
            frames++;
        }

        private static void Check(Transform[] joints, MeshFilter[] meshes, bool inspectMeshes, string context)
        {
            foreach (var joint in joints)
                if (!Finite(joint.position)) Fault(context + ": nonfinite transform " + joint.name);
            if (!inspectMeshes) return;
            foreach (var filter in meshes)
            {
                if (filter.sharedMesh == null) { Fault(context + ": missing mesh " + filter.name); continue; }
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                    if (!Finite(vertex)) { Fault(context + ": nonfinite mesh " + filter.name); break; }
            }
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);
        private static void Fault(string text) { faults++; if (faults < 100) errors.AppendLine(text); }

        private static Texture2D Pixels(Texture texture)
        {
            if (Readable.TryGetValue(texture, out var pixels)) return pixels;
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0);
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            pixels.Apply();
            RenderTexture.ReleaseTemporary(target);
            Readable[texture] = pixels;
            Masks[texture] = pixels.GetPixels32();
            return pixels;
        }

        private static void CheckOverlap(SpriteRenderer weapon, SpriteRenderer part, int combo, int facing, int from, int to, int frame, float x)
        {
            if (part == null || !weapon.gameObject.activeInHierarchy || part.sprite == null) return;
            Sprite blade = weapon.sprite;
            Texture2D a = Pixels(blade.texture), b = Pixels(part.sprite.texture);
            Color32[] bladeMask = Masks[blade.texture], partMask = Masks[part.sprite.texture];
            int aw = a.width, ah = a.height, bw = b.width, bh = b.height;
            Vector2 bladePivot = blade.pivot, partPivot = part.sprite.pivot;
            float bladePPU = blade.pixelsPerUnit, partPPU = part.sprite.pixelsPerUnit;
            Matrix4x4 bladeToPart = part.transform.worldToLocalMatrix * weapon.transform.localToWorldMatrix;
            int count = 0;
            // Sample actual opaque blade texels; excludes the lower handle quarter.
            for (int py = ah / 4; py < ah; py += 3)
            for (int px = 0; px < aw; px += 3)
            {
                if (bladeMask[py * aw + px].a < 179) continue;
                Vector3 local = bladeToPart.MultiplyPoint3x4(new Vector3((px - bladePivot.x) / bladePPU, (py - bladePivot.y) / bladePPU));
                int bx = Mathf.RoundToInt(local.x * partPPU + partPivot.x);
                int by = Mathf.RoundToInt(local.y * partPPU + partPivot.y);
                if (bx >= 0 && by >= 0 && bx < bw && by < bh && partMask[by * bw + bx].a >= 179) count++;
            }
            if (count < 2) return;
            overlapSamples++;
            overlaps.AppendLine($"{combo},{facing},{States[from]},{States[to]},{frame},{part.name},{count}");
            if (pictures >= 48 || combo != 0 || facing != 1) return;
            Render(x);
            var shot = new Texture2D(240, 240, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, 240, 240), 0, 0); shot.Apply();
            File.WriteAllBytes($"{Folder}/overlap-{pictures:D2}-{States[from]}-{States[to]}-{frame}.png", shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            pictures++;
        }
        private static void Render(float x)
        {
            float y = GameObject.Find("Character").transform.position.y;
            camera.transform.position = new Vector3(x, y + 0.7f, -10f);
            camera.Render(); camera.Render();
            RenderTexture.active = cell;
        }
    }
}
