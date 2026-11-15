using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ObjectAR.UI
{
    /// Chân đế model (hệ dữ liệu: x → phải, z → cạnh dưới mã; đơn vị file) — đo một lần từ HouseCatalog.MakeNode.
    static class Footprint
    {
        static readonly Dictionary<string, (Vector2 min, Vector2 max, float h)> cache = new Dictionary<string, (Vector2, Vector2, float)>();

        public static (Vector2 min, Vector2 max, float h) Of(string modelID)
        {
            if (cache.TryGetValue(modelID, out var c)) return c;
            c = (new Vector2(-5, -5), new Vector2(5, 5), 5f);
            var go = HouseCatalog.MakeNode(modelID);
            if (go != null)
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var bb = rs[0].bounds;
                    foreach (var r in rs) bb.Encapsulate(r.bounds);
                    // Unity +z = cạnh trên mã → đảo z về hệ dữ liệu
                    c = (new Vector2(bb.min.x, -bb.max.z), new Vector2(bb.max.x, -bb.min.z), bb.size.y);
                }
                Object.DestroyImmediate(go);
            }
            return cache[modelID] = c;
        }
    }

    /// Sơ đồ bảng nhìn từ trên xuống (port BoardCanvas): gốc = tâm mã QR, x → phải, z dữ liệu → xuống màn hình.
    sealed class BoardMap : MonoBehaviour, IPointerClickHandler
    {
        static readonly Color MapBg = new Color(0.07f, 0.07f, 0.07f);
        static readonly Color[] Palette =
        {
            new Color(1f, 0.58f, 0f), new Color(1f, 0.18f, 0.33f), new Color(0.2f, 0.78f, 0.35f), new Color(0.69f, 0.32f, 0.87f),
            new Color(0.19f, 0.69f, 0.78f), new Color(1f, 0.23f, 0.19f), new Color(0f, 0.78f, 0.75f), new Color(0.35f, 0.34f, 0.84f),
        };

        sealed class Item
        {
            public RectTransform root, rot, border, arrow;
            public Image borderImg, fill;
            public Text arrowText, name, info;
        }

        MarkerManager m;
        RectTransform rt, grid, items, qrFrame;
        RawImage qr;
        Text qrTop, qrBottom, gridLabel;
        readonly Dictionary<string, Item> placed = new Dictionary<string, Item>();
        MarkerBoard board;
        float range = 0.2f;             // nửa cạnh vùng nhìn (mét)
        string fittedFor;
        Vector3 gridKey;
        bool dirty;
        // kéo: bản tạm của model đang thao tác, chỉ ghi vào dữ liệu khi thả tay
        string dragID;
        Vector2 dragStart, dragFrom, live;

        float Ppm => Mathf.Min(rt.rect.width, rt.rect.height) / (range * 2);   // đơn vị UI / mét
        Vector2 ToLocal(Vector2 xz) => new Vector2(xz.x, -xz.y) * Ppm;

        public static BoardMap Create(Transform parent, MarkerManager m)
        {
            var bg = UIKit.NewPanel(parent, MapBg, "BoardMap");
            bg.gameObject.AddComponent<RectMask2D>();
            var map = bg.gameObject.AddComponent<BoardMap>();
            map.m = m;
            map.rt = bg.rectTransform;
            map.grid = UIKit.NewRect(bg.transform, "Grid");

            map.qrFrame = UIKit.NewPanel(bg.transform, UIKit.Cyan, "QR", rounded: false).rectTransform;
            map.qrFrame.GetComponent<Image>().raycastTarget = false;
            map.qr = UIKit.Stretch(UIKit.NewRect(map.qrFrame, "Code"), 3, 3, 3, 3).gameObject.AddComponent<RawImage>();
            map.qr.raycastTarget = false;
            map.qrTop = UIKit.NewLabel(bg.transform, "", UIKit.Small, UIKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            map.qrBottom = UIKit.NewLabel(bg.transform, "↓ Cạnh dưới mã (phía người xem)", UIKit.Tiny, UIKit.Cyan * 0.85f, TextAnchor.MiddleCenter);
            map.qrTop.rectTransform.sizeDelta = map.qrBottom.rectTransform.sizeDelta = new Vector2(800, 50);

            map.items = UIKit.NewRect(bg.transform, "Items");
            map.items.sizeDelta = Vector2.zero;

            var tools = UIKit.NewColumn(bg.transform, 12, null, "Zoom");
            var tr = (RectTransform)tools.transform;
            tr.anchorMin = tr.anchorMax = tr.pivot = Vector2.one;
            tr.anchoredPosition = new Vector2(-16, -16);
            var fit = tools.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UIKit.NewButton(tools.transform, "+", UIKit.Chip, () => map.Zoom(1 / 1.4f), UIKit.Title);
            UIKit.NewButton(tools.transform, "−", UIKit.Chip, () => map.Zoom(1.4f), UIKit.Title);
            UIKit.NewButton(tools.transform, "Vừa khung", UIKit.Chip, map.Fit, UIKit.Small);

            map.gridLabel = UIKit.NewLabel(bg.transform, "", UIKit.Tiny, UIKit.Muted, TextAnchor.LowerRight);
            var gl = map.gridLabel.rectTransform;
            gl.anchorMin = Vector2.zero;
            gl.anchorMax = new Vector2(1, 0);
            gl.pivot = new Vector2(0.5f, 0);
            gl.offsetMin = new Vector2(16, 12);
            gl.offsetMax = new Vector2(-16, 60);
            return map;
        }

        public void Show(MarkerBoard b)
        {
            board = b;
            if (fittedFor != b.id)
            {
                fittedFor = b.id;
                Fit();
            }
            else Redraw();
        }

        void OnRectTransformDimensionsChange() => dirty = true;

        void LateUpdate()
        {
            if (!dirty) return;
            dirty = false;
            Redraw();
        }

        public void OnPointerClick(PointerEventData e) => m.Select(null);   // chạm nền = bỏ chọn

        void Zoom(float k)
        {
            range = Mathf.Clamp(range * k, 0.05f, 1.5f);
            Redraw();
        }

        /// Căn khung để thấy mã QR và mọi model.
        void Fit()
        {
            if (board == null) return;
            float r = board.widthCm / 100 * 0.8f;
            foreach (var p in board.placements)
            {
                var (mn, mx, _) = Footprint.Of(p.modelID);
                float ext = Mathf.Max(Mathf.Abs(mn.x), Mathf.Abs(mx.x), Mathf.Abs(mn.y), Mathf.Abs(mx.y)) * p.scale;
                r = Mathf.Max(r, Mathf.Abs(p.x) + ext, Mathf.Abs(p.z) + ext);
            }
            range = Mathf.Max(r * 1.25f, 0.06f);
            Redraw();
        }

        void Redraw()
        {
            var size = rt.rect.size;
            if (board == null || size.x < 1 || size.y < 1) return;
            float ppm = Ppm;
            DrawGrid(size, ppm);

            // mã QR đúng kích thước thật (ô module = widthCm, ảnh có thêm viền trắng 4 module mỗi bên)
            var mods = QRMaker.Modules(board.payload);
            int n = mods != null ? mods.GetLength(0) : 21;
            float total = board.widthCm / 100 * ppm * (n + 8) / n;
            qr.texture = UIKit.QR(board);
            qrFrame.sizeDelta = Vector2.one * (total + 6);
            qrTop.text = $"Mã QR {UIKit.F1(board.widthCm)} cm";
            qrTop.rectTransform.anchoredPosition = new Vector2(0, total / 2 + 36);
            qrBottom.rectTransform.anchoredPosition = new Vector2(0, -total / 2 - 36);

            for (int i = 0; i < board.placements.Count; i++)
            {
                var p = board.placements[i];
                if (!placed.TryGetValue(p.id, out var it)) placed[p.id] = it = NewItem(p.id);
                UpdateItem(it, p, Palette[i % Palette.Length], ppm);
            }
            foreach (var id in placed.Keys.Where(k => !board.placements.Exists(p => p.id == k)).ToList())
            {
                Destroy(placed[id].root.gameObject);
                placed.Remove(id);
            }
            if (m.SelectedPlacement != null && placed.TryGetValue(m.SelectedPlacement, out var sel)) sel.root.SetAsLastSibling();
        }

        void DrawGrid(Vector2 size, float ppm)
        {
            float step = 1;
            foreach (var s in new[] { 0.01f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f })
                if (s * ppm >= 80) { step = s; break; }
            gridLabel.text = $"Ô lưới: {(step < 1 ? Mathf.RoundToInt(step * 100) + " cm" : "1 m")} • Kéo khối để di chuyển";

            var key = new Vector3(size.x, size.y, ppm);
            if (key == gridKey) return;
            gridKey = key;
            UIKit.Clear(grid);
            float px = step * ppm;
            int nx = Mathf.CeilToInt(size.x / 2 / px), nz = Mathf.CeilToInt(size.y / 2 / px);
            for (int i = -nx; i <= nx; i++) Line(new Vector2(i * px, 0), new Vector2(i == 0 ? 2 : 1, size.y), i == 0);
            for (int j = -nz; j <= nz; j++) Line(new Vector2(0, j * px), new Vector2(size.x, j == 0 ? 2 : 1), j == 0);
        }

        void Line(Vector2 pos, Vector2 size, bool axis)
        {
            var img = UIKit.NewPanel(grid, axis ? new Color(0.2f, 0.85f, 1f, 0.25f) : new Color(1, 1, 1, 0.08f), "Line", rounded: false);
            img.raycastTarget = false;
            img.rectTransform.anchoredPosition = pos;
            img.rectTransform.sizeDelta = size;
        }

        Item NewItem(string id)
        {
            var it = new Item();
            var hit = UIKit.NewPanel(items, Color.clear, id, rounded: false);   // vùng chạm/kéo
            it.root = hit.rectTransform;
            var h = hit.gameObject.AddComponent<PlacementHandle>();
            h.map = this;
            h.id = id;

            it.rot = UIKit.NewRect(it.root, "Rotate");
            it.rot.sizeDelta = Vector2.zero;
            it.borderImg = UIKit.NewPanel(it.rot, Color.white, "Footprint");
            it.borderImg.raycastTarget = false;
            it.border = it.borderImg.rectTransform;
            it.fill = UIKit.NewPanel(it.border, Color.white, "Fill");
            it.fill.raycastTarget = false;
            // mũi tên chỉ mặt tiền (+z của model)
            it.arrowText = UIKit.NewLabel(it.rot, "▼", UIKit.Tiny, Color.white, TextAnchor.MiddleCenter);
            it.arrow = it.arrowText.rectTransform;
            it.arrow.sizeDelta = new Vector2(40, 30);

            var tag = UIKit.NewPanel(it.root, new Color(0, 0, 0, 0.65f), "Tag");
            tag.raycastTarget = false;
            var col = tag.gameObject.AddComponent<VerticalLayoutGroup>();
            UIKit.Setup(col, 2, new RectOffset(12, 12, 6, 6));
            col.childAlignment = TextAnchor.MiddleCenter;
            var fit = tag.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            it.name = UIKit.NewLabel(tag.transform, "", UIKit.Tiny, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            it.info = UIKit.NewLabel(tag.transform, "", 18, Color.white, TextAnchor.MiddleCenter);
            return it;
        }

        void UpdateItem(Item it, Placement p, Color color, float ppm)
        {
            var (mn, mx, h) = Footprint.Of(p.modelID);
            float s = p.scale;
            float w = Mathf.Max((mx.x - mn.x) * s * ppm, 6), d = Mathf.Max((mx.y - mn.y) * s * ppm, 6);
            // tâm chân đế lệch so với gốc model
            var off = new Vector2((mx.x + mn.x) / 2 * s * ppm, -(mx.y + mn.y) / 2 * s * ppm);
            bool sel = p.id == m.SelectedPlacement;
            var edge = sel ? Color.yellow : color;

            it.root.anchoredPosition = ToLocal(p.id == dragID ? live : new Vector2(p.x, p.z));
            it.root.sizeDelta = Vector2.one * Mathf.Max(w, d, 100) * 1.5f;
            it.rot.localEulerAngles = new Vector3(0, 0, p.yaw);
            it.border.anchoredPosition = off;
            it.border.sizeDelta = new Vector2(w, d);
            it.borderImg.color = edge;
            UIKit.Stretch(it.fill.rectTransform, sel ? 4 : 2, sel ? 4 : 2, sel ? 4 : 2, sel ? 4 : 2);
            it.fill.color = Color.Lerp(MapBg, color, sel ? 0.55f : 0.35f);
            it.arrow.anchoredPosition = new Vector2(off.x, off.y - d / 2 - 16);
            it.arrowText.color = edge;

            var model = HouseCatalog.Model(p.modelID);
            it.name.text = model != null ? UIKit.ModelLabel(model) : p.modelID;
            it.info.text = $"1:{Mathf.RoundToInt(1 / s)} • cao {Mathf.RoundToInt(h * s * 100)} cm";
        }

        // MARK: - Kéo thả (đo theo hệ toạ độ cố định của sơ đồ để không rung)

        internal void Tap(string id) => m.Select(id);

        internal void BeginDrag(string id, PointerEventData e)
        {
            var p = board?.placements.Find(q => q.id == id);
            if (p == null) return;
            dragID = id;
            dragStart = live = new Vector2(p.x, p.z);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.pressPosition, e.pressEventCamera, out dragFrom);
            if (m.SelectedPlacement != id) m.Select(id);
        }

        internal void Drag(PointerEventData e)
        {
            if (dragID == null || !placed.TryGetValue(dragID, out var it)) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var pt);
            var d = (pt - dragFrom) / Ppm;
            live = dragStart + new Vector2(d.x, -d.y);
            it.root.anchoredPosition = ToLocal(live);
        }

        /// Ghi kết quả vào dữ liệu (1 lần khi thả tay), làm tròn 1 mm.
        internal void EndDrag()
        {
            if (dragID == null) return;
            var l = live;
            dragID = null;
            m.ModifySelected(p =>
            {
                p.x = Mathf.Round(l.x * 1000) / 1000;
                p.z = Mathf.Round(l.y * 1000) / 1000;
            });
        }
    }

    /// Vùng chạm của một model trên sơ đồ: chạm = chọn, kéo = di chuyển.
    sealed class PlacementHandle : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public BoardMap map;
        public string id;

        public void OnPointerClick(PointerEventData e) => map.Tap(id);
        public void OnBeginDrag(PointerEventData e) => map.BeginDrag(id, e);
        public void OnDrag(PointerEventData e) => map.Drag(e);
        public void OnEndDrag(PointerEventData e) => map.EndDrag();
    }
}
