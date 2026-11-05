using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Draws the test map and the character as placeholder rectangles.</summary>
    public sealed class WorldView
    {
        private const float ViewWidth = 16f;
        private const float MapWidth = 30f;
        private const float LookAhead = 0.1f * ViewWidth;
        private const float GroundMargin = 1.5f;
        private const float CameraSmoothing = 6f;

        private static readonly Color SkyColor = new Color32(0x14, 0x1B, 0x3A, 0xFF);
        private static readonly Color GroundColor = new Color32(0x2A, 0x2F, 0x5E, 0xFF);
        private static readonly Color PlatformColor = new Color32(0x74, 0x80, 0xC8, 0xFF);
        private static readonly Color BodyColor = new Color32(0x3D, 0x8B, 0xF2, 0xFF);
        private static readonly Color FaceColor = new Color32(0xDC, 0xE6, 0xFF, 0xFF);
        private static readonly Color WindupColor = new Color32(0x9C, 0xC4, 0xFA, 0xFF);
        private static readonly Color HurtColor = new Color32(0xD9, 0x4F, 0x4F, 0xFF);

        private readonly Sprite square;
        private readonly Camera camera;
        private readonly SpriteRenderer bodyRenderer;
        private readonly Transform body;
        private readonly Transform face;
        private readonly float bodyHeight;
        private readonly float bodyWidth;

        public WorldView(IReadOnlyList<MapBox> boxes, MovementConfig config)
        {
            square = CreateSquare();
            camera = Camera.main;
            bodyWidth = (float)config.BodyWidth;
            bodyHeight = (float)config.BodyHeight;

            foreach (MapBox box in boxes)
            {
                DrawBox(box);
            }

            body = CreateBlock("Body", BodyColor, 1, new Vector2(bodyWidth, bodyHeight)).transform;
            bodyRenderer = body.GetComponent<SpriteRenderer>();
            face = CreateBlock("Face", FaceColor, 2, new Vector2(bodyWidth / 3f, bodyHeight / 3f)).transform;

            camera.backgroundColor = SkyColor;
            camera.orthographicSize = ViewWidth / camera.aspect / 2f;
            camera.transform.position = new Vector3(ViewWidth / 2f, camera.orthographicSize - GroundMargin, -10f);
        }

        public void Show(Vector2 feet, int facing, CastPhase phase, bool hurt)
        {
            bodyRenderer.color = hurt ? HurtColor : phase == CastPhase.Windup ? WindupColor : BodyColor;
            body.position = new Vector3(feet.x, feet.y + bodyHeight / 2f, 0f);
            face.position = new Vector3(feet.x + facing * bodyWidth / 3f, feet.y + bodyHeight * 0.75f, 0f);

            float halfView = ViewWidth / 2f;
            float target = Mathf.Clamp(feet.x + facing * LookAhead, halfView, MapWidth - halfView);
            Vector3 position = camera.transform.position;
            position.x = Mathf.Lerp(position.x, target, 1f - Mathf.Exp(-CameraSmoothing * Time.deltaTime));
            camera.transform.position = position;
        }

        private void DrawBox(MapBox box)
        {
            var size = new Vector2((float)(box.MaxX - box.MinX), (float)(box.MaxY - box.MinY));
            GameObject block = CreateBlock(box.Id, box.OneWay ? PlatformColor : GroundColor, 0, size);
            block.transform.position = new Vector3((float)(box.MinX + box.MaxX) / 2f, (float)(box.MinY + box.MaxY) / 2f, 0f);
        }

        public GameObject CreateBlock(string name, Color color, int order, Vector2 size)
        {
            var block = new GameObject(name);
            var renderer = block.AddComponent<SpriteRenderer>();
            renderer.sprite = square;
            renderer.color = color;
            renderer.sortingOrder = order;
            block.transform.localScale = new Vector3(size.x, size.y, 1f);
            return block;
        }

        private static Sprite CreateSquare()
        {
            Texture2D texture = Texture2D.whiteTexture;
            var rect = new Rect(0f, 0f, texture.width, texture.height);
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), texture.width);
        }
    }
}
