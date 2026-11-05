using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Keeps Android's system bars hidden. Without this, a touch brings the navigation bar
    /// back over the buttons; with it, the bars only appear briefly after a swipe from the edge.
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

            if (version.GetStatic<int>("SDK_INT") >= InsetsControllerApi)
            {
                using var insetsType = new AndroidJavaClass("android.view.WindowInsets$Type");
                using AndroidJavaObject controller = window.Call<AndroidJavaObject>("getInsetsController");

                controller.Call("setSystemBarsBehavior", ShowTransientBarsBySwipe);
                controller.Call("hide", insetsType.CallStatic<int>("systemBars"));
            }
            else
            {
                using AndroidJavaObject decor = window.Call<AndroidJavaObject>("getDecorView");
                decor.Call("setSystemUiVisibility", LegacyImmersiveFlags);
            }
        }
#endif
    }
}
