using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Draws the sample map from its art and follows the character with the camera.</summary>
    public sealed class WorldView
    {
        private const string ArtFolder = "World/";
        private const float ViewWidth = 12f;
        private const float MapWidth = 30f;
        private const float LookAhead = 0.1f * ViewWidth;
        private const float GroundMargin = 0.95f;
        private const float CameraSmoothing = 6f;
        private const float ShakeSeconds = 0.2f;
        private const float ShakeSize = 0.09f;
        private const float ShakeTurnsPerSecond = 16f;

        // The background is wider than the view, so it can drift without showing an edge.
        private const float BackgroundWidth = ViewWidth * 1.3f;
        private const float BackgroundDrift = 0.15f;

        // How far down each image its walkable surface lies, as a share of its height.
        private const float GroundSurfaceShare = 0.111f;

        // The drawn grass rises this far behind the feet, so they stand on it, not on its top edge.
        private const float GrassBehindFeet = 0.2f;
        private const float PlatformSurfaceShare = 0.17f;

        // Things stand a little way into the ground, so the grass in front hides where they end.
        private const float LargePropSink = 0.16f;
        private const float SmallPropSink = 0.09f;

        // A platform rests on a rock, with this much of the rock below the grass.
        private const string PlatformRock = "rock_big";
        private const float PlatformRockSink = 0.44f;

        private const int BackgroundOrder = -100;
        private const int LargePropOrder = -30;
        private const int SmallPropOrder = -29;
        private const int GroundOrder = -10;
        private const int PlatformOrder = -9;

        private static readonly Color SkyColor = new Color32(0x14, 0x1B, 0x3A, 0xFF);

        private static readonly (string Name, float X, bool Large, bool Flip)[] Props =
        {
            ("tree_pink", 1.6f, true, false),
            ("bush", 3.5f, false, false),
            ("flower_a", 4.1f, false, false),
            ("house", 7.4f, true, false),
            ("flower_b", 10.6f, false, false),
            ("rock_a", 11.2f, false, false),
            ("tree_green", 13.6f, true, false),
            ("flower_c", 15.3f, false, false),
            ("rock_b", 16.1f, false, false),
            ("tree_pink", 18.4f, true, true),
            ("bush", 20.6f, false, true),
            ("flower_a", 21.3f, false, true),
            ("house", 24.2f, true, true),
            ("rock_a", 26.8f, false, true),
            ("flower_b", 27.4f, false, true),
            ("tree_green", 28.8f, true, true),
        };

        private readonly Sprite square;
        private readonly Camera camera;
        private readonly Transform background;
        private readonly float cameraTop;
        private Vector3 shakeOffset;
        private float shakeLeft;

        public WorldView(IReadOnlyList<MapBox> boxes)
        {
            square = CreateSquare();
            camera = Camera.main;
            camera.backgroundColor = SkyColor;
            camera.orthographicSize = ViewWidth / camera.aspect / 2f;
            camera.transform.position = new Vector3(ViewWidth / 2f, camera.orthographicSize - GroundMargin, -10f);
            cameraTop = camera.transform.position.y + camera.orthographicSize;

            background = Place("bg", ViewWidth / 2f, 0f, BackgroundOrder, false).transform;
            float scale = BackgroundWidth / background.GetComponent<SpriteRenderer>().bounds.size.x;
            background.localScale = new Vector3(scale, scale, 1f);
            PlaceBackground(camera.transform.position.x);

            foreach ((string name, float x, bool large, bool flip) in Props)
            {
                Place(name, x, GrassBehindFeet - (large ? LargePropSink : SmallPropSink), large ? LargePropOrder : SmallPropOrder, flip);
            }

            foreach (MapBox box in boxes)
            {
                if (box.OneWay)
                {
                    DrawPlatform(box);
                }
                else if (box.Id == "ground")
                {
                    DrawGround(box);
                }
            }
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
            PlaceBackground(position.x);
        }

        /// <summary>Moves the camera at once, for the review tools.</summary>
        public void Jump(float x)
        {
            Vector3 position = camera.transform.position;
            position.x = x;
            camera.transform.position = position;
            PlaceBackground(x);
        }

        /// <summary>A short jolt for the heaviest blows.</summary>
        public void Shake()
        {
            shakeLeft = ShakeSeconds;
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

        private void PlaceBackground(float cameraX)
        {
            float height = background.GetComponent<SpriteRenderer>().bounds.size.y;
            float x = cameraX + (MapWidth / 2f - cameraX) * BackgroundDrift;

            background.position = new Vector3(x, cameraTop - height / 2f, 0f);
        }

        private void DrawGround(MapBox box)
        {
            float x = (float)box.MinX;

            while (x < (float)box.MaxX)
            {
                GameObject tile = Place("ground", x, 0f, GroundOrder, false);
                Vector2 size = tile.GetComponent<SpriteRenderer>().bounds.size;

                tile.transform.position = new Vector3(x + size.x / 2f, (float)box.MaxY + GrassBehindFeet - size.y * (0.5f - GroundSurfaceShare), 0f);
                x += size.x;
            }
        }

        private void DrawPlatform(MapBox box)
        {
            float x = (float)(box.MinX + box.MaxX) / 2f;

            Place(PlatformRock, x, GrassBehindFeet - PlatformRockSink, SmallPropOrder, box.MinX > MapWidth / 2f);

            GameObject platform = Place("platform", x, 0f, PlatformOrder, false);
            float height = platform.GetComponent<SpriteRenderer>().bounds.size.y;
            Vector3 position = platform.transform.position;

            position.y = (float)box.MaxY - height * (0.5f - PlatformSurfaceShare);
            platform.transform.position = position;
        }

        private static GameObject Place(string art, float x, float bottom, int order, bool flip)
        {
            Sprite sprite = Resources.Load<Sprite>(ArtFolder + art);
            var item = new GameObject(art);
            var renderer = item.AddComponent<SpriteRenderer>();

            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.flipX = flip;
            item.transform.position = new Vector3(x, bottom + sprite.bounds.size.y / 2f, 0f);
            return item;
        }

        private static Sprite CreateSquare()
        {
            Texture2D texture = Texture2D.whiteTexture;
            var rect = new Rect(0f, 0f, texture.width, texture.height);
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), texture.width);
        }
    }
}
