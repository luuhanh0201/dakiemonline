using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// A fading ribbon behind the blade during a swing. The blade can turn a long way in one
    /// frame, even past half a circle on a slow phone, so each step is filled in along the arc
    /// in the direction of the swing to keep the ribbon curved and on the right side.
    /// </summary>
    public sealed class WeaponTrail
    {
        private const int MaxSamples = 40;
        private const int MinStepsPerFrame = 4;
        private const float DegreesPerStep = 20f;
        private const float BackwardLimitDegrees = 90f;
        private const int SortingOrder = 38;
        private const float Lifetime = 0.16f;
        private const float MaxOpacity = 0.85f;
        private const float FadePower = 1.6f;

        private static readonly Color TrailColor = new Color(0.8f, 0.94f, 1f);

        private readonly Mesh mesh = new Mesh();
        private readonly Vector3[] inner = new Vector3[MaxSamples];
        private readonly Vector3[] outer = new Vector3[MaxSamples];
        private readonly float[] ages = new float[MaxSamples];
        private readonly Vector3[] vertices = new Vector3[MaxSamples * 2];
        private readonly Color[] colors = new Color[MaxSamples * 2];
        private readonly MeshRenderer renderer;
        private Vector3 lastGrip;
        private Vector3 lastInner;
        private Vector3 lastOuter;
        private bool wasDrawing;
        private int count;
        private float strength;

        public WeaponTrail()
        {
            var host = new GameObject("WeaponTrail");
            var material = new Material(Shader.Find(LimbStrip.ShaderName)) { mainTexture = Texture2D.whiteTexture };
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(MaxSamples - 1) * 6];

            for (int index = 0; index < uv.Length; index++)
            {
                uv[index] = Vector2.one;
            }

            for (int sample = 0; sample < MaxSamples - 1; sample++)
            {
                int first = sample * 2;
                int at = sample * 6;
                triangles[at] = first;
                triangles[at + 1] = first + 1;
                triangles[at + 2] = first + 2;
                triangles[at + 3] = first + 1;
                triangles[at + 4] = first + 3;
                triangles[at + 5] = first + 2;
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.MarkDynamic();

            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = SortingOrder;
            renderer.enabled = false;
        }

        /// <param name="drawing">True while the blade should leave a mark.</param>
        /// <param name="grip">The point the blade turns around.</param>
        /// <param name="bladeInner">The part of the blade the ribbon starts from.</param>
        /// <param name="bladeOuter">The tip of the blade.</param>
        /// <param name="sweep">The way the blade swings on screen: 1 is anticlockwise, -1 clockwise.</param>
        /// <param name="opacity">0 to 1; heavier blows leave a brighter mark.</param>
        public void Show(bool drawing, Vector3 grip, Vector3 bladeInner, Vector3 bladeOuter, int sweep, float opacity, float deltaTime)
        {
            Age(deltaTime);

            if (drawing)
            {
                strength = opacity;

                if (wasDrawing)
                {
                    FillArc(grip, bladeInner, bladeOuter, sweep);
                }

                lastGrip = grip;
                lastInner = bladeInner;
                lastOuter = bladeOuter;
            }

            wasDrawing = drawing;
            Draw();
        }

        private void Age(float deltaTime)
        {
            int kept = 0;

            for (int sample = 0; sample < count; sample++)
            {
                float age = ages[sample] + deltaTime;

                if (age < Lifetime)
                {
                    inner[kept] = inner[sample];
                    outer[kept] = outer[sample];
                    ages[kept] = age;
                    kept++;
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

                Add(
                    centre + AlongArc(lastInner - lastGrip, bladeInner - grip, angle, t),
                    centre + AlongArc(lastOuter - lastGrip, bladeOuter - grip, angle, t));
            }
        }

        private static Vector3 AlongArc(Vector3 from, Vector3 to, float angle, float t)
        {
            return Quaternion.AngleAxis(angle * t, Vector3.forward) * from.normalized * Mathf.Lerp(from.magnitude, to.magnitude, t);
        }

        private void Add(Vector3 near, Vector3 far)
        {
            if (count == MaxSamples)
            {
                System.Array.Copy(inner, 1, inner, 0, count - 1);
                System.Array.Copy(outer, 1, outer, 0, count - 1);
                System.Array.Copy(ages, 1, ages, 0, count - 1);
                count--;
            }

            inner[count] = near;
            outer[count] = far;
            ages[count] = 0f;
            count++;
        }

        private void Draw()
        {
            renderer.enabled = count > 1;

            if (count < 2)
            {
                return;
            }

            for (int sample = 0; sample < MaxSamples; sample++)
            {
                // Spare samples collapse onto the newest one, so they draw nothing.
                int source = Mathf.Min(sample, count - 1);
                float life = 1f - ages[source] / Lifetime;
                var color = TrailColor;

                color.a = sample < count ? Mathf.Pow(life, FadePower) * MaxOpacity * strength : 0f;
                vertices[sample * 2] = inner[source];
                vertices[sample * 2 + 1] = outer[source];
                colors[sample * 2] = new Color(color.r, color.g, color.b, 0f);
                colors[sample * 2 + 1] = color;
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
        }
    }
}
