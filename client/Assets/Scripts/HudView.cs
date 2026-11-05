using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client
{
    /// <summary>Draws the bars, the action buttons and the joystick.</summary>
    public sealed class HudView
    {
        private const float FlashSeconds = 0.12f;
        private const int DiscTextureSize = 128;
        private const float BorderShare = 0.04f;
        private const float BarWidthDp = 180f;
        private const float BarHeightDp = 10f;
        private const float StaminaBarWidthDp = 120f;
        private const float StaminaBarHeightDp = 6f;
        private const float EdgeDp = 16f;
        private const float BarGapDp = 6f;
        private const int LabelSizeDp = 13;

        private static readonly Color Moon = new Color32(0xDC, 0xE6, 0xFF, 0xC0);
        private static readonly Color Panel = new Color32(0x14, 0x1B, 0x3A, 0x9E);
        private static readonly Color Pressed = new Color32(0x74, 0x80, 0xC8, 0xD0);
        private static readonly Color Dimmed = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color HealthColor = new Color32(0x46, 0xB3, 0x7E, 0xFF);
        private static readonly Color EnergyColor = new Color32(0x4F, 0x7B, 0xE8, 0xFF);
        private static readonly Color StaminaColor = new Color32(0xF2, 0xC7, 0x6E, 0xFF);
        private static readonly Color AccentColor = new Color32(0x3D, 0x8B, 0xF2, 0xFF);

        private readonly Dictionary<HudButton, Image> buttonImages = new Dictionary<HudButton, Image>();
        private readonly Dictionary<HudButton, float> flashUntil = new Dictionary<HudButton, float>();
        private readonly Dictionary<HudButton, Text> buttonLabels = new Dictionary<HudButton, Text>();
        private readonly HashSet<HudButton> dimmedButtons = new HashSet<HudButton>();
        private readonly RectTransform root;
        private readonly RectTransform healthFill;
        private readonly RectTransform energyFill;
        private readonly RectTransform staminaFill;
        private readonly RectTransform joystickBase;
        private readonly RectTransform joystickKnob;
        private readonly Sprite disc;
        private readonly Sprite attackDisc;
        private readonly Sprite solid;

        public HudView()
        {
            disc = CreateDisc(Moon);
            attackDisc = CreateDisc(AccentColor);
            solid = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), Vector2.one / 2f);

            root = CreateCanvas();
            healthFill = CreateBar(0, BarWidthDp, BarHeightDp, HealthColor);
            energyFill = CreateBar(1, BarWidthDp, BarHeightDp, EnergyColor);
            staminaFill = CreateBar(2, StaminaBarWidthDp, StaminaBarHeightDp, StaminaColor);

            foreach (HudButtonSpec spec in HudLayout.Buttons)
            {
                CreateButton(spec);
            }

            joystickBase = CreateDiscObject("JoystickBase", HudLayout.JoystickDiameterDp, new Color(1f, 1f, 1f, 0.45f));
            joystickKnob = CreateDiscObject("JoystickKnob", HudLayout.JoystickDiameterDp * 0.4f, new Color(1f, 1f, 1f, 0.85f));
            ShowJoystick(false, Vector2.zero, Vector2.zero);
        }

        public HudButton ButtonAt(Vector2 screenPosition)
        {
            float scale = HudLayout.DpToPixels;
            var corner = new Vector2(Screen.width, 0f);

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

            text.color = dimmed ? Dimmed : Color.white;

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
            joystickBase.anchoredPosition = originScreen / scale;
            joystickKnob.anchoredPosition = (originScreen + knobOffsetScreen) / scale;
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
            fill.sizeDelta = new Vector2(widthDp * Mathf.Clamp01(value), fill.sizeDelta.y);
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

        private RectTransform CreateBar(int row, float widthDp, float heightDp, Color color)
        {
            float top = -EdgeDp - row * (BarHeightDp + BarGapDp);
            var size = new Vector2(widthDp, heightDp);

            RectTransform back = CreateImage("BarBack", root, Panel, solid, size);
            back.anchorMin = back.anchorMax = back.pivot = new Vector2(0f, 1f);
            back.anchoredPosition = new Vector2(EdgeDp, top);

            RectTransform fill = CreateImage("BarFill", back, color, solid, size);
            fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0f, 0.5f);
            fill.anchoredPosition = Vector2.zero;
            return fill;
        }

        private void CreateButton(HudButtonSpec spec)
        {
            Sprite sprite = spec.Button == HudButton.Attack ? attackDisc : disc;
            var size = new Vector2(spec.DiameterDp, spec.DiameterDp);
            RectTransform button = CreateImage(spec.Label, root, Color.white, sprite, size);
            button.anchorMin = button.anchorMax = new Vector2(1f, 0f);
            button.anchoredPosition = spec.CentreDp;
            buttonImages[spec.Button] = button.GetComponent<Image>();

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRect = (RectTransform)label.transform;
            labelRect.SetParent(button, false);
            labelRect.sizeDelta = size;

            var text = label.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = LabelSizeDp;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = spec.Label;
            text.raycastTarget = false;
            buttonLabels[spec.Button] = text;
        }

        private RectTransform CreateDiscObject(string name, float diameterDp, Color color)
        {
            RectTransform rect = CreateImage(name, root, color, disc, new Vector2(diameterDp, diameterDp));
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

        private static Sprite CreateDisc(Color border)
        {
            var texture = new Texture2D(DiscTextureSize, DiscTextureSize, TextureFormat.RGBA32, false);
            float centre = (DiscTextureSize - 1) / 2f;
            float radius = DiscTextureSize / 2f - 1f;
            float inner = radius * (1f - BorderShare);

            for (int y = 0; y < DiscTextureSize; y++)
            {
                for (int x = 0; x < DiscTextureSize; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centre, centre));
                    Color color = distance <= inner ? Panel : border;
                    color.a *= Mathf.Clamp01(radius - distance + 0.5f);
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            var rect = new Rect(0f, 0f, DiscTextureSize, DiscTextureSize);
            return Sprite.Create(texture, rect, Vector2.one / 2f);
        }
    }
}
