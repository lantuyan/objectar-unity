using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ObjectAR.EditorTools
{
    /// <summary>
    /// Sanity checks for CI. Batch: -executeMethod ObjectAR.EditorTools.CIChecks.VerifyModels
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
    }
}
