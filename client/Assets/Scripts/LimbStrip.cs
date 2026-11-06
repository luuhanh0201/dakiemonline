using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Draws an arm or leg as one continuous mesh bent along a rounded path. The image is never
    /// split at the elbow or knee, so its outline stays unbroken however far the limb bends.
    /// </summary>
    public sealed class LimbStrip
    {
        public const string ShaderName = "DaKiem/CharacterLimb";

        private const int Rows = 16;
        private const float BendRoundness = 0.55f;
        private const float StiffBendRoundness = 0.1f;

        // How many turns of a ripple fit along a draped strip.
        private const float RippleTurns = 1.4f;

        private readonly Mesh mesh = new Mesh();
        private readonly Material material;
        private readonly Vector3[] vertices = new Vector3[(Rows + 1) * 2];
        private readonly Vector2[] points = new Vector2[Rows + 1];
        private float halfWidth;
        private float bendRoundness = BendRoundness;
        private readonly float shade;

        public LimbStrip(Sprite limb, Transform parent, int order, string name, float shade)
        {
            this.shade = shade;

            var host = new GameObject(name);
            host.transform.SetParent(parent, false);

            material = new Material(Shader.Find(ShaderName));
            SetImage(limb);

            BuildMesh();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
        }

        /// <summary>Swaps the drawing, for a sleeve or trouser leg. Its width sets the limb's width.</summary>
        public void SetImage(Sprite limb)
        {
            material.mainTexture = limb.texture;
            halfWidth = limb.texture.width / limb.pixelsPerUnit / 2f;
        }

        /// <summary>A stiff drawing folds sharply at the joint instead of curving like cloth.</summary>
        public void SetStiff(bool stiff)
        {
            bendRoundness = stiff ? StiffBendRoundness : BendRoundness;
        }

        /// <summary>Lays the limb from start through the bend to end, in the parent's local space.</summary>
        public void Bend(Vector2 start, Vector2 bend, Vector2 end)
        {
            float upperLength = Vector2.Distance(start, bend);
            float lowerLength = Vector2.Distance(bend, end);
            float total = upperLength + lowerLength;
            float round = Mathf.Min(upperLength, lowerLength) * bendRoundness;
            Vector2 upperWay = (bend - start).normalized;
            Vector2 lowerWay = (end - bend).normalized;

            for (int row = 0; row <= Rows; row++)
            {
                float along = total * row / Rows;
                points[row] = PointAt(along, start, bend, upperWay, lowerWay, upperLength, round);
            }

            LayAlongPoints();
        }

        /// <summary>
        /// Hangs a strip of cloth from start. The angle runs from upper at the top to lower at the
        /// tail, and a ripple grows toward the tail, so the free end moves most. Angles are in
        /// degrees from straight down, positive toward the front.
        /// </summary>
        public void Drape(Vector2 start, float length, float upper, float lower, float ripple, float ripplePhase)
        {
            float step = length / Rows;
            Vector2 point = start;
            points[0] = start;

            for (int row = 1; row <= Rows; row++)
            {
                float along = (row - 0.5f) / Rows;
                float wave = Mathf.Sin(ripplePhase - along * RippleTurns * Mathf.PI * 2f);
                float angle = (Mathf.Lerp(upper, lower, along * along) + ripple * along * wave) * Mathf.Deg2Rad;

                point += new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * step;
                points[row] = point;
            }

            LayAlongPoints();
        }

        private void LayAlongPoints()
        {
            for (int row = 0; row <= Rows; row++)
            {
                Vector2 way = (points[Mathf.Min(row + 1, Rows)] - points[Mathf.Max(row - 1, 0)]).normalized;
                Vector2 side = new Vector2(-way.y, way.x) * halfWidth;

                vertices[row * 2] = points[row] + side;
                vertices[row * 2 + 1] = points[row] - side;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        public void Tint(Color color, float alpha)
        {
            material.color = new Color(color.r * shade, color.g * shade, color.b * shade, alpha);
        }

        private void BuildMesh()
        {
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Rows * 6];

            for (int row = 0; row <= Rows; row++)
            {
                float height = 1f - row / (float)Rows;
                uv[row * 2] = new Vector2(0f, height);
                uv[row * 2 + 1] = new Vector2(1f, height);
            }

            for (int row = 0; row < Rows; row++)
            {
                int first = row * 2;
                int index = row * 6;

                triangles[index] = first;
                triangles[index + 1] = first + 1;
                triangles[index + 2] = first + 2;
                triangles[index + 3] = first + 1;
                triangles[index + 4] = first + 3;
                triangles[index + 5] = first + 2;
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
        }

        private static Vector2 PointAt(
            float along,
            Vector2 start,
            Vector2 bend,
            Vector2 upperWay,
            Vector2 lowerWay,
            float upperLength,
            float round)
        {
            if (along <= upperLength - round)
            {
                return start + upperWay * along;
            }

            if (along >= upperLength + round)
            {
                return bend + lowerWay * (along - upperLength);
            }

            // Inside the bend, follow a curve that cuts the corner.
            float t = (along - (upperLength - round)) / (2f * round);
            Vector2 enter = bend - upperWay * round;
            Vector2 leave = bend + lowerWay * round;
            return Vector2.Lerp(Vector2.Lerp(enter, bend, t), Vector2.Lerp(bend, leave, t), t);
        }
    }
}
