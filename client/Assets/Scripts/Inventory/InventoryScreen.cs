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
    /// The bag (T078), over the dimmed game: what is worn on the left, the 30 bag slots in the middle,
    /// the chosen item on the right with what can be done to it. Nothing changes here until the
    /// server answers with the whole bag; this screen only shows it and asks.
    /// </summary>
    public sealed class InventoryScreen : MonoBehaviour
    {
        // Mock-up layout on the 1672 x 941 stage. The frame's navy area is x 211-1461, y 186-867.
        private const float FrameLeft = 156f, FrameTop = 80f, FrameWidth = 1360f, FrameHeight = 820f;
        private const float WornLeft = 236f, BagLeft = 490f, CellTop = 282f, Cell = 96f, Step = 104f;
        private const int Columns = 6;
        private const float InfoLeft = 1140f, InfoWidth = 306f;
        private const float ConfirmSeconds = 3f;

        private static readonly string[] SlotNames = { "Vũ khí", "Áo", "Quần", "Mũ", "Choàng" };

        private GameConfig config;
        private Action<ItemActionKind, long, int> act;
        private RectTransform canvas;
        private RectTransform screen;
        private readonly List<(Image Card, Image Icon)> wornCells = new List<(Image, Image)>();
        private readonly List<(Image Card, Image Icon)> bagCells = new List<(Image, Image)>();
        private Text bagHeader, itemName, itemDetail, message;
        private Image itemIcon;
        private Button primary, secondary;
        private InventoryMessage shown;
        private long selected;
        private bool waiting;
        private float confirmUntil;

        public bool Visible => screen != null;

        public static InventoryScreen Create(GameConfig config, Action<ItemActionKind, long, int> act)
        {
            RectTransform canvas = UiKit.CreateCanvas("InventoryCanvas", 60);
            var view = canvas.gameObject.AddComponent<InventoryScreen>();
            view.config = config;
            view.act = act;
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
                Open(latest);
            }
        }

        public void Close()
        {
            if (screen != null)
            {
                Destroy(screen.gameObject);
            }

            screen = null;
            wornCells.Clear();
            bagCells.Clear();
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

            if (confirmUntil > 0f && Time.unscaledTime > confirmUntil)
            {
                confirmUntil = 0f;
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
            RectTransform stage = UiKit.Stage(content);
            var group = content.gameObject.AddComponent<CanvasGroup>();
            var presentation = content.gameObject.AddComponent<AccountPresentation>();
            presentation.Stage = stage;
            presentation.Group = group;

            Image frame = UiKit.Place(UiKit.Image(stage, "panel", sliced: true), FrameLeft, FrameTop, FrameWidth, FrameHeight);
            frame.pixelsPerUnitMultiplier = 1.75f;
            UiKit.Title(stage, "HÀNH TRANG", 586f, 148f, 500f, 74f);
            UiKit.Close(stage, FrameLeft + FrameWidth - 139f, 132f, 80f, Close);

            UiKit.Place(UiKit.Label(stage, "ĐANG MẶC", 28, UiKit.Gold, TextAnchor.MiddleLeft), WornLeft, 236f, 240f, 40f);

            for (int slot = 0; slot < SlotNames.Length; slot++)
            {
                int at = slot;
                wornCells.Add(MakeCell(stage, WornLeft, CellTop + slot * Step, () => SelectWorn(at)));
                UiKit.Place(UiKit.Label(stage, SlotNames[slot], 26, Color.white, TextAnchor.MiddleLeft), WornLeft + Cell + 12f, CellTop + slot * Step, 120f, Cell);
            }

            bagHeader = UiKit.Place(UiKit.Label(stage, "TÚI ĐỒ", 28, UiKit.Gold, TextAnchor.MiddleLeft), BagLeft, 236f, 616f, 40f);

            for (int index = 0; index < config.Bag.Slots; index++)
            {
                int at = index;
                bagCells.Add(MakeCell(stage, BagLeft + index % Columns * Step, CellTop + index / Columns * Step, () => SelectBag(at)));
            }

            Image inset = UiKit.Place(UiKit.Rect("Info", stage).gameObject.AddComponent<Image>(), InfoLeft, 236f, InfoWidth, 558f);
            inset.color = new Color(0f, 0f, 0f, 0.22f);
            inset.raycastTarget = false;
            itemIcon = UiKit.Place(UiKit.Rect("ItemIcon", stage).gameObject.AddComponent<Image>(), InfoLeft + (InfoWidth - 150f) / 2f, 252f, 150f, 150f);
            itemIcon.preserveAspect = true;
            itemIcon.raycastTarget = false;
            itemName = UiKit.Place(UiKit.Label(stage, string.Empty, 32, Color.white), InfoLeft + 10f, 410f, InfoWidth - 20f, 48f);
            UiKit.FitOneLine(itemName, 22);
            itemDetail = UiKit.Place(UiKit.Label(stage, string.Empty, 24, Color.white, style: FontStyle.Normal), InfoLeft + 14f, 462f, InfoWidth - 28f, 132f);
            primary = UiKit.Place(UiKit.Button(stage, "MẶC", UiKit.ButtonKind.Gold, Primary, 34), InfoLeft + 15f, 606f, InfoWidth - 30f, 80f);
            secondary = UiKit.Place(UiKit.Button(stage, "BỎ RA ĐẤT", UiKit.ButtonKind.Blue, Secondary, 28), InfoLeft + 15f, 700f, InfoWidth - 30f, 80f);

            message = UiKit.Place(UiKit.Label(stage, string.Empty, 26, UiKit.ErrorText, style: FontStyle.Normal), 260f, 800f, 1150f, 46f);
            UiKit.FitOneLine(message, 18);

            selected = 0;
            waiting = false;
            confirmUntil = 0f;
            shown = latest;
            presentation.RefreshLayout();
            Refresh();
        }

        private (Image Card, Image Icon) MakeCell(RectTransform stage, float left, float top, Action clicked)
        {
            Image card = UiKit.Place(UiKit.Image(stage, "card", sliced: true), left, top, Cell, Cell);
            card.pixelsPerUnitMultiplier = 2.2f;
            card.raycastTarget = true;
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = card;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => clicked());
            Image icon = UiKit.Rect("Icon", card.transform).gameObject.AddComponent<Image>();
            UiKit.Fill(icon.rectTransform, 10f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            return (card, icon);
        }

        private IReadOnlyList<OwnedItem> Items => shown?.Items ?? (IReadOnlyList<OwnedItem>)Array.Empty<OwnedItem>();

        private void SelectBag(int slot)
        {
            OwnedItem item = Items.FirstOrDefault(owned => owned.Place == ItemPlace.Bag && owned.Slot == slot);
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
            bagHeader.text = $"TÚI ĐỒ  <size=24>({used}/{config.Bag.Slots})</size>";

            for (int slot = 0; slot < wornCells.Count; slot++)
            {
                ShowCell(wornCells[slot], items.FirstOrDefault(item => item.Place == ItemPlace.Worn && item.Slot == slot));
            }

            for (int slot = 0; slot < bagCells.Count; slot++)
            {
                ShowCell(bagCells[slot], items.FirstOrDefault(item => item.Place == ItemPlace.Bag && item.Slot == slot));
            }

            ShowInfo();
        }

        private void ShowCell((Image Card, Image Icon) cell, OwnedItem item)
        {
            bool has = item.Id != 0 && item.Item >= 0 && item.Item < config.Gear.Count;
            cell.Card.sprite = UiKit.Art(has && item.Id == selected ? "card_selected" : "card");
            cell.Icon.enabled = has;
            cell.Icon.sprite = has ? IconOf(item.Item) : null;
        }

        private void ShowInfo()
        {
            if (!TryChosen(out OwnedItem item))
            {
                itemIcon.enabled = false;
                itemName.text = string.Empty;
                itemDetail.text = Items.Count == 0 ? "Đang tải túi đồ..." : "Chạm vào một món để xem.";
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
            itemDetail.text = $"{SlotNames[(int)gear.Slot]} · Bậc {gear.Tier}\nChưa có chỉ số"
                + (stays ? "\n<color=#F2C76E>Đang mặc. Mặc món khác để thay.</color>" : worn ? "\n<color=#F2C76E>Đang mặc.</color>" : string.Empty);

            primary.gameObject.SetActive(!stays);
            UiKit.SetText(primary, worn ? "THÁO" : "MẶC");
            primary.interactable = !waiting;
            secondary.gameObject.SetActive(!worn);
            UiKit.SetText(secondary, confirmUntil > 0f ? "BẤM LẦN NỮA ĐỂ BỎ" : "BỎ RA ĐẤT");
            secondary.interactable = !waiting;
        }

        private Sprite IconOf(int item)
        {
            GearItemConfig gear = config.Gear[item];
            return UiKit.Art($"Items/{gear.Slot.ToString().ToLowerInvariant()}_{gear.Art}");
        }
    }
}
