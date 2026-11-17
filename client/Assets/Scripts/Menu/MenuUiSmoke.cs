#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DaKiem.Client.Accounts;
using DaKiem.Client.Net;
using Game.Net;
using Game.Rules;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

namespace DaKiem.Client
{
    /// <summary>Opt-in PC check against an isolated memory server; never uses the saved account.</summary>
    public sealed class MenuUiSmoke : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private string output;
        private bool failed;
        private int passed;
        private GameLoop loop;
        private GameMenu menu;
        private InventoryScreen bag;
        private GameConfig config;
        private Touchscreen touch;
        private OnlineClient online;

        private static T Read<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-menu-ui-smoke");
            if (at < 0) return;
            if (Array.IndexOf(args, "-join") < 0) throw new InvalidOperationException("Menu smoke requires -join to a disposable server.");
            var check = new GameObject("MenuUiSmoke").AddComponent<MenuUiSmoke>();
            check.output = args[at + 1];
        }

        private IEnumerator Start()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "checks.txt"), string.Empty);
            Application.logMessageReceived += OnLog;
            var work = new Stack<IEnumerator>();
            work.Push(Check());
            while (work.Count > 0 && !failed)
            {
                object next = null;
                try
                {
                    if (!work.Peek().MoveNext()) { work.Pop(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    failed = true;
                    File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + error + "\n");
                    break;
                }
                if (next is IEnumerator nested) work.Push(nested);
                else yield return next;
            }
            if (touch != null) InputSystem.RemoveDevice(touch);
            Application.logMessageReceived -= OnLog;
            File.AppendAllText(Path.Combine(output, "checks.txt"), $"RESULT {(failed ? "FAIL" : "PASS")}: {passed} checks\n");
            Application.Quit(failed ? 1 : 0);
        }

        private void OnLog(string message, string trace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception) return;
            failed = true;
            File.AppendAllText(Path.Combine(output, "checks.txt"), "FAIL " + message + "\n" + trace + "\n");
        }

        private void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed++;
            File.AppendAllText(Path.Combine(output, "checks.txt"), "PASS " + name + "\n");
        }

        private IEnumerator Until(Func<bool> ready, string name)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(ready(), name);
        }

        private IEnumerator Check()
        {
            yield return Until(() => (loop = FindFirstObjectByType<GameLoop>()) != null
                && (online = Read<OnlineClient>(loop, "online")) != null
                && online.Status == OnlineStatus.Playing && online.Inventory != null, "Connected with a fresh test guest");
            menu = Read<GameMenu>(loop, "gameMenu");
            bag = Read<InventoryScreen>(loop, "inventory");
            config = Read<GameConfig>(loop, "config");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-door-check-only") >= 0)
            {
                yield return CheckDoors();
                yield break;
            }
            TouchSimulation.Disable();
            touch = InputSystem.AddDevice<Touchscreen>();
            InputSystem.EnableDevice(touch);
            InputSystem.EnableDevice(Keyboard.current);
            yield return new WaitForSecondsRealtime(1f);
            Capture("game");
            yield return Portrait();
            Require(menu.Visible && menu.GetComponentsInChildren<Button>().Length == 9, "Avatar opens eight menu tiles and close");
            Capture("menu");
            foreach (string name in new[] { "Tổ đội", "Bang hội", "Xếp hạng", "Thư" })
            {
                yield return Click(menu, name, prefix: true);
                Require(Read<Text>(menu, "notice").text == name + ": sắp có", name + " placeholder");
            }
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.B, Key.E, Key.D));
            float beforeX = (float)online.Predictor.Movement.X;
            yield return new WaitForSecondsRealtime(0.3f);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            Require(!bag.Visible && !Read<HudView>(loop, "hud").DialogVisible, "B and E do not act through menu");
            Require(Mathf.Abs((float)online.Predictor.Movement.X - beforeX) < 0.05f, "Movement blocked while menu is open");
            yield return Click(menu, "Nhân vật");
            yield return new WaitForSecondsRealtime(0.25f);
            Require(Has(menu, "Sinh lực") && Has(menu, config.Character.MaxHp.ToString("N0")), "Character shows configured health");
            Require(Has(menu, "Năng lượng") && Has(menu, "Thể lực") && Has(menu, "Công") && Has(menu, "Thủ"), "Character uses agreed stats");
            CheckFigure(menu);
            Capture("character");
            foreach (string name in new[] { "Chỉ số", "Tiềm năng", "Danh hiệu" })
            {
                yield return Click(menu, name);
                Require(menu.GetComponentsInChildren<Text>().Any(t => t.text.Contains("<size=26>Sắp có")), name + " tab is pending");
            }
            yield return Close(menu);
            yield return Click(menu, "Hành trang");
            yield return new WaitForSecondsRealtime(0.25f);
            Require(bag.Visible && !menu.Visible, "Bag opens without a second overlay");
            Require(Read<long>(bag, "selected") == online.Inventory.Items.Where(i => i.Place == ItemPlace.Bag).OrderBy(i => i.Slot).First().Id,
                "Opening bag selects first item");
            Require(Has(bag, "Thuộc tính tổng") && Has(bag, "Mô tả"), "Bag includes total attributes and description");
            Require(!string.IsNullOrWhiteSpace(Read<Text>(bag, "itemDescription").text), "Selected item description comes from catalog");
            OwnedItem weapon = online.Inventory.Items.First(i => i.Place == ItemPlace.Bag && config.Gear[i.Item].Slot == GearSlot.Weapon);
            SelectBag(weapon.Slot);
            CheckFigure(bag);
            Vector3 feetBefore = FigureFeet(bag);
            Capture("bag");
            yield return Click(bag, "MẶC");
            yield return Until(() => online.Inventory.Items.Any(i => i.Id == weapon.Id && i.Place == ItemPlace.Worn), "Server confirms wearing weapon");
            yield return Until(() => online.SelfAppearance.Weapon == weapon.Item, "Server appearance follows equipped weapon");
            yield return null;
            Require(Read<Appearance>(bag, "shownAppearance").Equals(online.SelfAppearance), "Bag preview follows server appearance");
            CheckFigure(bag);
            Require(Vector3.Distance(feetBefore, FigureFeet(bag)) < 0.01f, "Weapon change keeps feet on pedestal");
            Capture("bag-worn-weapon");
            OwnedItem hat = online.Inventory.Items.First(i => i.Place == ItemPlace.Bag && config.Gear[i.Item].Slot == GearSlot.Hat);
            yield return Click(bag, "Tất cả");
            yield return Click(bag, "Mũ");
            Require(Read<OwnedItem[]>(bag, "displayedItems")[0].Id == hat.Id, "Filter packs matching item while preserving its ID");
            Require(Read<OwnedItem[]>(bag, "displayedItems").Count(i => i.Id != 0) == 1, "Hat filter excludes other item types");
            SelectBag(0);
            Capture("bag-filter-hat");
            yield return Click(bag, "MẶC");
            yield return Until(() => online.Inventory.Items.Any(i => i.Id == hat.Id && i.Place == ItemPlace.Worn), "Server confirms wearing hat");
            CheckFigure(bag);
            yield return Click(bag, "THÁO");
            yield return Until(() => online.Inventory.Items.Any(i => i.Id == hat.Id && i.Place == ItemPlace.Bag), "Server confirms taking hat off");
            yield return Click(bag, "BỎ RA ĐẤT");
            Require(online.Inventory.Items.Any(i => i.Id == hat.Id), "First drop click keeps the item");
            Require(Has(bag, "BẤM LẦN NỮA"), "Drop asks for second click");
            yield return Click(bag, "BẤM LẦN NỮA");
            yield return Until(() => online.Inventory.Items.All(i => i.Id != hat.Id), "Server confirms drop after second click");
            Require(Has(bag, "Không có trang bị loại này"), "Empty filter explains missing items");
            yield return Click(bag, "Mũ");
            yield return Click(bag, "Tất cả");
            foreach (GearSlot slot in new[] { GearSlot.Armor, GearSlot.Pants })
            {
                OwnedItem clothing = online.Inventory.Items.First(i => i.Place == ItemPlace.Bag && config.Gear[i.Item].Slot == slot);
                SelectBag(clothing.Slot);
                yield return Click(bag, "MẶC");
                yield return Until(() => online.SelfAppearance[slot] == clothing.Item, "Server confirms front preview clothing: " + slot);
                yield return new WaitForSecondsRealtime(0.25f);
                CheckFigure(bag);
                Require(Vector3.Distance(feetBefore, FigureFeet(bag)) < 0.01f, "Clothing change keeps feet on pedestal: " + slot);
                Capture("bag-worn-" + slot.ToString().ToLowerInvariant());
            }
            foreach (string name in new[] { "Vật phẩm", "Nguyên liệu" })
            {
                yield return Click(bag, name);
                Require(Has(bag, name == "Vật phẩm" ? "Chưa có vật phẩm" : "Chưa có nguyên liệu"), name + " empty state");
            }
            yield return Close(bag);
            yield return Portrait();
            yield return Click(menu, "Nhân vật");
            Require(Read<GameMenu.MenuState>(menu, "state").Appearance.Equals(online.SelfAppearance), "Reopened character uses latest equipment");
            CheckFigure(menu);
            yield return new WaitForSecondsRealtime(0.25f);
            Capture("character-worn");
            yield return Close(menu);
            yield return Click(menu, "Nhiệm vụ");
            foreach (string name in new[] { "Chính", "Phụ", "Hằng ngày" })
            {
                yield return Click(menu, name);
                Require(Has(menu, "Chưa có nhiệm vụ"), name + " quest placeholder");
                yield return Click(menu, "Theo dõi");
                Require(Read<Text>(menu, "notice").text == "Chưa có nhiệm vụ để theo dõi", "Follow explains empty quest list");
            }
            yield return new WaitForSecondsRealtime(0.25f);
            Capture("quests");
            yield return Close(menu);
            yield return Click(menu, "Cài đặt");
            yield return new WaitForSecondsRealtime(0.25f);
            Capture("settings");
            Require(Has(menu, "Liên kết tài khoản"), "Guest has account linking in settings");
            yield return Click(menu, "Liên kết tài khoản");
            AccountScreens accounts = Read<AccountScreens>(loop, "screens");
            Require(accounts.Visible && !menu.Visible, "Link opens account form");
            accounts.Close();
            yield return Portrait();
            GameMenu.MenuState state = Read<GameMenu.MenuState>(menu, "state");
            state.Guest = false;
            yield return Click(menu, "Cài đặt");
            Require(!Has(menu, "Liên kết tài khoản") && Has(menu, "Thoát game"), "Linked account has no linking button");
            yield return Click(menu, "Thoát game");
            yield return Until(() => Read<OnlineClient>(loop, "online") == null, "Exit returns safely from online game");
        }

        private IEnumerator CheckDoors()
        {
            AccountScreens screens = Read<AccountScreens>(loop, "screens");
            screens.Close();
            LoadingScreen loading = Read<LoadingScreen>(loop, "loading");
            yield return Until(() => !loading.Visible && !Read<bool>(loop, "entering"), "Initial loading finishes");
            int house = config.MapIndex("map.house");
            int village = config.MapIndex("map.village");
            Require(online.Map == house, "Test character starts in its private house");
            MapDoor exit = config.Maps[house].Doors[0];
            InputSystem.EnableDevice(Keyboard.current);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.D));
            yield return Until(() => online.Predictor.Movement.X >= exit.X - 0.3d, "Character walks to the house door");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return new WaitForSecondsRealtime(0.15f);
            Require(MapRules.DoorAt(config.Maps[house], online.Predictor.Movement.X) != null, "Character is within the door's reach");
            // The keyboard follows the same GoThrough handler as the door button on every platform.
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.E));
            yield return new WaitForSecondsRealtime(0.1f);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return Until(() => loading.Visible, "Door action shows the loading screen");
            yield return Until(() => online.Map == village, "Door command reaches server while the loading screen covers the game");
            yield return Until(() => !loading.Visible, "Loading closes after arriving in the village");
            Capture("door-village");
            MapDoor home = config.Maps[village].Doors[0];
            typeof(GameLoop).GetMethod("GoThrough", Private).Invoke(loop, new object[] { home });
            yield return Until(() => online.Map == house, "Door button handler returns from village to house");
            yield return Until(() => !loading.Visible, "Return loading closes");
            Capture("door-house");
            typeof(GameLoop).GetMethod("GoThrough", Private).Invoke(loop, new object[] { exit });
            yield return Until(() => online.Map == village, "Door can be used again after returning home");
            yield return Until(() => !loading.Visible, "Repeated transition finishes loading");
            Require(!Read<bool>(loop, "doorWanted") && Read<int>(loop, "doorFromMap") < 0, "Door transition state is cleared");
        }

        private IEnumerator Portrait()
        {
            yield return new WaitForSecondsRealtime(0.3f);
            HudView hud = Read<HudView>(loop, "hud");
            yield return Until(() => hud.PortraitOpensMenu, "HUD avatar is ready");
            RectTransform rect = Read<RectTransform>(hud, "portrait");
            Vector2 position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Began, position = position });
            yield return new WaitForSecondsRealtime(0.1f);
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = position });
            yield return new WaitForSecondsRealtime(0.3f);
            Require(menu.Visible, "Touch on HUD avatar reaches menu");
        }

        private static bool Has(Component owner, string text) => owner.GetComponentsInChildren<Text>().Any(t => t.text == text);

        private void SelectBag(int slot)
        {
            var cells = Read<List<(Image Slot, Image Icon)>>(bag, "bagCells");
            Press(cells[slot].Slot.GetComponent<Button>());
            Require(cells[slot].Slot.sprite == MenuKit.Art("bag_cell_selected"), "Selected item has gold border");
        }

        private static Image FigureImage(Component owner) => owner.GetComponentsInChildren<Image>().Single(i => i.name == "Figure");

        private static Vector3 FigureFeet(Component owner)
        {
            RectTransform rect = FigureImage(owner).rectTransform;
            return rect.TransformPoint(new Vector3(rect.rect.xMin + rect.rect.width * (215f / 330f), rect.rect.yMax - rect.rect.height * 0.97f));
        }

        private void CheckFigure(Component owner)
        {
            Appearance current = online.SelfAppearance;
            string art = $"Figure/front_{config.Gear[current.Weapon].Art}_{config.Gear[current.Armor].Art}_{config.Gear[current.Pants].Art}";
            Image figure = FigureImage(owner);
            Require(figure.sprite != null && figure.sprite == MenuKit.Art(art), "Figure matches server equipment: " + art);
            Require(!FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(c => c.name == "Character preview camera"), "Figure needs no preview camera");
        }

        private IEnumerator Click(Component owner, string text, bool prefix = false)
        {
            yield return new WaitForSecondsRealtime(0.2f);
            Button button = owner.GetComponentsInChildren<Button>().First(b => b.GetComponentsInChildren<Text>().Any(t => prefix ? t.text.StartsWith(text) : t.text == text));
            Press(button);
            yield return null;
        }

        private IEnumerator Close(Component owner)
        {
            yield return new WaitForSecondsRealtime(0.2f);
            Press(owner.GetComponentsInChildren<Button>().First(b => b.name == "close_round"));
            yield return null;
        }

        private void Press(Button button)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform rect = (RectTransform)button.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Require(Screen.safeArea.Contains(point), "Button inside safe area: " + button.name);
            var data = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, "Button is reachable: " + button.name);
            if (button.name == "button_gold" || button.name == "button_blue")
            {
                Text label = button.GetComponentInChildren<Text>();
                Require(label.preferredWidth <= label.rectTransform.rect.width + 1f, "Button label fits: " + label.text);
            }
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler);
        }

        private void Capture(string name)
        {
            foreach (Camera preview in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (preview.enabled && preview.targetTexture != null) { preview.Render(); preview.Render(); }
            Camera camera = Camera.main;
            RenderTexture previous = camera.targetTexture;
            var target = new RenderTexture(Screen.width, Screen.height, 24);
            camera.targetTexture = target;
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = canvases.Select(c => c.renderMode).ToArray();
            var cameras = canvases.Select(c => c.worldCamera).ToArray();
            var distances = canvases.Select(c => c.planeDistance).ToArray();
            foreach (Canvas canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
            }
            Canvas.ForceUpdateCanvases();
            camera.Render(); camera.Render();
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = target;
            var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            shot.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), shot.EncodeToPNG());
            RenderTexture.active = active;
            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].renderMode = modes[i];
                canvases[i].worldCamera = cameras[i];
                canvases[i].planeDistance = distances[i];
            }
            camera.targetTexture = previous;
            target.Release();
            Destroy(target); Destroy(shot);
            Canvas.ForceUpdateCanvases();
        }
    }
}
#endif
