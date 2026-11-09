using UnityEngine;

namespace DaKiem.Client
{
    internal enum WaterForm { Cut, Surge, Crest, Spiral, Dragon, Heal, Impact, Shot }

    /// <summary>A reusable, time-shaped water drawing. Geometry is rebuilt into one pooled mesh.</summary>
    internal sealed class WaterEffect
    {
        private const int Samples = 64;
        private readonly WaterRibbon drawing;
        private readonly Vector3[] points = new Vector3[Samples];
        private readonly float[] widths = new float[Samples];
        private Vector2 place, size;
        private WaterForm form;
        private float age, life, tilt;
        private int facing, variation;
        public bool Playing { get; private set; }

        public WaterEffect(string name = "Water skill", int order = 36) => drawing = new WaterRibbon(name, order);

        public void Start(WaterForm shape, Vector2 position, Vector2 scale, int direction, float seconds, int variant = 0, float angle = 0f)
        {
            form = shape; place = position; size = scale; facing = direction < 0 ? -1 : 1;
            age = 0f; life = Mathf.Max(0.01f, seconds); variation = variant; tilt = angle;
            Playing = true;
            Draw();
        }

        public void Hide() { Playing = false; drawing.Hide(); }

        public void Advance(float deltaTime)
        {
            if (!Playing) return;
            age += Mathf.Max(0f, deltaTime);
            if (age >= life) { Hide(); return; }
            Draw();
        }

        public void Projectile(Vector2 position, float diameter, int direction, float clock)
        {
            form = WaterForm.Shot; place = position; size = Vector2.one * diameter;
            facing = direction; tilt = 0; age = Mathf.Repeat(clock, 0.35f); life = 100000f; variation = 0; Playing = true;
            Draw();
        }

        private Vector3 World(Vector2 p)
        {
            Vector3 scaled = new Vector3(p.x * size.x, p.y * size.y);
            scaled = Quaternion.Euler(0, 0, tilt) * scaled;
            scaled.x *= facing;
            return (Vector3)place + scaled;
        }

        private void Draw()
        {
            float progress = Mathf.Clamp01(age / life);
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, progress));
            float time = Mathf.Floor(age * 18f) / 18f;
            float flow = time * 15f + variation * 1.7f;
            drawing.Begin(alpha);
            if (form == WaterForm.Impact)
            {
                Impact(time, progress);
                drawing.End();
                return;
            }

            float reveal = form == WaterForm.Shot || form == WaterForm.Heal ? 1f : Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(age / 0.09f));
            for (int i = 0; i < Samples; i++)
            {
                float t = i / (float)(Samples - 1);
                float u = t * reveal;
                Vector2 p;
                float thickness;
                switch (form)
                {
                    case WaterForm.Surge:
                        p = new Vector2(-0.5f + u, 0.1f * Mathf.Sin(u * 8f - time * 4f) - 0.06f);
                        thickness = 0.12f;
                        break;
                    case WaterForm.Crest:
                        if (u < 0.58f)
                        {
                            float q = u / 0.58f;
                            p = new Vector2(Mathf.Lerp(-0.5f, 0.25f, q), -0.3f + 0.05f * Mathf.Sin(q * Mathf.PI));
                        }
                        else
                        {
                            float q = (u - 0.58f) / 0.42f;
                            float angle = -Mathf.PI / 2f + q * Mathf.PI * 1.8f;
                            float radius = Mathf.Lerp(1f, 0.35f, q);
                            p = new Vector2(0.25f + Mathf.Cos(angle) * 0.22f * radius, 0.02f + Mathf.Sin(angle) * 0.32f * radius);
                        }
                        thickness = 0.13f;
                        break;
                    case WaterForm.Spiral:
                        float turn = -2.5f + u * Mathf.PI * (variation == 0 ? 1.9f : 2.6f);
                        float r = Mathf.Lerp(0.48f, 0.23f, u);
                        p = new Vector2(Mathf.Cos(turn) * r, Mathf.Sin(turn) * r);
                        thickness = 0.085f;
                        break;
                    case WaterForm.Dragon:
                        p = new Vector2(-0.5f + u, 0.18f * Mathf.Sin(u * Mathf.PI * 2.1f - 0.9f + time * 0.6f));
                        thickness = 0.12f;
                        break;
                    case WaterForm.Heal:
                        float orbit = t * Mathf.PI * 2f + time * 1.8f;
                        p = new Vector2(Mathf.Cos(orbit) * (0.38f + progress * 0.12f), -0.29f + Mathf.Sin(orbit) * 0.12f);
                        thickness = 0.035f;
                        break;
                    default:
                        float arc = Mathf.Lerp(-1.4f, 1.4f, u);
                        p = new Vector2(Mathf.Cos(arc) * 0.44f - 0.13f, Mathf.Sin(arc) * 0.46f);
                        thickness = form == WaterForm.Shot ? 0.1f : 0.115f;
                        break;
                }
                float taper = form == WaterForm.Heal ? 0.85f
                    : form == WaterForm.Dragon ? Mathf.SmoothStep(0f, 1f, t * 5f) * (1f - 0.2f * t)
                    : Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * Mathf.PI)), 0.45f);
                points[i] = World(p);
                widths[i] = size.y * thickness * taper * (0.94f + 0.06f * Mathf.Sin(t * 25f - flow));
            }
            drawing.Stroke(points, widths, Samples, flow);

            if (form == WaterForm.Dragon)
                drawing.DragonHead(points[Samples - 1], points[Samples - 1] - points[Samples - 3], size.y * 0.42f);

            int drops = form == WaterForm.Heal ? 8 : form == WaterForm.Dragon ? 12 : 7;
            for (int i = 0; i < drops; i++)
            {
                float seed = Mathf.Repeat(i * 0.618f + variation * 0.17f, 1f);
                if (form == WaterForm.Heal)
                {
                    float lift = Mathf.Repeat(time * 0.8f + seed, 1f);
                    Vector3 p = World(new Vector2(Mathf.Sin(seed * 19f + time) * 0.42f, lift * 0.65f - 0.35f));
                    drawing.Drop(p, Vector3.up, size.y * 0.055f * Mathf.Sin(lift * Mathf.PI));
                }
                else
                {
                    int sample = Mathf.Clamp(5 + i * (Samples - 10) / drops, 1, Samples - 2);
                    Vector3 tangent = (points[sample + 1] - points[sample - 1]).normalized;
                    Vector3 normal = new Vector3(-tangent.y, tangent.x);
                    float sign = i % 3 == 0 ? -1f : 1f;
                    Vector3 velocity = normal * sign * (0.4f + seed) + tangent * 0.4f;
                    Vector3 p = points[sample] + velocity * (0.04f + time * 0.35f)
                        + Vector3.down * time * time * 0.18f;
                    drawing.Drop(p, velocity + Vector3.down * time * 0.35f, size.y * (0.025f + seed * 0.022f));
                }
            }
            if (form == WaterForm.Surge || form == WaterForm.Shot)
            {
                for (int i = 0; i < 3; i++)
                    drawing.Flash(World(new Vector2(-0.65f - i * 0.08f, 0.14f - i * 0.13f)),
                        World(new Vector2(0.12f - i * 0.1f, 0.13f - i * 0.13f)), size.y * 0.015f, WaterRibbon.Foam);
            }
            drawing.End();
        }

        private void Impact(float time, float progress)
        {
            float flash = Mathf.Clamp01(1f - progress * 2.4f);
            if (flash > 0f)
            {
                drawing.Flash(World(new Vector2(-0.5f, -0.12f)), World(new Vector2(0.5f, 0.12f)), size.y * 0.06f * flash, WaterRibbon.Foam);
                drawing.Flash(World(new Vector2(-0.12f, 0.3f)), World(new Vector2(0.12f, -0.3f)), size.y * 0.04f * flash, WaterRibbon.Cyan);
            }
            for (int i = 0; i < 10; i++)
            {
                float angle = i * 2.39996f + variation * 0.8f;
                Vector2 way = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 p = way * (0.07f + time * (0.5f + i % 3 * 0.15f));
                p.y -= time * time * 0.75f;
                drawing.Drop(World(p), new Vector3(way.x * facing, way.y - time), size.y * (0.06f + i % 3 * 0.015f) * (1f - progress * 0.4f));
            }
        }
    }
}
