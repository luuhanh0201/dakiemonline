using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DaKiem.Client.Editor
{
    public static class AndroidBuild
    {
        private const string OutputPath = "Builds/Android/DaKiemOnline.apk";

        public static void Run()
        {
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            });

            Debug.Log($"Android build {report.summary.result}, {report.summary.totalSize} bytes.");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        private static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = "Dạ Kiếm Online";
            PlayerSettings.companyName = "Da Kiem Online";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.dakiemonline.game");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // Online play talks to the server over WebSocket.
            PlayerSettings.Android.forceInternetPermission = true;

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            EditorUserBuildSettings.buildAppBundle = false;
        }
    }
}
