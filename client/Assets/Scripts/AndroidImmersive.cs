using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Keeps Android's system bars hidden so a touch cannot bring the navigation bar over the
    /// buttons. A swipe from the screen edge still shows them briefly and the system hides them
    /// again. The engine can undo the request while starting, so <see cref="Apply"/> runs repeatedly.
    /// </summary>
    public static class AndroidImmersive
    {
        public static void Apply()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");

            activity.Call("runOnUiThread", new AndroidJavaRunnable(() => HideBars(activity)));
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private const int InsetsControllerApi = 30;
        private const int ShowTransientBarsBySwipe = 2;

        // SYSTEM_UI_FLAG_IMMERSIVE_STICKY with the hide-navigation, fullscreen and layout flags.
        private const int LegacyImmersiveFlags = 0x1706;

        private static void HideBars(AndroidJavaObject activity)
        {
            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            using AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow");
            using AndroidJavaObject decor = window.Call<AndroidJavaObject>("getDecorView");

            if (version.GetStatic<int>("SDK_INT") >= InsetsControllerApi)
            {
                HideBarsWithController(window, decor);
            }
            else
            {
                decor.Call("setSystemUiVisibility", LegacyImmersiveFlags);
            }
        }

        private static void HideBarsWithController(AndroidJavaObject window, AndroidJavaObject decor)
        {
            using var insetsType = new AndroidJavaClass("android.view.WindowInsets$Type");
            using AndroidJavaObject controller = window.Call<AndroidJavaObject>("getInsetsController");
            using AndroidJavaObject insets = decor.Call<AndroidJavaObject>("getRootWindowInsets");

            controller.Call("setSystemBarsBehavior", ShowTransientBarsBySwipe);

            if (insets == null)
            {
                return;
            }

            // Bars shown by a swipe do not count as visible here, so they stay until the system
            // hides them and the player has time to press them.
            bool barsShown = insets.Call<bool>("isVisible", insetsType.CallStatic<int>("navigationBars"))
                || insets.Call<bool>("isVisible", insetsType.CallStatic<int>("statusBars"));

            if (barsShown)
            {
                controller.Call("hide", insetsType.CallStatic<int>("systemBars"));
            }
        }
#endif
    }
}
