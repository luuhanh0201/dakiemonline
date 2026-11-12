using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace DaKiem.Client.Accounts
{
    /// <summary>
    /// Builds the account screens from the approved art in Resources/UI (docs/giao-dien-goc). Every
    /// position is in the pixels of the 1672 x 941 mock-ups, measured from their top-left corner, so a
    /// screen can be checked against its mock-up by eye. The canvas scales that frame to the screen
    /// height; on wider screens the frame stays centred and the background reaches the edges.
    /// </summary>
    internal static class UiKit
    {
        public const float StageWidth = 1672f;
        public const float StageHeight = 941f;

        public static readonly Color Navy = new Color32(0x14, 0x1B, 0x3A, 0xFF);
        public static readonly Color Gold = new Color32(0xF2, 0xC7, 0x6E, 0xFF);
        public static readonly Color Blue = new Color32(0x3D, 0x8B, 0xF2, 0xFF);
        public static readonly Color Red = new Color32(0xE5, 0x48, 0x4D, 0xFF);
        public static readonly Color Green = new Color32(0xAD, 0xF2, 0xCF, 0xFF);
        public static readonly Color Light = new Color32(0xDC, 0xE6, 0xFF, 0xFF);
        public static readonly Color Hint = new Color(0.08f, 0.11f, 0.23f, 0.76f);
        public static readonly Color ErrorText = new Color32(0xFF, 0x6B, 0x6B, 0xFF);

        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static Font font;
        private static Font boldFont;

        /// <summary>Be Vietnam Pro (SIL OFL, docs/ASSETS.md); the web build has no system font to fall back on.</summary>
        public static Font Font => font != null ? font : font = Resources.Load<Font>("Fonts/BeVietnamPro-Regular") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Font BoldFont => boldFont != null ? boldFont : boldFont = Resources.Load<Font>("Fonts/BeVietnamPro-Bold") ?? Font;

        /// <summary>A sprite from Resources/UI, loaded once.</summary>
        public static Sprite Art(string name)
        {
            if (!Sprites.TryGetValue(name, out Sprite sprite))
            {
                sprite = Resources.Load<Sprite>("UI/" + name);
                Sprites[name] = sprite;

                if (sprite == null)
                {
                    Debug.LogWarning($"Missing UI art: Resources/UI/{name}");
                }
            }

            return sprite;
        }

        /// <summary>Sets the face for a style: bold comes from the bold file, so it is not Unity's thickened regular.</summary>
        public static void Apply(Text text, FontStyle style)
        {
            bool bold = style == FontStyle.Bold && BoldFont != Font;
            text.font = bold ? BoldFont : Font;
            text.fontStyle = bold ? FontStyle.Normal : style;
        }

        /// <summary>A canvas sized to the mock-up frame, above the HUD, with the event system fields and buttons need.</summary>
        public static RectTransform CreateCanvas(string name, int order)
        {
            if (EventSystem.current == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(events);
            }

            var host = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = host.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(StageWidth, StageHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            // Phones held sideways are wider than 16:9, so height decides; a 4:3 tablet would cut the
            // frame's sides off that way, so narrower screens are fitted by width instead.
            scaler.matchWidthOrHeight = (float)Screen.width / Screen.height >= StageWidth / StageHeight ? 1f : 0f;
            return (RectTransform)host.transform;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var host = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)host.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static void Fill(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>The mock-up frame, centred on the screen. Children are placed with <see cref="Place"/>.</summary>
        public static RectTransform Stage(Transform parent)
        {
            RectTransform stage = Rect("Stage", parent);
            stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 0.5f);
            stage.sizeDelta = new Vector2(StageWidth, StageHeight);
            return stage;
        }

        // The navy area of the "panel" art when it is drawn with pixelsPerUnitMultiplier 1.75, measured
        // from its pixels: the side wood, the roof beam and the bottom wood, plus the gold bottom corners.
        public const float FrameSide = 55f;
        public const float FrameTop = 106f;
        public const float FrameBottom = 33f;
        public const float FrameCornerWidth = 92f;
        public const float FrameCornerHeight = 62f;

        /// <summary>The part of a frame placed at a mock-up rectangle (top-left origin, y down) where content belongs.</summary>
        public static UnityEngine.Rect FrameInner(UnityEngine.Rect frame)
        {
            return UnityEngine.Rect.MinMaxRect(frame.xMin + FrameSide, frame.yMin + FrameTop, frame.xMax - FrameSide, frame.yMax - FrameBottom);
        }

        /// <summary>Puts a child of the stage at a mock-up rectangle given by its top-left corner and size.</summary>
        public static T Place<T>(T part, float left, float top, float width, float height)
            where T : Component
        {
            var rect = (RectTransform)part.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left + width / 2f, -(top + height / 2f));
            return part;
        }

        public static Image Image(Transform parent, string art, bool sliced = false, string name = null)
        {
            RectTransform rect = Rect(name ?? art, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Art(art);
            image.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = !sliced;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A dark sheet over the whole screen that also stops touches reaching the game.</summary>
        public static Image Shade(Transform parent, float alpha)
        {
            RectTransform shade = Rect("Shade", parent);
            Fill(shade);
            var image = shade.gameObject.AddComponent<Image>();
            image.color = new Color(Navy.r, Navy.g, Navy.b, alpha);
            return image;
        }

        /// <summary>Text in the mock-ups' style: bold, with a dark outline and a small drop below.</summary>
        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, bool outlined = true, FontStyle style = FontStyle.Bold)
        {
            RectTransform rect = Rect("Label", parent);
            var label = rect.gameObject.AddComponent<Text>();
            Apply(label, style);
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.text = text;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.supportRichText = true;

            if (outlined)
            {
                var outline = rect.gameObject.AddComponent<Outline>();
                outline.effectColor = Navy;
                outline.effectDistance = new Vector2(1.3f, -1.3f);
                var shadow = rect.gameObject.AddComponent<Shadow>();
                shadow.effectColor = Navy;
                shadow.effectDistance = new Vector2(0f, -2f);
            }

            return label;
        }

        public enum ButtonKind
        {
            /// <summary>Gold: the one main action of a screen.</summary>
            Gold,

            /// <summary>Blue: other actions.</summary>
            Blue,

            /// <summary>Small blue, like "Gửi mã".</summary>
            BlueSmall,

            /// <summary>Underlined text, like "Quên mật khẩu?".</summary>
            Link,
        }

        public static Button Button(Transform parent, string text, ButtonKind kind, Action clicked, int size = 0)
        {
            RectTransform rect = Rect(text, parent);
            var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>();
            rect.gameObject.AddComponent<AccountButtonMotion>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => clicked());

            if (kind == ButtonKind.Link)
            {
                image.color = new Color(0f, 0f, 0f, 0f);
                Text link = Label(rect, $"<b>{text}</b>", size > 0 ? size : 30, Color.white);
                Fill((RectTransform)link.transform);
                FitOneLine(link);
                var underline = Rect("Underline", link.transform).gameObject.AddComponent<Image>();
                var line = (RectTransform)underline.transform;
                line.anchorMin = new Vector2(0.06f, 0f);
                line.anchorMax = new Vector2(0.94f, 0f);
                line.sizeDelta = new Vector2(0f, 3f);
                line.anchoredPosition = new Vector2(0f, 6f);
                underline.color = Color.white;
                underline.raycastTarget = false;
                return button;
            }

            string art = kind == ButtonKind.Gold ? "btn_gold" : kind == ButtonKind.Blue ? "btn_blue" : "btn_blue_small";
            image.sprite = Art(art);
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = kind == ButtonKind.BlueSmall ? 2.2f : 2.6f;
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                pressedSprite = Art(kind == ButtonKind.Gold ? "btn_gold_pressed" : "btn_blue_pressed") ?? image.sprite,
                selectedSprite = image.sprite,
                highlightedSprite = image.sprite,
                disabledSprite = Art(kind == ButtonKind.Gold ? "btn_gold_disabled" : "btn_blue_disabled") ?? image.sprite,
            };

            Text label = Label(rect, text, size > 0 ? size : kind == ButtonKind.Gold ? 48 : 36, Color.white);
            Fill((RectTransform)label.transform, 4f);
            ((RectTransform)label.transform).offsetMin = new Vector2(kind == ButtonKind.BlueSmall ? 14f : 24f, 4f);
            ((RectTransform)label.transform).offsetMax = new Vector2(kind == ButtonKind.BlueSmall ? -14f : -24f, -4f);
            FitOneLine(label);
            return button;
        }

        /// <summary>Keeps a label on one line, shrinking it when the words are longer than its box.</summary>
        public static void FitOneLine(Text label, int smallest = 16)
        {
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.resizeTextForBestFit = false;
            OneLine fit = label.gameObject.GetComponent<OneLine>() ?? label.gameObject.AddComponent<OneLine>();
            fit.Largest = label.fontSize;
            fit.Smallest = smallest;
        }

        /// <summary>
        /// Unity's best fit wraps a long label onto a second line when the box is tall enough; this
        /// instead lowers the size until the words fit across, whenever the text or the box changes.
        /// </summary>
        [RequireComponent(typeof(Text))]
        internal sealed class OneLine : MonoBehaviour
        {
            public int Largest;
            public int Smallest;
            private string fitted;
            private float width;

            private void LateUpdate()
            {
                var label = GetComponent<Text>();
                float available = ((RectTransform)transform).rect.width;

                if (label.text == fitted && Mathf.Approximately(available, width))
                {
                    return;
                }

                fitted = label.text;
                width = available;
                label.fontSize = Largest;

                while (label.fontSize > Smallest && label.preferredWidth > available)
                {
                    label.fontSize--;
                }
            }
        }

        public static void SetText(Button button, string text)
        {
            button.GetComponentInChildren<Text>().text = text;
        }

        /// <summary>
        /// A light input box as in the mock-ups: an icon on the left behind a divider, and for a password
        /// an eye on the right that shows what was typed. Focus and error frames are swapped in.
        /// </summary>
        public static InputField Field(Transform parent, string placeholder, InputField.ContentType type, string icon, int characterLimit = 0, int size = 36)
        {
            RectTransform rect = Rect(placeholder, parent);
            var background = rect.gameObject.AddComponent<Image>();
            background.sprite = Art("field");
            background.type = UnityEngine.UI.Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 2.4f;
            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;
            field.transition = Selectable.Transition.SpriteSwap;
            field.spriteState = new SpriteState { selectedSprite = Art("field_focus"), highlightedSprite = Art("field"), pressedSprite = Art("field_focus") };
            bool password = type == InputField.ContentType.Password;
            float left = 24f;

            if (icon != null)
            {
                Image glyph = Image(rect, icon);
                var glyphRect = (RectTransform)glyph.transform;
                glyphRect.anchorMin = glyphRect.anchorMax = new Vector2(0f, 0.5f);
                glyphRect.sizeDelta = new Vector2(40f, 40f);
                glyphRect.anchoredPosition = new Vector2(50f, 0f);
                Divider(rect, 92f, true);
                left = 108f;
            }

            float right = password ? 100f : 24f;

            Text text = Label(rect, string.Empty, size, Navy, TextAnchor.MiddleLeft, outlined: false, style: FontStyle.Normal);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var textRect = (RectTransform)text.transform;
            Fill(textRect);
            textRect.offsetMin = new Vector2(left, 4f);
            textRect.offsetMax = new Vector2(-right, -4f);

            Text hint = Label(rect, placeholder, size, Hint, TextAnchor.MiddleLeft, outlined: false, style: FontStyle.Normal);
            var hintRect = (RectTransform)hint.transform;
            Fill(hintRect);
            hintRect.offsetMin = textRect.offsetMin;
            hintRect.offsetMax = textRect.offsetMax;
            FitOneLine(hint, size - 2);

            field.textComponent = text;
            field.placeholder = hint;
            field.contentType = type;
            field.characterLimit = characterLimit;
            field.caretColor = Navy;
            field.customCaretColor = true;
            field.caretWidth = 3;
            field.selectionColor = new Color(Blue.r, Blue.g, Blue.b, 0.45f);
            field.shouldHideMobileInput = false;

            if (icon != null)
            {
                Text caption = Label(rect, placeholder, 22, Navy, TextAnchor.UpperLeft, outlined: false, style: FontStyle.Normal);
                caption.rectTransform.anchorMin = new Vector2(0f, 1f);
                caption.rectTransform.anchorMax = Vector2.one;
                caption.rectTransform.pivot = new Vector2(0.5f, 1f);
                caption.rectTransform.offsetMin = new Vector2(left, -29f);
                caption.rectTransform.offsetMax = new Vector2(-right, -4f);
                FitOneLine(caption, 20);
                var persistent = rect.gameObject.AddComponent<AccountFieldCaption>();
                persistent.Field = field; persistent.Caption = caption;
                field.onValueChanged.AddListener(persistent.Refresh);
                persistent.Refresh(field.text);
            }
            field.onValueChanged.AddListener(_ => MarkError(field, false));

            if (password)
            {
                AddEye(rect, field);
            }

            return field;
        }

        private static void Divider(RectTransform box, float x, bool fromLeft)
        {
            var line = Rect("Divider", box).gameObject.AddComponent<Image>();
            var lineRect = (RectTransform)line.transform;
            lineRect.anchorMin = new Vector2(fromLeft ? 0f : 1f, 0.22f);
            lineRect.anchorMax = new Vector2(fromLeft ? 0f : 1f, 0.78f);
            lineRect.sizeDelta = new Vector2(3f, 0f);
            lineRect.anchoredPosition = new Vector2(fromLeft ? x : -x, 0f);
            line.color = Navy;
            line.raycastTarget = false;
        }

        private static void AddEye(RectTransform box, InputField field)
        {
            Divider(box, 92f, false);
            RectTransform host = Rect("Eye", box);
            host.anchorMin = host.anchorMax = new Vector2(1f, 0.5f);
            host.sizeDelta = new Vector2(92f, 84f);
            host.anchoredPosition = new Vector2(-46f, 0f);
            var hit = host.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            Image eye = Image(host, "eye_open");
            Fill((RectTransform)eye.transform, 8f);
            var button = host.gameObject.AddComponent<Button>();
            host.gameObject.AddComponent<AccountButtonMotion>();
            button.targetGraphic = hit;
            button.onClick.AddListener(() =>
            {
                bool show = field.contentType == InputField.ContentType.Password;
                field.contentType = show ? InputField.ContentType.Standard : InputField.ContentType.Password;
                eye.sprite = Art(show ? "eye_closed" : "eye_open");
                field.ForceLabelUpdate();
            });
        }

        /// <summary>Swaps in the red frame for the box the last refusal was about, and back.</summary>
        public static void MarkError(InputField field, bool wrong)
        {
            if (field != null)
            {
                ((Image)field.targetGraphic).sprite = Art(wrong ? "field_error" : "field");
                SpriteState state = field.spriteState;
                state.selectedSprite = state.pressedSprite = Art(wrong ? "field_error" : "field_focus");
                state.highlightedSprite = Art(wrong ? "field_error" : "field");
                field.spriteState = state;
            }
        }

        /// <summary>A tick box with its label; tapping either toggles it.</summary>
        public static Toggle Check(Transform parent, string text, bool on, int size = 28)
        {
            RectTransform rect = Rect(text, parent);
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = hit;

            Image box = Image(rect, "check_off");
            var boxRect = (RectTransform)box.transform;
            boxRect.anchorMin = boxRect.anchorMax = new Vector2(0f, 0.5f);
            boxRect.sizeDelta = new Vector2(54f, 54f);
            boxRect.anchoredPosition = new Vector2(22f, 0f);
            Image tick = Image(boxRect, "check_on");
            Fill((RectTransform)tick.transform);
            toggle.graphic = tick;
            toggle.isOn = on;

            Text label = Label(rect, text, size, Color.white, TextAnchor.MiddleLeft);
            var labelRect = (RectTransform)label.transform;
            Fill(labelRect);
            labelRect.offsetMin = new Vector2(60f, 0f);
            return toggle;
        }

        /// <summary>The title plaque under the frame's beam, as on the register and server mock-ups.</summary>
        public static Text Title(Transform stage, string text, float left, float top, float width, float height)
        {
            Image plaque = Place(Image(stage, "title", sliced: true), left, top, width, height);
            plaque.pixelsPerUnitMultiplier = 2.2f;
            Text label = Label(plaque.transform, text, (int)(height * 0.49f), Color.white);
            Fill((RectTransform)label.transform, 8f);
            FitOneLine(label, 30);
            return label;
        }

        /// <summary>The round red X that closes a dialog.</summary>
        public static Button Close(Transform stage, float left, float top, float size, Action clicked)
        {
            Image image = Place(Image(stage, "close"), left, top, size, size);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            image.gameObject.AddComponent<AccountButtonMotion>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => clicked());
            return button;
        }

        /// <summary>A round icon button from the icon sheet, such as the chain for linking an account.</summary>
        public static Button Icon(Transform parent, string art, Action clicked)
        {
            Image image = Image(parent, art);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            image.gameObject.AddComponent<AccountButtonMotion>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => clicked());
            return button;
        }
    }
}
