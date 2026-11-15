using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ObjectAR.EditorTools
{
    /// <summary>
    /// Sanity checks for CI. Batch: -executeMethod ObjectAR.EditorTools.CIChecks.VerifyModels
    /// RenderPreview cần GPU (batchmode KHÔNG -nographics): tools/win.sh preview
    /// </summary>
    public static class CIChecks
    {
        public static readonly string[] Models =
            { "AcerNitro5", "house-bietthu", "house-cap4", "house-chungcu", "house-pho", "low-poly-building", "tree" };

        [MenuItem("ObjectAR/Verify Models", priority = 40)]
        public static void VerifyModels()
        {
            var failures = 0;
            foreach (var name in Models)
            {
                var go = Resources.Load<GameObject>("Models/" + name);
                if (go == null)
                {
                    Debug.LogError($"[ObjectAR] VerifyModels: Resources.Load<GameObject>(\"Models/{name}\") == null");
                    failures++;
                    continue;
                }
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                var mats = renderers.SelectMany(r => r.sharedMaterials).ToArray();
                var bad = mats.Where(m => m == null || m.shader == null || !m.shader.isSupported).ToArray();
                var shaders = string.Join(", ", mats.Where(m => m != null && m.shader != null).Select(m => m.shader.name).Distinct());
                Debug.Log($"[ObjectAR] VerifyModels: {name}: renderers={renderers.Length} materials={mats.Length} bad={bad.Length} shaders=[{shaders}]");
                if (renderers.Length == 0 || bad.Length > 0) failures++;
            }
            Debug.Log(failures == 0 ? "[ObjectAR] VerifyModels OK" : $"[ObjectAR] VerifyModels FAILED ({failures})");
            if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        public const string PreviewDir = "Builds/previews";

        /// Ảnh kiểm tra hệ toạ độ: mỗi bảng mặc định dựng như luồng AR (holder = tư thế ảnh tại gốc, ảnh nằm trong XZ,
        /// +z về cạnh trên mã) + tấm QR in thật nằm đúng chiều. Chụp 2 góc: "front" — người đứng trước tờ giấy
        /// (phía cạnh dưới mã, -z) nhìn xuống 45°; "top" — nhìn thẳng từ trên, cạnh trên mã ở trên ảnh.
        [MenuItem("ObjectAR/Render Previews", priority = 41)]
        public static void RenderPreview()
        {
            var ok = true;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Directory.CreateDirectory(PreviewDir);
                var light = new GameObject("light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
                RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f);
                var cam = new GameObject("cam").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
                cam.nearClipPlane = 0.01f;
                cam.fieldOfView = 40;
                var rt = new RenderTexture(1024, 768, 24) { antiAliasing = 4 };
                cam.targetTexture = rt;

                foreach (var b in MarkerStore.Defaults())
                {
                    var holder = new GameObject("holder").transform;   // = tư thế ARTrackedImage
                    BoardNode.Create(b, holder, null);
                    QRSheet(b, holder);
                    var c = new Vector3(0.05f, 0.02f, 0);
                    cam.transform.position = c + new Vector3(0, 0.32f, -0.32f);
                    cam.transform.LookAt(c);
                    Shoot(cam, rt, $"{b.id}-front");
                    cam.transform.SetPositionAndRotation(c + Vector3.up * 0.5f, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                    Shoot(cam, rt, $"{b.id}-top");
                    Object.DestroyImmediate(holder.gameObject);
                }
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Debug.Log($"[ObjectAR] RenderPreview OK → {Path.GetFullPath(PreviewDir)}");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                ok = false;
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// Tờ QR in (có viền trắng) nằm trong XZ: Quad xoay 90° quanh x → mặt nhìn lên, cạnh trên ảnh (v = 1) về +z.
        static void QRSheet(MarkerBoard b, Transform parent)
        {
            var tex = QRMaker.DisplayTexture(b, 8);
            var n = QRMaker.Modules(b.payload).GetLength(0);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.transform.SetParent(parent, false);
            q.transform.SetLocalPositionAndRotation(new Vector3(0, 0.0005f, 0), Quaternion.Euler(90, 0, 0));
            q.transform.localScale = Vector3.one * (b.widthCm / 100f * (n + 8) / n);
            q.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Texture")) { mainTexture = tex };
        }

        static void Shoot(Camera cam, RenderTexture rt, string name)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var img = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            img.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(PreviewDir, name + ".png"), img.EncodeToPNG());
            Object.DestroyImmediate(img);
        }
    }
}
