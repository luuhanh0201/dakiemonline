using UnityEngine;

namespace DaKiem.Client
{
    public sealed class FpsOverlay : MonoBehaviour
    {
        private const float SampleSeconds = 0.5f;
        private const int FontSizeDivisor = 24;

        private float elapsed;
        private int frames;
        private int fps;
        private GUIStyle style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            // Android caps at 30 FPS by default, which would hide how much headroom the build has.
            Application.targetFrameRate = 60;
            new GameObject(nameof(FpsOverlay)).AddComponent<FpsOverlay>();
        }

        private void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            frames++;

            if (elapsed >= SampleSeconds)
            {
                fps = Mathf.RoundToInt(frames / elapsed);
                elapsed = 0f;
                frames = 0;
            }
        }

        private void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = Screen.height / FontSizeDivisor,
                normal = { textColor = Color.white },
            };

            GUI.Label(new Rect(20f, 60f, Screen.width, Screen.height / 8f), $"FPS {fps}", style);
        }
    }
}
