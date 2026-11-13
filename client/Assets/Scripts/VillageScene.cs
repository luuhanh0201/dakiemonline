using System;
using System.Collections.Generic;
using Game.Net;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// The village outside the house (KICH-BAN-CAC-MAP 6, docs/KE-HOACH-MAP-LANG.md): a sky drawn in code
    /// that follows the real time in Vietnam, far mountains and hills that drift slower than the road for
    /// depth, trees swaying through drawn frames, the buildings and the road, grass in front of the
    /// characters, the two shopkeepers and the five straw dummies played frame by frame, and the channel's
    /// weather. Only the picture lives here; the dummies' state comes from the server.
    /// </summary>
    public sealed class VillageScene
    {
        private const string Folder = "World/Village/";
        private const float ViewWidth = 12f;

        // Sorting, back to front. Characters draw at about 0 to 30.
        private const int SkyOrder = -200;
        private const int StarOrder = -195;
        private const int SunOrder = -194;
        private const int CloudOrder = -190;
        private const int MountainOrder = -150;
        private const int HillOrder = -140;
        private const int TreeOrder = -120;
        private const int FogBackOrder = -110;
        private const int FenceOrder = -60;
        private const int BuildingOrder = -40;
        private const int PropOrder = -35;
        private const int NpcOrder = -5;
        private const int DummyOrder = -4;
        private const int RainOrder = 40;
        private const int FrontOrder = 60;
        private const int FogFrontOrder = 65;
        private const int NightOrder = 80;
        private const int LitOrder = 90;
        private const int HealthOrder = 95;

        // How much each layer moves with the camera: 1 moves with the road, less is farther away.
        private const float MountainDepth = 0.15f;
        private const float HillDepth = 0.4f;
        private const float TreeDepth = 0.8f;
        private const float FrontDepth = 1.2f;

        // 24 frames bent from one drawing (docs/ASSETS.md): a slow sway in calm weather, a wider faster one in wind.
        private const float TreeFramesPerSecond = 12f;
        private const float WindTreeFramesPerSecond = 18f;

        // Buildings and things on the road: art, centre x, bottom y, height in map units.
        private static readonly (string Art, float X, float Y, float Height, int Order)[] Things =
        {
            ("Props/house", 1.95f, -0.05f, 3.4f, BuildingOrder),
            ("Props/books", 5.4f, -0.05f, 2.5f, BuildingOrder),
            ("Props/gear", 8.6f, -0.05f, 2.6f, BuildingOrder),
            ("Props/bush_big", 11.7f, -0.05f, 0.8f, PropOrder),
            ("Props/rock", 12.3f, -0.05f, 0.55f, PropOrder),
            ("Props/barrel", 10.4f, -0.05f, 0.6f, PropOrder),
            ("Props/rack", 20.3f, -0.05f, 1.25f, PropOrder),
            ("Props/step", 21.5f, -0.04f, 0.54f, PropOrder),
            ("Props/sign", 22.65f, -0.05f, 1.1f, PropOrder),
            ("Props/gate", 23.35f, -0.05f, 3.0f, BuildingOrder),
            ("Props/bush_flower", 3.9f, -0.05f, 0.5f, PropOrder),
        };

        // Grass and bushes in front of the characters, moving a little faster than the road.
        private static readonly (string Art, float X, float Height)[] FrontThings =
        {
            ("Props/grass", 0.8f, 0.5f),
            ("Props/grass", 6.9f, 0.42f),
            ("Props/bush_flower", 11.2f, 0.45f),
            ("Props/grass", 15.5f, 0.4f),
            ("Props/grass", 21.0f, 0.48f),
            ("Props/bush_big", 26.0f, 0.6f),
        };

        private static readonly (string Art, float X, float Height)[] Trees =
        {
            ("Trees/cherry_", 3.6f, 4.2f),
            ("Trees/green_", 13.0f, 4.0f),
            ("Trees/green_", 21.0f, 3.8f),
            ("Trees/cherry_", 27.5f, 4.4f),
        };

        private readonly Transform root;
        private readonly Camera camera;
        private readonly TrainingConfig training;
        private readonly SkyPainter sky;
        private readonly List<(Transform Item, float BaseX, float Depth)> drifting = new List<(Transform, float, float)>();
        private readonly List<TreeSway> trees = new List<TreeSway>();
        private readonly List<SpriteRenderer> lanterns = new List<SpriteRenderer>();
        private readonly List<DummyView> dummies = new List<DummyView>();
        private readonly List<ShopkeeperLife> shopkeepers = new List<ShopkeeperLife>();
        private readonly WeatherPainter weather;
        private readonly SpriteRenderer night;

        public VillageScene(Transform root, Camera camera, MapConfig map)
        {
            this.root = root;
            this.camera = camera;
            training = map.Training;
            sky = new SkyPainter(root);

            Strip("Far/mountains", 16f, 1.25f, MountainOrder, MountainDepth);
            Strip("Far/hills", 18.5f, 0.05f, HillOrder, HillDepth);

            foreach ((string art, float x, float height) in Trees)
            {
                Sprite[] calm = Frames(art + "calm_");
                Sprite[] wind = Frames(art + "wind_");

                if (calm.Length == 0 || wind.Length != calm.Length)
                {
                    continue;
                }

                SpriteRenderer tree = Sprite(calm[0], "Tree", x, -0.05f, height, TreeOrder);
                trees.Add(new TreeSway(tree, calm, wind, x * 0.37f));
                drifting.Add((tree.transform, x, TreeDepth));
            }

            // A low fence behind the training yard, laid end to end.
            Sprite fence = Load("Props/fence");

            if (fence != null)
            {
                float height = 0.7f;
                float width = height * fence.bounds.size.x / fence.bounds.size.y;

                for (float x = 12.6f; x < 22.8f; x += width * 0.98f)
                {
                    Sprite(fence, "Fence", x + width / 2f, 0.05f, height, FenceOrder);
                }
            }

            foreach ((string art, float x, float y, float height, int order) in Things)
            {
                Sprite thing = Load(art);

                if (thing != null)
                {
                    Sprite(thing, art, x, y, height, order);
                }
            }

            // Stone lanterns, lit after dark. The lit picture lies over the night shade so it stays bright.
            foreach (float x in new[] { 10.9f, 22.1f })
            {
                Sprite off = Load("Props/lantern_off");
                Sprite on = Load("Props/lantern_on");

                if (off != null && on != null)
                {
                    Sprite(off, "Lantern", x, -0.05f, 1.15f, PropOrder);
                    lanterns.Add(Sprite(on, "Lantern lit", x, -0.05f, 1.15f, LitOrder));
                }
            }

            foreach ((string art, float x, float height) in FrontThings)
            {
                Sprite thing = Load(art);

                if (thing != null)
                {
                    SpriteRenderer front = Sprite(thing, "Front " + art, x, -0.35f, height, FrontOrder);
                    front.color = new Color(0.62f, 0.7f, 0.6f, 1f);
                    drifting.Add((front.transform, x, FrontDepth));
                }
            }

            shopkeepers.Add(new ShopkeeperLife(root, Folder + "Npc/Lam/", 5.55f, 1.25f, NpcOrder));
            shopkeepers.Add(new ShopkeeperLife(root, Folder + "Npc/Vu/", 9.75f, 1.35f, NpcOrder));

            if (training != null)
            {
                foreach (MapDummy dummy in training.Dummies)
                {
                    dummies.Add(new DummyView(root, (float)dummy.X, training, DummyOrder, HealthOrder));
                }
            }

            weather = new WeatherPainter(root, camera, RainOrder, FogBackOrder, FogFrontOrder, CloudOrder);
            night = Block("Night shade", NightOrder);
        }

        /// <summary>Hours since midnight in Vietnam (UTC+7), which the sky and the light follow.</summary>
        public static float VietnamHour()
        {
            DateTime now = DateTime.UtcNow.AddHours(7);
            return (float)now.TimeOfDay.TotalHours;
        }

        public void Animate(float time, float frameSeconds, float playerX, Weather now, IReadOnlyList<RemoteDummy> yard)
        {
            Vector3 eye = camera.transform.position;
            float viewHeight = camera.orthographicSize * 2f;
            float viewWidth = viewHeight * camera.aspect;
            float hour = VietnamHour();
            float dark = Darkness(hour);

            sky.Paint(eye, viewWidth, viewHeight, hour, dark, now);

            foreach ((Transform item, float baseX, float depth) in drifting)
            {
                Vector3 position = item.position;
                position.x = baseX + (eye.x - ViewWidth) * (1f - depth);
                item.position = position;
            }

            foreach (TreeSway tree in trees)
            {
                tree.Animate(frameSeconds, now == Weather.Wind);
            }

            foreach (SpriteRenderer lantern in lanterns)
            {
                lantern.enabled = dark > 0.25f;
            }

            foreach (ShopkeeperLife keeper in shopkeepers)
            {
                keeper.Animate(time, frameSeconds, playerX);
            }

            for (int index = 0; index < dummies.Count; index++)
            {
                dummies[index].Show(index < yard.Count ? yard[index] : new RemoteDummy(0, false, int.MaxValue), playerX);
            }

            weather.Paint(time, frameSeconds, eye, viewWidth, viewHeight, now, dark);

            // The night shade lies over the world and the characters; rain greys the day a little too.
            Color shade = Color.Lerp(new Color(0.85f, 0.45f, 0.35f, 0f), new Color(0.05f, 0.07f, 0.22f, 0.5f), dark);
            shade.a = Mathf.Max(shade.a, dark * 0.5f);

            if (now == Weather.Rain)
            {
                shade = Color.Lerp(shade, new Color(0.2f, 0.24f, 0.32f, Mathf.Max(shade.a, 0.22f)), 0.5f);
            }

            night.color = shade;
            night.transform.position = new Vector3(eye.x, eye.y, 0f);
            night.transform.localScale = new Vector3(viewWidth + 1f, viewHeight + 1f, 1f);
        }

        /// <summary>0 by day, 1 at night, easing through dawn (5:00 to 6:30) and dusk (17:30 to 19:30).</summary>
        public static float Darkness(float hour)
        {
            if (hour < 5f || hour >= 19.5f)
            {
                return 1f;
            }

            if (hour < 6.5f)
            {
                return 1f - Mathf.SmoothStep(0f, 1f, (hour - 5f) / 1.5f);
            }

            if (hour < 17.5f)
            {
                return 0f;
            }

            return Mathf.SmoothStep(0f, 1f, (hour - 17.5f) / 2f);
        }

        private void Strip(string art, float width, float bottom, int order, float depth)
        {
            Sprite sprite = Load(art);

            if (sprite == null)
            {
                return;
            }

            float height = width * sprite.bounds.size.y / sprite.bounds.size.x;
            SpriteRenderer strip = Sprite(sprite, art, ViewWidth, bottom, height, order);
            drifting.Add((strip.transform, ViewWidth, depth));
        }

        private SpriteRenderer Sprite(Sprite sprite, string name, float x, float bottom, float height, int order)
        {
            var item = new GameObject(name);
            item.transform.SetParent(root, false);
            var renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            float scale = height / sprite.bounds.size.y;
            item.transform.localScale = new Vector3(scale, scale, 1f);
            item.transform.position = new Vector3(x, bottom + height / 2f, 0f);
            return renderer;
        }

        private SpriteRenderer Block(string name, int order)
        {
            var item = new GameObject(name);
            item.transform.SetParent(root, false);
            var renderer = item.AddComponent<SpriteRenderer>();
            Texture2D texture = Texture2D.whiteTexture;
            renderer.sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            renderer.sortingOrder = order;
            return renderer;
        }

        internal static Sprite Load(string art) => Resources.Load<Sprite>(Folder + art);

        /// <summary>Frames named prefix1, prefix2, … in order, as many as there are.</summary>
        internal static Sprite[] Frames(string prefix)
        {
            var frames = new List<Sprite>();

            for (int number = 1; ; number++)
            {
                Sprite frame = Resources.Load<Sprite>($"{Folder}{prefix}{number}");

                if (frame == null)
                {
                    return frames.ToArray();
                }

                frames.Add(frame);
            }
        }

        /// <summary>
        /// One tree swaying through its frames. The calm and the windy sets are the same drawing upright at
        /// their first and middle frames, so the tree only changes set there and never jumps.
        /// </summary>
        private sealed class TreeSway
        {
            private readonly SpriteRenderer renderer;
            private readonly Sprite[] calm;
            private readonly Sprite[] wind;
            private float clock;
            private bool windy;

            public TreeSway(SpriteRenderer renderer, Sprite[] calm, Sprite[] wind, float phase)
            {
                this.renderer = renderer;
                this.calm = calm;
                this.wind = wind;
                clock = phase * calm.Length;
            }

            public void Animate(float frameSeconds, bool wantWind)
            {
                int before = (int)clock % calm.Length;
                clock += frameSeconds * (windy ? WindTreeFramesPerSecond : TreeFramesPerSecond);
                int frame = (int)clock % calm.Length;

                if (wantWind != windy && (frame == 0 || frame == calm.Length / 2 || frame < before))
                {
                    windy = wantWind;
                }

                renderer.sprite = (windy ? wind : calm)[frame];
            }
        }

        /// <summary>
        /// The sky in flat bands, coloured for the hour: blue by day, purple and orange at dawn and dusk,
        /// deep blue at night with the moon and twinkling stars. Drawn in code so it changes smoothly.
        /// </summary>
        private sealed class SkyPainter
        {
            private static readonly Color[] Day = { Hex(0x3E7FE0), Hex(0x5592E8), Hex(0x72A9EE), Hex(0x96C3F3), Hex(0xBEDDF8) };
            private static readonly Color[] Dusk = { Hex(0x4B3A96), Hex(0x7D4CAE), Hex(0xC2639F), Hex(0xEE8676), Hex(0xF7B566) };
            private static readonly Color[] Night = { Hex(0x0A0F2C), Hex(0x111840), Hex(0x1A2256), Hex(0x252D68), Hex(0x343A7A) };
            private static readonly Color Rain = Hex(0x6F7C8E);

            private readonly SpriteRenderer[] bands = new SpriteRenderer[5];
            private readonly SpriteRenderer sun;
            private readonly SpriteRenderer moon;
            private readonly List<(SpriteRenderer Star, Vector2 At, float Phase)> stars = new List<(SpriteRenderer, Vector2, float)>();

            public SkyPainter(Transform root)
            {
                Sprite square = Square();

                for (int index = 0; index < bands.Length; index++)
                {
                    bands[index] = Renderer(root, "Sky band", square, SkyOrder + index);
                }

                Sprite disc = Disc();
                sun = Renderer(root, "Sun", disc, SunOrder);
                moon = Renderer(root, "Moon", disc, SunOrder);
                Sprite star = Resources.Load<Sprite>("World/Door/star");
                var random = new System.Random(11);

                for (int index = 0; index < 40 && star != null; index++)
                {
                    stars.Add((Renderer(root, "Star", star, StarOrder), new Vector2((float)random.NextDouble(), 0.35f + 0.6f * (float)random.NextDouble()), (float)random.NextDouble() * 6f));
                }
            }

            public void Paint(Vector3 eye, float width, float height, float hour, float dark, Weather weather)
            {
                float dusk = 1f - Mathf.Abs(dark * 2f - 1f);
                float top = eye.y + height / 2f;
                float bandHeight = height / bands.Length;

                for (int index = 0; index < bands.Length; index++)
                {
                    Color colour = dark < 0.5f ? Color.Lerp(Day[index], Dusk[index], dark * 2f) : Color.Lerp(Dusk[index], Night[index], dark * 2f - 1f);

                    if (weather == Weather.Rain || weather == Weather.Fog)
                    {
                        colour = Color.Lerp(colour, Rain * (1f - dark * 0.7f), weather == Weather.Rain ? 0.6f : 0.4f);
                        colour.a = 1f;
                    }

                    SpriteRenderer band = bands[index];
                    band.color = colour;
                    band.transform.position = new Vector3(eye.x, top - bandHeight * (index + 0.5f), 0f);
                    band.transform.localScale = new Vector3(width + 2f, bandHeight + 0.02f, 1f);
                }

                // The sun crosses from 6:00 to 18:00 and the moon from 18:00 to 6:00, in a low arc.
                float sunArc = Mathf.InverseLerp(6f, 18f, hour);
                float moonArc = Mathf.Repeat(hour - 18f, 24f) / 12f;
                Place(sun, eye, width, height, sunArc, 0.55f, Hex(0xFFE27A), hour >= 6f && hour < 18.5f && weather != Weather.Rain);
                Place(moon, eye, width, height, moonArc, 0.5f, Hex(0xF4EED2), dark > 0.4f && weather != Weather.Rain);

                float clear = weather == Weather.Clear || weather == Weather.Wind ? 1f : 0.25f;

                foreach ((SpriteRenderer star, Vector2 at, float phase) in stars)
                {
                    float twinkle = 0.55f + 0.45f * Mathf.Sin(Time.time * 2.1f + phase);
                    star.color = new Color(1f, 1f, 1f, Mathf.Clamp01(dark * 1.4f - 0.4f) * twinkle * clear);
                    star.transform.position = new Vector3(eye.x - width / 2f + at.x * width, eye.y - height / 2f + at.y * height, 0f);
                    star.transform.localScale = Vector3.one * (0.09f / star.sprite.bounds.size.x) * (0.7f + 0.3f * twinkle);
                }
            }

            private static void Place(SpriteRenderer body, Vector3 eye, float width, float height, float arc, float size, Color colour, bool shown)
            {
                body.enabled = shown;
                float x = eye.x - width * 0.4f + arc * width * 0.8f;
                float y = eye.y + height * (0.18f + 0.22f * Mathf.Sin(arc * Mathf.PI));
                body.transform.position = new Vector3(x, y, 0f);
                body.transform.localScale = Vector3.one * size;
                body.color = colour;
            }

            private static SpriteRenderer Renderer(Transform root, string name, Sprite sprite, int order)
            {
                var item = new GameObject(name);
                item.transform.SetParent(root, false);
                var renderer = item.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = order;
                return renderer;
            }

            private static Sprite Square()
            {
                Texture2D texture = Texture2D.whiteTexture;
                return UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            }

            /// <summary>A flat disc with a slightly darker rim, one unit across.</summary>
            private static Sprite Disc()
            {
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                        float alpha = Mathf.Clamp01((1f - d) * size / 2f);
                        float rim = d > 0.86f ? 0.85f : 1f;
                        texture.SetPixel(x, y, new Color(rim, rim, rim, alpha));
                    }
                }

                texture.Apply();
                return UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            }

            private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }

        /// <summary>
        /// A shopkeeper breathing through drawn frames and now and then blinking; when the player comes close
        /// she or he raises a hand through the greeting frames and waves, and lowers it when the player leaves.
        /// </summary>
        private sealed class ShopkeeperLife
        {
            private const float PixelsPerUnit = 802f * 0.3f;
            private const float NearUnits = 2f;
            private const float FarUnits = 2.5f;
            private const float BreathFramesPerSecond = 8f;
            private const float BreathRestSeconds = 0.45f;
            private const float BlinkSeconds = 0.12f;
            private const int BreathsBetweenBlinks = 3;
            private const float RaiseFramesPerSecond = 14f;
            private const float WaveFramesPerSecond = 7f;

            private readonly SpriteRenderer body;
            private readonly Sprite[] idle;
            private readonly Sprite[] greet;
            private readonly float x;
            private readonly float breathCycle;
            private bool near;
            private float raised;
            private float waveClock;

            public ShopkeeperLife(Transform root, string folder, float x, float height, int order)
            {
                this.x = x;
                idle = Load(folder + "idle_");
                greet = Load(folder + "greet_");

                var item = new GameObject("Shopkeeper " + folder);
                item.transform.SetParent(root, false);
                body = item.AddComponent<SpriteRenderer>();
                body.sortingOrder = order;

                if (idle.Length == 0)
                {
                    body.enabled = false;
                    return;
                }

                breathCycle = (idle.Length - 1) / BreathFramesPerSecond + BreathRestSeconds;
                body.sprite = idle[0];
                float canvas = idle[0].texture.height / PixelsPerUnit * height / 1.25f;
                float scale = canvas / idle[0].bounds.size.y;
                item.transform.localScale = new Vector3(scale, scale, 1f);
                item.transform.position = new Vector3(x, canvas / 2f, 0f);
            }

            /// <summary>The greeting frames up to the fourth raise the hand; the fourth to sixth wave.</summary>
            private int Raised => Mathf.Min(3, greet.Length - 1);

            public void Animate(float time, float frameSeconds, float playerX)
            {
                if (idle.Length == 0)
                {
                    return;
                }

                float distance = Mathf.Abs(playerX - x);
                near = distance < (near ? FarUnits : NearUnits);
                body.flipX = playerX < x - 0.3f;
                bool canGreet = greet.Length > Raised + 2;
                raised = Mathf.MoveTowards(raised, near && canGreet ? Raised : 0f, frameSeconds * RaiseFramesPerSecond);

                if (raised <= 0f)
                {
                    waveClock = 0f;
                    body.sprite = Breath(time);
                }
                else if (raised < Raised)
                {
                    body.sprite = greet[Mathf.RoundToInt(raised)];
                }
                else
                {
                    // Waving: up, to one side, up, to the other side.
                    waveClock += frameSeconds * WaveFramesPerSecond;
                    int[] wave = { Raised, Raised + 1, Raised, Raised + 2 };
                    body.sprite = greet[wave[(int)waveClock % wave.Length]];
                }
            }

            private Sprite Breath(float time)
            {
                int round = (int)(time / breathCycle);
                float at = Mathf.Repeat(time + x, breathCycle);
                int breathing = idle.Length - 1;

                if (at < BreathRestSeconds)
                {
                    return round % BreathsBetweenBlinks == 0 && at < BlinkSeconds ? idle[breathing] : idle[0];
                }

                return idle[Mathf.Clamp((int)((at - BreathRestSeconds) * BreathFramesPerSecond), 0, breathing - 1)];
            }

            private static Sprite[] Load(string prefix)
            {
                var frames = new List<Sprite>();

                for (int number = 1; ; number++)
                {
                    Sprite frame = Resources.Load<Sprite>($"{prefix}{number}");

                    if (frame == null)
                    {
                        return frames.ToArray();
                    }

                    frames.Add(frame);
                }
            }
        }

        /// <summary>
        /// One straw dummy, drawn from the server's state: swaying gently while it stands, jolting when hit,
        /// falling through drawn frames, lying still, then rising again through its own frames. The time since
        /// the last change lets a player who arrives late join the motion at the right frame.
        /// </summary>
        private sealed class DummyView
        {
            private const float SwayFramesPerSecond = 6f;
            private const float FallFramesPerSecond = 16f;
            private const float RiseFramesPerSecond = 14f;
            private const float HitFrames = 3f;
            private const float HealthAfterFallSeconds = 0.6f;

            // A blow: a short red flush, a shudder that dies away, and bits of straw flung off the far side.
            private const float FlushSeconds = 0.16f;
            private const float ShudderSeconds = 0.28f;
            private const float ShudderWidth = 0.05f;
            private const float ShudderPerSecond = 55f;
            private const float StrawSeconds = 0.6f;
            private const int StrawBits = 7;
            private const float StrawFall = 9f;
            private const float StrawSize = 0.16f;
            private static readonly Color Flush = new Color32(0xFF, 0xA8, 0x98, 0xFF);
            private static readonly Color Straw = new Color32(0xF2, 0xCC, 0x5A, 0xFF);

            private readonly SpriteRenderer body;
            private readonly Sprite[] idle;
            private readonly Sprite[] fall;
            private readonly Sprite[] rise;
            private readonly float phase;
            private readonly float x;
            private readonly float height;
            private readonly Vector3 rest;
            private readonly List<(SpriteRenderer Renderer, Vector3 Speed, float Spin)> straw = new List<(SpriteRenderer, Vector3, float)>();
            private readonly System.Random random;
            private bool seen;
            private int lastHits;
            private bool lastFallen;
            private float hitAt = -100f;
            private int away = 1;
            private readonly TrainingConfig training;
            private readonly DummyHealth health;

            public DummyView(Transform root, float x, TrainingConfig training, int order, int healthOrder)
            {
                float height = (float)training.Height;
                this.training = training;
                health = new DummyHealth(root, x, height, healthOrder);
                idle = Frames("Dummy/idle_");
                fall = Frames("Dummy/fall_");
                rise = Frames("Dummy/rise_");
                phase = x;
                this.x = x;
                this.height = height;
                random = new System.Random((int)(x * 100f));

                var item = new GameObject("Dummy");
                item.transform.SetParent(root, false);
                body = item.AddComponent<SpriteRenderer>();
                body.sortingOrder = order;

                if (idle.Length == 0)
                {
                    body.enabled = false;
                    return;
                }

                body.sprite = idle[0];

                // Every dummy frame shares one canvas whose standing dummy is the given height; the feet are its bottom centre.
                float canvas = height * DummyCanvasShare;
                float scale = canvas / idle[0].bounds.size.y;
                item.transform.localScale = new Vector3(scale, scale, 1f);
                rest = new Vector3(x, canvas / 2f - 0.05f, 0f);
                item.transform.position = rest;

                // The straw bits are the wind's leaf drawings, coloured like the dummy's straw.
                Sprite[] leaf = Frames("Weather/leaf_");

                for (int index = 0; index < StrawBits && leaf.Length > 0; index++)
                {
                    var bit = new GameObject("Straw");
                    bit.transform.SetParent(root, false);
                    SpriteRenderer renderer = bit.AddComponent<SpriteRenderer>();
                    renderer.sprite = leaf[index % leaf.Length];
                    renderer.sortingOrder = order + 1;
                    renderer.enabled = false;
                    float size = StrawSize / Mathf.Max(0.01f, renderer.sprite.bounds.size.y);
                    bit.transform.localScale = new Vector3(size, size, 1f);
                    straw.Add((renderer, Vector3.zero, 0f));
                }
            }

            /// <summary>The 300 px canvas holds a 247 px standing dummy (docs/map-lang/nguoi-rom.png, cut on a fixed grid).</summary>
            internal static float DummyCanvasShare = 300f / 247f;

            public void Show(RemoteDummy state, float playerX)
            {
                if (idle.Length == 0)
                {
                    return;
                }

                // A new blow shows as more hits, the knock-down blow included; standing up again resets them to 0.
                bool struck = seen && (state.Hits > lastHits || state.Fallen && !lastFallen)
                    && state.TicksSinceChange < Ticks.PerSecond / 2;
                seen = true;
                lastHits = state.Hits;
                lastFallen = state.Fallen;

                if (struck)
                {
                    Strike(playerX);
                    health.Hit(training.DamagePerHit, away);
                }

                float sinceChange = state.TicksSinceChange / (float)Ticks.PerSecond;
                health.Show(TrainingRules.Hp(training, state.Hits), training.MaxHp,
                    state.Hits > 0 && (!state.Fallen || sinceChange < HealthAfterFallSeconds));

                React();

                float seconds = state.TicksSinceChange / (float)Ticks.PerSecond;
                int fallFrame = (int)(seconds * FallFramesPerSecond);
                int riseFrame = (int)(seconds * RiseFramesPerSecond);
                float jolt = seconds * FallFramesPerSecond;

                if (state.Fallen)
                {
                    // Falling, then lying still on the last frame until it stands up.
                    body.sprite = fall.Length > 0 ? fall[Mathf.Min(fallFrame, fall.Length - 1)] : idle[0];
                }
                else if (state.Hits == 0 && rise.Length > 0 && riseFrame < rise.Length)
                {
                    // Just stood up again: the time counts from that moment.
                    body.sprite = rise[riseFrame];
                }
                else if (state.Hits > 0 && fall.Length > 0 && jolt < HitFrames * 2f)
                {
                    // Hit without falling: a jolt into the first fall frames and back.
                    int frame = (int)(jolt < HitFrames ? jolt : HitFrames * 2f - jolt);
                    body.sprite = fall[Mathf.Clamp(frame, 0, fall.Length - 1)];
                }
                else
                {
                    body.sprite = idle[(int)(Time.time * SwayFramesPerSecond + phase) % idle.Length];
                }
            }

            /// <summary>Starts a blow's reaction; the straw flies away from the player who is nearest the blow.</summary>
            private void Strike(float playerX)
            {
                hitAt = Time.time;
                away = x >= playerX ? 1 : -1;

                for (int index = 0; index < straw.Count; index++)
                {
                    SpriteRenderer renderer = straw[index].Renderer;
                    float across = away * (1.2f + (float)random.NextDouble() * 2.2f);
                    float up = 1.6f + (float)random.NextDouble() * 2.4f;
                    float spin = ((float)random.NextDouble() - 0.5f) * 1400f;
                    renderer.transform.position = new Vector3(
                        x + away * 0.05f, height * (0.45f + (float)random.NextDouble() * 0.35f), 0f);
                    renderer.transform.rotation = Quaternion.Euler(0f, 0f, (float)random.NextDouble() * 360f);
                    renderer.enabled = true;
                    straw[index] = (renderer, new Vector3(across, up, 0f), spin);
                }
            }

            /// <summary>The flush, the shudder and the flying straw, all fading out after a blow.</summary>
            private void React()
            {
                float since = Time.time - hitAt;
                float flush = Mathf.Clamp01(since / FlushSeconds);
                body.color = Color.Lerp(Flush, Color.white, flush * flush);

                float shudder = since < ShudderSeconds ? 1f - since / ShudderSeconds : 0f;
                body.transform.position = rest + new Vector3(
                    away * ShudderWidth * shudder * Mathf.Sin(since * ShudderPerSecond), 0f, 0f);

                float frame = Mathf.Min(Time.deltaTime, 0.05f);

                for (int index = 0; index < straw.Count; index++)
                {
                    (SpriteRenderer renderer, Vector3 speed, float spin) = straw[index];

                    if (!renderer.enabled)
                    {
                        continue;
                    }

                    if (since >= StrawSeconds)
                    {
                        renderer.enabled = false;
                        continue;
                    }

                    speed.y -= StrawFall * frame;
                    Transform bit = renderer.transform;
                    bit.position += speed * frame;
                    bit.Rotate(0f, 0f, spin * frame);
                    float left = 1f - since / StrawSeconds;
                    renderer.color = new Color(Straw.r, Straw.g, Straw.b, Mathf.Clamp01(left * 2f));
                    straw[index] = (renderer, speed, spin);
                }
            }
        }

        /// <summary>
        /// The weather drawn over the village: rain streaks and splashes, drifting fog, leaves and petals
        /// blown along by the wind, and clouds crossing the sky. The pieces are flat drawings; their motion is here.
        /// </summary>
        private sealed class WeatherPainter
        {
            private const int Drops = 90;
            private const int Splashes = 24;
            private const int Leaves = 26;

            private readonly Sprite[] rain = Frames("Weather/rain_");
            private readonly Sprite[] splash = Frames("Weather/splash_");
            private readonly Sprite[] fog = Frames("Weather/fog_");
            private readonly Sprite[] leaf = Frames("Weather/leaf_");
            private readonly Sprite[] petal = Frames("Weather/petal_");
            private readonly Sprite[] cloud = Frames("Weather/cloud_");
            private readonly List<(SpriteRenderer Renderer, Vector2 Seed)> drops = new List<(SpriteRenderer, Vector2)>();
            private readonly List<(SpriteRenderer Renderer, Vector2 Seed)> splashes = new List<(SpriteRenderer, Vector2)>();
            private readonly List<(SpriteRenderer Renderer, Vector2 Seed, bool Petal)> leaves = new List<(SpriteRenderer, Vector2, bool)>();
            private readonly List<(SpriteRenderer Renderer, float Seed, float Height)> fogs = new List<(SpriteRenderer, float, float)>();
            private readonly List<(SpriteRenderer Renderer, float Seed, float Height)> clouds = new List<(SpriteRenderer, float, float)>();
            private float rainAmount;
            private float fogAmount;
            private float windAmount;

            public WeatherPainter(Transform root, Camera camera, int rainOrder, int fogBackOrder, int fogFrontOrder, int cloudOrder)
            {
                var random = new System.Random(5);

                for (int index = 0; index < Drops && rain.Length > 0; index++)
                {
                    drops.Add((Make(root, rain[index % rain.Length], rainOrder), new Vector2((float)random.NextDouble(), (float)random.NextDouble())));
                }

                for (int index = 0; index < Splashes && splash.Length > 0; index++)
                {
                    splashes.Add((Make(root, splash[0], rainOrder), new Vector2((float)random.NextDouble(), (float)random.NextDouble())));
                }

                for (int index = 0; index < Leaves && leaf.Length > 0 && petal.Length > 0; index++)
                {
                    bool isPetal = index % 2 == 0;
                    leaves.Add((Make(root, isPetal ? petal[0] : leaf[0], rainOrder), new Vector2((float)random.NextDouble(), (float)random.NextDouble()), isPetal));
                }

                for (int index = 0; index < 4 && fog.Length > 0; index++)
                {
                    fogs.Add((Make(root, fog[index % fog.Length], index % 2 == 0 ? fogBackOrder : fogFrontOrder), (float)random.NextDouble(), 0.4f + index * 0.55f));
                }

                for (int index = 0; index < 5 && cloud.Length > 0; index++)
                {
                    clouds.Add((Make(root, cloud[index % cloud.Length], cloudOrder), (float)random.NextDouble(), 0.62f + 0.07f * (index % 3)));
                }
            }

            public void Paint(float time, float frameSeconds, Vector3 eye, float width, float height, Weather now, float dark)
            {
                // Each kind fades in and out over a few seconds instead of switching at once.
                float fade = frameSeconds / 3f;
                rainAmount = Mathf.MoveTowards(rainAmount, now == Weather.Rain ? 1f : 0f, fade);
                fogAmount = Mathf.MoveTowards(fogAmount, now == Weather.Fog ? 1f : 0f, fade);
                windAmount = Mathf.MoveTowards(windAmount, now == Weather.Wind ? 1f : 0f, fade);

                float left = eye.x - width / 2f;
                float bottom = eye.y - height / 2f;
                float night = 1f - dark * 0.45f;

                for (int index = 0; index < drops.Count; index++)
                {
                    (SpriteRenderer renderer, Vector2 seed) = drops[index];
                    float fallen = Mathf.Repeat(time * 1.6f + seed.y, 1f);
                    renderer.enabled = rainAmount > 0.01f;
                    renderer.transform.position = new Vector3(left + Mathf.Repeat(seed.x * width + fallen * 1.2f, width), bottom + height * (1.05f - fallen * 1.1f), 0f);
                    renderer.transform.localScale = Vector3.one * (0.42f / renderer.sprite.bounds.size.y);
                    renderer.transform.rotation = Quaternion.Euler(0f, 0f, 14f);
                    renderer.color = new Color(night, night, night, 0.7f * rainAmount);
                }

                for (int index = 0; index < splashes.Count; index++)
                {
                    (SpriteRenderer renderer, Vector2 seed) = splashes[index];
                    float cycle = Mathf.Repeat(time * 1.8f + seed.y, 1f);
                    int frame = Mathf.Min((int)(cycle * splash.Length * 2f), splash.Length - 1);
                    renderer.sprite = splash[frame];
                    renderer.enabled = rainAmount > 0.01f && cycle < 0.5f;
                    float x = left + seed.x * width;
                    renderer.transform.position = new Vector3(x + Mathf.Floor(time * 1.8f + seed.y) * 0.37f % width * 0f, 0.08f, 0f);
                    renderer.transform.localScale = Vector3.one * (0.3f / renderer.sprite.bounds.size.x);
                    renderer.color = new Color(night, night, night, 0.8f * rainAmount);
                }

                for (int index = 0; index < leaves.Count; index++)
                {
                    (SpriteRenderer renderer, Vector2 seed, bool isPetal) = leaves[index];
                    Sprite[] frames = isPetal ? petal : leaf;
                    float across = Mathf.Repeat(time * (0.35f + 0.25f * seed.y) + seed.x, 1f);
                    renderer.sprite = frames[(int)(time * 8f + index) % frames.Length];

                    // A few petals drift in calm weather; a strong wind blows many leaves and petals across.
                    bool shown = windAmount > 0.01f || (isPetal && index % 6 == 0);
                    float amount = Mathf.Max(windAmount, isPetal && index % 6 == 0 ? 0.8f : 0f);
                    renderer.enabled = shown;
                    float x = left - 1f + across * (width + 2f);
                    float y = bottom + height * (0.15f + 0.7f * seed.y) + 0.3f * Mathf.Sin(time * 2f + index);
                    renderer.transform.position = new Vector3(x, y, 0f);
                    renderer.transform.localScale = Vector3.one * (0.16f / renderer.sprite.bounds.size.x);
                    renderer.color = new Color(night, night, night, amount);
                }

                foreach ((SpriteRenderer renderer, float seed, float h) in fogs)
                {
                    float w = width * 1.4f;
                    float drift = Mathf.Repeat(time * 0.08f + seed, 1f);
                    renderer.enabled = fogAmount > 0.01f;
                    renderer.transform.position = new Vector3(left - w * 0.2f + drift * width * 0.4f + w / 2f - width * 0.2f, h, 0f);
                    renderer.transform.localScale = new Vector3(w / renderer.sprite.bounds.size.x, 1.1f / renderer.sprite.bounds.size.y, 1f);
                    renderer.color = new Color(night, night, night * 1.02f, 0.55f * fogAmount);
                }

                float cover = 0.55f + 0.45f * Mathf.Max(rainAmount, fogAmount);

                foreach ((SpriteRenderer renderer, float seed, float share) in clouds)
                {
                    float across = Mathf.Repeat(time * (0.006f + windAmount * 0.03f) + seed, 1f);
                    renderer.transform.position = new Vector3(left - 2f + across * (width + 4f), bottom + height * share, 0f);
                    renderer.transform.localScale = Vector3.one * (2.2f / renderer.sprite.bounds.size.x);
                    float grey = Mathf.Lerp(1f, 0.6f, rainAmount) * (1f - dark * 0.6f);
                    renderer.color = new Color(grey, grey, grey * 1.03f, cover);
                }
            }

            private static SpriteRenderer Make(Transform root, Sprite sprite, int order)
            {
                var item = new GameObject("Weather");
                item.transform.SetParent(root, false);
                var renderer = item.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = order;
                renderer.enabled = false;
                return renderer;
            }
        }
    }
}
