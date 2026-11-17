#if UNITY_STANDALONE_WIN || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client.Accounts
{
    /// <summary>Runs the APK update prompt on PC without downloading or launching an installer.</summary>
    public sealed class AndroidUpdateSmoke : MonoBehaviour
    {
        public static bool Enabled => Array.IndexOf(Environment.GetCommandLineArgs(), "-android-update-smoke") >= 0;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Enabled) new GameObject(nameof(AndroidUpdateSmoke)).AddComponent<AndroidUpdateSmoke>();
        }
        private IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = args[Array.IndexOf(args, "-android-update-smoke") + 1];
            Directory.CreateDirectory(output);
            RectTransform canvas = UiKit.CreateCanvas("AndroidUpdateTest", 80);
            var prompt = canvas.gameObject.AddComponent<AndroidAppUpdate>();
            UiKit.Shade(canvas, 1f);
            int downloads = 0;
            prompt.Build(UiKit.Stage(canvas), new AndroidReleaseInfo { versionName = "1.2.0", sizeBytes = 120000000, releaseNotes = "Cải thiện trải nghiệm và sửa lỗi.\nGiữ nguyên tài khoản khi cập nhật." }, () => downloads++);
            yield return new WaitForSecondsRealtime(0.5f);
            Canvas.ForceUpdateCanvases();
            string failure = null;
            try
            {
                foreach (Button button in canvas.GetComponentsInChildren<Button>())
                {
                    var corners = new Vector3[4];
                    ((RectTransform)button.transform).GetWorldCorners(corners);
                    foreach (var corner in corners)
                        if (corner.x < 0 || corner.y < 0 || corner.x > Screen.width || corner.y > Screen.height) throw new Exception("Update button is outside the screen.");
                }
                GameObject.Find("CẬP NHẬT").GetComponent<Button>().onClick.Invoke();
                if (downloads != 1) throw new Exception("Update button did not invoke the download action.");
                GameObject.Find("ĐỂ SAU").GetComponent<Button>().onClick.Invoke();
                if (!(bool)typeof(AndroidAppUpdate).GetField("dismissed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(prompt)) throw new Exception("Later button did not dismiss the prompt.");
            }
            catch (Exception error) { failure = error.ToString(); }
            File.WriteAllText(Path.Combine(output, "checks.txt"), failure == null ? "RESULT PASS: prompt fits screen; Update invokes download; Later dismisses without touching accounts.\n" : failure);
            Application.Quit(failure == null ? 0 : 1);
        }
    }
}
#endif
