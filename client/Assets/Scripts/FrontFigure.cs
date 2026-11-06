using System.Collections.Generic;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Draws the front-facing character as a mesh that can bend, so breathing moves the chest,
    /// shoulders and head while the feet stay still, and a turn sweeps through the body from the
    /// top down instead of the picture changing all at once. Equipment uses a larger canvas on the
    /// same bend, so everything worn moves with the body.
    /// </summary>
    public sealed class FrontFigure
    {
        private const int BaseColumns = 14;
        private const int BaseRows = 28;

        // Heights are shares of the figure, measured up from the feet.
        private const float WaistHeight = 0.4f;
        private const float ChestHeight = 0.6f;
        private const float ShoulderHeight = 0.68f;
        private const float ChestSpread = 0.09f;

        private const float BreathRise = 0.026f;
        private const float HeadExtraRise = 0.008f;
        private const float ChestSwell = 0.075f;
        private const float ArmDrift = 0.016f;
        private const float ArmStartShare = 0.24f;

        private const float TurnNarrowest = 0.5f;
        private const float TurnLeadBottom = 0.02f;
        private const float TurnLeadTop = 0.11f;
        private const float TurnNearSideStretch = 0.08f;
        private const float TurnDelayAtFeet = 0.35f;

        private readonly Mesh mesh = new Mesh();
        private readonly List<Material> layers = new List<Material>();
        private readonly Vector3[] vertices;
        private readonly Transform host;
        private readonly int columns;
        private readonly int rows;
        private readonly float width;
        private readonly float height;
        private readonly Vector2 canvasShare;

        /// <param name="figureWidth">Width of the body in world units.</param>
        /// <param name="figureHeight">Height of the body in world units.</param>
        /// <param name="canvasShare">Size of this canvas compared with the body, around its feet.</param>
        public FrontFigure(string name, Transform parent, float figureWidth, float figureHeight, Vector2 canvasShare)
        {
            width = figureWidth;
            height = figureHeight;
            this.canvasShare = canvasShare;
            columns = Mathf.CeilToInt(BaseColumns * canvasShare.x);
            rows = Mathf.CeilToInt(BaseRows * canvasShare.y);
            vertices = new Vector3[(columns + 1) * (rows + 1)];

            var figure = new GameObject(name);
            host = figure.transform;
            host.SetParent(parent, false);
            BuildMesh();
        }

        /// <summary>Adds one drawing on this canvas. Layers share the mesh, so they bend together.</summary>
        public FrontLayer AddLayer(string name, Texture texture, int order)
        {
            var layer = new GameObject(name);
            layer.transform.SetParent(host, false);

            var material = new Material(Shader.Find(LimbStrip.ShaderName)) { mainTexture = texture };
            layers.Add(material);
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            return new FrontLayer(renderer, material);
        }

        public void SetVisible(bool visible)
        {
            host.gameObject.SetActive(visible);
        }

        public void Tint(Color color, float alpha)
        {
            foreach (Material material in layers)
            {
                material.color = new Color(color.r, color.g, color.b, alpha);
            }
        }

        /// <param name="breath">0 with the lungs empty, 1 with them full.</param>
        /// <param name="turn">0 facing the camera, 1 turned side-on.</param>
        /// <param name="way">The side being turned toward, on screen: 1 for right, -1 for left.</param>
        /// <param name="drop">How far the body has sunk, in world units.</param>
        public void Pose(float breath, float turn, int way, float drop)
        {
            // The parent flips with facing, so cancel that to keep the drawing the same way round.
            host.localScale = new Vector3(way, 1f, 1f);
            host.localPosition = Vector3.down * drop;

            for (int row = 0; row <= rows; row++)
            {
                float up = row / (float)rows * canvasShare.y;

                for (int column = 0; column <= columns; column++)
                {
                    float across = (column / (float)columns - 0.5f) * canvasShare.x;
                    vertices[row * (columns + 1) + column] = Bent(across, up, breath, turn, way);
                }
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        private Vector3 Bent(float across, float up, float breath, float turn, int way)
        {
            float x = across * width;
            float y = up * height;

            // Below the waist nothing moves; above it the rise builds up to the shoulders.
            float risen = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WaistHeight, ShoulderHeight, up));
            float chest = Mathf.Exp(-Mathf.Pow((up - ChestHeight) / ChestSpread, 2f));
            float inArm = Mathf.InverseLerp(ArmStartShare, 0.5f, Mathf.Abs(across));
            float isHead = Mathf.InverseLerp(ShoulderHeight, 0.8f, up);

            y += breath * (BreathRise * risen + HeadExtraRise * isHead);
            x *= 1f + breath * ChestSwell * chest * (1f - inArm);
            x += Mathf.Sign(across) * breath * ArmDrift * inArm * risen;

            // The turn starts at the head and reaches the feet last, as a real body turns.
            float started = Mathf.Lerp(TurnDelayAtFeet, 0f, Mathf.Clamp01(up));
            float bodyTurn = Mathf.Clamp01((turn - started) / (1f - started));
            float eased = Mathf.SmoothStep(0f, 1f, bodyTurn);
            float nearSide = across * way;

            x *= Mathf.Lerp(1f, TurnNarrowest, eased);
            x += way * eased * (Mathf.Lerp(TurnLeadBottom, TurnLeadTop, Mathf.Clamp01(up)) + TurnNearSideStretch * nearSide * width);

            return new Vector3(x, y, 0f);
        }

        private void BuildMesh()
        {
            var uv = new Vector2[vertices.Length];
            var triangles = new int[columns * rows * 6];
            int index = 0;

            for (int row = 0; row <= rows; row++)
            {
                for (int column = 0; column <= columns; column++)
                {
                    uv[row * (columns + 1) + column] = new Vector2(column / (float)columns, row / (float)rows);
                }
            }

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int corner = row * (columns + 1) + column;
                    int above = corner + columns + 1;

                    triangles[index++] = corner;
                    triangles[index++] = above;
                    triangles[index++] = corner + 1;
                    triangles[index++] = corner + 1;
                    triangles[index++] = above;
                    triangles[index++] = above + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
        }
    }

    /// <summary>One drawing on a front canvas, which can be swapped or left empty.</summary>
    public sealed class FrontLayer
    {
        private readonly Renderer renderer;
        private readonly Material material;

        public FrontLayer(Renderer renderer, Material material)
        {
            this.renderer = renderer;
            this.material = material;
        }

        public void Show(Texture texture)
        {
            renderer.enabled = texture != null;
            material.mainTexture = texture;
        }
    }
}
