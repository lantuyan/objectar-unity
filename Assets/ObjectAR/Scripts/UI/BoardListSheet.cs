using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ObjectAR.UI
{
    /// Danh sách bảng marker (port BoardList): sheet nửa dưới màn hình.
    sealed class BoardListSheet
    {
        readonly MarkerManager m;
        readonly Action<string> toast;
        readonly GameObject root;
        readonly RectTransform list;
        readonly GameObject actions, confirm;
        string shape;

        public bool IsOpen => root.activeSelf;

        /// root: phủ toàn canvas; safe: con của root, khớp vùng an toàn.
        public BoardListSheet(RectTransform root, RectTransform safe, MarkerManager m, Action<string> toast)
        {
            this.m = m;
            this.toast = toast;
            this.root = root.gameObject;

            var dim = UIKit.NewTappable(root, new Color(0, 0, 0, 0.5f), Close, "Dim");
            ((Image)dim.targetGraphic).sprite = null;
            dim.transition = Selectable.Transition.None;
            UIKit.Stretch((RectTransform)dim.transform);
            dim.transform.SetAsFirstSibling();

            var sheet = UIKit.NewPanel(safe, UIKit.Card, "Sheet");
            var sr = sheet.rectTransform;
            sr.anchorMin = Vector2.zero;
            sr.anchorMax = new Vector2(1, 0.8f);
            sr.offsetMin = sr.offsetMax = Vector2.zero;
            var col = sheet.gameObject.AddComponent<VerticalLayoutGroup>();
            UIKit.Setup(col, 16, new RectOffset(32, 32, 24, 24));
            col.childForceExpandWidth = true;

            var head = UIKit.NewRow(sheet.transform);
            UIKit.Layout(UIKit.NewLabel(head.transform, "Bảng marker", UIKit.Title, style: FontStyle.Bold), flexW: 1);
            UIKit.NewButton(head.transform, "Đóng", UIKit.Chip, Close);

            var scroll = UIKit.NewScroll(sheet.transform, true, out var content, 16);
            UIKit.Layout(scroll, flexH: 1);
            list = (RectTransform)UIKit.NewColumn(content, 12, null, "Boards").transform;
            UIKit.NewLabel(content, "Chạm vào bảng để chỉnh vị trí model, kích thước mã và nội dung QR. " +
                                    "Nút PNG xuất ảnh QR để in hoặc mở trên điện thoại.", UIKit.Small, UIKit.Muted);

            actions = UIKit.NewColumn(content, 12, null, "Actions").gameObject;
            UIKit.NewButton(actions.transform, "+ Tạo bảng marker mới", UIKit.Brand, () =>
            {
                var b = m.AddBoard();
                Edit(b.id);
            });
            UIKit.NewButton(actions.transform, "Khôi phục bảng mặc định", UIKit.Chip, () => SetConfirm(true));

            confirm = UIKit.NewColumn(content, 12, null, "Confirm").gameObject;
            UIKit.NewLabel(confirm.transform, "Xoá mọi chỉnh sửa và quay về 4 bảng mặc định?", UIKit.Body, align: TextAnchor.MiddleCenter);
            var crow = UIKit.NewRow(confirm.transform, 16);
            UIKit.Layout(UIKit.NewButton(crow.transform, "Huỷ", UIKit.Chip, () => SetConfirm(false)), flexW: 1);
            UIKit.Layout(UIKit.NewButton(crow.transform, "Khôi phục", UIKit.Danger, () =>
            {
                SetConfirm(false);
                m.RestoreDefaults();
            }), flexW: 1);

            this.root.SetActive(false);
        }

        public void Open()
        {
            root.SetActive(true);
            SetConfirm(false);
            shape = null;
            Refresh();
        }

        public void Close() => root.SetActive(false);

        void SetConfirm(bool on)
        {
            actions.SetActive(!on);
            confirm.SetActive(on);
        }

        void Edit(string id)
        {
            m.StartEditing(id);
            Close();
            m.ShowLayout = true;
        }

        /// Chỉ dựng lại danh sách khi dữ liệu hiển thị đổi.
        public void Refresh()
        {
            var sb = new StringBuilder();
            foreach (var b in m.Boards)
                sb.Append(b.id).Append('|').Append(b.name).Append('|').Append(b.payload).Append('|')
                  .Append(b.widthCm).Append('|').Append(b.placements.Count).Append('|').Append(Tracking(b)).Append(';');
            var s = sb.ToString();
            if (s == shape) return;
            shape = s;
            UIKit.Clear(list);
            foreach (var b in m.Boards) Row(b);
        }

        bool Tracking(MarkerBoard b) => m.Visible.TryGetValue(b.id, out var v) && v;

        void Row(MarkerBoard b)
        {
            string id = b.id;
            var row = UIKit.NewTappable(list, UIKit.Field, () => Edit(id), b.id);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            UIKit.Setup(h, 20, new RectOffset(16, 16, 16, 16));
            h.childAlignment = TextAnchor.MiddleLeft;

            UIKit.NewRaw(row.transform, 140).texture = UIKit.QR(b);
            var text = UIKit.NewColumn(row.transform, 4);
            UIKit.Layout(text, flexW: 1);
            var title = UIKit.NewRow(text.transform, 12);
            UIKit.NewLabel(title.transform, b.name, UIKit.Body, style: FontStyle.Bold);
            if (Tracking(b)) UIKit.NewLabel(title.transform, "●", UIKit.Small, Color.green);
            UIKit.NewLabel(text.transform, $"{b.id} • {b.placements.Count} model • mã {UIKit.F1(b.widthCm)} cm", UIKit.Small, UIKit.Muted);
            UIKit.NewLabel(text.transform, b.payload, UIKit.Tiny, UIKit.Muted);

            UIKit.NewButton(row.transform, "PNG", UIKit.Chip, () =>
            {
                var cur = m.Boards.Find(x => x.id == id);
                if (cur != null) toast("Đã lưu " + QRMaker.SavePng(cur));
            }, UIKit.Small);
            UIKit.NewButton(row.transform, "Xoá", UIKit.Danger, () => m.DeleteBoard(id), UIKit.Small);
        }
    }
}
