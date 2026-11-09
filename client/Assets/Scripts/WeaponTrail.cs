using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// A ribbon of water drawn along the blade's path. The blade can turn a long way in one frame,
    /// so its arc is filled in and then revealed a little at a time from the old position forward.
    /// Each sample dies after the same time, so the ribbon dissolves from its tail. A change of
    /// swing starts a new run and the old one is left to finish dissolving.
    /// </summary>
    public sealed class WeaponTrail
    {
        private const int MaxSamples = 96;
        private const int MinStepsPerFrame = 2;
        private const float DegreesPerStep = 6f;
        private const float BackwardLimitDegrees = 90f;
        private const float RevealSamplesPerSecond = 700f;
        private const float Lifetime = 0.18f;
        private const int TailTaperSamples = 6;
        private const int HeadTaperSamples = 2;
        private const int MinRunSamples = 2;

        private readonly WaterRibbon water = new WaterRibbon("Water blade trail", 32);
        private readonly Vector3[] inner = new Vector3[MaxSamples];
        private readonly Vector3[] outer = new Vector3[MaxSamples];
        private readonly float[] ages = new float[MaxSamples];
        private readonly bool[] startsRun = new bool[MaxSamples];
        private readonly int[] sweeps = new int[MaxSamples];
        private readonly Vector3[] waitingInner = new Vector3[MaxSamples];
        private readonly Vector3[] waitingOuter = new Vector3[MaxSamples];
        private readonly Vector3[] path = new Vector3[MaxSamples];
        private readonly float[] widths = new float[MaxSamples];
        private Vector3 lastGrip;
        private Vector3 lastInner;
        private Vector3 lastOuter;
        private bool wasDrawing;
        private bool nextStartsRun = true;
        private int count;
        private int waitingStart;
        private int waitingEnd;
        private float strength;
        private float clock;
        private float revealBudget;
        private int lastSweep;

        /// <param name="drawing">True while the blade should leave a mark.</param>
        /// <param name="grip">The point the blade turns around.</param>
        /// <param name="bladeInner">The part of the blade the ribbon starts from.</param>
        /// <param name="bladeOuter">The tip of the blade.</param>
        /// <param name="sweep">The way the blade swings on screen: 1 is anticlockwise, -1 clockwise.</param>
        /// <param name="opacity">0 to 1; heavier blows leave a brighter mark.</param>
        public void Show(bool drawing, Vector3 grip, Vector3 bladeInner, Vector3 bladeOuter, int sweep, float opacity, float deltaTime)
        {
            Age(deltaTime);
            clock += deltaTime;

            if (drawing && wasDrawing && (sweep != lastSweep || (grip - lastGrip).sqrMagnitude > 4f))
            {
                wasDrawing = false;
            }

            if (drawing)
            {
                strength = opacity;

                if (!wasDrawing)
                {
                    FinishWaiting();
                    nextStartsRun = true;
                }
                else if (bladeOuter != lastOuter)
                {
                    FillArc(grip, bladeInner, bladeOuter, sweep);
                }

                lastGrip = grip;
                lastInner = bladeInner;
                lastOuter = bladeOuter;
            }

            wasDrawing = drawing;
            lastSweep = sweep;
            Reveal(deltaTime, sweep);
            Draw();
        }

        private void Age(float deltaTime)
        {
            int kept = 0;
            bool carriedStart = false;

            for (int sample = 0; sample < count; sample++)
            {
                float age = ages[sample] + deltaTime;

                if (age < Lifetime)
                {
                    inner[kept] = inner[sample];
                    outer[kept] = outer[sample];
                    ages[kept] = age;
                    startsRun[kept] = startsRun[sample] || carriedStart;
                    sweeps[kept] = sweeps[sample];
                    carriedStart = false;
                    kept++;
                }
                else
                {
                    carriedStart |= startsRun[sample];
                }
            }

            count = kept;
        }

        private void FillArc(Vector3 grip, Vector3 bladeInner, Vector3 bladeOuter, int sweep)
        {
            float angle = Vector3.SignedAngle(lastOuter - lastGrip, bladeOuter - grip, Vector3.forward);

            if (angle * sweep < -BackwardLimitDegrees)
            {
                angle += 360f * sweep;
            }

            int steps = Mathf.Max(MinStepsPerFrame, Mathf.CeilToInt(Mathf.Abs(angle) / DegreesPerStep));

            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                Vector3 centre = Vector3.Lerp(lastGrip, grip, t);

                Wait(
                    centre + AlongArc(lastInner - lastGrip, bladeInner - grip, angle, t),
                    centre + AlongArc(lastOuter - lastGrip, bladeOuter - grip, angle, t));
            }
        }

        private static Vector3 AlongArc(Vector3 from, Vector3 to, float angle, float t)
        {
            return Quaternion.AngleAxis(angle * t, Vector3.forward) * from.normalized * Mathf.Lerp(from.magnitude, to.magnitude, t);
        }

        private void Wait(Vector3 near, Vector3 far)
        {
            if (waitingEnd == MaxSamples)
            {
                FinishWaiting();
            }

            waitingInner[waitingEnd] = near;
            waitingOuter[waitingEnd] = far;
            waitingEnd++;
        }

        private void Reveal(float deltaTime, int sweep)
        {
            revealBudget += RevealSamplesPerSecond * deltaTime;

            while (revealBudget >= 1f && waitingStart < waitingEnd)
            {
                Add(waitingInner[waitingStart], waitingOuter[waitingStart], sweep);
                waitingStart++;
                revealBudget -= 1f;
            }

            if (waitingStart == waitingEnd)
            {
                waitingStart = 0;
                waitingEnd = 0;
                revealBudget = 0f;
            }
        }

        /// <summary>Shows at once whatever is still waiting, before a new swing begins.</summary>
        private void FinishWaiting()
        {
            while (waitingStart < waitingEnd)
            {
                Add(waitingInner[waitingStart], waitingOuter[waitingStart], lastSweep);
                waitingStart++;
            }

            waitingStart = 0;
            waitingEnd = 0;
            revealBudget = 0f;
        }

        private void Add(Vector3 near, Vector3 far, int sweep)
        {
            if (count == MaxSamples)
            {
                System.Array.Copy(inner, 1, inner, 0, count - 1);
                System.Array.Copy(outer, 1, outer, 0, count - 1);
                System.Array.Copy(ages, 1, ages, 0, count - 1);
                System.Array.Copy(startsRun, 1, startsRun, 0, count - 1);
                System.Array.Copy(sweeps, 1, sweeps, 0, count - 1);
                count--;
            }

            inner[count] = near;
            outer[count] = far;
            ages[count] = 0f;
            startsRun[count] = nextStartsRun;
            sweeps[count] = sweep;
            nextStartsRun = false;
            count++;
        }

        private void Draw()
        {
            if (count < MinRunSamples)
            {
                water.Hide();
                return;
            }

            water.Begin(Mathf.Lerp(0.82f, 1f, strength));
            int first = 0;

            for (int sample = 1; sample <= count; sample++)
            {
                if (sample == count || startsRun[sample])
                {
                    DrawRun(first, sample - first);
                    first = sample;
                }
            }

            water.End();
        }

        private void DrawRun(int first, int length)
        {
            if (length < MinRunSamples)
            {
                return;
            }

            for (int place = 0; place < length; place++)
            {
                int sample = first + place;
                float life = Mathf.Clamp01(1f - ages[sample] / Lifetime);
                float tail = Mathf.Clamp01(place / (float)TailTaperSamples);
                float head = Mathf.Clamp01((length - 1 - place) / (float)HeadTaperSamples);

                // Water hugs the outer blade arc. The open centre leaves the fighter readable.
                path[place] = Vector3.Lerp(inner[sample], outer[sample], 0.82f);
                widths[place] = (outer[sample] - inner[sample]).magnitude * Mathf.Lerp(0.16f, 0.25f, strength)
                    * Mathf.Sqrt(life) * tail * head;
            }

            // A clockwise swing has its outside on the left of its path.
            water.BladeStroke(path, widths, length, Mathf.Floor(clock * 18f) * 0.7f, sweeps[first] < 0 ? 1f : -1f);
        }
    }
}
