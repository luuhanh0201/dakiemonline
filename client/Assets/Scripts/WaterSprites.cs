using System.Collections.Generic;
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
        private const int SequenceLength = 8;
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

        private readonly Sprite[] wave;
        private readonly Sprite[] column;
        private readonly Sprite[] pieces = new Sprite[PieceCount];
        private readonly Sprite stream;
        private readonly List<Item> items = new List<Item>();

        public WaterSprites()
        {
            wave = LoadSequence("wave");
            column = LoadSequence("column");
            for (int i = 0; i < PieceCount; i++) pieces[i] = Resources.Load<Sprite>(Folder + "piece_" + i);
            stream = Resources.Load<Sprite>(Folder + "band_1");
        }

        public bool Available => wave != null && column != null && stream != null && pieces[RingPiece] != null;

        public void Clear()
        {
            foreach (Item item in items) item.Stop();
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

        private static Sprite[] LoadSequence(string name)
        {
            var frames = new Sprite[SequenceLength];
            for (int i = 0; i < SequenceLength; i++)
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
                color.a = share < fadeShare ? 1f : Mathf.InverseLerp(1f, fadeShare, share);
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
