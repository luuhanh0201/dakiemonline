using System;
using System.IO;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    /// <summary>
    /// Renders the character's poses into one image, so motion can be reviewed without a
    /// phone. Run in batch mode with -executeMethod and -poseSheetOut followed by a file path.
    /// </summary>
    public static class CharacterPoseSheet
    {
        private const string OutputArgument = "-poseSheetOut";
        private const int Columns = 4;
        private const int Rows = 4;
        private const int CellWidth = 300;
        private const int CellHeight = 340;
        private const float ViewHeight = 1.35f;
        private const float BackgroundShade = 0.12f;

        // Four moments of the run, then the other poses.
        private static readonly (CharacterPose Pose, float Cycle)[] Frames =
        {
            (CharacterPose.Run, 0f),
            (CharacterPose.Run, 0.2f),
            (CharacterPose.Run, 0.4f),
            (CharacterPose.Run, 0.7f),
            (CharacterPose.Idle, 0f),
            (CharacterPose.Rise, 0f),
            (CharacterPose.Fall, 0f),
            (CharacterPose.Dash, 0f),
            (CharacterPose.Walk, 0f),
            (CharacterPose.Walk, 0.25f),
            (CharacterPose.Walk, 0.5f),
            (CharacterPose.Walk, 0.75f),
        };

        // Moments of turning from the front to the side.
        private static readonly float[] TurnMoments = { 0.8f, 0.6f, 0.45f, 0.25f };

        public static void Run()
        {
            string output = ReadOutputPath();
            var view = new CharacterView();
            var camera = new GameObject("PoseCamera").AddComponent<Camera>();
            var cell = new RenderTexture(CellWidth, CellHeight, 24);
            var sheet = new Texture2D(CellWidth * Columns, CellHeight * Rows, TextureFormat.RGB24, false);

            camera.orthographic = true;
            camera.orthographicSize = ViewHeight / 2f;
            camera.transform.position = new Vector3(0f, ViewHeight / 2f - 0.1f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(BackgroundShade, BackgroundShade, BackgroundShade * 2f);
            camera.targetTexture = cell;

            // The first render after loading can show stale sprites.
            view.Preview(CharacterPose.Idle, 0f);
            camera.Render();

            for (int frame = 0; frame < Frames.Length; frame++)
            {
                view.Preview(Frames[frame].Pose, Frames[frame].Cycle);
                camera.Render();

                RenderTexture.active = cell;
                int column = frame % Columns;
                int row = Rows - 1 - frame / Columns;
                sheet.ReadPixels(new Rect(0, 0, CellWidth, CellHeight), column * CellWidth, row * CellHeight);
            }

            for (int moment = 0; moment < TurnMoments.Length; moment++)
            {
                view.PreviewTurn(TurnMoments[moment]);
                camera.Render();

                RenderTexture.active = cell;
                sheet.ReadPixels(new Rect(0, 0, CellWidth, CellHeight), moment * CellWidth, 0);
            }

            RenderTexture.active = null;
            sheet.Apply();
            File.WriteAllBytes(output, sheet.EncodeToPNG());
            Debug.Log($"Pose sheet written to {output}");
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
