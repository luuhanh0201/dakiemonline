using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Waist stays on the pelvis; each hem follows its thigh.</summary>
    public sealed class SkinnedShorts
    {
        private const int Columns = 12;
        private const int Rows = 10;
        private readonly Mesh mesh = new Mesh();
        private readonly Material material;
        private readonly Vector3[] rest = new Vector3[(Columns + 1) * (Rows + 1)];
        private readonly Vector3[] vertices = new Vector3[(Columns + 1) * (Rows + 1)];

        public SkinnedShorts(Sprite sprite, Transform parent, Vector2 centre, int order)
        {
            var host = new GameObject("ShortsSkin");
            host.transform.SetParent(parent, false);
            material = new Material(Shader.Find(LimbStrip.ShaderName)) { mainTexture = sprite.texture };
            var uv = new Vector2[rest.Length];
            var triangles = new int[Columns * Rows * 6];
            Vector2 size = sprite.bounds.size;
            int t = 0;
            for (int y = 0; y <= Rows; y++)
            for (int x = 0; x <= Columns; x++)
            {
                int i = y * (Columns + 1) + x;
                uv[i] = new Vector2(x / (float)Columns, y / (float)Rows);
                rest[i] = centre + Vector2.Scale(uv[i] - Vector2.one * 0.5f, size);
                if (y == Rows || x == Columns) continue;
                int above = i + Columns + 1;
                triangles[t++] = i; triangles[t++] = i + 1; triangles[t++] = above;
                triangles[t++] = i + 1; triangles[t++] = above + 1; triangles[t++] = above;
            }
            mesh.vertices = rest;
            mesh.uv = uv;
            mesh.triangles = triangles;
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
        }

        public void Pose(Vector2 nearRoot, float nearAngle, Vector2 farRoot, float farAngle)
        {
            for (int y = 0; y <= Rows; y++)
            for (int x = 0; x <= Columns; x++)
            {
                int i = y * (Columns + 1) + x;
                // Blend across the crotch, with full thigh influence at either hem.
                float farWeight = Mathf.SmoothStep(0f, 1f, x / (float)Columns);
                float legWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.05f, y / (float)Rows));
                Vector2 original = rest[i];
                Vector2 moved = Vector2.Lerp(Rotate(original, nearRoot, nearAngle), Rotate(original, farRoot, farAngle), farWeight);
                vertices[i] = Vector2.Lerp(original, moved, legWeight);
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        public void Tint(Color color, float alpha) => material.color = new Color(color.r, color.g, color.b, alpha);

        private static Vector2 Rotate(Vector2 point, Vector2 pivot, float angle)
        {
            Vector2 d = point - pivot;
            return pivot + new Vector2(d.x * Mathf.Cos(angle) - d.y * Mathf.Sin(angle), d.x * Mathf.Sin(angle) + d.y * Mathf.Cos(angle));
        }
    }
}
