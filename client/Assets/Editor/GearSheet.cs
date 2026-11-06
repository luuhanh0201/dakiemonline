using System;
using System.IO;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    /// <summary>
    /// Renders every equipment combination in every action, facing both ways, into one image.
    /// Run in batch mode with -executeMethod and -gearSheetOut followed by a file path.
    /// </summary>
    public static class GearSheet
    {
        private const string OutputArgument = "-gearSheetOut";
        private const int CellWidth = 150;
        private const int CellHeight = 190;
        private const float ViewHeight = 1.7f;
        private const float BackgroundShade = 0.12f;

        private static readonly (CharacterPose Pose, float Cycle)[] Actions =
        {
            (CharacterPose.Idle, 0f),
            (CharacterPose.Walk, 0.1f),
            (CharacterPose.Walk, 0.4f),
            (CharacterPose.Run, 0f),
            (CharacterPose.Run, 0.2f),
            (CharacterPose.Run, 0.4f),
            (CharacterPose.Rise, 0f),
            (CharacterPose.Fall, 0f),
            (CharacterPose.Dash, 0f),
            (CharacterPose.Windup, 0f),
            (CharacterPose.Strike, 0f),
            (CharacterPose.Windup2, 0f),
            (CharacterPose.Strike2, 0f),
            (CharacterPose.Windup3, 0f),
            (CharacterPose.Strike3, 0f),
            (CharacterPose.Hurt, 0f),
        };

        private static readonly int[] Facings = { 1, -1 };

        public static void Run()
        {
            string output = ReadOutputPath();
            var view = new CharacterView();
            var catalog = new EquipmentCatalog();
            var slots = (GearSlot[])Enum.GetValues(typeof(GearSlot));

            int combinations = 1;
            foreach (GearSlot slot in slots)
            {
                combinations *= catalog.ItemsIn(slot).Count;
            }

            int columns = Actions.Length * Facings.Length;
            var camera = new GameObject("GearCamera").AddComponent<Camera>();
            var cell = new RenderTexture(CellWidth, CellHeight, 24);
            var sheet = new Texture2D(CellWidth * columns, CellHeight * combinations, TextureFormat.RGB24, false);

            camera.orthographic = true;
            camera.orthographicSize = ViewHeight / 2f;
            camera.transform.position = new Vector3(0f, ViewHeight / 2f - 0.15f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(BackgroundShade, BackgroundShade, BackgroundShade * 2f);
            camera.targetTexture = cell;

            // The first render after loading can show stale sprites.
            view.Preview(CharacterPose.Idle, 0f);
            camera.Render();

            for (int combination = 0; combination < combinations; combination++)
            {
                int remaining = combination;

                foreach (GearSlot slot in slots)
                {
                    int count = catalog.ItemsIn(slot).Count;
                    view.Wear(slot, remaining % count);
                    remaining /= count;
                }

                for (int column = 0; column < columns; column++)
                {
                    view.PreviewFacing(Facings[column / Actions.Length]);
                    view.Preview(Actions[column % Actions.Length].Pose, Actions[column % Actions.Length].Cycle);
                    camera.Render();

                    RenderTexture.active = cell;
                    int row = combinations - 1 - combination;
                    sheet.ReadPixels(new Rect(0, 0, CellWidth, CellHeight), column * CellWidth, row * CellHeight);
                }
            }

            RenderTexture.active = null;
            sheet.Apply();
            File.WriteAllBytes(output, sheet.EncodeToPNG());
            Debug.Log($"Gear sheet written to {output}: {combinations} combinations, {columns} views each.");
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
