using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Draws the map the player is on from its art. On the test map the camera follows the character;
    /// the house is one screen and the camera stands still. Every door gets the same light effect.
    /// </summary>
    public sealed class WorldView
    {
        private const string ArtFolder = "World/";
        private const string HouseFolder = "World/House/";
        private const string HouseMapId = "map.house";
        private const float ViewWidth = 12f;
        private const float MapWidth = 30f;
        private const float LookAhead = 0.1f * ViewWidth;
        private const float GroundMargin = 0.95f;
        private const float CameraSmoothing = 6f;
        private const float ShakeSeconds = 0.2f;
        private const float ShakeSize = 0.09f;
        private const float ShakeTurnsPerSecond = 16f;

        // The background is wider than the view, so it can drift without showing an edge.
        private const float BackgroundWidth = ViewWidth * 1.3f;
        private const float BackgroundDrift = 0.15f;

        // How far down each image its walkable surface lies, as a share of its height.
        private const float GroundSurfaceShare = 0.111f;

        // The drawn grass rises this far behind the feet, so they stand on it, not on its top edge.
        private const float GrassBehindFeet = 0.2f;
        private const float PlatformSurfaceShare = 0.17f;

        // Things stand a little way into the ground, so the grass in front hides where they end.
        private const float LargePropSink = 0.16f;
        private const float SmallPropSink = 0.09f;

        // A platform rests on a rock, with this much of the rock below the grass.
        private const string PlatformRock = "rock_big";
        private const float PlatformRockSink = 0.44f;

        // The house picture is one screen wide; its floor meets the wall 715 px down its 941 px.
        private const float HouseWidth = ViewWidth;
        private const float HouseFloorShare = 715f / 941f;

        private const int BackgroundOrder = -100;
        private const int LargePropOrder = -30;
        private const int SmallPropOrder = -29;
        private const int GroundOrder = -10;
        private const int PlatformOrder = -9;

        private static readonly Color SkyColor = new Color32(0x14, 0x1B, 0x3A, 0xFF);

        // Behind the house picture, where a tall screen shows more than it: the colour of its beams.
        private static readonly Color HouseEdgeColor = new Color32(0x4A, 0x2C, 0x18, 0xFF);

        private static readonly (string Name, float X, bool Large, bool Flip)[] Props =
        {
            ("tree_pink", 1.6f, true, false),
            ("bush", 3.5f, false, false),
            ("flower_a", 4.1f, false, false),
            ("house", 7.4f, true, false),
            ("flower_b", 10.6f, false, false),
            ("rock_a", 11.2f, false, false),
            ("tree_green", 13.6f, true, false),
            ("flower_c", 15.3f, false, false),
            ("rock_b", 16.1f, false, false),
            ("tree_pink", 18.4f, true, true),
            ("bush", 20.6f, false, true),
            ("flower_a", 21.3f, false, true),
            ("house", 24.2f, true, true),
            ("rock_a", 26.8f, false, true),
            ("flower_b", 27.4f, false, true),
            ("tree_green", 28.8f, true, true),
        };

        /// <summary>
        /// The house, as in the approved sample picture (docs/map-nha/anh-mau.png): x is the centre,
        /// y the bottom (or the top, for the hanging lantern), in map units; size is the height,
        /// and the width too where the art is squeezed.
        /// </summary>
        private static readonly (string Name, float X, float Y, float Height, float Width, bool Hangs)[] HouseProps =
        {
            ("bag", 0.72f, 0f, 0.68f, 0f, false),
            ("chest", 1.88f, 0f, 1.12f, 0f, false),
            ("lantern", 2.2f, 5.1f, 2.05f, 0f, true),
            ("sword", 3.05f, 2.15f, 0.5f, 0f, false),
            ("master", 4.05f, 0f, 1.55f, 0f, false),
            ("table_1", 8.35f, 0f, 1.6f, 0f, false),
            ("door_open", 11.45f, 0f, 2.75f, 0.8f, false),
        };

        private readonly GameConfig config;
        private readonly Sprite square;
        private readonly Camera camera;
        private readonly List<DoorLight> doors = new List<DoorLight>();
        private Transform root;
        private Transform background;
        private float cameraTop;
        private Vector3 shakeOffset;
        private float shakeLeft;
        private bool fixedCamera;

        public WorldView(GameConfig config)
        {
            this.config = config;
            square = CreateSquare();
            camera = Camera.main;
            Show(0);
        }

        /// <summary>The map shown, as its place in <see cref="GameConfig.Maps"/>.</summary>
        public int Map { get; private set; } = -1;

        /// <summary>The boss lives on the map shown.</summary>
        public bool HasBoss => config.Maps[Map].Boss;

        /// <summary>Builds the art of another map, throwing away the old one.</summary>
        public void Show(int map)
        {
            if (map == Map || map < 0 || map >= config.Maps.Count)
            {
                return;
            }

            if (root != null)
            {
                Object.Destroy(root.gameObject);
            }

            Map = map;
            doors.Clear();
            shakeLeft = 0f;
            shakeOffset = Vector3.zero;
            root = new GameObject($"Map {config.Maps[map].Id}").transform;

            if (config.Maps[map].Id == HouseMapId)
            {
                BuildHouse();
            }
            else
            {
                BuildTestMap(config.Maps[map].Boxes);
            }

            foreach (MapDoor door in config.Maps[map].Doors)
            {
                doors.Add(new DoorLight(root, (float)door.X));
            }
        }

        public void Follow(Vector2 feet, int facing)
        {
            Vector3 position = camera.transform.position - shakeOffset;

            if (fixedCamera)
            {
                position = HouseCamera();
            }
            else
            {
                float halfView = ViewWidth / 2f;
                float target = Mathf.Clamp(feet.x + facing * LookAhead, halfView, MapWidth - halfView);
                position.x = Mathf.Lerp(position.x, target, 1f - Mathf.Exp(-CameraSmoothing * Time.deltaTime));
            }

            shakeLeft = Mathf.Max(0f, shakeLeft - Time.deltaTime);
            float amount = shakeLeft / ShakeSeconds;
            float wave = Mathf.Sin(shakeLeft * ShakeTurnsPerSecond * Mathf.PI * 2f);
            shakeOffset = new Vector3(wave * 0.5f, wave, 0f) * ShakeSize * amount;

            camera.transform.position = position + shakeOffset;

            if (!fixedCamera)
            {
                PlaceBackground(position.x);
            }

            foreach (DoorLight door in doors)
            {
                door.Animate(Time.time);
            }
        }

        /// <summary>Moves the camera at once, for the review tools.</summary>
        public void Jump(float x)
        {
            Vector3 position = camera.transform.position;
            position.x = x;
            camera.transform.position = position;

            if (!fixedCamera)
            {
                PlaceBackground(x);
            }
        }

        /// <summary>A short jolt for the heaviest blows.</summary>
        public void Shake()
        {
            shakeLeft = ShakeSeconds;
        }

        public GameObject CreateBlock(string name, Color color, int order, Vector2 size)
        {
            var block = new GameObject(name);
            var renderer = block.AddComponent<SpriteRenderer>();
            renderer.sprite = square;
            renderer.color = color;
            renderer.sortingOrder = order;
            block.transform.localScale = new Vector3(size.x, size.y, 1f);
            return block;
        }

        private void BuildTestMap(IReadOnlyList<MapBox> boxes)
        {
            fixedCamera = false;
            camera.backgroundColor = SkyColor;
            camera.orthographicSize = ViewWidth / camera.aspect / 2f;
            camera.transform.position = new Vector3(ViewWidth / 2f, camera.orthographicSize - GroundMargin, -10f);
            cameraTop = camera.transform.position.y + camera.orthographicSize;

            background = Place(ArtFolder + "bg", ViewWidth / 2f, 0f, BackgroundOrder, false).transform;
            float scale = BackgroundWidth / background.GetComponent<SpriteRenderer>().bounds.size.x;
            background.localScale = new Vector3(scale, scale, 1f);
            PlaceBackground(camera.transform.position.x);

            foreach ((string name, float x, bool large, bool flip) in Props)
            {
                Place(ArtFolder + name, x, GrassBehindFeet - (large ? LargePropSink : SmallPropSink), large ? LargePropOrder : SmallPropOrder, flip);
            }

            foreach (MapBox box in boxes)
            {
                if (box.OneWay)
                {
                    DrawPlatform(box);
                }
                else if (box.Id == "ground")
                {
                    DrawGround(box);
                }
            }
        }

        private void BuildHouse()
        {
            fixedCamera = true;
            camera.backgroundColor = HouseEdgeColor;
            background = null;

            GameObject picture = Place(HouseFolder + "bg", HouseWidth / 2f, 0f, BackgroundOrder, false);
            Vector2 size = picture.GetComponent<SpriteRenderer>().sprite.bounds.size;
            float scale = HouseWidth / size.x;
            picture.transform.localScale = new Vector3(scale, scale, 1f);
            float height = size.y * scale;
            // The floor line of the picture lies on y = 0, where feet stand.
            picture.transform.position = new Vector3(HouseWidth / 2f, height * (0.5f - (1f - HouseFloorShare)), 0f);

            foreach ((string name, float x, float y, float tall, float wide, bool hangs) in HouseProps)
            {
                GameObject item = Place(HouseFolder + name, x, y, y > 0f ? LargePropOrder : SmallPropOrder, false);
                Vector2 art = item.GetComponent<SpriteRenderer>().sprite.bounds.size;
                float sy = tall / art.y;
                float sx = wide > 0f ? wide / art.x : sy;
                item.transform.localScale = new Vector3(sx, sy, 1f);
                item.transform.position = new Vector3(x, hangs ? y - tall / 2f : y + tall / 2f, 0f);
            }

            camera.transform.position = HouseCamera();
        }

        /// <summary>
        /// The whole width of the house on screen, with the floor where it is on the test map; a short
        /// screen cuts the beams and the stones evenly, a tall one shows the colour of the beams.
        /// </summary>
        private Vector3 HouseCamera()
        {
            camera.orthographicSize = HouseWidth / camera.aspect / 2f;
            float size = camera.orthographicSize;
            float pictureHeight = HouseWidth * 941f / 1672f;
            float bottom = -pictureHeight * (1f - HouseFloorShare);
            float top = bottom + pictureHeight;
            float wanted = size - GroundMargin;
            float y = size * 2f >= pictureHeight ? (bottom + top) / 2f : Mathf.Clamp(wanted, bottom + size, top - size);
            return new Vector3(HouseWidth / 2f, y, -10f);
        }

        private void PlaceBackground(float cameraX)
        {
            if (background == null)
            {
                return;
            }

            float height = background.GetComponent<SpriteRenderer>().bounds.size.y;
            float x = cameraX + (MapWidth / 2f - cameraX) * BackgroundDrift;

            background.position = new Vector3(x, cameraTop - height / 2f, 0f);
        }

        private void DrawGround(MapBox box)
        {
            float x = (float)box.MinX;

            while (x < (float)box.MaxX)
            {
                GameObject tile = Place(ArtFolder + "ground", x, 0f, GroundOrder, false);
                Vector2 size = tile.GetComponent<SpriteRenderer>().bounds.size;

                tile.transform.position = new Vector3(x + size.x / 2f, (float)box.MaxY + GrassBehindFeet - size.y * (0.5f - GroundSurfaceShare), 0f);
                x += size.x;
            }
        }

        private void DrawPlatform(MapBox box)
        {
            float x = (float)(box.MinX + box.MaxX) / 2f;

            Place(ArtFolder + PlatformRock, x, GrassBehindFeet - PlatformRockSink, SmallPropOrder, box.MinX > MapWidth / 2f);

            GameObject platform = Place(ArtFolder + "platform", x, 0f, PlatformOrder, false);
            float height = platform.GetComponent<SpriteRenderer>().bounds.size.y;
            Vector3 position = platform.transform.position;

            position.y = (float)box.MaxY - height * (0.5f - PlatformSurfaceShare);
            platform.transform.position = position;
        }

        private GameObject Place(string art, float x, float bottom, int order, bool flip)
        {
            Sprite sprite = Resources.Load<Sprite>(art);
            var item = new GameObject(art);
            var renderer = item.AddComponent<SpriteRenderer>();

            item.transform.SetParent(root, false);
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.flipX = flip;
            item.transform.position = new Vector3(x, bottom + sprite.bounds.size.y / 2f, 0f);
            return item;
        }

        private static Sprite CreateSquare()
        {
            Texture2D texture = Texture2D.whiteTexture;
            var rect = new Rect(0f, 0f, texture.width, texture.height);
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), texture.width);
        }

        /// <summary>
        /// The light at every door: a soft pool on the floor, a few motes drifting up and a small arrow
        /// bobbing above. The art is flat; the fading and moving are done here (docs/ASSETS.md).
        /// </summary>
        private sealed class DoorLight
        {
            private const string Folder = "World/Door/";
            private const int PoolOrder = -8;
            private const int MoteOrder = -7;
            private const float PoolWidth = 1.5f;
            private const float PoolHeight = 0.2f;
            private const float ArrowY = 2.45f;
            private const float ArrowWidth = 0.42f;
            private const float ArrowBob = 0.09f;
            private const float RiseHeight = 1.9f;
            private const float RiseSeconds = 2.6f;

            // Each mote: its art, how far from the door's middle, its size and when in the cycle it starts.
            private static readonly (string Art, float Offset, float Size, float Phase)[] Motes =
            {
                ("star", -0.32f, 0.2f, 0f),
                ("dot", 0.18f, 0.1f, 0.17f),
                ("wisp", -0.05f, 0.42f, 0.33f),
                ("dot", 0.38f, 0.08f, 0.5f),
                ("star", 0.1f, 0.15f, 0.66f),
                ("dot", -0.42f, 0.09f, 0.83f),
            };

            private readonly float x;
            private readonly SpriteRenderer pool;
            private readonly SpriteRenderer arrow;
            private readonly List<(SpriteRenderer Renderer, float Offset, float Phase)> motes = new List<(SpriteRenderer, float, float)>();

            public DoorLight(Transform parent, float x)
            {
                this.x = x;
                pool = Make(parent, "glow", PoolOrder, PoolWidth, PoolHeight);
                arrow = Make(parent, "arrow", MoteOrder, ArrowWidth, 0f);

                foreach ((string art, float offset, float size, float phase) in Motes)
                {
                    motes.Add((Make(parent, art, MoteOrder, size, 0f), offset, phase));
                }
            }

            public void Animate(float time)
            {
                float breathe = 0.5f + 0.5f * Mathf.Sin(time * 2.4f);
                pool.transform.position = new Vector3(x, 0.04f, 0f);
                pool.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.28f, 0.5f, breathe));

                arrow.transform.position = new Vector3(x, ArrowY + ArrowBob * Mathf.Sin(time * 3.2f), 0f);
                arrow.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.75f, 1f, breathe));

                foreach ((SpriteRenderer renderer, float offset, float phase) in motes)
                {
                    float t = Mathf.Repeat(time / RiseSeconds + phase, 1f);
                    float sway = 0.06f * Mathf.Sin((time + phase * 7f) * 2f);
                    renderer.transform.position = new Vector3(x + offset + sway, 0.15f + t * RiseHeight, 0f);

                    // Fades in near the floor and out near the top, so no mote ever pops.
                    float fade = Mathf.Clamp01(t / 0.2f) * Mathf.Clamp01((1f - t) / 0.35f);
                    renderer.color = new Color(1f, 1f, 1f, 0.8f * fade);
                }
            }

            /// <summary>Sized by width; a height of 0 keeps the art's shape.</summary>
            private static SpriteRenderer Make(Transform parent, string art, int order, float width, float height)
            {
                Sprite sprite = Resources.Load<Sprite>(Folder + art);
                var item = new GameObject($"Door {art}");
                item.transform.SetParent(parent, false);
                var renderer = item.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = order;
                Vector2 size = sprite.bounds.size;
                float sx = width / size.x;
                float sy = height > 0f ? height / size.y : sx;
                item.transform.localScale = new Vector3(sx, sy, 1f);
                return renderer;
            }
        }
    }
}
