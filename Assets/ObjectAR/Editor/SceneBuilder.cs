using System;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;

namespace ObjectAR.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Main.unity as described in docs/PORTING.md ("Scene").
    /// Batch: -executeMethod ObjectAR.EditorTools.SceneBuilder.Build
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        static readonly string[] AppComponents = { "ObjectAR.MarkerManager", "ObjectAR.UI.ObjectARUI" };

        [MenuItem("ObjectAR/Build Main Scene", priority = 1)]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[ObjectAR] SceneBuilder cancelled.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // AR Session
            var session = new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));

            // XR Origin + Camera Offset + Main Camera
            var originGo = new GameObject("XR Origin", typeof(XROrigin));
            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(originGo.transform, false);

            var camGo = new GameObject("Main Camera",
                typeof(Camera), typeof(AudioListener),
                typeof(ARCameraManager), typeof(ARCameraBackground), typeof(TrackedPoseDriver));
            camGo.transform.SetParent(offsetGo.transform, false);
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 50f;

            var tpd = camGo.GetComponent<TrackedPoseDriver>();
            var pos = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            pos.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rot = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rot.AddBinding("<HandheldARInputDevice>/deviceRotation");
            tpd.positionInput = new InputActionProperty(pos);
            tpd.rotationInput = new InputActionProperty(rot);

            var origin = originGo.GetComponent<XROrigin>();
            origin.CameraFloorOffsetObject = offsetGo;
            origin.Camera = cam;

            var images = originGo.AddComponent<ARTrackedImageManager>();
            images.referenceLibrary = null;
            images.requestedMaxNumberOfMovingImages = 4;
            images.enabled = false; // MarkerManager enables it after building the runtime library

            // Directional Light
            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // EventSystem
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // App root
            var app = new GameObject("ObjectAR");
            foreach (var typeName in AppComponents)
            {
                var t = FindType(typeName);
                if (t == null)
                {
                    Debug.LogWarning($"[ObjectAR] SceneBuilder: type {typeName} not found (not written yet?) — skipped.");
                    continue;
                }
                app.AddComponent(t);
            }

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new Exception("[ObjectAR] Failed to save " + ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[ObjectAR] SceneBuilder: saved {ScenePath} (session={session.name}, components on ObjectAR: " +
                      string.Join(", ", app.GetComponents<Component>().Select(c => c.GetType().Name)) + ")");
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null && typeof(Component).IsAssignableFrom(t)) return t;
            }
            return null;
        }
    }
}
