using System.Collections.Generic;
using System.Linq;
using Game.Net;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Draws the map the player is on from its art. On the test map the camera follows the character;
    /// in the house it follows too, with an empty corner past the door so the buttons never cover it.
    /// Every door gets the same light effect.
    /// </summary>
    public sealed class WorldView
    {
        private const string ArtFolder = "World/";
        private const string HouseFolder = "World/House/";
        private const string HouseMapId = "map.house";
        private const string VillageMapId = "map.village";
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

        // The house picture is 2067 x 941 px at the scale of the approved 1672 px one shown 12 units wide, so
        // there is an empty corner past the door; its floor meets the wall 715 px down.
        private const float HousePixelsPerUnit = 1672f / ViewWidth;
        private const float HouseWidth = 2067f / HousePixelsPerUnit;
        private const float HouseHeight = 941f / HousePixelsPerUnit;
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
            ("table_1", 8.35f, 0f, 1.6f, 0f, false),
            ("door_open", 11.45f, 0f, 2.75f, 0.8f, false),
        };

        private readonly GameConfig config;
        private readonly Sprite square;
        private readonly Camera camera;
        private readonly List<DoorLight> doors = new List<DoorLight>();
        private MasterLife master;
        private StoveLife stove;
        private VillageScene village;
        private Weather villageWeather;
        private IReadOnlyList<RemoteDummy> villageDummies = System.Array.Empty<RemoteDummy>();
        private Transform root;
        private Transform background;
        private float cameraTop;
        private Vector3 shakeOffset;
        private float shakeLeft;
        private bool house;
        private float mapWidth = MapWidth;

        // After a new map is built the camera starts where it belongs rather than sliding there.
        private bool snap;

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
            snap = true;
            doors.Clear();
            master = null;
            stove = null;
            village = null;
            shakeLeft = 0f;
            shakeOffset = Vector3.zero;
            root = new GameObject($"Map {config.Maps[map].Id}").transform;

            if (config.Maps[map].Id == HouseMapId)
            {
                BuildHouse();
            }
            else if (config.Maps[map].Id == VillageMapId)
            {
                BuildVillage(config.Maps[map]);
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

            if (house)
            {
                position.y = HouseCamera().y;
            }
            else
            {
                // The window or the game frame changed shape: twelve units across still, ground in place.
                float size = ViewWidth / camera.aspect / 2f;

                if (!Mathf.Approximately(camera.orthographicSize, size))
                {
                    camera.orthographicSize = size;
                    position.y = size - GroundMargin;
                    cameraTop = position.y + size;
                }
            }

            float halfView = ViewWidth / 2f;
            float target = Mathf.Clamp(feet.x + facing * LookAhead, halfView, mapWidth - halfView);
            position.x = snap ? target : Mathf.Lerp(position.x, target, 1f - Mathf.Exp(-CameraSmoothing * Time.deltaTime));
            snap = false;

            shakeLeft = Mathf.Max(0f, shakeLeft - Time.deltaTime);
            float amount = shakeLeft / ShakeSeconds;
            float wave = Mathf.Sin(shakeLeft * ShakeTurnsPerSecond * Mathf.PI * 2f);
            shakeOffset = new Vector3(wave * 0.5f, wave, 0f) * ShakeSize * amount;

            camera.transform.position = position + shakeOffset;

            PlaceBackground(position.x);

            foreach (DoorLight door in doors)
            {
                door.Animate(Time.time);
            }

            master?.Animate(Time.time, Time.deltaTime, feet.x);
            stove?.Animate(Time.time, Time.deltaTime);
            village?.Animate(Time.time, Time.deltaTime, feet.x, villageWeather, villageDummies);
        }

        /// <summary>The channel's weather and the dummies of the village, as the server last sent them.</summary>
        public void SetVillage(Weather weather, IReadOnlyList<RemoteDummy> dummies)
        {
            villageWeather = weather;
            villageDummies = dummies ?? System.Array.Empty<RemoteDummy>();
        }

        /// <summary>Moves the camera at once, for the review tools.</summary>
        public void Jump(float x)
        {
            Vector3 position = camera.transform.position;
            position.x = x;
            camera.transform.position = position;

            PlaceBackground(x);
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
            house = false;
            mapWidth = MapWidth;
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

        private void BuildVillage(MapConfig map)
        {
            house = false;
            mapWidth = (float)(map.Boxes.First(box => box.Id == "ground").MaxX - 1d);
            camera.backgroundColor = SkyColor;
            camera.orthographicSize = ViewWidth / camera.aspect / 2f;
            camera.transform.position = new Vector3(ViewWidth / 2f, camera.orthographicSize - GroundMargin, -10f);
            cameraTop = camera.transform.position.y + camera.orthographicSize;
            background = null;

            foreach (MapBox box in map.Boxes)
            {
                if (box.Id == "ground")
                {
                    DrawGround(box);
                }
            }

            village = new VillageScene(root, camera, map);
        }

        private void BuildHouse()
        {
            house = true;
            mapWidth = HouseWidth;
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

            master = new MasterLife(root);
            stove = new StoveLife(root);

            camera.transform.position = HouseCamera();
        }

        /// <summary>
        /// Twelve units of the house across the screen, as on the test map, starting at its left wall. A
        /// short screen cuts the beams and the stones evenly, a tall one shows the colour of the beams.
        /// </summary>
        private Vector3 HouseCamera()
        {
            camera.orthographicSize = ViewWidth / camera.aspect / 2f;
            float size = camera.orthographicSize;
            float pictureHeight = HouseHeight;
            float bottom = -pictureHeight * (1f - HouseFloorShare);
            float top = bottom + pictureHeight;
            float wanted = size - GroundMargin;
            float y = size * 2f >= pictureHeight ? (bottom + top) / 2f : Mathf.Clamp(wanted, bottom + size, top - size);
            return new Vector3(ViewWidth / 2f, y, -10f);
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
        /// The old master, so the house does not feel frozen. Every movement is a drawn frame, played fast
        /// with small steps between frames so it looks smooth: he breathes in and out with his hands behind
        /// his back and now and then blinks; when the player comes close he brings a hand round and holds it
        /// out, nodding, and puts it back when they leave. He turns to face the player. Only the picture
        /// moves; he is not part of the rules.
        /// </summary>
        private sealed class MasterLife
        {
            private const string Folder = HouseFolder + "Master/";
            private const float X = 4.05f;

            // The frames are saved at the character's density, 240.6 px a unit (CharacterRigScale).
            private const float PixelsPerUnit = 802f * 0.3f;

            // Close enough to be greeted, and a little farther before he stops, so he does not flicker at the edge.
            private const float NearUnits = 2.2f;
            private const float FarUnits = 2.7f;
            private const float TurnUnits = 0.3f;

            // Breathing: every idle frame but the last in turn, then a rest breathed out. The last frame is the blink.
            private const float BreathFramesPerSecond = 8f;
            private const float BreathRestSeconds = 0.45f;
            private const float BlinkSeconds = 0.12f;
            private const int BreathsBetweenBlinks = 3;

            // Reaching out plays the invite frames up to the third from last, which holds the hand out; the last two
            // are a nod, halfway then all the way, played down and back up while the hand is held out.
            private const float ReachFramesPerSecond = 16f;
            private const float HoldSeconds = 1.4f;
            private const float NodHalfSeconds = 0.09f;
            private const float NodDownSeconds = 0.32f;
            private const float NodSeconds = NodHalfSeconds * 2f + NodDownSeconds;

            private readonly Transform feet;
            private readonly SpriteRenderer body;
            private readonly List<Sprite> idle = Load("idle_");
            private readonly List<Sprite> invite = Load("invite_");
            private readonly float breathCycle;
            private bool near;
            private int facing = 1;

            // How far the hand is out, in invite frames: 0 behind the back, HoldFrame held out.
            private float reach;
            private float holdClock;

            public MasterLife(Transform parent)
            {
                breathCycle = (idle.Count - 1) / BreathFramesPerSecond + BreathRestSeconds;

                feet = new GameObject("Master").transform;
                feet.SetParent(parent, false);
                feet.position = new Vector3(X, 0f, 0f);

                var item = new GameObject("Master body");
                item.transform.SetParent(feet, false);
                body = item.AddComponent<SpriteRenderer>();
                body.sortingOrder = SmallPropOrder;
                body.sprite = idle[0];

                // Every frame shares one canvas with the feet at its bottom centre.
                float height = idle[0].texture.height / PixelsPerUnit;
                float scale = height / idle[0].bounds.size.y;
                item.transform.localScale = new Vector3(scale, scale, 1f);
                item.transform.localPosition = new Vector3(0f, height / 2f, 0f);
            }

            private int HoldFrame => invite.Count - 3;

            public void Animate(float time, float frameSeconds, float playerX)
            {
                float distance = playerX - X;
                near = Mathf.Abs(distance) < (near ? FarUnits : NearUnits);

                if (Mathf.Abs(distance) > TurnUnits)
                {
                    facing = distance > 0f ? 1 : -1;
                }

                feet.localScale = new Vector3(facing, 1f, 1f);
                reach = Mathf.MoveTowards(reach, near ? HoldFrame : 0f, frameSeconds * ReachFramesPerSecond);

                if (reach <= 0f)
                {
                    holdClock = 0f;
                    body.sprite = BreathFrame(time);
                }
                else if (reach < HoldFrame)
                {
                    holdClock = 0f;
                    body.sprite = invite[Mathf.Clamp(Mathf.RoundToInt(reach), 0, HoldFrame)];
                }
                else
                {
                    holdClock = Mathf.Repeat(holdClock + frameSeconds, HoldSeconds + NodSeconds);
                    float nod = holdClock - HoldSeconds;
                    int frame = nod < 0f ? HoldFrame
                        : nod < NodHalfSeconds ? HoldFrame + 1
                        : nod < NodHalfSeconds + NodDownSeconds ? HoldFrame + 2
                        : HoldFrame + 1;
                    body.sprite = invite[frame];
                }
            }

            private Sprite BreathFrame(float time)
            {
                int round = (int)(time / breathCycle);
                float at = Mathf.Repeat(time, breathCycle);
                int breathing = idle.Count - 1;

                // The rest comes first, breathed out; every few breaths a blink starts it.
                if (at < BreathRestSeconds)
                {
                    return round % BreathsBetweenBlinks == 0 && at < BlinkSeconds ? idle[breathing] : idle[0];
                }

                int frame = (int)((at - BreathRestSeconds) * BreathFramesPerSecond);
                return idle[Mathf.Clamp(frame, 0, breathing - 1)];
            }

            /// <summary>Frames named prefix_1, prefix_2, … in order, as many as there are.</summary>
            private static List<Sprite> Load(string prefix)
            {
                var frames = new List<Sprite>();

                for (int number = 1; ; number++)
                {
                    Sprite frame = Resources.Load<Sprite>($"{Folder}{prefix}{number}");

                    if (frame == null)
                    {
                        return frames;
                    }

                    frames.Add(frame);
                }
            }
        }

        /// <summary>
        /// The little stove on the level 1 potion table: a flame that flickers through six drawn frames,
        /// sparks that pop inside the firebox, and smoke that rises from the pot lid, swelling, swaying
        /// and fading. The table picture has its own flame and smoke painted out. Places are measured on
        /// table_1.png (452 x 324 px shown 1.6 units tall, centred at x 8.35); other levels will need theirs.
        /// </summary>
        private sealed class StoveLife
        {
            private const string Folder = HouseFolder + "Stove/";
            private const int FireOrder = -27;
            private const int SmokeOrder = -26;

            // The bed of coals in the firebox, and the knob on the pot lid.
            private static readonly Vector2 FireBase = new Vector2(7.997f, 0.648f);
            private static readonly Vector2 Lid = new Vector2(7.99f, 1.19f);

            // The firebox opening is 22 px tall above the coals, so the flame stays inside it.
            private const float FireHeight = 0.112f;
            private const float FireFramesPerSecond = 11f;

            private const float SparkEverySecondsMin = 0.35f;
            private const float SparkEverySecondsMax = 1.1f;
            private const float SparkSeconds = 0.55f;
            private const float SparkRise = 0.085f;
            private const float SparkSize = 0.028f;

            private const int SmokePuffs = 5;
            private const float SmokeEverySeconds = 0.75f;
            private const float SmokeSeconds = SmokePuffs * SmokeEverySeconds;
            private const float SmokeRise = 1.15f;
            private const float SmokeDrift = 0.18f;
            private const float SmokeStartWidth = 0.1f;
            private const float SmokeEndWidth = 0.34f;

            private readonly SpriteRenderer fire;
            private readonly Sprite[] fireFrames = new Sprite[6];
            private readonly Sprite[] smokeShapes = new Sprite[4];
            private readonly Sprite[] sparkShapes = new Sprite[3];
            private readonly List<SpriteRenderer> puffs = new List<SpriteRenderer>();
            private readonly List<Spark> sparks = new List<Spark>();
            private readonly System.Random random = new System.Random(7);
            private float firePixelScale;
            private float nextSpark;

            public StoveLife(Transform parent)
            {
                for (int index = 0; index < fireFrames.Length; index++)
                {
                    fireFrames[index] = Resources.Load<Sprite>($"{Folder}fire_{index + 1}");
                }

                for (int index = 0; index < smokeShapes.Length; index++)
                {
                    smokeShapes[index] = Resources.Load<Sprite>($"{Folder}smoke_{index + 1}");
                }

                for (int index = 0; index < sparkShapes.Length; index++)
                {
                    sparkShapes[index] = Resources.Load<Sprite>($"{Folder}ember_{index + 1}");
                }

                // One scale for every frame, so a taller frame shows as a taller flame rather than shrinking.
                float tallest = 0f;

                foreach (Sprite frame in fireFrames)
                {
                    tallest = Mathf.Max(tallest, frame.bounds.size.y);
                }

                firePixelScale = FireHeight / tallest;
                fire = Make(parent, "Stove fire", FireOrder);

                for (int index = 0; index < SmokePuffs; index++)
                {
                    SpriteRenderer puff = Make(parent, $"Stove smoke {index}", SmokeOrder);
                    puff.sprite = smokeShapes[index % smokeShapes.Length];
                    puffs.Add(puff);
                }

                for (int index = 0; index < 4; index++)
                {
                    sparks.Add(new Spark(Make(parent, $"Stove spark {index}", FireOrder + 1)));
                }
            }

            public void Animate(float time, float frameSeconds)
            {
                AnimateFire(time);
                AnimateSparks(time, frameSeconds);
                AnimateSmoke(time);
            }

            private void AnimateFire(float time)
            {
                Sprite frame = fireFrames[(int)(time * FireFramesPerSecond) % fireFrames.Length];
                fire.sprite = frame;

                // Two slow waves on top of the drawn frames, so the loop never looks exactly the same.
                float flutter = 0.06f * Mathf.Sin(time * 13.1f) + 0.04f * Mathf.Sin(time * 7.3f + 1.7f);
                Vector2 size = frame.bounds.size;
                fire.transform.localScale = new Vector3(firePixelScale * (1f - flutter * 0.5f), firePixelScale * (1f + flutter), 1f);
                fire.transform.position = new Vector3(FireBase.x, FireBase.y + size.y * firePixelScale * (1f + flutter) / 2f, 0f);

                float warmth = 0.9f + 0.1f * Mathf.Sin(time * 17.3f + 0.4f);
                fire.color = new Color(1f, warmth, warmth * 0.95f, 1f);
            }

            private void AnimateSparks(float time, float frameSeconds)
            {
                if (time >= nextSpark)
                {
                    nextSpark = time + Mathf.Lerp(SparkEverySecondsMin, SparkEverySecondsMax, (float)random.NextDouble());
                    Spark free = sparks.Find(spark => spark.Left <= 0f);

                    if (free != null)
                    {
                        free.Left = SparkSeconds;
                        free.Drift = ((float)random.NextDouble() - 0.5f) * 0.05f;
                        free.Renderer.sprite = sparkShapes[random.Next(sparkShapes.Length)];
                    }
                }

                foreach (Spark spark in sparks)
                {
                    spark.Left = Mathf.Max(0f, spark.Left - frameSeconds);
                    float t = 1f - spark.Left / SparkSeconds;
                    bool alive = spark.Left > 0f;
                    spark.Renderer.enabled = alive;

                    if (!alive)
                    {
                        continue;
                    }

                    Vector2 size = spark.Renderer.sprite.bounds.size;
                    float scale = SparkSize * (1f - 0.6f * t) / size.x;
                    spark.Renderer.transform.localScale = new Vector3(scale, scale, 1f);
                    spark.Renderer.transform.position = new Vector3(
                        FireBase.x + spark.Drift * t,
                        FireBase.y + 0.03f + SparkRise * Mathf.Sin(t * Mathf.PI * 0.5f),
                        0f);
                    spark.Renderer.color = new Color(1f, 1f, 1f, 1f - t * t);
                }
            }

            private void AnimateSmoke(float time)
            {
                for (int index = 0; index < puffs.Count; index++)
                {
                    SpriteRenderer puff = puffs[index];

                    // Each puff starts a little after the one before and loops; a new loop picks the next shape.
                    float age = time + index * SmokeEverySeconds;
                    int round = (int)(age / SmokeSeconds);
                    float t = Mathf.Repeat(age, SmokeSeconds) / SmokeSeconds;
                    puff.sprite = smokeShapes[(index + round) % smokeShapes.Length];

                    float rise = SmokeRise * (1f - (1f - t) * (1f - t));
                    float sway = (0.02f + 0.07f * t) * Mathf.Sin((t * 2.2f + index * 0.37f) * Mathf.PI * 2f);
                    float width = Mathf.Lerp(SmokeStartWidth, SmokeEndWidth, Mathf.Sqrt(t));
                    float scale = width / puff.sprite.bounds.size.x;

                    puff.transform.position = new Vector3(Lid.x + SmokeDrift * t + sway, Lid.y + 0.04f + rise, 0f);
                    puff.transform.localScale = new Vector3(scale, scale, 1f);
                    puff.transform.rotation = Quaternion.Euler(0f, 0f, 14f * Mathf.Sin((t + index * 0.21f) * Mathf.PI * 2f));

                    // Fades in just above the lid and thins out as it climbs, so no puff appears or vanishes at once.
                    float alpha = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((1f - t) / 0.55f) * 0.85f;
                    puff.color = new Color(1f, 1f, 1f, alpha);
                }
            }

            private static SpriteRenderer Make(Transform parent, string name, int order)
            {
                var item = new GameObject(name);
                item.transform.SetParent(parent, false);
                var renderer = item.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = order;
                return renderer;
            }

            private sealed class Spark
            {
                public Spark(SpriteRenderer renderer)
                {
                    Renderer = renderer;
                    renderer.enabled = false;
                }

                public SpriteRenderer Renderer { get; }
                public float Left { get; set; }
                public float Drift { get; set; }
            }
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
