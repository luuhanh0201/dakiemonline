using System;
using System.Collections.Generic;
using System.Linq;
using DaKiem.Client.Accounts;
using Game.Net;
using Game.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client
{
    /// <summary>
    /// The main menu the portrait opens and the screens behind it, after the mock-up approved 15/11/2026
    /// (docs/menu/mau-menu-tham-khao.webp): eight tiles, the character screen, the quest screen (its
    /// look only; quests come later) and settings with linking and leaving. The bag has its own screen.
    /// </summary>
    public sealed class GameMenu : MonoBehaviour
    {
        // Mock-up stage, 1672 x 941, as the bag screen.
        private const float BigLeft = 55f, BigTop = 65f, BigWidth = 1562f, BigHeight = 820f;
        private const float MenuLeft = 386f, MenuTop = 110f, MenuWidth = 900f, MenuHeight = 660f;
        private const float Tile = 172f, TileGap = 28f;
        private const float TabLeft = 105f, TabTop = 154f, TabWidth = 208f, TabHeight = 154f, TabStep = 166f;
        private const float NoticeSeconds = 1.6f;

        private GameConfig config;
        private RectTransform canvas;
        private RectTransform screen;
        private RectTransform stage;
        private CharacterPreview preview;
        private Text notice;
        private float noticeUntil;
        private MenuState state;

        /// <summary>What the screens show, taken when one opens.</summary>
        public sealed class MenuState
        {
            public string Name = string.Empty;
            public Appearance Appearance;
            public InventoryMessage Inventory;
            public bool Guest;
            public Action OpenBag;
            public Action Link;
            public Action Leave;
        }

        private enum Page
        {
            Menu,
            Character,
            Quests,
            Settings,
        }

        public bool Visible => screen != null;

        public static GameMenu Create(GameConfig config)
        {
            RectTransform canvas = UiKit.CreateCanvas("MenuCanvas", 61);
            var menu = canvas.gameObject.AddComponent<GameMenu>();
            menu.config = config;
            menu.canvas = canvas;
            return menu;
        }

        public void Open(MenuState current)
        {
            state = current;
            Build(Page.Menu, 0);
        }

        public void Close()
        {
            if (screen != null)
            {
                screen.gameObject.SetActive(false);
                Destroy(screen.gameObject);
            }

            screen = null;
            preview?.Hide();
        }

        private void Update()
        {
            if (notice != null && noticeUntil > 0f && Time.unscaledTime > noticeUntil)
            {
                notice.text = string.Empty;
                noticeUntil = 0f;
            }
        }

        private void Build(Page page, int tab)
        {
            Close();
            screen = UiKit.Rect("Menu", canvas);
            UiKit.Fill(screen);
            UiKit.Shade(screen, 0.6f);
            RectTransform content = UiKit.Rect("SafeContent", screen);
            UiKit.Fill(content);
            stage = UiKit.Stage(content);
            var group = content.gameObject.AddComponent<CanvasGroup>();
            var presentation = content.gameObject.AddComponent<AccountPresentation>();
            presentation.Stage = stage;
            presentation.Group = group;

            switch (page)
            {
                case Page.Menu:
                    BuildMenu();
                    break;
                case Page.Character:
                    BuildCharacter(tab);
                    break;
                case Page.Quests:
                    BuildQuests(tab);
                    break;
                default:
                    BuildSettings();
                    break;
            }

            notice = MenuKit.Line(stage, string.Empty, 24, UiKit.Gold, 300f, 900f, 1072f, 36f, TextAnchor.MiddleCenter);
            presentation.RefreshLayout();
        }

        private void Notify(string text)
        {
            notice.text = text;
            noticeUntil = Time.unscaledTime + NoticeSeconds;
        }

        // ---- Main menu ----------------------------------------------------------------------------

        private void BuildMenu()
        {
            MenuKit.Frame(stage, "MENU", MenuLeft, MenuTop, MenuWidth, MenuHeight, Close);

            var tiles = new (string Icon, string Name, Action Open)[]
            {
                ("menu_character", "Nhân vật", () => Build(Page.Character, 0)),
                ("menu_bag", "Hành trang", () => { Close(); state.OpenBag(); }),
                ("menu_quest", "Nhiệm vụ", () => Build(Page.Quests, 0)),
                ("menu_party", "Tổ đội", null),
                ("menu_guild", "Bang hội", null),
                ("menu_rank", "Xếp hạng", null),
                ("menu_mail", "Thư", null),
                ("menu_settings", "Cài đặt", () => Build(Page.Settings, 0)),
            };

            float rowWidth = 4f * Tile + 3f * TileGap;
            float left = MenuLeft + (MenuWidth - rowWidth) / 2f;

            for (int index = 0; index < tiles.Length; index++)
            {
                (string icon, string name, Action open) = tiles[index];
                float x = left + index % 4 * (Tile + TileGap);
                float y = MenuTop + 222f + index / 4 * (Tile + TileGap);
                bool ready = open != null;
                Image back = MenuKit.Image(stage, ready ? "tile" : "tile_locked", x, y, Tile, Tile, 1.6f);
                Image picture = MenuKit.Image(back.transform, icon, 0f, 0f, Tile * 0.62f, Tile * 0.62f);
                RectTransform at = picture.rectTransform;
                at.anchorMin = at.anchorMax = new Vector2(0.5f, 1f);
                at.anchoredPosition = new Vector2(0f, -Tile * 0.37f);
                picture.color = ready ? Color.white : new Color(0.55f, 0.58f, 0.66f, 1f);

                Text label = UiKit.Label(back.transform, ready ? name : $"{name}\n<size=18>Sắp có</size>", 26, ready ? Color.white : new Color(0.8f, 0.82f, 0.88f, 1f));
                RectTransform place = label.rectTransform;
                place.anchorMin = new Vector2(0f, 0f);
                place.anchorMax = new Vector2(1f, 0f);
                place.sizeDelta = new Vector2(-16f, ready ? 44f : 58f);
                place.anchoredPosition = new Vector2(0f, ready ? 30f : 36f);

                string named = name;
                MenuKit.Clickable(back).onClick.AddListener(() =>
                {
                    if (open != null)
                    {
                        open();
                    }
                    else
                    {
                        Notify($"{named}: sắp có");
                    }
                });
            }
        }

        // ---- Character -----------------------------------------------------------------------------

        private void BuildCharacter(int tab)
        {
            MenuKit.RectFrame(stage, "NHÂN VẬT", BigLeft, BigTop, BigWidth, BigHeight, () => Build(Page.Menu, 0));
            var tabs = new[] { ("tab_overview", "Tổng quan"), ("tab_stats", "Chỉ số"), ("tab_potential", "Tiềm năng"), ("tab_title", "Danh hiệu") };
            BuildTabs(tabs, tab, chosen => Build(Page.Character, chosen));

            if (tab != 0)
            {
                Soon(tabs[tab].Item2);
                return;
            }

            // The figure on its pedestal, with what is worn on either side.
            MenuKit.Box(stage, 338f, 140f, 660f, 692f);
            preview ??= new CharacterPreview();
            preview.Show(config, state.Appearance);
            MenuKit.Figure(stage, preview, 668f, 753f, 475f);

            GearSlot[] left = { GearSlot.Weapon, GearSlot.Armor, GearSlot.Pants };
            GearSlot[] right = { GearSlot.Hat, GearSlot.Cape };

            for (int index = 0; index < left.Length; index++)
            {
                WornSlot(left[index], 362f, 198f + index * 142f);
            }

            for (int index = 0; index < right.Length; index++)
            {
                WornSlot(right[index], 862f, 198f + index * 142f);
            }

            // Who the character is and the stats that exist so far (GDD 5.1); the rest says it is coming.
            CharacterConfig stats = config.Character;
            const float InfoLeft = 1040f, InfoWidth = 500f;
            MenuKit.Box(stage, 1012f, 140f, 563f, 692f);
            UiKit.FitOneLine(MenuKit.Line(stage, state.Name, 42, Color.white, InfoLeft, 162f, InfoWidth, 58f), 22);
            InfoRow("tab_potential", "Hệ", "Thủy", InfoLeft, 244f);
            InfoRow("stat_strength", "Sức mạnh", "Sắp có", InfoLeft, 298f);
            InfoRow("stat_potential", "Tiềm năng", "Sắp có", InfoLeft, 352f);

            MenuKit.Box(stage, InfoLeft - 8f, 424f, InfoWidth + 16f, 380f);
            MenuKit.Line(stage, "Chỉ số cơ bản", 32, UiKit.Gold, InfoLeft + 14f, 438f, InfoWidth - 28f, 44f);
            MenuKit.Image(stage, "divider", InfoLeft + 14f, 480f, InfoWidth - 28f, 16f);
            InfoRow("stat_hp", "Sinh lực", stats.MaxHp.ToString("N0"), InfoLeft + 14f, 500f);
            InfoRow("stat_energy", "Năng lượng", stats.MaxEnergy.ToString("N0"), InfoLeft + 14f, 548f);
            InfoRow("stat_stamina", "Thể lực", stats.MaxStamina.ToString("N0"), InfoLeft + 14f, 596f);
            InfoRow("stat_attack", "Công", stats.Attack.ToString("N0"), InfoLeft + 14f, 644f);
            InfoRow("stat_defense", "Thủ", stats.Defense.ToString("N0"), InfoLeft + 14f, 692f);
            InfoRow("stat_power", "Chiến lực", "Sắp có", InfoLeft + 14f, 740f);
        }

        private void WornSlot(GearSlot slot, float left, float top)
        {
            (Image Slot, Image Icon) cell = MenuKit.Slot(stage, left, top, 112f, null);
            OwnedItem item = (state.Inventory?.Items ?? (IReadOnlyList<OwnedItem>)Array.Empty<OwnedItem>())
                .FirstOrDefault(owned => owned.Place == ItemPlace.Worn && owned.Slot == (int)slot);
            bool has = item.Id != 0 && item.Item >= 0 && item.Item < config.Gear.Count;
            MenuKit.SetSlot(cell, has ? GearIcon(item.Item) : null, false);

            if (!has)
            {
                cell.Icon.enabled = true;
                cell.Icon.sprite = MenuKit.SlotShadow(slot);
            }
        }

        private void InfoRow(string icon, string name, string value, float left, float top)
        {
            MenuKit.Image(stage, icon, left, top, 34f, 34f);
            MenuKit.Line(stage, name, 28, Color.white, left + 44f, top - 2f, 230f, 40f, style: FontStyle.Normal);
            MenuKit.Line(stage, value, 28, Color.white, left + 300f, top - 2f, 170f, 40f, TextAnchor.MiddleRight);
        }

        private Sprite GearIcon(int item)
        {
            GearItemConfig gear = config.Gear[item];
            return UiKit.Art($"Items/{gear.Slot.ToString().ToLowerInvariant()}_{gear.Art}");
        }

        // ---- Quests (the look only) ------------------------------------------------------------------

        private void BuildQuests(int tab)
        {
            MenuKit.RectFrame(stage, "NHIỆM VỤ", BigLeft, BigTop, BigWidth, BigHeight, () => Build(Page.Menu, 0));
            BuildTabs(new[] { ("tab_quest_main", "Chính"), ("tab_quest_side", "Phụ"), ("tab_quest_daily", "Hằng ngày") }, tab, chosen => Build(Page.Quests, chosen));

            const float Left = 338f, Width = 1237f;
            MenuKit.Box(stage, Left, 140f, Width, 692f);
            MenuKit.Line(stage, "Chưa có nhiệm vụ", 36, UiKit.Gold, Left + 30f, 170f, Width - 60f, 50f);
            MenuKit.Line(stage, "Sư phụ sẽ giao nhiệm vụ khi hệ thống nhiệm vụ mở. Nhiệm vụ đang làm sẽ hiện ở đây.", 26, Color.white, Left + 30f, 244f, Width - 60f, 70f, style: FontStyle.Normal);

            MenuKit.Line(stage, "Mục tiêu", 28, UiKit.Gold, Left + 30f, 430f, 300f, 40f);
            MenuKit.Line(stage, "• Chưa có", 24, new Color(0.8f, 0.84f, 0.92f, 1f), Left + 40f, 474f, Width - 80f, 36f, style: FontStyle.Normal);

            MenuKit.Line(stage, "Phần thưởng", 28, UiKit.Gold, Left + 30f, 540f, 300f, 40f);

            for (int index = 0; index < 3; index++)
            {
                MenuKit.Slot(stage, Left + 40f + index * 112f, 590f, 96f, null);
            }

            Button follow = MenuKit.LongButton(stage, "Theo dõi", true, Left + (Width - 320f) / 2f, 730f, 320f, 82f, () => Notify("Chưa có nhiệm vụ để theo dõi"));
            follow.GetComponent<Image>().color = new Color(0.75f, 0.75f, 0.75f, 1f);
        }

        // ---- Settings ------------------------------------------------------------------------------

        private void BuildSettings()
        {
            MenuKit.Frame(stage, "CÀI ĐẶT", MenuLeft, MenuTop, MenuWidth, MenuHeight, () => Build(Page.Menu, 0));
            float top = MenuTop + 260f;

            if (state.Guest)
            {
                MenuKit.LongButton(stage, "Liên kết tài khoản", true, MenuLeft + 200f, top, 500f, 96f, () => { Close(); state.Link(); });
                top += 130f;
            }

            MenuKit.LongButton(stage, "Thoát game", false, MenuLeft + 200f, top, 500f, 96f, () => { Close(); state.Leave(); });
        }

        // ---- Shared -------------------------------------------------------------------------------

        private void BuildTabs((string Icon, string Name)[] tabs, int chosen, Action<int> pick)
        {
            MenuKit.Box(stage, 94f, 140f, 230f, 692f);
            for (int index = 0; index < tabs.Length; index++)
            {
                int at = index;
                MenuKit.VerticalTab(stage, tabs[index].Icon, tabs[index].Name, TabLeft, TabTop + index * TabStep, TabWidth, TabHeight, index == chosen, () => pick(at));
            }
        }

        private void Soon(string name)
        {
            MenuKit.Box(stage, 338f, 140f, 1237f, 692f);
            MenuKit.Line(stage, $"{name}\n<size=26>Sắp có</size>", 40, UiKit.Gold, 338f, 400f, 1237f, 140f, TextAnchor.MiddleCenter);
        }
    }
}
