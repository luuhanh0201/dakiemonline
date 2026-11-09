using System;
using System.IO;
using UnityEngine;
using Game.Rules;
using Object = UnityEngine.Object;

namespace DaKiem.Client.Editor
{
    // Samples the actual package particles, including the first and last frames of each cut.
    public static class SlashComboReview
    {
        private static readonly int[] Frames = { 0, 1, 2, 3, 5, 7, 10, 14, 18, 24, 30, 39 };

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-slashReviewOut");
            string output = at < 0 ? "Logs/slash-combo" : args[at + 1];
            foreach (var obj in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
                if (obj != null && obj.transform.parent == null) Object.DestroyImmediate(obj);
            const int width = 260, height = 240;
            var camera = new GameObject("Slash review camera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.6f;
            camera.aspect = width / (float)height;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.065f, 0.12f);
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            var sheet = new Texture2D(width * Frames.Length, height * 6, TextureFormat.RGB24, false);
            var character = new CharacterView();
            var fx = new PackageSlashFx();
            if (!fx.Available) throw new Exception("Slash package missing");
            int row = 0;
            foreach (int facing in new[] { 1, -1 })
            for (int hit = 0; hit < 3; hit++, row++)
            {
                foreach (var part in Object.FindObjectsByType<ParticleSystem>())
                    part.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                for (int rest = 0; rest < 90; rest++)
                    character.Show(Vector2.zero, facing, true, 0, false, CastPhase.None, false, 1f / 60f);
                for (int windup = 0; windup < 9; windup++)
                    character.Show(Vector2.zero, facing, true, 0, false, CastPhase.Windup, false,
                        1f / 60f, new SwingMoment(hit, 0, 1, 0));
                fx.PlayComboSlash(new Vector2(facing * 0.525f, 0.6f), facing, hit);
                var roots = Array.FindAll(Object.FindObjectsByType<ParticleSystem>(), p => p.transform.parent == null);
                foreach (var root in roots)
                {
                    root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    foreach (var part in root.GetComponentsInChildren<ParticleSystem>())
                    {
                        part.useAutoRandomSeed = false;
                        part.randomSeed = 37;
                    }
                    root.Simulate(0, true, true, false);
                }
                int column = 0;
                for (int frame = 0; frame <= 360; frame++)
                {
                    if (frame > 0)
                        foreach (var root in roots) root.Simulate(1f / 60f, true, false, false);
                    character.Show(Vector2.zero, facing, true, 0, false,
                        frame < 6 ? CastPhase.Active : CastPhase.Recovery, false, 1f / 60f,
                        new SwingMoment(hit, 0, 1, 0));
                    if (column >= Frames.Length || frame != Frames[column]) continue;
                    camera.transform.position = new Vector3(facing * 0.65f, 0.85f, -10);
                    camera.Render(); camera.Render();
                    RenderTexture.active = target;
                    sheet.ReadPixels(new Rect(0, 0, width, height), column++ * width, (5 - row) * height);
                }
                foreach (var root in roots)
                    foreach (var part in root.GetComponentsInChildren<ParticleSystem>())
                        if (part.particleCount != 0) throw new Exception("Slash particles did not expire: " + part.name);
            }
            sheet.Apply();
            File.WriteAllBytes(output + ".png", sheet.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Debug.Log("Slash combo review passed: " + output);
        }
    }
}
