using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Net;
using Game.Rules;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client.Editor
{
    /// <summary>
    /// Renders the main menu and the screens behind it with sample data, without a server, so their layout
    /// can be looked at: tools/review-menu.ps1 writes one picture per screen.
    /// </summary>
    public static class MenuScreenReview
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int Width = 1672, Height = 941;

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = args[Array.IndexOf(args, "-menuReviewOut") + 1];
            Directory.CreateDirectory(output);
            GameConfig config = LoadConfig();
            CheckFigureAssets(config, output);

            foreach ((string page, int tab) in new[] { ("Menu", 0), ("Character", 0), ("Character", 1), ("Quests", 0), ("Settings", 0) })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameMenu menu = GameMenu.Create(config);
                typeof(GameMenu).GetField("state", Private).SetValue(menu, Sample(config));
                Type pages = typeof(GameMenu).GetNestedType("Page", BindingFlags.NonPublic);
                typeof(GameMenu).GetMethod("Build", Private).Invoke(menu, new[] { Enum.Parse(pages, page), tab });
                Capture(output, $"{page.ToLowerInvariant()}-{tab}", menu);
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var bag = InventoryScreen.Create(config, (kind, target, to) => { }, () => GearRules.Default(config));
            InventoryMessage sample = Sample(config).Inventory;
            bag.Toggle(sample);
            typeof(InventoryScreen).GetMethod("Select", Private).Invoke(bag, new object[] { sample.Items.First(item => item.Place == ItemPlace.Bag).Id });
            Capture(output, "bag-0", bag);
        }

        private static void CheckFigureAssets(GameConfig config, string output)
        {
            var preview = new CharacterPreview();
            var report = new System.Text.StringBuilder();
            var slots = new[] { GearSlot.Weapon, GearSlot.Armor, GearSlot.Pants };
            var choices = slots.Select(slot => Enumerable.Range(0, config.Gear.Count).Where(i => config.Gear[i].Slot == slot).ToArray()).ToArray();
            foreach (int weapon in choices[0])
            foreach (int armor in choices[1])
            foreach (int pants in choices[2])
            {
                var appearance = new Appearance(weapon, armor, pants);
                string art = $"Figure/front_{config.Gear[weapon].Art}_{config.Gear[armor].Art}_{config.Gear[pants].Art}";
                Sprite expected = MenuKit.Art(art);
                if (expected == null || expected.rect.width != 330 || expected.rect.height != 412)
                    throw new InvalidOperationException("Missing or misaligned figure crop: " + art);
                preview.Show(config, appearance);
                if (preview.Sprite != expected) throw new InvalidOperationException("Incorrect figure: " + art);
                report.AppendLine("PASS " + art + " (330 x 412)");
            }
            int defaultWeapon = GearRules.Default(config).Weapon;
            string originalArt = config.Gear[defaultWeapon].Art;
            try
            {
                config.Gear[defaultWeapon].Art = "review_missing_art";
                preview.Show(config, GearRules.Default(config));
                if (preview.Sprite != MenuKit.Art("Figure/front_kiem_aovai_vai"))
                    throw new InvalidOperationException("Missing figure must use default sprite");
                report.AppendLine("PASS Unknown art uses default figure and logs warning");
            }
            finally { config.Gear[defaultWeapon].Art = originalArt; }
            foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
                if (MenuKit.SlotShadow(slot) == null) throw new InvalidOperationException("Missing empty slot art: " + slot);
            report.AppendLine("PASS All five empty slot silhouettes exist");
            File.WriteAllText(Path.Combine(output, "assets.txt"), report.ToString());
        }

        private static GameMenu.MenuState Sample(GameConfig config)
        {
            long id = 0;
            var items = InventoryRules.Starter(config).Select(item => new OwnedItem(++id, item.Item, item.Quantity, item.Place, item.Slot)).ToList();
            return new GameMenu.MenuState
            {
                Name = "KiếmKhách",
                Appearance = GearRules.Default(config),
                Inventory = new InventoryMessage(config.Bag.Slots, items),
                Guest = true,
                OpenBag = () => { },
                Link = () => { },
                Leave = () => { },
            };
        }

        private static void Capture(string output, string name, Component screen)
        {
            var camera = new GameObject("ReviewCamera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Height / 2f;
            camera.transform.position = new Vector3(0f, 0f, -1000f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(0x2F, 0x5A, 0x3E, 0xFF);
            var target = new RenderTexture(Width, Height, 24);
            camera.targetTexture = target;

            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 100f;
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                canvas.scaleFactor = 1f;
            }

            Canvas.ForceUpdateCanvases();

            foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                MethodInfo late = behaviour.GetType().GetMethod("LateUpdate", Private | BindingFlags.Public);

                if (behaviour.GetType().Name == "OneLine" || behaviour.GetType().Name == "AccountPresentation")
                {
                    late?.Invoke(behaviour, null);
                }
            }

            Canvas.ForceUpdateCanvases();
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            shot.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(output, name + ".png"), shot.EncodeToPNG());
        }

        private static GameConfig LoadConfig()
        {
            string folder = Path.Combine(Application.dataPath, "../../config");
            return GameConfig.Parse(GameConfig.FileNames.ToDictionary(file => file, file => File.ReadAllText(Path.Combine(folder, file))));
        }
    }
}
