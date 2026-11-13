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

        public HudView()
        {
            disc = DrawnShapes.Disc(ButtonFill, ButtonLit);
            attackDisc = DrawnShapes.Disc(AttackFill, AttackLit);
            plainDisc = DrawnShapes.PlainDisc();
            barFrame = DrawnShapes.Frame(Panel, false);
            barFill = DrawnShapes.BarFill();
            panelFrame = DrawnShapes.Frame(EndPanelColor, true);
            actionFrame = DrawnShapes.Frame(AccentColor, true);

            root = CreateCanvas();
            canvas = root.GetComponent<Canvas>();
            area = CreateArea(root);
            CreatePortrait();
            float barLeft = EdgeDp + PortraitDp + BarGapDp;
            healthFill = CreateBar(barLeft, -EdgeDp, BarWidthDp, BarHeightDp, HealthColor);
            energyFill = CreateBar(barLeft, -EdgeDp - BarHeightDp - BarGapDp, BarWidthDp, EnergyBarHeightDp, EnergyColor);
            staminaFill = CreateBar(barLeft, -EdgeDp - BarHeightDp - EnergyBarHeightDp - 2f * BarGapDp, StaminaBarWidthDp, StaminaBarHeightDp, StaminaColor);

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
            var corner = new Vector2(frame.xMax, frame.yMin);

            foreach (HudButtonSpec spec in HudLayout.Buttons)
            {
                Vector2 centre = corner + spec.CentreDp * scale;

                if (Vector2.Distance(screenPosition, centre) <= spec.DiameterDp * scale / 2f)
                {
                    return spec.Button;
                }
            }

            return HudButton.None;
        }

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

        public void ShowJoystick(bool active, Vector2 originScreen, Vector2 knobOffsetScreen)
        {
            joystickBase.gameObject.SetActive(active);
            joystickKnob.gameObject.SetActive(active);

            float scale = HudLayout.DpToPixels;
            Vector2 inFrame = originScreen - GameFrame.Pixels.min;
            joystickBase.anchoredPosition = inFrame / scale;
            joystickKnob.anchoredPosition = (inFrame + knobOffsetScreen) / scale;
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

        private void CreatePortrait()
        {
            var size = new Vector2(PortraitDp, PortraitDp);
            RectTransform frame = CreateImage("Portrait", area, Color.white, disc, size);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0f, 1f);
            frame.anchoredPosition = new Vector2(EdgeDp, -EdgeDp);

            AddPortraitPart(frame, "Character/Bold/hair_back", -0.04f, -0.02f, 0.72f);
            AddPortraitPart(frame, "Character/Bold/head", 0f, -0.04f, 0.66f);
            AddPortraitPart(frame, "Character/Bold/hair_front", 0.09f, 0.12f, 0.86f);
        }

        private static void AddPortraitPart(RectTransform frame, string art, float x, float y, float widthShare)
        {
            Sprite sprite = Resources.Load<Sprite>(art);
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
            back.anchoredPosition = new Vector2(0f, -EdgeDp);
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
            Sprite sprite = spec.Button == HudButton.Attack ? attackDisc : disc;
            var size = new Vector2(spec.DiameterDp, spec.DiameterDp);
            RectTransform button = CreateImage(spec.Label, area, Color.white, sprite, size);
            button.anchorMin = button.anchorMax = new Vector2(1f, 0f);
            button.anchoredPosition = spec.CentreDp;
            buttonImages[spec.Button] = button.GetComponent<Image>();

            Image sweep = CreateImage("Cooldown", button, CooldownSweep, plainDisc, size).GetComponent<Image>();
            sweep.type = Image.Type.Filled;
            sweep.fillMethod = Image.FillMethod.Radial360;
            sweep.fillOrigin = (int)Image.Origin360.Top;
            sweep.fillClockwise = false;
            sweep.fillAmount = 0f;
            cooldownSweeps[spec.Button] = sweep;

            Text text = CreateLabel("Label", button, size, Vector2.zero, LabelSizeDp);
            text.text = spec.Label;
            buttonLabels[spec.Button] = text;
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
