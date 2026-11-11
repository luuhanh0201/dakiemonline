using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client.Accounts
{
    /// <summary>Shears only authored grass tips around fixed roots. UVs and the ground stay stationary.</summary>
    [RequireComponent(typeof(Image))]
    public sealed class GrassTipMotion : BaseMeshEffect
    {
        public float Amplitude = 2f;
        public float Speed = 0.55f;
        private float time;
        private float gust = 1f;
        private readonly List<float> xs = new List<float>();
        private readonly List<float> ys = new List<float>();

        // Small patches above the hillside silhouette. None includes a rock, flower or solid ground.
        // Coordinates are in the original 1672 x 941 artwork, measured from its top left.
        private static readonly Rect[] Tips =
        {
            new Rect(0f, 663f, 48f, 32f),
            new Rect(104f, 680f, 48f, 29f),
            new Rect(436f, 760f, 50f, 24f),
            new Rect(655f, 838f, 55f, 35f),
            new Rect(832f, 872f, 54f, 30f),
            new Rect(930f, 863f, 64f, 35f),
            new Rect(1198f, 808f, 42f, 25f),
            new Rect(1450f, 777f, 54f, 28f),
        };

        public void Animate(float seconds, float wind)
        {
            time = seconds; gust = wind;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public static float Weight(Vector2 point, out float phase)
        {
            for (int i = 0; i < Tips.Length; i++)
            {
                Rect patch = Tips[i];
                if (!patch.Contains(point)) continue;
                float side = Mathf.SmoothStep(0f, 1f, Mathf.Min(point.x - patch.xMin, patch.xMax - point.x) / 9f);
                float height = Mathf.Clamp01((patch.yMax - point.y) / patch.height);
                phase = i * 1.71f;
                return side * height * height;
            }
            phase = 0f;
            return 0f;
        }

        public static bool IsTipRegion(Vector2 point, float margin = 0f)
        {
            foreach (Rect patch in Tips)
                if (Rect.MinMaxRect(patch.xMin - margin, patch.yMin - margin, patch.xMax + margin, patch.yMax + margin).Contains(point)) return true;
            return false;
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount != 4) return;
            UIVertex bottomLeft = default, topRight = default;
            mesh.PopulateUIVertex(ref bottomLeft, 0);
            mesh.PopulateUIVertex(ref topRight, 2);
            mesh.Clear();
            // Refine only around the patches. Static terrain needs no per-pixel geometry.
            Grid(xs, true); Grid(ys, false);
            for (int y = 0; y < ys.Count; y++)
            {
                for (int x = 0; x < xs.Count; x++)
                {
                    Vector2 authored = new Vector2(xs[x], ys[y]);
                    float weight = Weight(authored, out float phase);
                    float sway = Mathf.Sin(time * Speed * Mathf.PI * 2f + phase) * Amplitude * gust * weight;
                    UIVertex vertex = bottomLeft;
                    vertex.position = new Vector3(
                        Mathf.Lerp(bottomLeft.position.x, topRight.position.x, (authored.x + sway) / UiKit.StageWidth),
                        Mathf.Lerp(bottomLeft.position.y, topRight.position.y, 1f - authored.y / UiKit.StageHeight), 0f);
                    vertex.uv0 = new Vector4(
                        Mathf.Lerp(bottomLeft.uv0.x, topRight.uv0.x, authored.x / UiKit.StageWidth),
                        Mathf.Lerp(bottomLeft.uv0.y, topRight.uv0.y, 1f - authored.y / UiKit.StageHeight), 0f, 0f);
                    mesh.AddVert(vertex);
                    if (x == 0 || y == 0) continue;
                    int at = y * xs.Count + x;
                    mesh.AddTriangle(at - xs.Count - 1, at - xs.Count, at);
                    mesh.AddTriangle(at - xs.Count - 1, at, at - 1);
                }
            }
        }

        private static void Grid(List<float> values, bool horizontal)
        {
            if (values.Count > 0) return;
            values.Add(0f); values.Add(horizontal ? UiKit.StageWidth : UiKit.StageHeight);
            foreach (Rect patch in Tips)
            {
                float start = horizontal ? patch.xMin : patch.yMin;
                float end = horizontal ? patch.xMax : patch.yMax;
                for (float p = start; p < end; p += 4f) if (!values.Contains(p)) values.Add(p);
                if (!values.Contains(end)) values.Add(end);
            }
            values.Sort();
        }
    }
}
