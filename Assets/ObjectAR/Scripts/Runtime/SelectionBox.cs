using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace ObjectAR
{
    /// Khung dây vàng quanh model đang chọn (MeshTopology.Lines, không ghi depth).
    public static class SelectionBox
    {
        static Material material;
        static Mesh mesh;

        static Material Material
        {
            get
            {
                if (material != null) return material;
                // Internal-Colored luôn có trong build; Unlit/Color để dự phòng.
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                material = new Material(sh) { name = "SelectionBox", color = Color.yellow, hideFlags = HideFlags.DontSave };
                material.SetInt("_ZWrite", 0);
                material.SetInt("_Cull", (int)CullMode.Off);
                return material;
            }
        }

        /// Hộp dây đơn vị, gốc ở tâm đáy (y = 0 là mặt bàn) — dùng chung, co giãn bằng localScale.
        static Mesh Mesh
        {
            get
            {
                if (mesh != null) return mesh;
                mesh = new Mesh { name = "SelectionBox", hideFlags = HideFlags.DontSave };
                mesh.vertices = new[]
                {
                    new Vector3(-.5f, 0, -.5f), new Vector3(.5f, 0, -.5f), new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f),
                    new Vector3(-.5f, 1, -.5f), new Vector3(.5f, 1, -.5f), new Vector3(.5f, 1, .5f), new Vector3(-.5f, 1, .5f),
                };
                mesh.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), 8).ToArray();
                mesh.SetIndices(new[] { 0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7 },
                                MeshTopology.Lines, 0);
                return mesh;
            }
        }

        /// Khung kích thước size, tâm đáy tại bottomCenter (hệ parent).
        public static GameObject Create(Transform parent, Vector3 bottomCenter, Vector3 size, string name = "sel")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = bottomCenter;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }
}
