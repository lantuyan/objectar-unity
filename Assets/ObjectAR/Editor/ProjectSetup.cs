using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.ARCore;
using UnityEditor.XR.ARKit;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

namespace ObjectAR.EditorTools
{
    /// <summary>
    /// Idempotent project configuration (PlayerSettings + XR Plug-in Management).
    /// Batch: -executeMethod ObjectAR.EditorTools.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        public const string CompanyName = "Sixpilot";
        public const string ProductName = "ObjectAR";
        public const string BundleId = "com.sixpilot.objectar";
        public const string CameraUsage = "Dùng camera để nhận mã QR và hiển thị mô hình 3D AR";

        const string XRFolder = "Assets/XR";
        const string XRSettingsFolder = "Assets/XR/Settings";
        const string ARCoreLoader = "UnityEngine.XR.ARCore.ARCoreLoader";
        const string ARKitLoader = "UnityEngine.XR.ARKit.ARKitLoader";

        [MenuItem("ObjectAR/Setup Project", priority = 0)]
        public static void Run()
        {
            ConfigurePlayer();
            ConfigureXR();
            IncludeShaders("Unlit/Color", "Hidden/Internal-Colored");
            AssetDatabase.SaveAssets();
            Debug.Log("[ObjectAR] ProjectSetup done.");
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            // Android (ARCore)
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

            // iOS (ARKit)
            PlayerSettings.iOS.cameraUsageDescription = CameraUsage;
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);

            SetActiveInputHandlingBoth();
        }

        /// <summary>Player Settings > Active Input Handling = Both (no public API in 2022.3).</summary>
        static void SetActiveInputHandlingBoth()
        {
            var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (ps == null) { Debug.LogWarning("[ObjectAR] Cannot load ProjectSettings.asset"); return; }
            var so = new SerializedObject(ps);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null) { Debug.LogWarning("[ObjectAR] activeInputHandler not found"); return; }
            if (prop.intValue != 2)
            {
                prop.intValue = 2; // 0 = old, 1 = new, 2 = both
                so.ApplyModifiedProperties();
                Debug.Log("[ObjectAR] Active Input Handling = Both (an open editor needs a restart to apply).");
            }
        }

        /// <summary>Graphics Settings > Always Included Shaders: shader runtime tạo bằng Shader.Find (SelectionBox, màn hình laptop).</summary>
        static void IncludeShaders(params string[] names)
        {
            var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in names)
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogWarning($"[ObjectAR] Shader {name} not found"); continue; }
                var has = false;
                for (var i = 0; i < arr.arraySize && !has; i++) has = arr.GetArrayElementAtIndex(i).objectReferenceValue == sh;
                if (has) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
                Debug.Log($"[ObjectAR] Always Included Shaders += {name}");
            }
            so.ApplyModifiedProperties();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void ConfigureXR()
        {
            EnsureFolder(XRSettingsFolder);

            var perTarget = GetOrCreatePerBuildTarget();

            var android = GetOrCreateManager(perTarget, BuildTargetGroup.Android);
            var ios = GetOrCreateManager(perTarget, BuildTargetGroup.iOS);
            var standalone = GetOrCreateManager(perTarget, BuildTargetGroup.Standalone);

            AssignOnly(android, BuildTargetGroup.Android, ARCoreLoader);
            AssignOnly(ios, BuildTargetGroup.iOS, ARKitLoader);
            AssignOnly(standalone, BuildTargetGroup.Standalone, null);

            foreach (var g in new[] { BuildTargetGroup.Android, BuildTargetGroup.iOS })
            {
                var s = perTarget.SettingsForBuildTarget(g);
                if (!s.InitManagerOnStart) { s.InitManagerOnStart = true; EditorUtility.SetDirty(s); }
            }
            EditorUtility.SetDirty(perTarget);

            // ARCore: AR required
            var arcore = ARCoreSettings.currentSettings;
            if (arcore == null)
            {
                arcore = ScriptableObject.CreateInstance<ARCoreSettings>();
                AssetDatabase.CreateAsset(arcore, XRSettingsFolder + "/ARCoreSettings.asset");
                ARCoreSettings.currentSettings = arcore;
            }
            if (arcore.requirement != ARCoreSettings.Requirement.Required)
            {
                arcore.requirement = ARCoreSettings.Requirement.Required;
                EditorUtility.SetDirty(arcore);
            }

            // ARKit: AR required
            var arkit = ARKitSettings.currentSettings;
            if (arkit == null)
            {
                arkit = ScriptableObject.CreateInstance<ARKitSettings>();
                AssetDatabase.CreateAsset(arkit, XRSettingsFolder + "/ARKitSettings.asset");
                ARKitSettings.currentSettings = arkit;
            }
            if (arkit.requirement != ARKitSettings.Requirement.Required)
            {
                arkit.requirement = ARKitSettings.Requirement.Required;
                EditorUtility.SetDirty(arkit);
            }
        }

        static XRGeneralSettingsPerBuildTarget GetOrCreatePerBuildTarget()
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                var guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
                if (guids.Length > 0)
                    perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, XRFolder + "/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            return perTarget;
        }

        static XRManagerSettings GetOrCreateManager(XRGeneralSettingsPerBuildTarget perTarget, BuildTargetGroup group)
        {
            if (!perTarget.HasSettingsForBuildTarget(group))
                perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            return perTarget.ManagerSettingsForBuildTarget(group);
        }

        /// <summary>Make <paramref name="loaderType"/> the only active loader (null = none).</summary>
        static void AssignOnly(XRManagerSettings manager, BuildTargetGroup group, string loaderType)
        {
            foreach (var loader in manager.activeLoaders.ToList())
            {
                if (loader == null) continue;
                var name = loader.GetType().FullName;
                if (name != loaderType)
                    XRPackageMetadataStore.RemoveLoader(manager, name, group);
            }
            if (loaderType != null && !manager.activeLoaders.Any(l => l != null && l.GetType().FullName == loaderType))
            {
                if (!XRPackageMetadataStore.AssignLoader(manager, loaderType, group))
                    Debug.LogError($"[ObjectAR] Failed to assign {loaderType} for {group}");
            }
            EditorUtility.SetDirty(manager);
            Debug.Log($"[ObjectAR] XR {group}: [{string.Join(", ", manager.activeLoaders.Where(l => l != null).Select(l => l.GetType().Name))}]");
        }
    }
}
