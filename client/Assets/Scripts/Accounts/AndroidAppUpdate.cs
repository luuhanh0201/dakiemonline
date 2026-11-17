using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace DaKiem.Client.Accounts
{
    /// <summary>Checks the APK release before login; Android's installer handles the downloaded update.</summary>
    public sealed class AndroidAppUpdate : MonoBehaviour
    {
        public const string ManifestUrl = "https://dakiem.online/updates/android.json";
        private bool dismissed;

        public static IEnumerator CheckBeforeLogin()
        {
            int installedCode = InstalledVersionCode();
            if (installedCode <= 0) yield break;
            RectTransform canvas = UiKit.CreateCanvas("AndroidUpdate", 80);
            var prompt = canvas.gameObject.AddComponent<AndroidAppUpdate>();
            UiKit.Shade(canvas, 1f);
            RectTransform stage = UiKit.Stage(canvas);
            Text checking = UiKit.Place(UiKit.Label(stage, "Đang kiểm tra cập nhật...", 36, UiKit.Light), 350, 370, 972, 150);
            AndroidReleaseInfo release = null;
            using (var request = UnityWebRequest.Get(ManifestUrl + "?check=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            {
                request.timeout = 8;
                request.SetRequestHeader("Cache-Control", "no-cache");
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try { release = JsonUtility.FromJson<AndroidReleaseInfo>(request.downloadHandler.text); }
                    catch (ArgumentException) { Debug.LogWarning("Android update manifest is invalid; continuing to login."); }
                }
                else Debug.Log("Android update check unavailable; continuing to login.");
            }
            Destroy(checking.gameObject);
            if (release != null && release.IsUpdateFor(installedCode, Application.identifier))
            {
                prompt.Build(stage, release, () => Application.OpenURL(release.apkUrl));
                while (!prompt.dismissed) yield return null;
            }
            canvas.gameObject.SetActive(false);
            Destroy(canvas.gameObject);
        }

        internal void Build(RectTransform stage, AndroidReleaseInfo release, Action download)
        {
            RectTransform frame = UiKit.Place(UiKit.Rect("UpdatePanel", stage), 350, 190, 972, 590);
            var image = frame.gameObject.AddComponent<Image>();
            image.sprite = UiKit.Art("panel");
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.75f;
            Text title = UiKit.Place(UiKit.Label(stage, "ĐÃ CÓ BẢN CẬP NHẬT", 42, UiKit.Gold), 430, 305, 812, 65);
            title.supportRichText = false;
            string size = release.sizeBytes > 0 ? $" • {Mathf.CeilToInt(release.sizeBytes / 1048576f)} MB" : "";
            Text version = UiKit.Place(UiKit.Label(stage, $"Phiên bản {release.versionName}{size}", 30, UiKit.Light), 430, 375, 812, 45);
            version.supportRichText = false;
            string notes = string.IsNullOrWhiteSpace(release.releaseNotes) ? "Cải thiện trải nghiệm và sửa lỗi." : release.releaseNotes;
            if (notes.Length > 300) notes = notes.Substring(0, 297) + "...";
            Text details = UiKit.Place(UiKit.Label(stage, notes, 28, UiKit.Light, TextAnchor.MiddleCenter, false, FontStyle.Normal), 445, 430, 782, 115);
            details.supportRichText = false;
            details.verticalOverflow = VerticalWrapMode.Truncate;
            Text hint = UiKit.Place(UiKit.Label(stage, "Tải APK mới, rồi mở tệp để cài cập nhật. Giữ nguyên ứng dụng đang cài.", 25, UiKit.Light, TextAnchor.MiddleCenter, false, FontStyle.Normal), 445, 550, 782, 75);
            UiKit.Place(UiKit.Button(stage, "ĐỂ SAU", UiKit.ButtonKind.Blue, () => dismissed = true, 32), 460, 650, 340, 82);
            UiKit.Place(UiKit.Button(stage, "CẬP NHẬT", UiKit.ButtonKind.Gold, () =>
            {
                download();
                hint.text = "Sau khi tải xong, mở tệp APK và xác nhận Cài đặt. Nếu chưa tải được, bấm Cập nhật để thử lại.";
            }, 32), 872, 650, 340, 82);
        }

        private static int InstalledVersionCode()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var manager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var info = manager.Call<AndroidJavaObject>("getPackageInfo", Application.identifier, 0))
                    return info.Get<int>("versionCode");
            }
            catch (AndroidJavaException) { Debug.LogWarning("Could not read the installed Android version."); }
#endif
            return 0;
        }
    }
}
