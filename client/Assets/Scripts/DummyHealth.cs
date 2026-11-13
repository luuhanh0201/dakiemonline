using DaKiem.Client.Accounts;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client
{
    /// <summary>
    /// Over a straw dummy: a small red health bar with "hp/max" on it, shown from its first blow until it
    /// stands up again, and a gold "-10" that pops out of its head on every blow, rises and fades
    /// (approved mock-up: docs/map-lang/nguoi-rom-sat-thuong.png, style B). It draws above the night shade.
    /// </summary>
    public sealed class DummyHealth
    {
        // The canvas is laid out in pixels; this many pixels make one world unit.
        private const float PixelsPerUnit = 100f;
        private const float BarWidth = 84f;
        private const float BarHeight = 12f;
        private const float BarBorder = 2f;
        private const float BarGap = 0.12f;
        private const int BarFontSize = 11;
        private const int NumberFontSize = 30;
        private const int Numbers = 4;
        private const float NumberSeconds = 0.85f;
        private const float NumberRise = 0.55f;
        private const float NumberPopSeconds = 0.12f;
        private const float NumberPop = 1.45f;
        private const float NumberFadeSeconds = 0.3f;
        private const float BarFollowPerSecond = 3f;

        private static readonly Color Edge = new Color32(0x1E, 0x14, 0x0F, 0xDC);
        private static readonly Color Empty = new Color32(0x46, 0x28, 0x1E, 0xFF);
        private static readonly Color Full = new Color32(0xE6, 0x3C, 0x32, 0xFF);
        private static readonly Color Gold = new Color32(0xFF, 0xD2, 0x50, 0xFF);
        private static readonly Color Brown = new Color32(0x3C, 0x14, 0x0A, 0xFF);

        private readonly RectTransform bar;
        private readonly Image fill;
        private readonly Text label;
        private readonly (RectTransform Rect, Text Text, Outline Outline)[] numbers = new (RectTransform, Text, Outline)[Numbers];
        private readonly float[] numberAt = new float[Numbers];
        private readonly float[] numberDrift = new float[Numbers];
        private readonly float head;
        private float shown = 1f;
        private int next;

        public DummyHealth(Transform root, float x, float height, int order)
        {
            var holder = new GameObject("Dummy health", typeof(RectTransform));
            holder.transform.SetParent(root, false);
            var canvas = holder.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            var area = (RectTransform)holder.transform;
            area.sizeDelta = new Vector2(400f, 400f);
            // Draw the words at a few times the canvas resolution so they stay sharp when the camera is close.
            holder.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            area.localScale = Vector3.one / PixelsPerUnit;
            area.position = new Vector3(x, 0f, 0f);
            head = height * PixelsPerUnit;

            bar = Box("Bar", area, Edge, BarWidth + BarBorder * 2f, BarHeight + BarBorder * 2f);
            bar.anchoredPosition = new Vector2(0f, head + BarGap * PixelsPerUnit);
            RectTransform back = Box("Empty", bar, Empty, BarWidth, BarHeight);
            fill = Box("Fill", back, Full, BarWidth, BarHeight).GetComponent<Image>();
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchorMin = fill.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            fill.rectTransform.anchoredPosition = Vector2.zero;
            label = Words(bar, BarFontSize, Color.white, BarWidth, BarHeight + 4f);
            bar.gameObject.SetActive(false);

            for (int index = 0; index < Numbers; index++)
            {
                Text text = Words(area, NumberFontSize, Gold, 160f, 60f);
                Outline outline = text.gameObject.AddComponent<Outline>();
                outline.effectColor = Brown;
                outline.effectDistance = new Vector2(2f, -2f);
                Shadow shadow = text.gameObject.AddComponent<Shadow>();
                shadow.effectColor = Brown;
                shadow.effectDistance = new Vector2(-2f, 2f);
                text.gameObject.SetActive(false);
                numbers[index] = (text.rectTransform, text, outline);
                numberAt[index] = -100f;
            }
        }

        /// <summary>A blow landed for this much; the number springs out over the dummy's head.</summary>
        public void Hit(int damage, int side)
        {
            int index = next++ % Numbers;
            numbers[index].Text.text = "-" + damage;
            numbers[index].Text.gameObject.SetActive(true);
            numberAt[index] = Time.time;
            // Each number leans a little off the head, toward the side the blow came from, so quick blows don't stack.
            numberDrift[index] = (side * 0.08f + (index % 2 == 0 ? -0.06f : 0.06f)) * PixelsPerUnit;
        }

        /// <summary>The bar shows while the dummy has been hurt and is up, or for a moment as it goes down.</summary>
        public void Show(int hp, int max, bool barVisible)
        {
            float wanted = max > 0 ? Mathf.Clamp01(hp / (float)max) : 0f;
            shown = barVisible ? Mathf.MoveTowards(shown, wanted, Time.deltaTime * BarFollowPerSecond) : 1f;

            if (bar.gameObject.activeSelf != barVisible)
            {
                bar.gameObject.SetActive(barVisible);
            }

            if (barVisible)
            {
                fill.rectTransform.sizeDelta = new Vector2(BarWidth * shown, BarHeight);
                label.text = hp + "/" + max;
            }

            for (int index = 0; index < Numbers; index++)
            {
                (RectTransform rect, Text text, Outline outline) = numbers[index];
                float since = Time.time - numberAt[index];

                if (!text.gameObject.activeSelf)
                {
                    continue;
                }

                if (since >= NumberSeconds)
                {
                    text.gameObject.SetActive(false);
                    continue;
                }

                float rise = 1f - (1f - since / NumberSeconds) * (1f - since / NumberSeconds);
                float pop = since < NumberPopSeconds ? Mathf.Lerp(NumberPop, 1f, since / NumberPopSeconds) : 1f;
                float alpha = Mathf.Clamp01((NumberSeconds - since) / NumberFadeSeconds);
                float baseHeight = head + (BarGap + 0.32f) * PixelsPerUnit;
                rect.anchoredPosition = new Vector2(numberDrift[index], baseHeight + rise * NumberRise * PixelsPerUnit);
                rect.localScale = Vector3.one * pop;
                text.color = new Color(Gold.r, Gold.g, Gold.b, alpha);
                outline.effectColor = new Color(Brown.r, Brown.g, Brown.b, alpha);
            }
        }

        private static RectTransform Box(string name, Transform parent, Color color, float width, float height)
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            Image image = item.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            var rect = (RectTransform)item.transform;
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static Text Words(Transform parent, int size, Color color, float width, float height)
        {
            var item = new GameObject("Words", typeof(RectTransform));
            item.transform.SetParent(parent, false);
            Text text = item.AddComponent<Text>();
            text.font = UiKit.BoldFont;
            text.fontSize = size;
            text.fontStyle = FontStyle.Normal;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            ((RectTransform)item.transform).sizeDelta = new Vector2(width, height);
            return text;
        }
    }
}
