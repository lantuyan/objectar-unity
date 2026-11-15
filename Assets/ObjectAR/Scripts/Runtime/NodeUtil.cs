using UnityEngine;

namespace ObjectAR
{
    /// Tiện ích cây Transform dùng chung.
    public static class NodeUtil
    {
        public static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        /// Tìm node con theo tên (đệ quy, gồm cả root).
        public static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var f = Find(c, name);
                if (f != null) return f;
            }
            return null;
        }

        /// Hộp bao các mesh dưới root, tính trong hệ toạ độ của space. Không có mesh → Bounds rỗng tại gốc.
        public static Bounds BoundsIn(Transform space, GameObject root)
        {
            var b = new Bounds();
            var has = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                Bounds lb;
                Transform t;
                if (r is SkinnedMeshRenderer s) { lb = s.localBounds; t = s.rootBone != null ? s.rootBone : r.transform; }
                else if (r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null) { lb = f.sharedMesh.bounds; t = r.transform; }
                else continue;
                var m = space.worldToLocalMatrix * t.localToWorldMatrix;
                for (var i = 0; i < 8; i++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (has) b.Encapsulate(p); else { b = new Bounds(p, Vector3.zero); has = true; }
                }
            }
            return b;
        }

        /// Destroy khi đang chạy, DestroyImmediate trong Editor (test EditMode).
        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
