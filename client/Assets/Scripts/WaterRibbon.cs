using System.Collections.Generic;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Cel-painted water made from moving contours, ink lanes and broken foam crests.</summary>
    internal sealed class WaterRibbon
    {
        internal static readonly Color Ink = new Color32(9, 26, 62, 255);
        internal static readonly Color Deep = new Color32(16, 78, 177, 255);
        internal static readonly Color Blue = new Color32(24, 155, 238, 255);
        internal static readonly Color Cyan = new Color32(72, 224, 250, 255);
        internal static readonly Color Foam = new Color32(236, 252, 255, 255);
        // Mesh colours skip the conversion sprites get, so these are stated as linear to show true.
        private static readonly Color BladeInk = ((Color)new Color32(0x14, 0x1B, 0x3A, 0xFF)).linear;
        private static readonly Color BladeBody = ((Color)new Color32(0x25, 0x63, 0xD0, 0xFF)).linear;
        private static readonly Color BladeLight = ((Color)new Color32(0x3D, 0x8B, 0xF2, 0xFF)).linear;
        private static Material sharedMaterial;
        private readonly Mesh mesh = new Mesh { name = "Water contours" };
        private readonly MeshRenderer renderer;
        private readonly List<Vector3> vertices = new List<Vector3>(8192);
        private readonly List<Color> colors = new List<Color>(8192);
        private readonly List<int> triangles = new List<int>(24576);
        private readonly Vector3[] normals = new Vector3[128];
        private readonly Vector3[] curl = new Vector3[9];
        private readonly float[] curlWidths = new float[9];
        private float opacity;

        public WaterRibbon(string name, int order)
        {
            if (sharedMaterial == null)
                sharedMaterial = new Material(Shader.Find(LimbStrip.ShaderName)) { mainTexture = Texture2D.whiteTexture };
            var host = new GameObject(name);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = sharedMaterial;
            renderer.sortingOrder = order;
            renderer.enabled = false;
            mesh.MarkDynamic();
        }

        public bool Visible => renderer.enabled;
        public void Hide() => renderer.enabled = false;

        public void Begin(float alpha)
        {
            opacity = Mathf.Clamp01(alpha);
            vertices.Clear(); colors.Clear(); triangles.Clear();
        }

        public void End()
        {
            renderer.enabled = vertices.Count > 0 && opacity > 0.001f;
            if (!renderer.enabled) return;
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        public void Stroke(Vector3[] points, float[] widths, int count, float phase, bool foam = true)
        {
            if (count < 2) return;
            MakeNormals(points, count);
            Band(points, widths, count, -1.12f, 1.12f, Ink, phase, 0);
            Band(points, widths, count, -1f, 1f, Deep, phase, 0);
            Band(points, widths, count, -0.76f, 0.82f, Blue, phase, 0.07f);
            Band(points, widths, count, -0.3f, 0.33f, Cyan, phase, 0.12f);
            Band(points, widths, count, -0.67f, -0.54f, Ink, phase + 1f, 0.09f);
            Band(points, widths, count, 0.4f, 0.49f, Deep, phase + 2f, 0.1f);
            Band(points, widths, count, -0.23f, -0.17f, Foam, phase + 0.4f, 0.12f);
            if (!foam) return;
            Band(points, widths, count, 0.73f, 1.01f, Foam, phase, 0.12f);

            // Several distinct curled fingers, rather than a continuous neon outline.
            for (int i = 5; i < count - 2; i += 9)
            {
                float width = widths[i] * (0.72f + 0.38f * Mathf.Sin(i * 1.73f));
                if (width < 0.012f) continue;
                Vector3 normal = normals[i];
                Vector3 tangent = new Vector3(normal.y, -normal.x);
                Vector3 root = points[i] + normal * width * 0.7f;
                for (int k = 0; k < curl.Length; k++)
                {
                    float t = k / (float)(curl.Length - 1);
                    float angle = t * Mathf.PI * 1.4f;
                    curl[k] = root + tangent * (Mathf.Sin(angle) * 0.8f - t * 0.55f) * width
                        + normal * (1f - Mathf.Cos(angle)) * width * 0.8f;
                    curlWidths[k] = width * 0.24f * Mathf.Pow(1f - t, 0.6f);
                }
                SolidStroke(curl, curlWidths, curl.Length, Ink, 1.4f);
                SolidStroke(curl, curlWidths, curl.Length, Foam, 1f);
            }
        }

        /// <summary>
        /// The sword's stroke: flat indigo with a rim of foam on the outside of the swing.
        /// </summary>
        /// <param name="outside">Which side of the path the rim goes on: 1 or -1.</param>
        public void BladeStroke(Vector3[] points, float[] widths, int count, float phase, float outside)
        {
            if (count < 2) return;
            MakeNormals(points, count);
            Band(points, widths, count, -1.18f, 1.18f, BladeInk, phase, 0f);
            Band(points, widths, count, -1f, 1f, BladeBody, phase, 0f);
            Band(points, widths, count, outside > 0f ? -0.15f : -0.45f, outside > 0f ? 0.45f : 0.15f, BladeLight, phase, 0.08f);
            Band(points, widths, count, outside > 0f ? 0.72f : -1f, outside > 0f ? 1f : -0.72f, Foam, phase, 0.05f);
        }

        private void MakeNormals(Vector3[] points, int count)
        {
            Vector3 previous = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = points[Mathf.Min(count - 1, i + 1)] - points[Mathf.Max(0, i - 1)];
                if (tangent.sqrMagnitude > 0.000001f) previous = new Vector3(-tangent.y, tangent.x).normalized;
                normals[i] = previous;
            }
        }

        private void Band(Vector3[] points, float[] widths, int count, float low, float high, Color color, float phase, float ripple)
        {
            int start = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                float wave = Mathf.Sin(i * 0.73f - phase) * ripple + Mathf.Sin(i * 0.27f + phase) * ripple * 0.4f;
                Add(points[i] + normals[i] * widths[i] * (low + wave), color);
                Add(points[i] + normals[i] * widths[i] * (high + wave), color);
                if (i > 0) Quad(start + (i - 1) * 2);
            }
        }

        private void SolidStroke(Vector3[] points, float[] widths, int count, Color color, float scale)
        {
            // Separate normals: foam curls must not overwrite the main path's normals.
            int start = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = points[Mathf.Min(count - 1, i + 1)] - points[Mathf.Max(0, i - 1)];
                Vector3 normal = new Vector3(-tangent.y, tangent.x).normalized;
                Add(points[i] - normal * widths[i] * scale, color);
                Add(points[i] + normal * widths[i] * scale, color);
                if (i > 0) Quad(start + (i - 1) * 2);
            }
        }

        public void Drop(Vector3 place, Vector3 direction, float size)
        {
            if (size < 0.002f) return;
            Vector3 forward = direction.sqrMagnitude > 0f ? direction.normalized : Vector3.up;
            Vector3 side = new Vector3(-forward.y, forward.x);
            Tear(place, forward, side, size * 1.13f, Ink);
            Tear(place, forward, side, size, Blue);
            Tear(place + side * size * 0.08f, forward, side, size * 0.56f, Foam);
        }

        private void Tear(Vector3 place, Vector3 forward, Vector3 side, float size, Color color)
        {
            int start = vertices.Count;
            Add(place + forward * size, color);
            Add(place + side * size * 0.32f, color);
            Add(place - forward * size * 0.3f + side * size * 0.2f, color);
            Add(place - forward * size * 0.4f, color);
            Add(place - forward * size * 0.3f - side * size * 0.2f, color);
            Add(place - side * size * 0.32f, color);
            Fan(start, 6);
        }

        public void Flash(Vector3 from, Vector3 to, float width, Color color)
        {
            Vector3 side = new Vector3(-(to - from).y, (to - from).x).normalized * width;
            int start = vertices.Count;
            Add(from, color); Add(Vector3.Lerp(from, to, 0.4f) + side, color);
            Add(to, color); Add(Vector3.Lerp(from, to, 0.4f) - side, color);
            Fan(start, 4);
        }

        // Water-shaped head for the last ultimate hit; no imported character/film art.
        private static readonly Vector2[] Skull = { new Vector2(-0.5f,-0.24f), new Vector2(-0.42f,0.22f),
            new Vector2(-0.12f,0.4f), new Vector2(0.22f,0.24f), new Vector2(0.38f,0), new Vector2(0,-0.2f) };
        private static readonly Vector2[] Snout = { new Vector2(-0.05f,0.17f), new Vector2(0.66f,0.1f),
            new Vector2(0.55f,-0.04f), new Vector2(0,-0.1f) };
        private static readonly Vector2[] Jaw = { new Vector2(-0.27f,-0.18f), new Vector2(0.52f,-0.14f),
            new Vector2(0.36f,-0.3f), new Vector2(-0.18f,-0.37f) };

        public void DragonHead(Vector3 place, Vector3 forward, float size)
        {
            forward.Normalize();
            Vector3 side = new Vector3(-forward.y, forward.x);
            Poly(place, forward, side, size * 1.12f, Skull, Ink);
            Poly(place, forward, side, size, Skull, Foam);
            Poly(place - forward * size * 0.05f, forward, side, size * 0.74f, Skull, Cyan);
            Poly(place, forward, side, size * 1.12f, Snout, Ink);
            Poly(place, forward, side, size, Snout, Foam);
            Poly(place, forward, side, size * 1.12f, Jaw, Ink);
            Poly(place, forward, side, size, Jaw, Foam);
            for (int i = 0; i < 3; i++)
            {
                Vector3 root = place - forward * size * (0.22f + 0.13f * i);
                Flash(root, root - forward * size * (0.65f + i * 0.12f) + side * size * 0.55f, size * 0.1f, Foam);
                Flash(root, root - forward * size * 0.5f - side * size * 0.4f, size * 0.07f, Foam);
            }
            Vector3 eye = place + side * size * 0.12f;
            Flash(eye - forward * size * 0.1f, eye + forward * size * 0.21f, size * 0.055f, Ink);
            Flash(eye, eye + forward * size * 0.12f, size * 0.018f, Foam);
        }

        private void Poly(Vector3 place, Vector3 forward, Vector3 side, float size, Vector2[] shape, Color color)
        {
            int start = vertices.Count;
            foreach (Vector2 p in shape) Add(place + (forward * p.x + side * p.y) * size, color);
            Fan(start, shape.Length);
        }

        private void Add(Vector3 vertex, Color color)
        {
            color.a *= opacity;
            vertices.Add(vertex); colors.Add(color);
        }
        private void Quad(int i)
        {
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            triangles.Add(i + 1); triangles.Add(i + 3); triangles.Add(i + 2);
        }
        private void Fan(int start, int count)
        {
            for (int i = 1; i < count - 1; i++)
            { triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1); }
        }
    }
}
