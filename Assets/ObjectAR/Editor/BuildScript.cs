using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ObjectAR.EditorTools
{
    /// <summary>
    /// Batch: -executeMethod ObjectAR.EditorTools.BuildScript.BuildAndroid  → Builds/ObjectAR.apk
    /// Exits with code 1 on failure (batchmode).
    /// </summary>
    public static class BuildScript
    {
        public const string ApkPath = "Builds/ObjectAR.apk";
        public const string IosPath = "Builds/iOS";

        [MenuItem("ObjectAR/Build Android APK", priority = 20)]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            Build(BuildTarget.Android, BuildTargetGroup.Android, ApkPath);
        }

        [MenuItem("ObjectAR/Build iOS Xcode project", priority = 21)]
        public static void BuildIOS() => Build(BuildTarget.iOS, BuildTargetGroup.iOS, IosPath);

        static void Build(BuildTarget target, BuildTargetGroup group, string path)
        {
            BuildReport report = null;
            try
            {
                ProjectSetup.Run();
                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0)
                {
                    SceneBuilder.Build();
                    scenes = new[] { SceneBuilder.ScenePath };
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                Debug.Log($"[ObjectAR] Building {target} → {path} (scenes: {string.Join(", ", scenes)})");
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = path,
                    target = target,
                    targetGroup = group,
                    options = BuildOptions.None,
                });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            var ok = report != null && report.summary.result == BuildResult.Succeeded;
            if (report != null)
            {
                var s = report.summary;
                Debug.Log($"[ObjectAR] BuildReport: result={s.result} platform={s.platform} output={s.outputPath} " +
                          $"size={s.totalSize / (1024f * 1024f):F1}MB time={s.totalTime} errors={s.totalErrors} warnings={s.totalWarnings}");
                foreach (var step in report.steps)
                    foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                        Debug.Log($"[ObjectAR] BuildError [{step.name}]: {m.content}");
            }
            Debug.Log(ok ? "[ObjectAR] BUILD SUCCEEDED" : "[ObjectAR] BUILD FAILED");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
