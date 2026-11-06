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
