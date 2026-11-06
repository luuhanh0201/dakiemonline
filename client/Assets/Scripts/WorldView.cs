using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Draws the test map as placeholder rectangles and follows the character with the camera.</summary>
    public sealed class WorldView
    {
        private const float ViewWidth = 12f;
        private const float MapWidth = 30f;
        private const float LookAhead = 0.1f * ViewWidth;
        private const float GroundMargin = 1.1f;
        private const float CameraSmoothing = 6f;
        private const float ShakeSeconds = 0.2f;
        private const float ShakeSize = 0.09f;
        private const float ShakeTurnsPerSecond = 16f;

        private static readonly Color SkyColor = new Color32(0x14, 0x1B, 0x3A, 0xFF);
        private static readonly Color GroundColor = new Color32(0x2A, 0x2F, 0x5E, 0xFF);
        private static readonly Color PlatformColor = new Color32(0x74, 0x80, 0xC8, 0xFF);

        private readonly Sprite square;
        private readonly Camera camera;
        private Vector3 shakeOffset;
        private float shakeLeft;

        public WorldView(IReadOnlyList<MapBox> boxes)
        {
            square = CreateSquare();
            camera = Camera.main;

            foreach (MapBox box in boxes)
            {
                DrawBox(box);
            }

            camera.backgroundColor = SkyColor;
            camera.orthographicSize = ViewWidth / camera.aspect / 2f;
            camera.transform.position = new Vector3(ViewWidth / 2f, camera.orthographicSize - GroundMargin, -10f);
        }

        public void Follow(Vector2 feet, int facing)
        {
            float halfView = ViewWidth / 2f;
            float target = Mathf.Clamp(feet.x + facing * LookAhead, halfView, MapWidth - halfView);
            Vector3 position = camera.transform.position - shakeOffset;
            position.x = Mathf.Lerp(position.x, target, 1f - Mathf.Exp(-CameraSmoothing * Time.deltaTime));

            shakeLeft = Mathf.Max(0f, shakeLeft - Time.deltaTime);
            float amount = shakeLeft / ShakeSeconds;
            float wave = Mathf.Sin(shakeLeft * ShakeTurnsPerSecond * Mathf.PI * 2f);
            shakeOffset = new Vector3(wave * 0.5f, wave, 0f) * ShakeSize * amount;

            camera.transform.position = position + shakeOffset;
        }

        /// <summary>A short jolt for the heaviest blows.</summary>
        public void Shake()
        {
            shakeLeft = ShakeSeconds;
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
