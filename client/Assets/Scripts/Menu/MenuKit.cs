using System;
using System.Collections.Generic;
using DaKiem.Client.Accounts;
using Game.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client
{
    /// <summary>
    /// The menu screens' parts, drawn by ChatGPT and approved 15/11/2026 (docs/menu/, docs/ASSETS.md): the
    /// big frame with its crest and title ribbon, the round close button, tabs, item slots, menu tiles and
    /// the long gold and blue buttons. Stretchable parts are cut into nine pieces when first used.
    /// </summary>
    public static class MenuKit
    {
        private const string Folder = "UI/Menu/";

        // How thick the big frame draws: the art's border pixels are divided by this.
        private const float FrameScale = 0.75f;

        // The stretchable pieces and their borders in art pixels: left, bottom, right, top.
        private static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            ["panel_big"] = new Vector4(95f, 90f, 95f, 118f),
            ["title_ribbon"] = new Vector4(90f, 50f, 90f, 50f),
            ["tab"] = new Vector4(45f, 45f, 45f, 45f),
            ["tab_selected"] = new Vector4(45f, 45f, 45f, 45f),
            ["slot"] = new Vector4(45f, 45f, 45f, 45f),
            ["slot_selected"] = new Vector4(45f, 45f, 45f, 45f),
            ["tile"] = new Vector4(55f, 55f, 55f, 55f),
            ["tile_selected"] = new Vector4(55f, 55f, 55f, 55f),
            ["tile_locked"] = new Vector4(55f, 55f, 55f, 55f),
            ["button_gold"] = new Vector4(70f, 45f, 70f, 45f),
            ["button_blue"] = new Vector4(70f, 45f, 70f, 45f),
            ["subpanel"] = new Vector4(35f, 35f, 35f, 35f),
            ["panel_rect"] = new Vector4(95f, 90f, 95f, 2f),
            ["frame_top"] = new Vector4(95f, 0f, 95f, 0f),
            ["panel_straight"] = new Vector4(96f, 90f, 96f, 90f),
            ["bag_cell"] = new Vector4(40f, 40f, 40f, 40f),
            ["bag_cell_selected"] = new Vector4(42f, 42f, 42f, 42f),
            ["tab_vertical"] = new Vector4(40f, 40f, 40f, 40f),
            ["tab_vertical_selected"] = new Vector4(40f, 40f, 40f, 40f),
            ["dropdown"] = new Vector4(30f, 40f, 80f, 40f),
            ["tag_pill"] = new Vector4(56f, 50f, 56f, 50f),
        };

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>The content area inside the big frame placed by <see cref="Frame"/>, as offsets from its edges.</summary>
        public const float InnerSide = 70f, InnerTop = 190f, InnerBottom = 62f;

        public static Sprite Art(string name)
        {
            // A sprite made here is destroyed when a scene is unloaded, as the review tool does; make it again.
            if (Cache.TryGetValue(name, out Sprite cached) && cached != null)
            {
                return cached;
            }

            bool framePiece = name == "panel_rect" || name == "frame_top";
            Sprite sprite = DaKiem.Client.Content.ContentAssets.Load<Sprite>(Folder + (framePiece ? "panel_big" : name));

            if (sprite != null && Borders.TryGetValue(name, out Vector4 border))
            {
                Rect area = sprite.rect;
                if (name == "panel_rect") area.height -= 150f;
                if (name == "frame_top") area.height = 90f;
                sprite = Sprite.Create(sprite.texture, area, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            }

            Cache[name] = sprite;
            return sprite;
        }

        public static Image Image(Transform parent, string art, float left, float top, float width, float height, float scale = 1f)
        {
            bool sliced = Borders.ContainsKey(art);
            Image image = UiKit.Rect(art, parent).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = Art(art);
            image.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.preserveAspect = !sliced;
            image.pixelsPerUnitMultiplier = scale;
            return UiKit.Place(image, left, top, width, height);
        }

        /// <summary>The big frame with its crest, its title on the ribbon and the close button in its corner.</summary>
        public static void Frame(Transform stage, string title, float left, float top, float width, float height, Action close)
        {
            Image(stage, "panel_big", left, top, width, height, FrameScale);
            float crest = 92f / FrameScale;
            Image(stage, "panel_crest", left + (width - crest) / 2f, top, crest, crest);

            Image ribbon = Image(stage, "title_ribbon", left + (width - 520f) / 2f, top + 92f, 520f, 84f, 1.3f);
            Text label = UiKit.Place(UiKit.Label(ribbon.transform, title, 40, Color.white), 0f, 0f, 520f, 84f);
            ((RectTransform)label.transform).anchorMin = Vector2.zero;
            ((RectTransform)label.transform).anchorMax = Vector2.one;
            ((RectTransform)label.transform).offsetMin = new Vector2(60f, 4f);
            ((RectTransform)label.transform).offsetMax = new Vector2(-60f, -4f);
            UiKit.FitOneLine(label, 24);

            Button closing = Clickable(Image(stage, "close_round", left + width - 132f, top + 84f, 86f, 86f));
            closing.onClick.AddListener(() => close());
        }

        /// <summary>A tab down the left side: an icon and a word, gold while chosen.</summary>
        public static Button Tab(Transform stage, string icon, string text, float left, float top, float width, float height, bool chosen, Action pressed)
        {
            Image back = Image(stage, chosen ? "tab_selected" : "tab", left, top, width, height, 1.6f);
            Image picture = Image(back.transform, icon, 0f, 0f, height * 0.62f, height * 0.62f);
            Anchor(picture.rectTransform, new Vector2(0f, 0.5f), new Vector2(14f + height * 0.31f, 0f));
            Text label = UiKit.Label(back.transform, text, 26, chosen ? UiKit.Navy : Color.white, TextAnchor.MiddleLeft, outlined: !chosen);
            Stretch(label.rectTransform, 22f + height * 0.62f, 8f);
            UiKit.FitOneLine(label, 16);
            Button button = Clickable(back);
            button.onClick.AddListener(() => pressed());
            return button;
        }

        /// <summary>The approved straight frame, with its title and close button over the top edge.</summary>
        public static void RectFrame(Transform stage, string title, float left, float top, float width, float height, Action close)
        {
            Image(stage, "panel_straight", left, top, width, height, 1.8f);
            Image ribbon = Image(stage, "title_ribbon", left + (width - 580f) / 2f, top - 20f, 580f, 82f, 1.4f);
            Text label = UiKit.Label(ribbon.transform, title, 40, Color.white);
            UiKit.Fill(label.rectTransform, 50f);
            label.rectTransform.offsetMin = new Vector2(65f, 4f);
            label.rectTransform.offsetMax = new Vector2(-65f, -4f);
            UiKit.FitOneLine(label, 24);
            Clickable(Image(stage, "close_round", left + width - 76f, top - 12f, 82f, 82f)).onClick.AddListener(() => close());
        }

        public static Button VerticalTab(Transform stage, string icon, string text, float left, float top, float width, float height, bool chosen, Action pressed)
        {
            Image back = Image(stage, chosen ? "tab_vertical_selected" : "tab_vertical", left, top, width, height, 1.8f);
            Image(back.transform, icon, (width - 82f) / 2f, 14f, 82f, 82f);
            Text label = UiKit.Place(UiKit.Label(back.transform, text, 29, chosen ? UiKit.Navy : Color.white, outlined: !chosen),
                8f, height - 54f, width - 16f, 44f);
            UiKit.FitOneLine(label, 18);
            Button button = Clickable(back);
            button.onClick.AddListener(() => pressed());
            return button;
        }

        public static Image Surface(Transform parent, string name, float left, float top, float width, float height, Color fill, Color border)
        {
            var image = UiKit.Rect(name, parent).gameObject.AddComponent<MenuSurface>();
            image.color = fill;
            image.Border = border;
            image.raycastTarget = false;
            return UiKit.Place(image, left, top, width, height);
        }

        public static void Figure(Transform stage, CharacterPreview preview, float center, float feet, float bodyHeight)
        {
            float width = bodyHeight * CharacterPreview.Aspect;
            // All eight sprites share a crop: feet stay at 97% from the top when equipment changes.
            float pedestalWidth = bodyHeight * 0.7f, pedestalHeight = bodyHeight * 0.22f;
            Image(stage, "pedestal", center - pedestalWidth / 2f, feet - pedestalHeight * 0.28f, pedestalWidth, pedestalHeight);
            Image figure = UiKit.Place(UiKit.Rect("Figure", stage).gameObject.AddComponent<Image>(), center - width * CharacterPreview.FeetFromLeft, feet - bodyHeight * CharacterPreview.FeetFromTop, width, bodyHeight);
            figure.preserveAspect = true;
            figure.raycastTarget = false;
            preview.Bind(figure);
        }

        public static Sprite SlotShadow(GearSlot slot)
        {
            return Art("empty_" + slot.ToString().ToLowerInvariant());
        }

        /// <summary>A square holding an item's picture; the picture is the returned image's first child.</summary>
        public static (Image Slot, Image Icon) Slot(Transform stage, float left, float top, float size, Action pressed)
        {
            Image slot = Image(stage, "bag_cell", left, top, size, size, 2.2f);
            Image icon = UiKit.Rect("Icon", slot.transform).gameObject.AddComponent<Image>();
            UiKit.Fill(icon.rectTransform, size * 0.13f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
            Clickable(slot).onClick.AddListener(() => pressed?.Invoke());
            return (slot, icon);
        }

        public static void SetSlot((Image Slot, Image Icon) cell, Sprite icon, bool chosen)
        {
            cell.Slot.sprite = Art(chosen ? "bag_cell_selected" : "bag_cell");
            cell.Icon.enabled = icon != null;
            cell.Icon.sprite = icon;
            cell.Icon.color = Color.white;
        }

        public static Button LongButton(Transform stage, string text, bool gold, float left, float top, float width, float height, Action pressed)
        {
            Image back = Image(stage, gold ? "button_gold" : "button_blue", left, top, width, height, 1.5f);
            Text label = UiKit.Label(back.transform, text, 32, gold ? UiKit.Navy : Color.white, outlined: !gold);
            Stretch(label.rectTransform, 40f, 6f);
            label.rectTransform.offsetMax = new Vector2(-40f, -6f);
            UiKit.FitOneLine(label, 18);
            Button button = Clickable(back);
            button.onClick.AddListener(() => pressed());
            return button;
        }

        public static void SetButtonText(Button button, string text)
        {
            button.GetComponentInChildren<Text>().text = text;
        }

        /// <summary>A dark inset box for a group of lines.</summary>
        public static Image Box(Transform stage, float left, float top, float width, float height)
        {
            return Image(stage, "subpanel", left, top, width, height, 1.8f);
        }

        public static Text Line(Transform stage, string text, int size, Color color, float left, float top, float width, float height, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Bold)
        {
            return UiKit.Place(UiKit.Label(stage, text, size, color, anchor, style: style), left, top, width, height);
        }

        public static Button Clickable(Image image)
        {
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = Color.white,
                selectedColor = Color.white,
                pressedColor = new Color(0.82f, 0.86f, 1f, 1f),
                disabledColor = Color.white,
                colorMultiplier = 1f,
                fadeDuration = 0.06f,
            };
            return button;
        }

        public static void Stretch(RectTransform rect, float sideInset, float edgeInset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(sideInset, edgeInset);
            rect.offsetMax = new Vector2(-12f, -edgeInset);
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
        }
    }

    /// <summary>Rounded UI fill and thin border drawn as a mesh, so slot edges remain crisp at any size.</summary>
    internal sealed class MenuSurface : Image
    {
        public override Texture mainTexture => Texture2D.whiteTexture;
        public Color Border = Color.white;
        public float Thickness = 2.5f;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect box = GetPixelAdjustedRect();
            const int Steps = 6;
            const int Count = 4 * (Steps + 1);
            float radius = Mathf.Min(12f, Mathf.Min(box.width, box.height) / 4f);
            Vector2 center = box.center;
            mesh.AddVert(center, color, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 pivot = new Vector2(corner == 0 || corner == 3 ? box.xMax - radius : box.xMin + radius,
                    corner < 2 ? box.yMax - radius : box.yMin + radius);
                for (int step = 0; step <= Steps; step++)
                {
                    float angle = (corner * 90f + step * 90f / Steps) * Mathf.Deg2Rad;
                    Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    mesh.AddVert(pivot + direction * radius, Border, Vector2.zero);
                    mesh.AddVert(pivot + direction * Mathf.Max(0f, radius - Thickness), color, Vector2.zero);
                }
            }
            for (int point = 0; point < Count; point++)
            {
                int a = 1 + point * 2, b = 1 + (point + 1) % Count * 2;
                mesh.AddTriangle(a, b, a + 1);
                mesh.AddTriangle(a + 1, b, b + 1);
                mesh.AddTriangle(0, a + 1, b + 1);
            }
        }
    }
}
