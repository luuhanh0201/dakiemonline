using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Draws the front-facing character as a mesh that can bend, so breathing moves the chest,
    /// shoulders and head while the feet stay still, and a turn sweeps through the body from the
    /// top down instead of the picture changing all at once.
    /// </summary>
    public sealed class FrontFigure
    {
        private const int Columns = 14;
        private const int Rows = 28;

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
        private readonly Material material;
        private readonly Vector3[] vertices = new Vector3[(Columns + 1) * (Rows + 1)];
        private readonly Transform host;
        private readonly float width;
        private readonly float height;

        public FrontFigure(Sprite image, Transform parent, int order, float heightUnits)
        {
            height = heightUnits;
            width = heightUnits * image.texture.width / image.texture.height;

            var figure = new GameObject("FrontFigure");
            host = figure.transform;
            host.SetParent(parent, false);

            material = new Material(Shader.Find(LimbStrip.ShaderName)) { mainTexture = image.texture };

            BuildMesh();
            figure.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = figure.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
        }

        public void SetVisible(bool visible)
        {
            host.gameObject.SetActive(visible);
        }

        public void Tint(Color color, float alpha)
        {
            material.color = new Color(color.r, color.g, color.b, alpha);
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

            for (int row = 0; row <= Rows; row++)
            {
                float up = row / (float)Rows;

                for (int column = 0; column <= Columns; column++)
                {
                    float across = column / (float)Columns - 0.5f;
                    vertices[row * (Columns + 1) + column] = Bent(across, up, breath, turn, way);
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
            float started = Mathf.Lerp(TurnDelayAtFeet, 0f, up);
            float bodyTurn = Mathf.Clamp01((turn - started) / (1f - started));
            float eased = Mathf.SmoothStep(0f, 1f, bodyTurn);
            float nearSide = across * way;

            x *= Mathf.Lerp(1f, TurnNarrowest, eased);
            x += way * eased * (Mathf.Lerp(TurnLeadBottom, TurnLeadTop, up) + TurnNearSideStretch * nearSide * width);

            return new Vector3(x, y, 0f);
        }

        private void BuildMesh()
        {
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Columns * Rows * 6];
            int index = 0;

            for (int row = 0; row <= Rows; row++)
            {
                for (int column = 0; column <= Columns; column++)
                {
                    uv[row * (Columns + 1) + column] = new Vector2(column / (float)Columns, row / (float)Rows);
                }
            }

            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    int corner = row * (Columns + 1) + column;
                    int above = corner + Columns + 1;

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
}
