using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    /// <summary>Builds the web version for testing. The game server serves it, so it reaches /ws on the same address.</summary>
    public static class WebBuild
    {
        private const string OutputPath = "Builds/Web";

        public static void Run()
        {
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });

            Debug.Log($"Web build {report.summary.result}, {report.summary.totalSize} bytes.");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = "Dạ Kiếm Online";
            PlayerSettings.companyName = "Da Kiem Online";

            // Gzip files are sent with Content-Encoding by the game server, so the browser unpacks them itself.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultWebScreenWidth = 1170;
            PlayerSettings.defaultWebScreenHeight = 540;
        }
    }
}
