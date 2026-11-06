using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    /// <summary>Builds a Windows version for trying changes on the computer before the phone.</summary>
    public static class PcBuild
    {
        private const string OutputPath = "Builds/PC/DaKiemOnline.exe";

        // Half of the phone's 2340 x 1080 screen, so the view matches what the phone shows.
        private const int WindowWidth = 1170;
        private const int WindowHeight = 540;

        public static void Run()
        {
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            Debug.Log($"PC build {report.summary.result}, {report.summary.totalSize} bytes.");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = "Dạ Kiếm Online";
            PlayerSettings.companyName = "Da Kiem Online";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = WindowWidth;
            PlayerSettings.defaultScreenHeight = WindowHeight;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.runInBackground = true;
        }
    }
}
