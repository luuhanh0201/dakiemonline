using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Paints the HUD parts and skill effects in the bold outlined style. Each shape is a signed
    /// distance: negative inside, positive outside, in units of the image height.
    /// </summary>
    internal static class DrawnShapes
    {
        private const int EffectHeight = 128;
        private const int DiscSize = 160;
        private const int FrameSize = 32;
        private const int FrameCorner = 12;
        private const float FramePixelsPerUnit = 300f;

        private const float EffectOutline = 0.03f;
        private const float EffectCore = 0.06f;

        private static readonly Color OutlineColor = new Color32(0x14, 0x1B, 0x3A, 0xFF);
        private static readonly Color WaterColor = new Color32(0x3D, 0x8B, 0xF2, 0xFF);
        private static readonly Color FoamColor = new Color32(0xDC, 0xE6, 0xFF, 0xFF);
        private static readonly Color GoldColor = new Color32(0xF2, 0xC7, 0x6E, 0xFF);

        private delegate float Shape(Vector2 point);

        /// <summary>A blade of water bulging to the right.</summary>
        public static Sprite Crescent()
        {
            return Paint(1, p => Mathf.Max(p.magnitude - 0.46f, 0.47f - (p - new Vector2(-0.11f, 0f)).magnitude));
        }

        /// <summary>A sharp seven-pointed splash for a landed blow.</summary>
        public static Sprite Burst()
        {
            return Paint(1, p =>
            {
                float turn = Mathf.Repeat(Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) * 7f, 1f);
                float point = 1f - Mathf.Abs(turn - 0.5f) * 2f;
                return (p.magnitude - (0.15f + 0.33f * point)) * 0.5f;
            });
        }

        public static Sprite Droplet()
        {
            return Paint(1, p =>
            {
                const float Bottom = -0.14f;
                const float Top = 0.44f;
                float round = (p - new Vector2(0f, Bottom)).magnitude - 0.26f;
                float taper = Mathf.Abs(p.x) - 0.26f * (1f - (p.y - Bottom) / (Top - Bottom));
                return Mathf.Min(round, Mathf.Max(taper, Mathf.Max(p.y - Top, Bottom - p.y)));
            });
        }

        public static Sprite Ring()
        {
            return Paint(1, p => Mathf.Abs(p.magnitude - 0.42f) - 0.04f);
        }

        /// <summary>A long streak of water, four times as wide as it is tall.</summary>
        public static Sprite Streak()
        {
            return Paint(4, p => Mathf.Abs(p.y) - 0.34f * Mathf.Max(0f, 1f - Mathf.Pow(p.x / 1.96f, 2f)));
        }

        /// <summary>A wave rising to the right, twice as wide as it is tall, foaming along its top.</summary>
        public static Sprite Wave()
        {
            const float Width = 2f;
            int height = EffectHeight;
            int width = Mathf.RoundToInt(height * Width);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    var point = new Vector2((column + 0.5f) / height - Width / 2f, (row + 0.5f) / height - 0.5f);
                    float crest = (point.x - 0.5f) / (point.x > 0.5f ? 0.28f : 0.6f);
                    float surface = -0.42f + 0.82f * Mathf.Exp(-crest * crest) + 0.03f * Mathf.Sin(point.x * 13f);
                    float distance = Mathf.Max((point.y - surface) * 0.7f, Mathf.Abs(point.x) - 0.96f);
                    Color color = distance > 0f ? OutlineColor : distance > -0.09f ? FoamColor : WaterColor;

                    color.a = Mathf.Clamp01((EffectOutline - distance) * height + 0.5f)
                        * Mathf.Clamp01((point.y + 0.5f) / 0.3f);
                    texture.SetPixel(column, row, color);
                }
            }

            return Finish(texture, height);
        }

        /// <summary>A round button with a dark outline, a gold ring and a lit upper edge.</summary>
        public static Sprite Disc(Color fill, Color lit)
        {
            var texture = new Texture2D(DiscSize, DiscSize, TextureFormat.RGBA32, false);
            float radius = DiscSize / 2f - 1f;

            for (int y = 0; y < DiscSize; y++)
            {
                for (int x = 0; x < DiscSize; x++)
                {
                    var point = new Vector2(x + 0.5f - DiscSize / 2f, y + 0.5f - DiscSize / 2f);
                    float distance = point.magnitude;
                    float share = distance / radius;
                    bool inLitBand = (point - new Vector2(0f, -radius * 0.16f)).magnitude > radius * 0.8f && point.y > 0f;
                    Color color = share > 0.9f ? OutlineColor : share > 0.83f ? GoldColor : inLitBand ? lit : fill;

                    color.a = Mathf.Clamp01(radius - distance + 0.5f);
                    texture.SetPixel(x, y, color);
                }
            }

            return Finish(texture, DiscSize);
        }

        /// <summary>A plain white circle, for the cooldown sweep.</summary>
        public static Sprite PlainDisc()
        {
            var texture = new Texture2D(DiscSize, DiscSize, TextureFormat.RGBA32, false);
            float radius = DiscSize / 2f - 1f;

            for (int y = 0; y < DiscSize; y++)
            {
                for (int x = 0; x < DiscSize; x++)
                {
                    float distance = new Vector2(x + 0.5f - DiscSize / 2f, y + 0.5f - DiscSize / 2f).magnitude;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 0.5f)));
                }
            }

            return Finish(texture, DiscSize);
        }

        /// <summary>A white rounded box for a bar's fill, tinted by the image.</summary>
        public static Sprite BarFill()
        {
            return Frame(Color.white, false, false);
        }

        /// <summary>A rounded box that stretches, with an outline and optionally a gold line.</summary>
        public static Sprite Frame(Color fill, bool gold)
        {
            return Frame(fill, gold, true);
        }

        private static Sprite Frame(Color fill, bool gold, bool outlined)
        {
            var texture = new Texture2D(FrameSize, FrameSize, TextureFormat.RGBA32, false);

            for (int y = 0; y < FrameSize; y++)
            {
                for (int x = 0; x < FrameSize; x++)
                {
                    float half = FrameSize / 2f;
                    var corner = new Vector2(
                        Mathf.Abs(x + 0.5f - half) - (half - FrameCorner),
                        Mathf.Abs(y + 0.5f - half) - (half - FrameCorner));
                    float distance = new Vector2(Mathf.Max(corner.x, 0f), Mathf.Max(corner.y, 0f)).magnitude - FrameCorner;
                    Color color = outlined && distance > -4f ? OutlineColor : gold && distance > -6f ? GoldColor : fill;

                    color.a *= Mathf.Clamp01(0.5f - distance);
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply();
            texture.wrapMode = TextureWrapMode.Clamp;
            var area = new Rect(0f, 0f, FrameSize, FrameSize);
            var border = Vector4.one * FrameCorner;
            return Sprite.Create(texture, area, Vector2.one / 2f, FramePixelsPerUnit, 0, SpriteMeshType.FullRect, border);
        }

        private static Sprite Paint(int widthInHeights, Shape shape)
        {
            int height = EffectHeight;
            int width = height * widthInHeights;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    var point = new Vector2((column + 0.5f) / height - widthInHeights / 2f, (row + 0.5f) / height - 0.5f);
                    float distance = shape(point);
                    Color color = distance > 0f ? OutlineColor : distance > -EffectCore ? WaterColor : FoamColor;

                    color.a = Mathf.Clamp01((EffectOutline - distance) * height + 0.5f);
                    texture.SetPixel(column, row, color);
                }
            }

            return Finish(texture, height);
        }

        private static Sprite Finish(Texture2D texture, float pixelsPerUnit)
        {
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.Apply();
            var area = new Rect(0f, 0f, texture.width, texture.height);
            return Sprite.Create(texture, area, Vector2.one / 2f, pixelsPerUnit);
        }
    }
}
