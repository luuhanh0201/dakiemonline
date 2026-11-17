using System.IO;
using System;
using System.Reflection;
using DaKiem.Client.Accounts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DaKiem.Client.Editor
{
    public static class AndroidUpdateReview
    {
        private static T Ui<T>(string method, params object[] arguments) => (T)typeof(AndroidAppUpdate).Assembly.GetType("DaKiem.Client.Accounts.UiKit").GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, arguments);
        public static void Run()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/android-update/preview"));
            Directory.CreateDirectory(output);
            foreach (Vector2Int size in new[] { new Vector2Int(1170, 540), new Vector2Int(1024, 768), new Vector2Int(1672, 941) })
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                RectTransform root = Ui<RectTransform>("CreateCanvas", "UpdatePreview", 80);
                var prompt = root.gameObject.AddComponent<AndroidAppUpdate>();
                Ui<Image>("Shade", root, 1f);
                typeof(AndroidAppUpdate).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(prompt, new object[] { Ui<RectTransform>("Stage", root), new AndroidReleaseInfo { versionName = "1.2.0", sizeBytes = 120000000, releaseNotes = "Cải thiện trải nghiệm và sửa lỗi.\nGiữ nguyên tài khoản khi cập nhật." }, (Action)(() => { }) });
                var camera = new GameObject("UpdatePreviewCamera").AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = size.y / 2f;
                camera.transform.position = new Vector3(0, 0, -1000);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(0x14, 0x1B, 0x3A, 0xFF);
                var target = new RenderTexture(size.x, size.y, 24);
                camera.targetTexture = target;
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 100;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = Mathf.Min(size.x / 1672f, size.y / 941f);
                canvas.scaleFactor = scaler.scaleFactor;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                var pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(output, size.x + "x" + size.y + ".png"), pixels.EncodeToPNG());
                RenderTexture.active = null;
                camera.targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
            }
            Debug.Log("Android update preview saved: " + output);
        }
    }
}
