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
    /// The bag (T078), over the dimmed game, laid out after the menu mock-up approved 15/11/2026: tabs on
    /// the left, the character with what is worn on either side, the 30 bag slots, and the chosen item
    /// on the right with what can be done to it. Nothing changes here until the server answers with the
    /// whole bag; this screen only shows it and asks.
    /// </summary>
    public sealed class InventoryScreen : MonoBehaviour
    {
        // Mock-up stage, 1672 x 941.
        private const float FrameLeft = 55f, FrameTop = 65f, FrameWidth = 1562f, FrameHeight = 820f;
        private const float TabLeft = 104f, TabTop = 152f, TabWidth = 170f, TabHeight = 154f, TabStep = 166f;
        private const float WornLeft = 303f, WornRight = 619f, WornTop = 166f, WornCell = 84f, WornStep = 108f;
        private const float BagLeft = 742f, BagTop = 216f, Cell = 96f, Step = 102f;
        private const int Columns = 5;
        private const float InfoLeft = 1282f, InfoWidth = 293f;
        private const float ConfirmSeconds = 3f;

        private static readonly string[] SlotNames = { "Vũ khí", "Áo", "Quần", "Mũ", "Choàng" };
        private static readonly (string Icon, string Name)[] Tabs =
        {
            ("tab_gear", "Trang bị"), ("tab_bag", "Túi đồ"), ("tab_items", "Vật phẩm"), ("tab_materials", "Nguyên liệu"),
        };

        private GameConfig config;
        private Action<ItemActionKind, long, int> act;
        private Func<Appearance> appearance;
        private RectTransform canvas;
        private RectTransform screen;
        private CharacterPreview preview;
        private Appearance shownAppearance;
        private readonly List<(Image Slot, Image Icon)> wornCells = new List<(Image, Image)>();
        private readonly List<(Image Slot, Image Icon)> bagCells = new List<(Image, Image)>();
        private Text bagHeader, itemName, itemDetail, message, empty;
        private Image itemIcon;
        private Button primary, secondary;
        private InventoryMessage shown;
        private long selected;
        private bool waiting;
        private float confirmUntil;
        private int tab;
        private int filter;
        private RectTransform stage, filterPopup;
        private Text filterLabel, itemType, itemDescription;
        private Image typeBadge;
        private Button filterButton;
        private OwnedItem[] displayedItems;
        private bool selectOnArrival;
        private static readonly string[] Filters = { "Tất cả", "Vũ khí", "Áo", "Quần", "Mũ", "Choàng" };

        public bool Visible => screen != null;

        public static InventoryScreen Create(GameConfig config, Action<ItemActionKind, long, int> act, Func<Appearance> appearance)
        {
            RectTransform canvas = UiKit.CreateCanvas("InventoryCanvas", 60);
            var view = canvas.gameObject.AddComponent<InventoryScreen>();
            view.config = config;
            view.act = act;
            view.appearance = appearance;
            view.canvas = canvas;
            return view;
        }

        public void Toggle(InventoryMessage latest)
        {
            if (Visible)
            {
                Close();
            }
            else
            {
                tab = 0;
                filter = 0;
                Open(latest);
            }
        }

        public void Close()
        {
            if (screen != null)
            {
                screen.gameObject.SetActive(false);
                Destroy(screen.gameObject);
            }

            screen = null;
            filterPopup = null;
            wornCells.Clear();
            bagCells.Clear();
            preview?.Hide();
        }

        /// <summary>Call every frame with the newest bag from the server.</summary>
        public void Show(InventoryMessage latest)
        {
            if (!Visible || latest == null)
            {
                return;
            }

            if (latest != shown)
            {
                waiting = false;
                message.text = latest.Problem == InventoryProblem.None ? string.Empty : InventoryRules.Message(latest.Problem);
            }

            shown = latest;
            if (selectOnArrival)
            {
                SelectFirst();
                selectOnArrival = false;
            }

            if (confirmUntil > 0f && Time.unscaledTime > confirmUntil)
            {
                confirmUntil = 0f;
            }

            Appearance now = appearance();

            if (!now.Equals(shownAppearance))
            {
                shownAppearance = now;
                preview.Show(config, now);
            }

            Refresh();
        }

        private void Open(InventoryMessage latest)
        {
            screen = UiKit.Rect("Inventory", canvas);
            UiKit.Fill(screen);
            UiKit.Shade(screen, 0.6f);
            RectTransform content = UiKit.Rect("SafeContent", screen);
            UiKit.Fill(content);
            stage = UiKit.Stage(content);
            var group = content.gameObject.AddComponent<CanvasGroup>();
            var presentation = content.gameObject.AddComponent<AccountPresentation>();
            presentation.Stage = stage;
            presentation.Group = group;

            MenuKit.RectFrame(stage, "HÀNH TRANG", FrameLeft, FrameTop, FrameWidth, FrameHeight, Close);
            MenuKit.Box(stage, 94f, 140f, 190f, 692f);
            MenuKit.Box(stage, 295f, 140f, 420f, 465f);
            MenuKit.Box(stage, 728f, 140f, 542f, 692f);

            for (int index = 0; index < Tabs.Length; index++)
            {
                int at = index;
                MenuKit.VerticalTab(stage, Tabs[index].Icon, Tabs[index].Name, TabLeft, TabTop + index * TabStep, TabWidth, TabHeight, index == tab, () => PickTab(at));
            }

            // The character on its pedestal, weapon, armour and trousers on the left, hat and cape on the right.
            preview ??= new CharacterPreview();
            shownAppearance = appearance();
            preview.Show(config, shownAppearance);
            MenuKit.Figure(stage, preview, 515f, 548f, 365f);

            for (int slot = 0; slot < SlotNames.Length; slot++)
            {
                int at = slot;
                float left = slot < 3 ? WornLeft : WornRight;
                float top = WornTop + (slot < 3 ? slot : slot - 3) * WornStep;
                wornCells.Add(MenuKit.Slot(stage, left, top, WornCell, () => SelectWorn(at)));
            }

            BuildStats();
            MenuKit.Image(stage, "menu_bag", BagLeft, 155f, 44f, 44f);
            bagHeader = MenuKit.Line(stage, string.Empty, 27, Color.white, BagLeft + 50f, 155f, 263f, 44f);
            UiKit.FitOneLine(bagHeader, 21);
            Image filterBack = MenuKit.Image(stage, "dropdown", 1060f, 156f, 146f, 44f, 2.8f);
            filterLabel = MenuKit.Line(filterBack.transform, Filters[filter], 24, Color.white, 12f, 0f, 100f, 44f);
            UiKit.FitOneLine(filterLabel, 18);
            filterButton = MenuKit.Clickable(filterBack);
            filterButton.onClick.AddListener(ToggleFilter);
            Image list = MenuKit.Image(stage, "button_list", 1214f, 156f, 40f, 44f);
            MenuKit.Clickable(list).onClick.AddListener(() => message.text = "Chế độ danh sách: sắp có");
            displayedItems = new OwnedItem[config.Bag.Slots];

            for (int index = 0; index < config.Bag.Slots; index++)
            {
                int at = index;
                bagCells.Add(MenuKit.Slot(stage, BagLeft + index % Columns * Step, BagTop + index / Columns * Step, Cell, () => SelectBag(at)));
            }

            empty = MenuKit.Line(stage, string.Empty, 24, UiKit.Gold, BagLeft, 840f, Columns * Step, 32f, TextAnchor.MiddleCenter);

            MenuKit.Box(stage, InfoLeft, 140f, InfoWidth, 692f);
            itemIcon = UiKit.Place(UiKit.Rect("ItemIcon", stage).gameObject.AddComponent<Image>(), InfoLeft + (InfoWidth - 156f) / 2f, 158f, 156f, 156f);
            itemIcon.preserveAspect = true;
            itemIcon.raycastTarget = false;
            itemName = MenuKit.Line(stage, string.Empty, 32, Color.white, InfoLeft + 10f, 318f, InfoWidth - 20f, 44f, TextAnchor.MiddleCenter);
            UiKit.FitOneLine(itemName, 18);
            typeBadge = MenuKit.Image(stage, "tag_pill", InfoLeft + 24f, 368f, InfoWidth - 48f, 38f, 3f);
            itemType = MenuKit.Line(typeBadge.transform, string.Empty, 23, Color.white, 6f, 0f, InfoWidth - 60f, 38f, TextAnchor.MiddleCenter);
            MenuKit.Line(stage, "Chỉ số", 29, UiKit.Gold, InfoLeft + 20f, 408f, InfoWidth - 40f, 40f);
            MenuKit.Image(stage, "divider", InfoLeft + 20f, 446f, InfoWidth - 40f, 12f);
            itemDetail = MenuKit.Line(stage, string.Empty, 22, Color.white, InfoLeft + 20f, 462f, InfoWidth - 40f, 74f, TextAnchor.UpperLeft, FontStyle.Normal);
            MenuKit.Line(stage, "Mô tả", 29, UiKit.Gold, InfoLeft + 20f, 536f, InfoWidth - 40f, 40f);
            MenuKit.Image(stage, "divider", InfoLeft + 20f, 574f, InfoWidth - 40f, 12f);
            itemDescription = MenuKit.Line(stage, string.Empty, 22, Color.white, InfoLeft + 20f, 590f, InfoWidth - 40f, 78f, TextAnchor.UpperLeft, FontStyle.Normal);
            foreach (Text paragraph in new[] { itemDetail, itemDescription })
            {
                paragraph.resizeTextForBestFit = true;
                paragraph.resizeTextMinSize = 18;
                paragraph.resizeTextMaxSize = 22;
                paragraph.verticalOverflow = VerticalWrapMode.Truncate;
            }
            primary = MenuKit.LongButton(stage, "MẶC", true, InfoLeft + 10f, 676f, InfoWidth - 20f, 66f, Primary);
            secondary = MenuKit.LongButton(stage, "BỎ RA ĐẤT", false, InfoLeft + 10f, 750f, InfoWidth - 20f, 66f, Secondary);

            message = MenuKit.Line(stage, string.Empty, 24, UiKit.ErrorText, 300f, 900f, 1100f, 36f, TextAnchor.MiddleCenter, FontStyle.Normal);
            UiKit.FitOneLine(message, 16);

            selected = 0;
            waiting = false;
            confirmUntil = 0f;
            shown = latest;
            selectOnArrival = latest == null;
            SelectFirst();
            presentation.RefreshLayout();
            Refresh();
        }

        private void PickTab(int chosen)
        {
            InventoryMessage latest = shown;
            Close();
            tab = chosen;
            filter = 0;
            Open(latest);
        }

        private void BuildStats()
        {
            MenuKit.Box(stage, 295f, 613f, 420f, 219f);
            MenuKit.Line(stage, "Thuộc tính tổng", 27, UiKit.Gold, 313f, 615f, 380f, 36f);
            MenuKit.Image(stage, "divider", 313f, 649f, 380f, 10f);
            var stats = new[] { ("stat_hp", "Sinh lực", config.Character.MaxHp.ToString("N0")),
                ("stat_energy", "Năng lượng", config.Character.MaxEnergy.ToString("N0")),
                ("stat_stamina", "Thể lực", config.Character.MaxStamina.ToString("N0")),
                ("stat_attack", "Công", config.Character.Attack.ToString("N0")),
                ("stat_defense", "Thủ", config.Character.Defense.ToString("N0")) };
            for (int i = 0; i < stats.Length; i++)
            {
                float top = 660f + i * 32f;
                MenuKit.Image(stage, stats[i].Item1, 313f, top, 27f, 27f);
                MenuKit.Line(stage, stats[i].Item2, 22, Color.white, 348f, top, 170f, 30f, style: FontStyle.Normal);
                MenuKit.Line(stage, stats[i].Item3, 22, Color.white, 514f, top, 115f, 30f, TextAnchor.MiddleRight);
                MenuKit.Line(stage, "+0", 20, new Color32(0x9D, 0xB5, 0xDA, 255), 640f, top, 52f, 30f, TextAnchor.MiddleRight);
            }
        }

        private bool Matches(OwnedItem item) => ShowsGear && item.Place == ItemPlace.Bag
            && item.Item >= 0 && item.Item < config.Gear.Count
            && (filter == 0 || (int)config.Gear[item.Item].Slot == filter - 1);

        private void SelectFirst() => selected = Items.Where(Matches).OrderBy(item => item.Slot).FirstOrDefault().Id;

        private void ToggleFilter()
        {
            if (filterPopup != null) { CloseFilter(); return; }
            filterPopup = UiKit.Rect("FilterOptions", stage);
            UiKit.Fill(filterPopup);
            Image outside = UiKit.Shade(filterPopup, 0f);
            MenuKit.Clickable(outside).onClick.AddListener(CloseFilter);
            MenuKit.Box(filterPopup, 1054f, 204f, 206f, 302f);
            for (int i = 0; i < Filters.Length; i++)
            {
                int at = i;
                Image row = MenuKit.Surface(filterPopup, "FilterOption", 1060f, 210f + i * 48f, 194f, 44f,
                    i == filter ? new Color32(0x3D, 0x60, 0x91, 255) : UiKit.Navy, new Color32(0x76, 0xAC, 0xDB, 255));
                MenuKit.Line(row.transform, Filters[i], 23, Color.white, 8f, 0f, 178f, 44f, TextAnchor.MiddleCenter);
                MenuKit.Clickable(row).onClick.AddListener(() => PickFilter(at));
            }
        }

        private void CloseFilter()
        {
            if (filterPopup == null) return;
            filterPopup.gameObject.SetActive(false);
            Destroy(filterPopup.gameObject);
            filterPopup = null;
        }

        private void PickFilter(int chosen)
        {
            filter = chosen;
            confirmUntil = 0f;
            CloseFilter();
            SelectFirst();
            Refresh();
        }

        /// <summary>The bag holds only equipment so far, so items and materials are empty.</summary>
        private bool ShowsGear => tab <= 1;

        private IReadOnlyList<OwnedItem> Items => shown?.Items ?? (IReadOnlyList<OwnedItem>)Array.Empty<OwnedItem>();

        private void SelectBag(int slot)
        {
            if (!ShowsGear)
            {
                return;
            }

            OwnedItem item = displayedItems[slot];
            Select(item.Id);
        }

        private void SelectWorn(int slot)
        {
            OwnedItem item = Items.FirstOrDefault(owned => owned.Place == ItemPlace.Worn && owned.Slot == slot);
            Select(item.Id);
        }

        private void Select(long id)
        {
            selected = id;
            confirmUntil = 0f;
            message.text = string.Empty;
            Refresh();
        }

        private bool TryChosen(out OwnedItem item)
        {
            item = Items.FirstOrDefault(owned => owned.Id == selected);
            return selected != 0 && item.Id == selected && item.Item >= 0 && item.Item < config.Gear.Count;
        }

        private void Primary()
        {
            if (waiting || !TryChosen(out OwnedItem item))
            {
                return;
            }

            if (item.Place == ItemPlace.Bag)
            {
                Send(ItemActionKind.Wear, item.Id);
            }
            else if (!GearRules.IsShown(config.Gear[item.Item].Slot))
            {
                Send(ItemActionKind.TakeOff, item.Slot);
            }
        }

        /// <summary>Dropping asks once more, since the item leaves the bag for anyone to pick up.</summary>
        private void Secondary()
        {
            if (waiting || !TryChosen(out OwnedItem item) || item.Place != ItemPlace.Bag)
            {
                return;
            }

            if (confirmUntil <= 0f)
            {
                confirmUntil = Time.unscaledTime + ConfirmSeconds;
                Refresh();
                return;
            }

            confirmUntil = 0f;
            Send(ItemActionKind.Drop, item.Id);
            selected = 0;
        }

        private void Send(ItemActionKind kind, long target)
        {
            waiting = true;
            message.text = string.Empty;
            act(kind, target, 0);
            Refresh();
        }

        private void Refresh()
        {
            if (!Visible)
            {
                return;
            }

            IReadOnlyList<OwnedItem> items = Items;
            int used = items.Count(item => item.Place == ItemPlace.Bag);
            bagHeader.text = $"Túi đồ ({(ShowsGear ? used : 0)}/{config.Bag.Slots})";
            filterLabel.text = Filters[filter];
            filterButton.interactable = ShowsGear;
            empty.text = tab == 2 ? "Chưa có vật phẩm" : tab == 3 ? "Chưa có nguyên liệu" : string.Empty;

            for (int slot = 0; slot < wornCells.Count; slot++)
            {
                OwnedItem worn = items.FirstOrDefault(item => item.Place == ItemPlace.Worn && item.Slot == slot);
                ShowCell(wornCells[slot], worn);
                if (worn.Id == 0)
                {
                    wornCells[slot].Icon.enabled = true;
                    wornCells[slot].Icon.sprite = MenuKit.SlotShadow((GearSlot)slot);
                }
            }

            Array.Clear(displayedItems, 0, displayedItems.Length);
            int packed = 0;
            foreach (OwnedItem item in items.Where(Matches).OrderBy(item => item.Slot))
            {
                int position = filter == 0 ? item.Slot : packed++;
                if (position >= 0 && position < displayedItems.Length) displayedItems[position] = item;
            }
            if (ShowsGear && filter > 0 && packed == 0) empty.text = "Không có trang bị loại này";
            for (int slot = 0; slot < bagCells.Count; slot++)
            {
                ShowCell(bagCells[slot], displayedItems[slot]);
            }

            ShowInfo();
        }

        private void ShowCell((Image Slot, Image Icon) cell, OwnedItem item)
        {
            bool has = item.Id != 0 && item.Item >= 0 && item.Item < config.Gear.Count;
            MenuKit.SetSlot(cell, has ? IconOf(item.Item) : null, has && item.Id == selected);
        }

        private void ShowInfo()
        {
            if (!TryChosen(out OwnedItem item))
            {
                itemIcon.enabled = false;
                itemName.text = string.Empty;
                typeBadge.gameObject.SetActive(false);
                itemDescription.text = string.Empty;
                itemDetail.text = shown == null ? "Đang tải túi đồ..." : "Chạm vào một món để xem.";
                primary.gameObject.SetActive(false);
                secondary.gameObject.SetActive(false);
                return;
            }

            GearItemConfig gear = config.Gear[item.Item];
            bool worn = item.Place == ItemPlace.Worn;
            bool stays = worn && GearRules.IsShown(gear.Slot);
            itemIcon.enabled = true;
            itemIcon.sprite = IconOf(item.Item);
            itemName.text = gear.Name;
            typeBadge.gameObject.SetActive(true);
            itemType.text = $"{SlotNames[(int)gear.Slot]} · Bậc {gear.Tier}";
            itemDescription.text = gear.Description;
            itemDetail.text = "Chưa có chỉ số"
                + (stays ? "\n<color=#F2C76E>Đang mặc. Mặc món khác để thay.</color>" : worn ? "\n<color=#F2C76E>Đang mặc.</color>" : string.Empty);

            primary.gameObject.SetActive(!stays);
            MenuKit.SetButtonText(primary, worn ? "THÁO" : "MẶC");
            primary.interactable = !waiting;
            secondary.gameObject.SetActive(!worn);
            MenuKit.SetButtonText(secondary, confirmUntil > 0f ? "BẤM LẦN NỮA" : "BỎ RA ĐẤT");
            secondary.interactable = !waiting;
        }

        private Sprite IconOf(int item)
        {
            GearItemConfig gear = config.Gear[item];
            return UiKit.Art($"Items/{gear.Slot.ToString().ToLowerInvariant()}_{gear.Art}");
        }
    }
}
