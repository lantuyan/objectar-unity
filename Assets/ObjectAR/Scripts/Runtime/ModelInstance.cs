using UnityEngine;

namespace ObjectAR
{
    /// Một bản sao mô hình 3D có ModelSpec (vd laptop). Gốc = tâm đáy thân máy, mặt trước hướng +z (hệ dữ liệu).
    /// Cây: model-overlay → orient (Euler 0, 180 - frontYaw) → content (bản glTF, scale = unitScale).
    public sealed class ModelInstance : MonoBehaviour
    {
        Transform lid;

        public static ModelInstance Create(GameObject template, ModelSpec spec)
        {
            var mi = new GameObject("model-overlay").AddComponent<ModelInstance>();
            var orient = NodeUtil.Child(mi.transform, "orient");
            // glTFast đảo trục X khi nhập (= gương z + xoay 180°) → bù 180°, yaw đổi dấu.
            orient.localRotation = Quaternion.Euler(0, 180f - spec.FrontYaw * Mathf.Rad2Deg, 0);
            var content = Instantiate(template, orient, false).transform;

            // Căn: tâm (x,z) và đáy của thân máy về gốc.
            var baseRef = spec.BaseNode != null ? NodeUtil.Find(content, spec.BaseNode) : null;
            var b = NodeUtil.BoundsIn(content, (baseRef != null ? baseRef : content).gameObject);
            var pivot = new Vector3(b.center.x, b.min.y, b.center.z);
            content.localScale = Vector3.one * spec.UnitScale;
            content.localPosition -= orient.InverseTransformPoint(content.TransformPoint(pivot));

            mi.lid = spec.LidNode != null ? NodeUtil.Find(content, spec.LidNode) : null;
            if (spec.Screen != null && mi.lid != null) AddScreen(spec.Screen, mi.lid);
            mi.SetLid(spec.LidDefault);
            return mi;
        }

        /// Tấm màn hình đen (hộp mỏng) phủ lên mặt trong nắp laptop. Toạ độ x đảo dấu vì glTFast đảo trục X.
        static void AddScreen(ScreenSpec sc, Transform lid)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.name = "Screen";
            DestroyImmediate(s.GetComponent<Collider>());
            var r = s.GetComponent<MeshRenderer>();
            // Unlit/Color (Always Included qua ProjectSetup) — Standard của primitive có thể bị strip khỏi build.
            r.sharedMaterial = new Material(Shader.Find("Unlit/Color")) { name = "Screen", color = new Color(0.02f, 0.02f, 0.02f) };
            s.transform.SetParent(lid, false);
            s.transform.localPosition = new Vector3(-(sc.X0 + sc.X1) / 2, sc.Y, (sc.Z0 + sc.Z1) / 2);
            s.transform.localScale = new Vector3(sc.X1 - sc.X0, 0.2f, sc.Z1 - sc.Z0);
        }

        /// Góc mở nắp (độ). Dấu đảo so với SceneKit vì glTFast đảo trục X.
        public void SetLid(float degrees)
        {
            if (lid != null) lid.localRotation = Quaternion.AngleAxis(-degrees, Vector3.forward);
        }
    }
}
