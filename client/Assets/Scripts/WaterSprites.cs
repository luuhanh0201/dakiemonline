using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Hand-drawn water from docs/canh-mau/hieu-ung: frame sequences and loose pieces. A sequence
    /// steps through its drawings while the code moves, scales and fades it every frame, so the
    /// steps between drawings are hidden by smooth motion.
    /// </summary>
    internal sealed class WaterSprites
    {
        private const string Folder = "World/Fx/";
        private const int PieceCount = 16;
        private const float Gravity = 9f;

        // All under the red warning at 41.
        private const int RingOrder = 35;
        private const int StreamOrder = 36;
        private const int WaveOrder = 37;
        private const int ColumnOrder = 38;
        private const int SprayOrder = 39;

        // A drawing appears this much larger and snaps back, which gives each burst its force.
        private const float WavePunch = 0.2f;
        private const float ColumnPunch = 0.35f;
        private const float PunchSeconds = 0.07f;

        // Pieces in the sheet of sixteen.
        private const int FoamPiece = 0;
        private const int DropPiece = 4;
        private const int FlyingDropPiece = 5;
        private const int RingPiece = 12;
        private const int PuddlePiece = 13;

        // Seconds each drawing shows: quick while the water rises, longer as it peaks and settles.
        private static readonly float[] WaveHolds = { 0.035f, 0.035f, 0.04f, 0.045f, 0.05f, 0.055f, 0.06f, 0.07f };
        private static readonly float[] ColumnHolds = { 0.035f, 0.035f, 0.04f, 0.05f, 0.05f, 0.055f, 0.06f, 0.07f };

        // The wave head runs forward fast and slows as it breaks.
        private const float WaveStart = 0.25f;
        private const float WaveSpeed = 9f;
        private const float WaveSlowing = 6f;
        private const float WaveHeightShare = 1.15f;
        private const float BreakPoint = 0.85f;
        private const float ColumnHeight = 1.5f;
        private const float StreamHeight = 0.3f;
        private const float StreamUnderWave = 0.3f;

        // A landed blow bursts on the target; a heavy one is larger.
        private static readonly float[] HitHolds = { 0.03f, 0.035f, 0.04f, 0.05f, 0.06f, 0.07f };
        private const float HitHeight = 0.9f;
        private const float HeavyHitHeight = 1.3f;
        private const float HitPunch = 0.25f;

        // The flying blade has a life of its own: it forms, flies with water running through it,
        // then breaks on what it hits or falls apart when it runs out of range. All its drawings
        // share one scale, set so the flying blade is ShotHeight tall.
        private const float ShotHeight = 0.85f;
        private const float FlyingBladePixels = 215f;
        private const float BladeUnit = ShotHeight / FlyingBladePixels;
        private const float BladeWidth = 210f * BladeUnit;
        private const float BladeBehind = 0.1f;
        private const float ShotFramesPerSecond = 16f;
        private static readonly float[] FormHolds = { 0.03f, 0.03f, 0.035f, 0.04f };
        private static readonly float[] BreakHolds = { 0.03f, 0.035f, 0.04f, 0.045f, 0.05f, 0.06f, 0.07f, 0.08f };
        private static readonly float[] FadeHolds = { 0.05f, 0.06f, 0.07f, 0.09f };
        private const float BreakPunch = 0.15f;
        private const float FadeDrift = 2.5f;
        private const float FadeSlowing = 6f;

        // The blade tears the air: a faint haze trails it, and soft pressure ripples peel off its
        // front and fade where they were left. Nothing is drawn as a clear line.
        private const int AirOrder = 38;
        private const float WakeLength = 2.2f;
        private const float WakeHeight = 0.75f;
        private const float WakeStrength = 0.2f;
        private const float RippleSeconds = 0.045f;
        private const float RippleLife = 0.18f;
        private const float RippleHeight = 0.95f;
        private const float RippleStrength = 0.32f;
        private const float RippleGrowth = 0.9f;
        private static readonly Color AirColor = new Color(0.88f, 0.92f, 1f, 1f);

        // Healing: water rises from a ring at the feet, winds round the body and lifts away.
        private static readonly float[] HealHolds = { 0.045f, 0.05f, 0.055f, 0.06f, 0.065f, 0.07f, 0.08f, 0.09f };
        private const float HealHeight = 1.75f;
        private const float HealPunch = 0.12f;

        // The ultimate: water gathers round the feet through the first two blows, then a wall of
        // water rises in front and crashes down on the last one.
        private const float GatherHeight = 0.6f;
        private const float GatherHold = 0.07f;
        private const float GatherPunch = 0.08f;
        private static readonly float[] WallHolds = { 0.035f, 0.04f, 0.045f, 0.07f, 0.05f, 0.06f, 0.07f, 0.09f };
        private const float WallBehind = 0.15f;
        private const float WallOverhang = 0.5f;
        private const float WallPunch = 0.15f;
        private const int CrashFrame = 4;

        // The lunge leaves a low run of water along its whole path.
        private static readonly float[] DashHolds = { 0.03f, 0.03f, 0.035f, 0.04f, 0.05f, 0.055f, 0.06f, 0.07f };
        private const float DashOverhang = 0.6f;
        private const float DashBehind = 0.3f;
        private const float DashPunch = 0.12f;

        private readonly Sprite[] wave;
        private readonly Sprite[] column;
        private readonly Sprite[] pieces = new Sprite[PieceCount];
        private readonly Sprite stream;
        private readonly Sprite[] hit;
        private readonly Sprite[] bladeForm;
        private readonly Sprite[] bladeFly;
        private readonly Sprite[] bladeBreak;
        private readonly Sprite[] bladeFade;
        private readonly Sprite[] dash;
        private readonly Sprite[] heal;
        private readonly Sprite[] gather;
        private readonly Sprite[] wall;
        private readonly List<Item> items = new List<Item>();
        private readonly Dictionary<Projectile, Flight> flights = new Dictionary<Projectile, Flight>();
        private readonly List<Flight> spareFlights = new List<Flight>();
        private readonly List<Projectile> ended = new List<Projectile>();
        private readonly Sprite airWake = MakeAirWake();
        private readonly Sprite airRipple = MakeAirRipple();
        private float rippleClock;

        public WaterSprites()
        {
            wave = LoadSequence("wave", 8);
            column = LoadSequence("column", 8);
            hit = LoadSequence("hit", 6);
            bladeForm = LoadSequence("blade_form", 4);
            bladeFly = LoadSequence("blade_fly", 8);
            bladeBreak = LoadSequence("blade_break", 8);
            bladeFade = LoadSequence("blade_fade", 4);
            dash = LoadSequence("dash", 8);
            heal = LoadSequence("heal", 8);
            gather = LoadSequence("gather", 4);
            wall = LoadSequence("ult", 8);
            for (int i = 0; i < PieceCount; i++) pieces[i] = Resources.Load<Sprite>(Folder + "piece_" + i);
            stream = Resources.Load<Sprite>(Folder + "band_1");
        }

        public bool Available => wave != null && column != null && stream != null && pieces[RingPiece] != null;

        public bool HasHit => hit != null;
        public bool HasShots => bladeForm != null && bladeFly != null && bladeBreak != null && bladeFade != null;
        public bool HasDash => dash != null && pieces[DropPiece] != null;
        public bool HasHeal => heal != null;
        public bool HasUltimate => gather != null && wall != null && pieces[DropPiece] != null;

        /// <summary>Water winding up round the body from the feet.</summary>
        public void PlayHeal(Vector2 feet)
        {
            Next(SprayOrder).Sequence(heal, HealHolds).At(feet + Vector2.down * 0.03f, Anchor.Bottom, 1, HealHeight)
                .Punching(HealPunch).Lasting(0f, Sum(HealHolds), 0.75f);
        }

        /// <summary>Water churning round the feet for the given time; each loop starts with a small swell.</summary>
        public void PlayGather(Vector2 feet, int facing, float seconds)
        {
            var holds = new float[gather.Length];
            for (int i = 0; i < holds.Length; i++) holds[i] = GatherHold;
            float loop = Sum(holds);
            int loops = Mathf.Max(1, Mathf.CeilToInt(seconds / loop));

            for (int i = 0; i < loops; i++)
            {
                bool last = i == loops - 1;
                Next(RingOrder).Sequence(gather, holds).At(feet + Vector2.down * 0.03f, Anchor.Bottom, facing, GatherHeight)
                    .Punching(GatherPunch).Lasting(i * loop, loop, last ? 0.4f : 1f);
            }
        }

        /// <summary>The last blow: a wall of water rises in front and crashes. Returns when it lands.</summary>
        public float PlayWall(Vector2 feet, int facing, float reach)
        {
            float width = reach + WallOverhang;
            float height = width * wall[3].bounds.size.y / wall[3].bounds.size.x;
            Vector2 back = feet + Vector2.right * facing * WallBehind + Vector2.down * 0.03f;

            Next(WaveOrder).Sequence(wall, WallHolds).At(back, Anchor.BottomBack, facing, height)
                .Punching(WallPunch).Lasting(0f, Sum(WallHolds), 0.8f);

            float crash = 0f;
            for (int i = 0; i < CrashFrame; i++) crash += WallHolds[i];

            // Spray thrown up where the wall comes down.
            Vector2 landing = new Vector2(back.x + facing * width * 0.8f, feet.y + height * 0.3f);
            for (int i = 0; i < 8; i++)
            {
                int piece = i % 3 == 0 ? FoamPiece : i % 2 == 1 ? FlyingDropPiece : DropPiece;
                var velocity = new Vector2(facing * Random.Range(-0.6f, 2.4f), Random.Range(1.8f, 3.6f));
                Next(SprayOrder).Single(pieces[piece]).At(landing, Anchor.Centre, facing, Random.Range(0.14f, 0.28f))
                    .Moving(velocity, 0f, Gravity).Aligned(piece == FlyingDropPiece).Lasting(crash, 0.5f, 0.45f);
            }

            return crash;
        }

        public void Clear()
        {
            foreach (Item item in items) item.Stop();
            foreach (Flight flight in flights.Values) Retire(flight);
            flights.Clear();
        }

        /// <summary>Water bursting from a landed blow, thrown the way the attacker faces.</summary>
        public void PlayHit(Vector2 place, int facing, bool heavy)
        {
            Next(SprayOrder).Sequence(hit, HitHolds).At(place, Anchor.Centre, facing, heavy ? HeavyHitHeight : HitHeight)
                .Punching(HitPunch).Lasting(0f, Sum(HitHolds), 0.7f);
        }

        /// <summary>
        /// Draws each flying blade where the rules put it, tearing the air as it goes. A blade that is
        /// gone breaks where it was if one of this frame's ranged hits was its own, otherwise it falls apart.
        /// </summary>
        public void ShowShots(IReadOnlyList<Projectile> flying, int landed, float deltaTime)
        {
            rippleClock += deltaTime;
            bool ripple = rippleClock >= RippleSeconds;
            if (ripple) rippleClock = 0f;
            float formed = Sum(FormHolds);

            foreach (Flight flight in flights.Values) flight.Seen = false;

            foreach (Projectile blade in flying)
            {
                if (!flights.TryGetValue(blade, out Flight flight))
                {
                    flight = TakeFlight();
                    flights.Add(blade, flight);
                }
                else
                {
                    flight.Age += deltaTime;
                }

                flight.Seen = true;
                flight.Direction = blade.Direction;
                flight.Place = new Vector2((float)blade.X - blade.Direction * BladeBehind, (float)blade.Y);

                Sprite frame = flight.Age < formed
                    ? FrameAt(bladeForm, FormHolds, flight.Age)
                    : bladeFly[Mathf.FloorToInt((flight.Age - formed) * ShotFramesPerSecond) % bladeFly.Length];
                float scale = BladeUnit * frame.pixelsPerUnit;
                SpriteRenderer view = flight.View;
                view.gameObject.SetActive(true);
                view.sprite = frame;
                view.flipX = blade.Direction < 0;
                view.transform.localScale = new Vector3(scale, scale, 1f);
                view.transform.position = new Vector3(flight.Place.x, flight.Place.y, 0f);

                // The haze begins inside the blade and thins out far behind it; it builds up while the blade forms.
                SpriteRenderer wake = flight.Wake;
                wake.gameObject.SetActive(true);
                var tint = AirColor;
                tint.a = WakeStrength * Mathf.Clamp01(flight.Age / formed);
                wake.color = tint;
                wake.transform.position = new Vector3(flight.Place.x + blade.Direction * BladeWidth * 0.1f, flight.Place.y, 0f);
                wake.transform.localScale = new Vector3(blade.Direction * WakeLength / airWake.bounds.size.x, WakeHeight / airWake.bounds.size.y, 1f);

                if (ripple && flight.Age >= formed)
                {
                    var front = new Vector2(flight.Place.x + blade.Direction * BladeWidth * 0.15f, flight.Place.y);
                    Next(AirOrder).Single(airRipple).At(front, Anchor.Centre, blade.Direction, RippleHeight)
                        .Growing(RippleGrowth).Faint(RippleStrength).Lasting(0f, RippleLife, 0f);
                }
            }

            ended.Clear();
            foreach (KeyValuePair<Projectile, Flight> pair in flights)
                if (!pair.Value.Seen) ended.Add(pair.Key);

            foreach (Projectile blade in ended)
            {
                Flight flight = flights[blade];
                flights.Remove(blade);
                if (landed > 0)
                {
                    landed--;
                    Next(SprayOrder).Sequence(bladeBreak, BreakHolds).At(flight.Place, Anchor.Centre, flight.Direction, BladeHeight(bladeBreak))
                        .Punching(BreakPunch).Lasting(0f, Sum(BreakHolds), 0.75f)
                        .Advance(0f);
                }
                else
                {
                    Next(SprayOrder).Sequence(bladeFade, FadeHolds).At(flight.Place, Anchor.Centre, flight.Direction, BladeHeight(bladeFade))
                        .Moving(new Vector2(flight.Direction * FadeDrift, 0f), FadeSlowing, 0f).Lasting(0f, Sum(FadeHolds), 0.6f)
                        .Advance(0f);
                }

                Retire(flight);
            }
        }

        private static float BladeHeight(Sprite[] frames) => frames[0].rect.height * BladeUnit;

        private static Sprite FrameAt(Sprite[] frames, float[] holds, float age)
        {
            float elapsed = 0f;
            for (int i = 0; i < frames.Length; i++)
            {
                elapsed += holds[i];
                if (age < elapsed) return frames[i];
            }

            return frames[frames.Length - 1];
        }

        private Flight TakeFlight()
        {
            if (spareFlights.Count > 0)
            {
                Flight spare = spareFlights[spareFlights.Count - 1];
                spareFlights.RemoveAt(spareFlights.Count - 1);
                spare.Age = 0f;
                return spare;
            }

            var view = new GameObject("Drawn blade").AddComponent<SpriteRenderer>();
            view.sortingOrder = SprayOrder;
            var wake = new GameObject("Air wake").AddComponent<SpriteRenderer>();
            wake.sprite = airWake;
            wake.sortingOrder = AirOrder;
            return new Flight { View = view, Wake = wake };
        }

        private void Retire(Flight flight)
        {
            flight.View.gameObject.SetActive(false);
            flight.Wake.gameObject.SetActive(false);
            spareFlights.Add(flight);
        }

        /// <summary>One blade in the air and the haze behind it.</summary>
        private sealed class Flight
        {
            public SpriteRenderer View;
            public SpriteRenderer Wake;
            public Vector2 Place;
            public float Age;
            public int Direction;
            public bool Seen;
        }

        /// <summary>A soft haze, widest where it leaves the blade at the right, thinning away to the left.</summary>
        private static Sprite MakeAirWake()
        {
            const int Width = 256;
            const int Height = 64;
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < Height; y++)
            {
                float across = Mathf.Abs((y + 0.5f) / Height - 0.5f) * 2f;
                for (int x = 0; x < Width; x++)
                {
                    float along = (x + 0.5f) / Width;
                    float spread = Mathf.Lerp(0.15f, 0.8f, along);
                    float haze = Mathf.Exp(-Mathf.Pow(across / spread, 2f));
                    float ends = Mathf.Pow(along, 1.8f) * Mathf.SmoothStep(0f, 1f, (1f - along) / 0.25f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, haze * ends));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, Width, Height), new Vector2(1f, 0.5f), Height);
        }

        /// <summary>A soft arc bulging to the right, like a pressure wave in front of a fast blade.</summary>
        private static Sprite MakeAirRipple()
        {
            const int Size = 128;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    var point = new Vector2((x + 0.5f) / Size - 0.15f, (y + 0.5f) / Size - 0.5f);
                    float ring = Mathf.Exp(-Mathf.Pow((point.magnitude - 0.38f) / 0.05f, 2f));
                    float angle = Mathf.Abs(Mathf.Atan2(point.y, point.x)) / (Mathf.PI * 0.42f);
                    float arc = point.x > 0f ? Mathf.Clamp01(1f - angle * angle) : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, ring * arc));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), Size);
        }

        /// <summary>The lunge: a low run of water from where it began, spraying up at its far end.</summary>
        public void PlayDash(Vector2 feet, int facing, float reach)
        {
            float width = reach + DashOverhang;
            float height = width * dash[0].bounds.size.y / dash[0].bounds.size.x;
            Vector2 back = feet - Vector2.right * facing * DashBehind + Vector2.down * 0.02f;

            Next(WaveOrder).Sequence(dash, DashHolds).At(back, Anchor.BottomBack, facing, height)
                .Punching(DashPunch).Lasting(0f, Sum(DashHolds), 0.8f);

            float spray = DashHolds[0] + DashHolds[1] + DashHolds[2];
            Vector2 front = new Vector2(back.x + facing * width * 0.9f, feet.y + height * 0.5f);
            for (int i = 0; i < 5; i++)
            {
                int piece = i % 2 == 0 ? FlyingDropPiece : DropPiece;
                var velocity = new Vector2(facing * Random.Range(0.6f, 2f), Random.Range(1.2f, 2.6f));
                Next(SprayOrder).Single(pieces[piece]).At(front, Anchor.Centre, facing, Random.Range(0.1f, 0.18f))
                    .Moving(velocity, 0f, Gravity).Aligned(piece == FlyingDropPiece).Lasting(spray, 0.4f, 0.45f);
            }
        }

        public void Advance(float deltaTime)
        {
            foreach (Item item in items) item.Advance(deltaTime);
        }

        /// <summary>The wave skill: a stream along the ground, a wave that rises and breaks, then a column of spray.</summary>
        public float PlayWave(Vector2 feet, int facing, float height)
        {
            float waveHeight = height * WaveHeightShare;
            float waveWidth = waveHeight * wave[0].bounds.size.x / wave[0].bounds.size.y;
            float waveTime = Sum(WaveHolds);
            var back = feet + Vector2.right * facing * WaveStart;

            Item head = Next(WaveOrder).Sequence(wave, WaveHolds).At(back, Anchor.BottomBack, facing, waveHeight)
                .Moving(Vector2.right * facing * WaveSpeed, WaveSlowing, 0f).Punching(WavePunch).Lasting(0f, waveTime, 0.8f);

            Next(StreamOrder).Single(stream).At(feet + Vector2.right * facing * 0.15f + Vector2.down * 0.03f, Anchor.BottomBack, facing, StreamHeight)
                .Stretching(head, StreamUnderWave).Lasting(0f, waveTime + 0.08f, 0.6f);

            // Spray leaves the crest as it curls over.
            float curl = WaveHolds[0] + WaveHolds[1] + WaveHolds[2] + WaveHolds[3];
            Vector2 crest = new Vector2(back.x + facing * (Travel(curl) + waveWidth * 0.75f), feet.y + waveHeight * 0.85f);
            for (int i = 0; i < 7; i++)
            {
                int piece = i == 0 ? FoamPiece : i % 2 == 1 ? FlyingDropPiece : DropPiece;
                var velocity = new Vector2(facing * Random.Range(0.8f, 2.6f), Random.Range(1.2f, 3.2f));
                Next(SprayOrder).Single(pieces[piece]).At(crest, Anchor.Centre, facing, Random.Range(0.14f, 0.26f))
                    .Moving(velocity, 0f, Gravity).Aligned(piece == FlyingDropPiece).Lasting(curl, 0.42f, 0.45f);
            }

            // Where the wave breaks, a column of water shoots up and falls back.
            float breakTime = waveTime * 0.85f;
            var breakPlace = new Vector2(back.x + facing * (Travel(waveTime) + waveWidth * BreakPoint), feet.y);
            Next(ColumnOrder).Sequence(column, ColumnHolds).At(breakPlace, Anchor.Bottom, facing, ColumnHeight)
                .Punching(ColumnPunch).Lasting(breakTime, Sum(ColumnHolds), 0.85f);
            Next(RingOrder).Single(pieces[RingPiece]).At(breakPlace + Vector2.down * 0.02f, Anchor.Bottom, facing, 0.12f)
                .Growing(1.2f).Lasting(breakTime, 0.45f, 0.4f);
            Next(RingOrder).Single(pieces[PuddlePiece]).At(breakPlace + Vector2.down * 0.03f, Anchor.Bottom, facing, 0.1f)
                .Lasting(breakTime + 0.12f, 0.5f, 0.5f);

            float columnTop = breakTime + ColumnHolds[0] + ColumnHolds[1] + ColumnHolds[2] + ColumnHolds[3];
            for (int i = 0; i < 5; i++)
            {
                var velocity = new Vector2(Random.Range(-1.2f, 1.2f), Random.Range(1.5f, 3f));
                Next(SprayOrder).Single(pieces[DropPiece]).At(breakPlace + Vector2.up * ColumnHeight * 0.8f, Anchor.Centre, facing, Random.Range(0.11f, 0.2f))
                    .Moving(velocity, 0f, Gravity).Lasting(columnTop, 0.45f, 0.5f);
            }

            return breakTime;
        }

        private static float Travel(float seconds)
        {
            return WaveSpeed / WaveSlowing * (1f - Mathf.Exp(-WaveSlowing * seconds));
        }

        private static float Sum(float[] values)
        {
            float total = 0f;
            foreach (float value in values) total += value;
            return total;
        }

        private static Sprite[] LoadSequence(string name, int length)
        {
            var frames = new Sprite[length];
            for (int i = 0; i < length; i++)
            {
                frames[i] = Resources.Load<Sprite>(Folder + name + "_" + i);
                if (frames[i] == null) return null;
            }

            return frames;
        }

        private Item Next(int order)
        {
            foreach (Item item in items)
            {
                if (!item.Playing) return item.Reset(order);
            }

            var created = new Item();
            items.Add(created);
            return created.Reset(order);
        }

        private enum Anchor
        {
            Centre,
            Bottom,
            BottomBack,
        }

        /// <summary>One drawing or sequence, placed and moved in world units.</summary>
        private sealed class Item
        {
            private readonly SpriteRenderer renderer;
            private readonly Transform body;
            private Sprite[] frames;
            private float[] holds;
            private Vector2 origin;
            private Vector2 velocity;
            private Anchor anchor;
            private Item stretchTo;
            private float stretchOverlap;
            private float slowing;
            private float gravity;
            private float height;
            private float growth;
            private float delay;
            private float life;
            private float fadeShare;
            private float age;
            private int facing;
            private bool aligned;
            private float punch;
            private float strength = 1f;

            public Item()
            {
                renderer = new GameObject("Drawn water").AddComponent<SpriteRenderer>();
                body = renderer.transform;
                renderer.gameObject.SetActive(false);
            }

            public bool Playing { get; private set; }

            /// <summary>The back edge of the drawing, for a stream that runs up to it.</summary>
            private float BackX => Position(age - delay).x;

            public Item Reset(int order)
            {
                renderer.sortingOrder = order;
                punch = 0f;
                strength = 1f;
                frames = null;
                holds = null;
                velocity = Vector2.zero;
                stretchTo = null;
                slowing = 0f;
                gravity = 0f;
                growth = 0f;
                age = 0f;
                aligned = false;
                Playing = true;
                return this;
            }

            public Item Sequence(Sprite[] drawings, float[] times)
            {
                frames = drawings;
                holds = times;
                return this;
            }

            public Item Single(Sprite drawing)
            {
                frames = new[] { drawing };
                holds = null;
                return this;
            }

            public Item At(Vector2 place, Anchor how, int way, float drawnHeight)
            {
                origin = place;
                anchor = how;
                facing = way;
                height = drawnHeight;
                return this;
            }

            public Item Moving(Vector2 speed, float slowingRate, float fall)
            {
                velocity = speed;
                slowing = slowingRate;
                gravity = fall;
                return this;
            }

            public Item Stretching(Item target, float overlap)
            {
                stretchTo = target;
                stretchOverlap = overlap;
                return this;
            }

            public Item Growing(float amount)
            {
                growth = amount;
                return this;
            }

            public Item Faint(float alpha)
            {
                strength = alpha;
                return this;
            }

            public Item Punching(float amount)
            {
                punch = amount;
                return this;
            }

            public Item Aligned(bool alongPath)
            {
                aligned = alongPath;
                return this;
            }

            public Item Lasting(float wait, float seconds, float fadeFrom)
            {
                delay = wait;
                life = seconds;
                fadeShare = fadeFrom;
                renderer.gameObject.SetActive(false);
                return this;
            }

            public void Stop()
            {
                Playing = false;
                renderer.gameObject.SetActive(false);
            }

            public void Advance(float deltaTime)
            {
                if (!Playing) return;

                age += deltaTime;
                float local = age - delay;

                if (local < 0f)
                {
                    return;
                }

                if (local >= life)
                {
                    Stop();
                    return;
                }

                renderer.gameObject.SetActive(true);
                renderer.sprite = Frame(local);
                float share = local / life;

                Vector2 size = renderer.sprite.bounds.size;
                float scale = height / size.y * (1f + growth * share) * (1f + punch * Mathf.Exp(-local / PunchSeconds));
                float width = size.x * scale;
                float xScale = scale;

                if (stretchTo != null)
                {
                    width = Mathf.Max(0.05f, Mathf.Abs(stretchTo.BackX - origin.x) + stretchOverlap);
                    xScale = width / size.x;
                }

                Vector2 place = Position(local);
                float drawnHeight = size.y * scale;
                Vector2 centre = anchor == Anchor.Centre ? place
                    : anchor == Anchor.Bottom ? place + Vector2.up * drawnHeight / 2f
                    : place + new Vector2(facing * width / 2f, drawnHeight / 2f);

                body.position = new Vector3(centre.x, centre.y, 0f);
                body.localScale = new Vector3(xScale, scale, 1f);

                if (aligned)
                {
                    Vector2 way = velocity + Vector2.down * gravity * local;
                    renderer.flipX = false;
                    body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(way.y, way.x) * Mathf.Rad2Deg);
                }
                else
                {
                    renderer.flipX = facing < 0;
                    body.localRotation = Quaternion.identity;
                }

                Color color = Color.white;
                color.a = strength * (share < fadeShare ? 1f : Mathf.InverseLerp(1f, fadeShare, share));
                renderer.color = color;
            }

            private Sprite Frame(float local)
            {
                if (holds == null) return frames[0];

                float elapsed = 0f;
                for (int i = 0; i < frames.Length; i++)
                {
                    elapsed += holds[i];
                    if (local < elapsed) return frames[i];
                }

                return frames[frames.Length - 1];
            }

            private Vector2 Position(float local)
            {
                local = Mathf.Max(0f, local);
                Vector2 moved = slowing > 0f
                    ? velocity / slowing * (1f - Mathf.Exp(-slowing * local))
                    : velocity * local;
                return origin + moved + Vector2.down * gravity * local * local / 2f;
            }
        }
    }
}
