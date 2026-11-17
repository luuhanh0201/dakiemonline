using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DaKiem.Client.Accounts;
using Game.Rules;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace DaKiem.Client.Content
{
    public static class ContentUpdater
    {
        public const string Origin = "https://dakiem.online/";
        public static IEnumerator BeforeLogin()
        {
            string json = null;
            using (var baselineRequest = UnityWebRequest.Get(Application.streamingAssetsPath.Contains("://")
                ? Application.streamingAssetsPath + "/content/baseline.json"
                : "file://" + Application.streamingAssetsPath + "/content/baseline.json"))
            {
                baselineRequest.timeout = 10;
                yield return baselineRequest.SendWebRequest();
                if (baselineRequest.result == UnityWebRequest.Result.Success) json = baselineRequest.downloadHandler.text;
            }
            if (json == null) { Debug.LogWarning("No embedded content baseline; using built-in resources."); yield break; }
            ContentManifest baseline = null;
            try
            {
                baseline = JsonUtility.FromJson<ContentManifest>(json);
                baseline.Validate(Application.platform == RuntimePlatform.Android ? "android" : "windows", baseline.buildCode);
            }
            catch (Exception error) { Debug.LogWarning("Invalid content baseline: " + error.Message); yield break; }
            var original = new Dictionary<string, string>();
            foreach (string name in GameConfig.FileNames.Concat(new[] { GameConfig.AccountsFile, "login-scene.json" }))
                yield return ConfigFiles.LoadOriginal(name, original);
            ContentUpdateSession session = null;
            try
            {
                var cache = new ContentCache(Path.Combine(Application.persistentDataPath, "content-v1", baseline.platform), baseline);
                session = new ContentUpdateSession(cache, original, new Uri(Origin));
            }
            catch (Exception error) { Debug.LogWarning("Content cache unavailable: " + error.Message); yield break; }
            yield return ShowSession(session);
        }

        internal static IEnumerator ShowSession(ContentUpdateSession session)
        {
            ContentAssets.Configure(session.Cache, session.Current);
            UiKit.ClearArtCache();
            RectTransform canvas = UiKit.CreateCanvas("ContentUpdate", 100);
            UiKit.Shade(canvas, 1f);
            RectTransform stage = UiKit.Stage(canvas);
            RectTransform frame = UiKit.Place(UiKit.Rect("ContentPanel", stage), 350, 190, 972, 590);
            var panel = frame.gameObject.AddComponent<Image>();
            panel.sprite = UiKit.Art("panel"); panel.type = Image.Type.Sliced; panel.pixelsPerUnitMultiplier = 1.75f;
            UiKit.Place(UiKit.Label(stage, "CẬP NHẬT DỮ LIỆU", 42, UiKit.Gold), 430, 310, 812, 65);
            Text status = UiKit.Place(UiKit.Label(stage, "Đang kiểm tra...", 29, UiKit.Light, TextAnchor.MiddleCenter, false, FontStyle.Normal), 445, 395, 782, 110);
            status.supportRichText = false;
            var barBack = UiKit.Place(UiKit.Rect("ProgressBackground", stage), 465, 535, 742, 32).gameObject.AddComponent<Image>();
            barBack.color = new Color32(5, 12, 32, 255);
            var bar = UiKit.Rect("Progress", barBack.transform).gameObject.AddComponent<Image>();
            UiKit.Fill(bar.rectTransform);
            bar.color = UiKit.Gold;
            int choice = 0;
            Button retry = UiKit.Place(UiKit.Button(stage, "THỬ LẠI", UiKit.ButtonKind.Gold, () => choice = 1, 30), 460, 650, 340, 82);
            Button keep = UiKit.Place(UiKit.Button(stage, "DÙNG BẢN HIỆN TẠI", UiKit.ButtonKind.Blue, () => choice = 2, 26), 872, 650, 340, 82);
            do
            {
                choice = 0;
                retry.gameObject.SetActive(false); keep.gameObject.SetActive(false);
                yield return session.Run(message =>
                {
                    status.text = message;
                    bar.rectTransform.anchorMax = new Vector2(session.Progress, 1f);
                });
                if (session.Success)
                {
                    status.text = "Dữ liệu đã sẵn sàng.";
                    bar.rectTransform.anchorMax = Vector2.one;
                    yield return new WaitForSecondsRealtime(0.3f);
                    break;
                }
                if (session.Unavailable) break;
                status.text = session.Error;
                retry.gameObject.SetActive(true); keep.gameObject.SetActive(true);
                while (choice == 0) yield return null;
            } while (choice == 1);
            canvas.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(canvas.gameObject);
            UiKit.ClearArtCache();
            ContentAssets.Configure(session.Cache, session.Current);
            yield return null;
        }
    }
}
