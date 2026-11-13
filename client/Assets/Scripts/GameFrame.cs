using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Where the game shows on screen while playing. A screen at least 16:9 wide is used whole; a taller
    /// one, such as a browser window made narrow, shows the game in a 16:9 band across the middle with
    /// black above and below, and the HUD and every on-screen button sit inside that band and scale with
    /// it. The account screens are not affected.
    /// </summary>
    public static class GameFrame
    {
        public const float MinAspect = 16f / 9f;

        private static readonly Color Backdrop = Color.black;
        private static Camera backdrop;

        /// <summary>True while a fight or an online stay is on screen.</summary>
        public static bool Active { get; set; }

        /// <summary>The frame in screen pixels, measured from the bottom left as input positions are.</summary>
        public static Rect Pixels
        {
            get
            {
                float width = Screen.width;
                float height = Screen.height;

                if (!Active || height <= 0f || width / height >= MinAspect)
                {
                    return new Rect(0f, 0f, width, height);
                }

                float frameHeight = width / MinAspect;
                return new Rect(0f, (height - frameHeight) / 2f, width, frameHeight);
            }
        }

        /// <summary>The same frame for OnGUI, which measures from the top left.</summary>
        public static Rect Gui
        {
            get
            {
                Rect frame = Pixels;
                return new Rect(frame.x, Screen.height - frame.yMax, frame.width, frame.height);
            }
        }

        /// <summary>The frame as shares of the screen, for a camera's viewport and canvas anchors.</summary>
        public static Rect Normalized
        {
            get
            {
                Rect frame = Pixels;
                float width = Mathf.Max(1f, Screen.width);
                float height = Mathf.Max(1f, Screen.height);
                return new Rect(frame.x / width, frame.y / height, frame.width / width, frame.height / height);
            }
        }

        /// <summary>Draws the world only inside the frame; a camera behind it paints the rest black.</summary>
        public static void Apply(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            Rect viewport = Normalized;

            if (camera.rect != viewport)
            {
                camera.rect = viewport;
            }

            if (backdrop == null)
            {
                backdrop = new GameObject("Frame backdrop").AddComponent<Camera>();
                backdrop.clearFlags = CameraClearFlags.SolidColor;
                backdrop.backgroundColor = Backdrop;
                backdrop.cullingMask = 0;
                backdrop.depth = camera.depth - 1f;
                Object.DontDestroyOnLoad(backdrop.gameObject);
            }

            backdrop.enabled = viewport.height < 0.999f;
        }
    }
}
