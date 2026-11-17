using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client
{
    /// <summary>Draws the portrait, the bars, the action buttons and the joystick.</summary>
    public sealed class HudView
    {
        private const float FlashSeconds = 0.12f;
        private const float PortraitDp = 56f;
        private const float BarWidthDp = 180f;
        private const float BarHeightDp = 15f;
        private const float EnergyBarHeightDp = 12f;
        private const float StaminaBarWidthDp = 120f;
        private const float StaminaBarHeightDp = 9f;
        private const float BarInsetDp = 1.5f;
        private const float EdgeDp = 16f;
        private const float BarGapDp = 3f;
        private const int LabelSizeDp = 13;
        private const float BossBarWidthDp = 300f;
        private const float BossBarHeightDp = 15f;
        private const float EndPanelWidthDp = 320f;
        private const float EndPanelHeightDp = 250f;
        private const float ReplayWidthDp = 160f;
        private const float ReplayHeightDp = 56f;
        private const int EndTitleSizeDp = 28;
        private const int EndBodySizeDp = 16;
        private const float NameHeightDp = 14f;
        private const int NameSizeDp = 13;
        private const int BarNumberSizeDp = 10;
        private const float PlaqueWidthDp = 170f;
        private const float PlaqueHeightDp = 24f;
        private const int PlaqueTextSizeDp = 13;
        private const float BossBarTopDp = 44f;
        private const float IconShare = 0.64f;
        private const float TargetWidthDp = 170f;
        private const float TargetHeightDp = 34f;
        private const int TargetTextSizeDp = 11;
        private const float DialogWidthDp = 360f;
        private const float DialogHeightDp = 140f;
        private const int DialogTitleSizeDp = 16;
        private const int DialogBodySizeDp = 13;
        private const float JoystickRestAlpha = 0.45f;
        private const float JoystickHeldAlpha = 0.7f;
        private const int CountSizeDp = 12;

        private static readonly Color Panel = new Color32(0x14, 0x1B, 0x3A, 0xFF);
        private static readonly Color ButtonFill = new Color32(0x1E, 0x3F, 0x7A, 0xFF);
        private static readonly Color ButtonLit = new Color32(0x2A, 0x63, 0xB8, 0xFF);
        private static readonly Color AttackFill = new Color32(0x2A, 0x63, 0xB8, 0xFF);
        private static readonly Color AttackLit = new Color32(0x3D, 0x8B, 0xF2, 0xFF);
        private static readonly Color Pressed = new Color32(0xB8, 0xC8, 0xFF, 0xFF);
        private static readonly Color Dimmed = new Color(0.55f, 0.55f, 0.65f, 1f);
        private static readonly Color DimmedLabel = new Color(1f, 1f, 1f, 0.75f);
        private static readonly Color CooldownSweep = new Color32(0x14, 0x1B, 0x3A, 0xB0);
        private static readonly Color LabelOutline = new Color32(0x14, 0x1B, 0x3A, 0xFF);
        private static readonly Color HealthColor = new Color32(0x46, 0xB3, 0x7E, 0xFF);
        private static readonly Color EnergyColor = new Color32(0x4F, 0x7B, 0xE8, 0xFF);
        private static readonly Color StaminaColor = new Color32(0xF2, 0xC7, 0x6E, 0xFF);
        private static readonly Color AccentColor = new Color32(0x3D, 0x8B, 0xF2, 0xFF);
        private static readonly Color BossHealthColor = new Color32(0xD9, 0x4F, 0x4F, 0xFF);
        private static readonly Color EndPanelColor = new Color32(0x14, 0x1B, 0x3A, 0xF0);

        private readonly Dictionary<HudButton, Image> buttonImages = new Dictionary<HudButton, Image>();
        private readonly Dictionary<HudButton, float> flashUntil = new Dictionary<HudButton, float>();
        private readonly Dictionary<HudButton, Text> buttonLabels = new Dictionary<HudButton, Text>();
        private readonly Dictionary<HudButton, Image> cooldownSweeps = new Dictionary<HudButton, Image>();
        private readonly Dictionary<HudButton, Image> buttonIcons = new Dictionary<HudButton, Image>();
        private readonly Dictionary<HudButton, Text> buttonCounts = new Dictionary<HudButton, Text>();
        private readonly HashSet<HudButton> dimmedButtons = new HashSet<HudButton>();
        private readonly RectTransform root;
        private readonly RectTransform area;
        private readonly Canvas canvas;
        private readonly RectTransform healthFill;
        private readonly RectTransform energyFill;
        private readonly RectTransform staminaFill;
        private readonly RectTransform bossHealthFill;
        private readonly GameObject endPanel;
        private readonly Text endTitle;
        private readonly Text endBody;
        private readonly RectTransform replayButton;
        private readonly RectTransform joystickBase;
        private readonly RectTransform joystickKnob;
        private readonly Sprite disc;
        private readonly Sprite attackDisc;
        private readonly Sprite plainDisc;
        private readonly Sprite barFrame;
        private readonly Sprite barFill;
        private readonly Sprite panelFrame;
        private readonly Sprite actionFrame;
        private readonly Sprite goldFrame;
        private readonly Sprite skillSquare;
        private readonly RectTransform portrait;
        private readonly Text nameLabel;
        private readonly Text healthNumber;
        private readonly RectTransform plaque;
        private readonly Text plaqueLabel;
        private int shownHealth = -1;
        private int shownMaxHealth = -1;
        private bool joystickHeld;
        private readonly RectTransform targetPanel;
        private readonly Text targetLabel;
        private readonly RectTransform targetFill;
        private readonly RectTransform dialog;
        private readonly Text dialogTitle;
        private readonly Text dialogBody;
        private readonly Dictionary<HudButton, Image> autoMarks = new Dictionary<HudButton, Image>();
        private Sprite markFrame;
        private Sprite plainAttackIcon;

        public HudView()
        {
            disc = DrawnShapes.Disc(ButtonFill, ButtonLit);
            attackDisc = DrawnShapes.Disc(AttackFill, AttackLit);
            plainDisc = DrawnShapes.PlainDisc();
            barFrame = DrawnShapes.Frame(Panel, false);
            barFill = DrawnShapes.BarFill();
            panelFrame = DrawnShapes.Frame(EndPanelColor, true);
            actionFrame = DrawnShapes.Frame(AccentColor, true);
            goldFrame = DrawnShapes.Frame(Panel, true);
            skillSquare = DrawnShapes.Frame(ButtonFill, true);

            root = CreateCanvas();
            canvas = root.GetComponent<Canvas>();
            area = CreateArea(root);
            portrait = CreatePortrait();

            // The name sits on top, the three bars under it, together as tall as the portrait.
            float barLeft = EdgeDp + PortraitDp + BarGapDp;
            float barTop = -EdgeDp - NameHeightDp;
            nameLabel = CreateLabel("Name", area, new Vector2(BarWidthDp, NameHeightDp), Vector2.zero, NameSizeDp);
            nameLabel.alignment = TextAnchor.MiddleLeft;

            // The bold font's line is taller than the strip, which would otherwise hide the name.
            nameLabel.verticalOverflow = VerticalWrapMode.Overflow;
            PinTopLeft(nameLabel.rectTransform, new Vector2(barLeft + BarInsetDp, -EdgeDp + 1f));
            healthFill = CreateBar(barLeft, barTop, BarWidthDp, BarHeightDp, HealthColor);
            healthNumber = CreateLabel("Number", (RectTransform)healthFill.parent, new Vector2(BarWidthDp, BarHeightDp), Vector2.zero, BarNumberSizeDp);
            energyFill = CreateBar(barLeft, barTop - BarHeightDp - BarGapDp, BarWidthDp, EnergyBarHeightDp, EnergyColor);
            staminaFill = CreateBar(barLeft, barTop - BarHeightDp - EnergyBarHeightDp - 2f * BarGapDp, StaminaBarWidthDp, StaminaBarHeightDp, StaminaColor);

            plaque = CreateFrame("MapName", area, Color.white, goldFrame, new Vector2(PlaqueWidthDp, PlaqueHeightDp));
            plaque.anchorMin = plaque.anchorMax = plaque.pivot = new Vector2(0.5f, 1f);
            plaque.anchoredPosition = new Vector2(0f, -EdgeDp + 4f);
            plaqueLabel = CreateLabel("Label", plaque, new Vector2(PlaqueWidthDp, PlaqueHeightDp), Vector2.zero, PlaqueTextSizeDp);

            SetPlace(null);

            // What is selected: its name and health, under the map plaque.
            targetPanel = CreateFrame("Target", area, Color.white, goldFrame, new Vector2(TargetWidthDp, TargetHeightDp));
            targetPanel.anchorMin = targetPanel.anchorMax = targetPanel.pivot = new Vector2(0.5f, 1f);
            targetPanel.anchoredPosition = new Vector2(0f, -EdgeDp - PlaqueHeightDp);
            targetLabel = CreateLabel("Name", targetPanel, new Vector2(TargetWidthDp, 16f), new Vector2(0f, 7f), TargetTextSizeDp);
            RectTransform targetBar = CreateFrame("Bar", targetPanel, Color.white, barFrame, new Vector2(TargetWidthDp - 16f, 8f));
            targetBar.anchoredPosition = new Vector2(0f, -8f);
            targetFill = CreateBarFill(targetBar, TargetWidthDp - 16f, 8f, BossHealthColor);
            targetPanel.gameObject.SetActive(false);

            // An NPC's words, over the middle of the screen.
            dialog = CreateFrame("Dialog", area, Color.white, panelFrame, new Vector2(DialogWidthDp, DialogHeightDp));
            dialog.anchoredPosition = new Vector2(0f, 30f);
            dialogTitle = CreateLabel("Title", dialog, new Vector2(DialogWidthDp - 24f, 26f), new Vector2(0f, 46f), DialogTitleSizeDp);
            dialogTitle.color = new Color32(0xF2, 0xC7, 0x6E, 0xFF);
            dialogBody = CreateLabel("Body", dialog, new Vector2(DialogWidthDp - 32f, 56f), new Vector2(0f, 4f), DialogBodySizeDp);
            dialogBody.horizontalOverflow = HorizontalWrapMode.Wrap;
            RectTransform close = CreateFrame("Close", dialog, Color.white, actionFrame, new Vector2(110f, 30f));
            close.anchoredPosition = new Vector2(0f, -48f);
            CreateLabel("Label", close, new Vector2(110f, 30f), Vector2.zero, DialogBodySizeDp).text = "Đóng";
            dialog.gameObject.SetActive(false);

            foreach (HudButtonSpec spec in HudLayout.Buttons)
            {
                CreateButton(spec);
            }

            joystickBase = CreateDiscObject("JoystickBase", disc, HudLayout.JoystickDiameterDp, new Color(1f, 1f, 1f, 0.6f));
            joystickKnob = CreateDiscObject("JoystickKnob", attackDisc, HudLayout.JoystickDiameterDp * 0.4f, Color.white);
            ShowJoystick(false, Vector2.zero, Vector2.zero);

            bossHealthFill = CreateBossBar();

            RectTransform panel = CreateFrame("EndPanel", area, Color.white, panelFrame, new Vector2(EndPanelWidthDp, EndPanelHeightDp));
            endPanel = panel.gameObject;
            endTitle = CreateLabel("Title", panel, new Vector2(EndPanelWidthDp, 44f), new Vector2(0f, 90f), EndTitleSizeDp);
            endBody = CreateLabel("Body", panel, new Vector2(EndPanelWidthDp, 110f), new Vector2(0f, 8f), EndBodySizeDp);

            replayButton = CreateFrame("Replay", panel, Color.white, actionFrame, new Vector2(ReplayWidthDp, ReplayHeightDp));
            replayButton.anchoredPosition = new Vector2(0f, -85f);
            CreateLabel("Label", replayButton, new Vector2(ReplayWidthDp, ReplayHeightDp), Vector2.zero, EndBodySizeDp).text = "Chơi lại";
            endPanel.SetActive(false);
        }

        public bool EndScreenVisible => endPanel.activeSelf;

        /// <summary>Hidden behind the account screens, before any fight has started.</summary>
        public void SetVisible(bool visible)
        {
            canvas.enabled = visible;
        }

        public bool ReplayButtonAt(Vector2 screenPosition)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(replayButton, screenPosition, null);
        }

        public void ShowEndScreen(string title, string body)
        {
            endTitle.text = title;
            endBody.text = body;
            endPanel.SetActive(true);
        }

        public void HideEndScreen()
        {
            endPanel.SetActive(false);
        }

        public void ShowBossHealth(float health)
        {
            SetFill(bossHealthFill, BossBarWidthDp, health);
        }

        /// <summary>Online there is no boss yet, so its bar is hidden.</summary>
        public void ShowBossBar(bool visible)
        {
            bossHealthFill.parent.gameObject.SetActive(visible);
        }

        /// <summary>The character's name over the bars; empty when practising alone.</summary>
        public void SetName(string name)
        {
            if (nameLabel.text != name)
            {
                nameLabel.text = name;
            }
        }

        /// <summary>Writes "hp/max" on the health bar, only when either number changes.</summary>
        public void ShowHealthNumber(int health, int maxHealth)
        {
            if (health == shownHealth && maxHealth == shownMaxHealth)
            {
                return;
            }

            shownHealth = health;
            shownMaxHealth = maxHealth;
            healthNumber.text = $"{health}/{maxHealth}";
        }

        /// <summary>The selected target's name and health; red while it is pinned and fought.</summary>
        public void ShowTarget(string name, float health, bool pinned)
        {
            targetPanel.gameObject.SetActive(true);

            if (targetLabel.text != name)
            {
                targetLabel.text = name;
            }

            targetLabel.color = pinned ? new Color32(0xFF, 0xB0, 0xA8, 0xFF) : Color.white;
            SetFill(targetFill, TargetWidthDp - 16f, health);
        }

        public void HideTarget()
        {
            if (targetPanel.gameObject.activeSelf)
            {
                targetPanel.gameObject.SetActive(false);
            }
        }

        public bool DialogVisible => dialog.gameObject.activeSelf;

        public void ShowDialog(string name, string words)
        {
            dialogTitle.text = name;
            dialogBody.text = words;
            dialog.gameObject.SetActive(true);
            dialog.SetAsLastSibling();
        }

        public void HideDialog()
        {
            dialog.gameObject.SetActive(false);
        }

        /// <summary>
        /// Lights the square of the skill the attack button casts and shows that skill on the attack button;
        /// None lights none and the attack button shows the plain attack.
        /// </summary>
        public void SetChosenSkill(HudButton chosen)
        {
            foreach (KeyValuePair<HudButton, Image> pair in autoMarks)
            {
                pair.Value.enabled = pair.Key == chosen;
            }

            if (!buttonIcons.TryGetValue(HudButton.Attack, out Image attack))
            {
                return;
            }

            plainAttackIcon ??= attack.sprite;
            Sprite shown = chosen != HudButton.None && buttonIcons.TryGetValue(chosen, out Image icon) ? icon.sprite : plainAttackIcon;

            if (attack.sprite != shown)
            {
                attack.sprite = shown;
            }
        }

        /// <summary>Names the map on the plaque; null hides it, as in the practice fight.</summary>
        public void SetPlace(string label)
        {
            plaque.gameObject.SetActive(label != null);

            if (label != null)
            {
                plaqueLabel.text = label;
            }
        }

        /// <summary>Whether touching the portrait opens the main menu: only while playing online.</summary>
        public bool PortraitOpensMenu { get; set; }

        /// <summary>Whether a touch lands on the portrait while it opens the menu.</summary>
        public bool PortraitAt(Vector2 screenPosition)
        {
            return PortraitOpensMenu && RectTransformUtility.RectangleContainsScreenPoint(portrait, screenPosition, null);
        }

        /// <summary>
        /// Keeps the HUD inside the game frame and at its scale after the window or browser page changes size.
        /// </summary>
        public void FitScreen()
        {
            var scaler = root.GetComponent<CanvasScaler>();

            if (scaler.enabled && !Mathf.Approximately(scaler.scaleFactor, HudLayout.DpToPixels))
            {
                scaler.scaleFactor = HudLayout.DpToPixels;
            }

            Rect frame = GameFrame.Normalized;
            area.anchorMin = frame.min;
            area.anchorMax = frame.max;

            // The resting joystick follows the frame when the window changes size.
            if (!joystickHeld)
            {
                ShowJoystick(false, Vector2.zero, Vector2.zero);
            }
        }

        /// <summary>Draws the HUD through a camera at a set scale, for the review tools.</summary>
        public void DrawThrough(Camera camera, float scale)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvas.scaleFactor = scale;
            root.GetComponent<CanvasScaler>().enabled = false;
        }

        /// <summary>Darkens the share of a button still waiting, sweeping away as it recovers.</summary>
        public void SetCooldown(HudButton button, float share)
        {
            cooldownSweeps[button].fillAmount = Mathf.Clamp01(share);
        }

        public HudButton ButtonAt(Vector2 screenPosition)
        {
            float scale = HudLayout.DpToPixels;
            Rect frame = GameFrame.Pixels;

            foreach (HudButtonSpec spec in HudLayout.Buttons)
            {
                Vector2 centre = frame.min + Vector2.Scale(spec.Anchor, frame.size) + spec.CentreDp * scale;
                Vector2 away = screenPosition - centre;
                float half = spec.DiameterDp * scale / 2f;
                bool inside = spec.Square
                    ? Mathf.Abs(away.x) <= half && Mathf.Abs(away.y) <= half
                    : away.magnitude <= half;

                if (inside)
                {
                    return spec.Button;
                }
            }

            return HudButton.None;
        }

        /// <summary>Where the fixed joystick rests on screen, in pixels.</summary>
        public static Vector2 JoystickRestScreen => GameFrame.Pixels.min + HudLayout.JoystickCentreDp * HudLayout.DpToPixels;

        public void Flash(HudButton button)
        {
            flashUntil[button] = Time.unscaledTime + FlashSeconds;
        }

        /// <summary>Sets what a button says and whether it looks unavailable.</summary>
        public void SetButton(HudButton button, string label, bool dimmed)
        {
            Text text = buttonLabels[button];

            if (text.text != label)
            {
                text.text = label;
            }

            text.color = dimmed ? DimmedLabel : Color.white;

            if (dimmed)
            {
                dimmedButtons.Add(button);
            }
            else
            {
                dimmedButtons.Remove(button);
            }
        }

        /// <summary>
        /// The joystick always shows at its fixed place: faint at rest, clear while held, with the knob
        /// pushed by the finger.
        /// </summary>
        public void ShowJoystick(bool active, Vector2 originScreen, Vector2 knobOffsetScreen)
        {
            joystickHeld = active;
            joystickBase.gameObject.SetActive(true);
            joystickKnob.gameObject.SetActive(true);
            joystickBase.GetComponent<Image>().color = new Color(1f, 1f, 1f, active ? JoystickHeldAlpha : JoystickRestAlpha);
            joystickKnob.GetComponent<Image>().color = new Color(1f, 1f, 1f, active ? 1f : JoystickRestAlpha + 0.2f);

            float scale = HudLayout.DpToPixels;
            Vector2 inFrame = (active ? originScreen : JoystickRestScreen) - GameFrame.Pixels.min;
            joystickBase.anchoredPosition = inFrame / scale;
            joystickKnob.anchoredPosition = (inFrame + (active ? knobOffsetScreen : Vector2.zero)) / scale;
        }

        public void ShowResources(float health, float energy, float stamina)
        {
            SetFill(healthFill, BarWidthDp, health);
            SetFill(energyFill, BarWidthDp, energy);
            SetFill(staminaFill, StaminaBarWidthDp, stamina);

            foreach (KeyValuePair<HudButton, Image> pair in buttonImages)
            {
                bool flashing = flashUntil.TryGetValue(pair.Key, out float until) && Time.unscaledTime < until;
                pair.Value.color = dimmedButtons.Contains(pair.Key) ? Dimmed : flashing ? Pressed : Color.white;

                if (buttonIcons.TryGetValue(pair.Key, out Image icon))
                {
                    icon.color = dimmedButtons.Contains(pair.Key) ? Dimmed : Color.white;
                }
            }
        }

        private static void SetFill(RectTransform fill, float widthDp, float value)
        {
            fill.sizeDelta = new Vector2((widthDp - 2f * BarInsetDp) * Mathf.Clamp01(value), fill.sizeDelta.y);
        }

        /// <summary>Everything of the HUD hangs from this, stretched over the game frame.</summary>
        private static RectTransform CreateArea(RectTransform parent)
        {
            var area = (RectTransform)new GameObject("Frame", typeof(RectTransform)).transform;
            area.SetParent(parent, false);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = area.offsetMax = Vector2.zero;
            return area;
        }

        private RectTransform CreateCanvas()
        {
            var host = new GameObject("Hud", typeof(Canvas), typeof(CanvasScaler));
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = HudLayout.DpToPixels;

            return (RectTransform)host.transform;
        }

        private RectTransform CreatePortrait()
        {
            var size = new Vector2(PortraitDp, PortraitDp);
            RectTransform frame = CreateImage("Portrait", area, Color.white, disc, size);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0f, 1f);
            frame.anchoredPosition = new Vector2(EdgeDp, -EdgeDp);

            AddPortraitPart(frame, "Character/Bold/hair_back", -0.04f, -0.02f, 0.72f);
            AddPortraitPart(frame, "Character/Bold/head", 0f, -0.04f, 0.66f);
            AddPortraitPart(frame, "Character/Bold/hair_front", 0.09f, 0.12f, 0.86f);
            return frame;
        }

        private static void PinTopLeft(RectTransform rect, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
        }

        private static void AddPortraitPart(RectTransform frame, string art, float x, float y, float widthShare)
        {
            Sprite sprite = DaKiem.Client.Content.ContentAssets.Load<Sprite>(art);
            float width = PortraitDp * widthShare;
            var size = new Vector2(width, width * sprite.rect.height / sprite.rect.width);
            RectTransform part = CreateImage(art, frame, Color.white, sprite, size);
            part.anchoredPosition = new Vector2(x, y) * PortraitDp;
        }

        private RectTransform CreateBar(float left, float top, float widthDp, float heightDp, Color color)
        {
            RectTransform back = CreateFrame("BarBack", area, Color.white, barFrame, new Vector2(widthDp, heightDp));
            back.anchorMin = back.anchorMax = back.pivot = new Vector2(0f, 1f);
            back.anchoredPosition = new Vector2(left, top);
            return CreateBarFill(back, widthDp, heightDp, color);
        }

        private RectTransform CreateBossBar()
        {
            RectTransform back = CreateFrame("BossBarBack", area, Color.white, barFrame, new Vector2(BossBarWidthDp, BossBarHeightDp));
            back.anchorMin = back.anchorMax = back.pivot = new Vector2(0.5f, 1f);
            back.anchoredPosition = new Vector2(0f, -BossBarTopDp);
            return CreateBarFill(back, BossBarWidthDp, BossBarHeightDp, BossHealthColor);
        }

        private RectTransform CreateBarFill(RectTransform back, float widthDp, float heightDp, Color color)
        {
            var size = new Vector2(widthDp - 2f * BarInsetDp, heightDp - 2f * BarInsetDp);
            RectTransform fill = CreateFrame("BarFill", back, color, barFill, size);
            fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0f, 0.5f);
            fill.anchoredPosition = new Vector2(BarInsetDp, 0f);
            return fill;
        }

        private static RectTransform CreateFrame(string name, Transform parent, Color color, Sprite sprite, Vector2 size)
        {
            RectTransform rect = CreateImage(name, parent, color, sprite, size);
            rect.GetComponent<Image>().type = Image.Type.Sliced;
            return rect;
        }

        private static Text CreateLabel(string name, RectTransform parent, Vector2 size, Vector2 position, int fontSize)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)label.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var text = label.GetComponent<Text>();
            Accounts.UiKit.Apply(text, FontStyle.Bold);
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            label.AddComponent<Outline>().effectColor = LabelOutline;
            return text;
        }

        private void CreateButton(HudButtonSpec spec)
        {
            Sprite sprite = spec.Square ? skillSquare : spec.Button == HudButton.Attack ? attackDisc : disc;
            var size = new Vector2(spec.DiameterDp, spec.DiameterDp);
            RectTransform button = CreateImage(spec.Label, area, Color.white, sprite, size);
            button.anchorMin = button.anchorMax = spec.Anchor;
            button.anchoredPosition = spec.CentreDp;
            buttonImages[spec.Button] = button.GetComponent<Image>();

            if (spec.Square)
            {
                button.GetComponent<Image>().type = Image.Type.Sliced;

                // A bright frame around the square marks the skill chosen to fight with on its own.
                markFrame ??= DrawnShapes.Frame(new Color(0f, 0f, 0f, 0f), true);
                RectTransform mark = CreateFrame("Auto", button, new Color(1f, 0.95f, 0.55f, 1f), markFrame, size + new Vector2(8f, 8f));
                mark.GetComponent<Image>().enabled = false;
                autoMarks[spec.Button] = mark.GetComponent<Image>();
            }

            // The approved icon sits under the cooldown sweep; without one the button keeps its word.
            string iconName = IconName(spec.Button);
            Sprite icon = iconName.Length > 0 ? Accounts.UiKit.Art("Skills/" + iconName) : null;

            if (icon != null)
            {
                Image picture = CreateImage("Icon", button, Color.white, icon, size * IconShare).GetComponent<Image>();
                picture.preserveAspect = true;
                buttonIcons[spec.Button] = picture;
            }

            Image sweep = CreateImage("Cooldown", button, CooldownSweep, spec.Square ? barFill : plainDisc, size).GetComponent<Image>();
            sweep.type = Image.Type.Filled;
            sweep.fillMethod = Image.FillMethod.Radial360;
            sweep.fillOrigin = (int)Image.Origin360.Top;
            sweep.fillClockwise = false;
            sweep.fillAmount = 0f;
            cooldownSweeps[spec.Button] = sweep;

            Text text = CreateLabel("Label", button, size, Vector2.zero, LabelSizeDp);
            text.text = icon != null ? string.Empty : spec.Label;
            buttonLabels[spec.Button] = text;

            if (spec.Button == HudButton.Potion)
            {
                // How many potions are left, small in the lower right so the bottle stays visible.
                Text count = CreateLabel("Count", button, size * 0.42f, new Vector2(0.3f, -0.3f) * spec.DiameterDp, CountSizeDp);
                count.alignment = TextAnchor.LowerRight;
                buttonCounts[spec.Button] = count;
            }
        }

        /// <summary>The file under Resources/UI/Skills drawn on a button.</summary>
        private static string IconName(HudButton button)
        {
            switch (button)
            {
                case HudButton.Attack: return "attack";
                case HudButton.Dash: return "dash";
                case HudButton.Ranged: return "ranged";
                case HudButton.SkillA: return "skill_a";
                case HudButton.SkillB: return "skill_b";
                case HudButton.Heal: return "heal";
                case HudButton.Ultimate: return "ultimate";
                case HudButton.Potion: return "potion";
                default: return string.Empty;
            }
        }

        /// <summary>Whether the button shows an icon, so its word is only needed for the cooldown seconds.</summary>
        public bool HasIcon(HudButton button) => buttonIcons.ContainsKey(button);

        /// <summary>The small number in a button's corner, such as the potions left.</summary>
        public void SetCount(HudButton button, string count)
        {
            if (buttonCounts.TryGetValue(button, out Text text) && text.text != count)
            {
                text.text = count;
            }
        }

        private RectTransform CreateDiscObject(string name, Sprite sprite, float diameterDp, Color color)
        {
            RectTransform rect = CreateImage(name, area, color, sprite, new Vector2(diameterDp, diameterDp));
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            return rect;
        }

        private static RectTransform CreateImage(string name, Transform parent, Color color, Sprite sprite, Vector2 size)
        {
            var host = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;

            var image = host.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }
    }
}
