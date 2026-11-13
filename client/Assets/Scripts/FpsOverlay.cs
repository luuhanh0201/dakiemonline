using UnityEngine;

namespace DaKiem.Client
{
    public sealed class FpsOverlay : MonoBehaviour
    {
        private const float SampleSeconds = 0.5f;
        private const float SettleSeconds = 3f;
        private const int FontSizeDivisor = 24;

        private float elapsed;
        private int frames;
        private int fps;
        private GUIStyle style;

        /// <summary>Lowest half-second sample since the last reset, for the player test table.</summary>
        public static int LowestFps { get; private set; } = int.MaxValue;

        // Loading and switching back from another app drop the frame rate without the fight being
        // slow, so samples are ignored for a moment after either.
        private static float ignoreSamplesUntil;

        public static void ResetLowestFps()
        {
            LowestFps = int.MaxValue;
            IgnoreSamplesForAMoment();
        }

        private static void IgnoreSamplesForAMoment()
        {
            ignoreSamplesUntil = Time.unscaledTime + SettleSeconds;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            // Android caps at 30 FPS by default, which would hide how much headroom the build has.
            Application.targetFrameRate = 60;
            new GameObject(nameof(FpsOverlay)).AddComponent<FpsOverlay>();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                IgnoreSamplesForAMoment();
            }
        }

        private void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            frames++;

            if (elapsed >= SampleSeconds)
            {
                fps = Mathf.RoundToInt(frames / elapsed);
                LowestFps = Time.unscaledTime < ignoreSamplesUntil ? LowestFps : Mathf.Min(LowestFps, fps);
                elapsed = 0f;
                frames = 0;
            }
        }

        /// <summary>Set while the login screens are up: the count would sit on the logo.</summary>
        public static bool Hidden { get; set; }

        private void OnGUI()
        {
            if (Hidden)
            {
                return;
            }

            style ??= new GUIStyle(GUI.skin.label) { normal = { textColor = Color.white } };

            // Inside the game frame, like the HUD, and sized with it when the window changes.
            Rect frame = GameFrame.Gui;
            style.fontSize = Mathf.Max(1, (int)(frame.height / FontSizeDivisor));
            GUI.Label(new Rect(frame.x + frame.width * 0.3f, frame.y + frame.height / 12f, frame.width, frame.height / 8f), $"FPS {fps}", style);
        }
    }
}
