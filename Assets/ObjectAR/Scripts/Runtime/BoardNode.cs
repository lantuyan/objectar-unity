using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ObjectAR
{
    /// Node hiển thị 1 bảng marker, con của holder (tư thế mã QR). Đổi hệ dữ liệu ARKit → Unity:
    /// pos (x, y, -z), xoay Euler(0, -yaw, 0) (ảnh AR Foundation có +z hướng cạnh trên mã).
    public sealed class BoardNode : MonoBehaviour
    {
        public string BoardID { get; private set; }

        sealed class Item { public Transform Node; public string ModelID; }
        readonly Dictionary<string, Item> items = new();   // placement id → node

        public static BoardNode Create(MarkerBoard board, Transform parent, string selected)
        {
            var go = new GameObject("board");
            go.transform.SetParent(parent, false);
            var bn = go.AddComponent<BoardNode>();
            bn.Sync(board, false, selected);
            return bn;
        }

        /// Đồng bộ danh sách model + vị trí theo dữ liệu bảng.
        public void Sync(MarkerBoard board, bool animated = true, string selected = null)
        {
            BoardID = board.id;
            var ids = new HashSet<string>(board.placements.Select(p => p.id));
            foreach (var id in items.Keys.Where(k => !ids.Contains(k)).ToList())
            {
                NodeUtil.Kill(items[id].Node.gameObject);
                items.Remove(id);
            }
            foreach (var p in board.placements)
            {
                if (!items.TryGetValue(p.id, out var it) || it.ModelID != p.modelID)
                {
                    if (it != null) { NodeUtil.Kill(it.Node.gameObject); items.Remove(p.id); }
                    var model = HouseCatalog.MakeNode(p.modelID);
                    if (model == null) continue;
                    var node = NodeUtil.Child(transform, "pl-" + p.id);
                    model.transform.SetParent(node, false);
                    items[p.id] = it = new Item { Node = node, ModelID = p.modelID };
                    if (animated && Application.isPlaying) StartCoroutine(ScaleIn(model.transform, 0.3f));
                }
                var t = it.Node;
                t.localPosition = new Vector3(p.x, p.y, -p.z);
                t.localRotation = Quaternion.Euler(0, -p.yaw, 0);
                t.localScale = Vector3.one * p.scale;
                SetSelected(t, p.id == selected);
            }
        }

        static void SetSelected(Transform node, bool on)
        {
            var sel = node.Find("sel");
            if (!on)
            {
                if (sel != null) { sel.SetParent(null, false); NodeUtil.Kill(sel.gameObject); }   // tách ra vì Destroy trễ 1 frame
                return;
            }
            if (sel != null) return;
            // Tính trong hệ của chính model (model đặt tại gốc node) để không lệ thuộc animation scale.
            var model = node.GetChild(0);
            var b = NodeUtil.BoundsIn(model, model.gameObject);
            SelectionBox.Create(node, new Vector3(b.center.x, b.min.y, b.center.z), b.size * 1.05f);
        }

        /// Hiện bảng: phóng từ 0.01 → 1 (thay cho fade của SceneKit; vật liệu glTF là opaque).
        public void Appear() => StartCoroutine(ScaleIn(transform, 0.4f));

        static IEnumerator ScaleIn(Transform t, float dur)
        {
            for (float e = 0; e < dur && t != null; e += Time.deltaTime)
            {
                var k = 1 - Mathf.Pow(1 - e / dur, 3);   // ease-out
                t.localScale = Vector3.one * Mathf.Lerp(0.01f, 1f, k);
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        /// Placement gần nhất mà tia chạm (theo hộp bao renderer). null nếu không trúng.
        public string HitTest(Ray ray, out float distance)
        {
            string hit = null;
            distance = float.MaxValue;
            foreach (var kv in items)
                foreach (var r in kv.Value.Node.GetComponentsInChildren<Renderer>())
                    if (r.bounds.IntersectRay(ray, out var d) && d < distance) { distance = d; hit = kv.Key; }
            return hit;
        }

        /// Giao điểm tia với mặt phẳng mã QR, trả toạ độ local Unity của bảng.
        public bool PointOnBoard(Ray ray, out Vector3 local)
        {
            var a = transform.InverseTransformPoint(ray.origin);
            var d = transform.InverseTransformPoint(ray.origin + ray.direction) - a;
            local = default;
            if (Mathf.Abs(d.y) < 1e-6f) return false;
            var t = -a.y / d.y;
            if (t <= 0) return false;
            local = a + d * t;
            return true;
        }
    }
}
