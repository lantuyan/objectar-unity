using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ObjectAR.UI
{
    /// Trình chỉnh bảng marker (port MarkerEditor): tab Model / Mã QR. Dùng ở màn AR và trong editor sơ đồ.
    sealed class MarkerEditorPanel
    {
        public readonly RectTransform Root;
        readonly MarkerManager m;
        readonly Action<string> toast;
        readonly InputField nameField;
        readonly Image[] tabs = new Image[2];
        readonly RectTransform body;
        /// Cập nhật giá trị tại chỗ (không dựng lại) mỗi lần Changed.
        readonly List<Action<MarkerBoard>> updaters = new List<Action<MarkerBoard>>();
        int tab;
        bool picking;
        string shape;   // cấu trúc phần thân đang dựng; đổi thì dựng lại

        public MarkerEditorPanel(Transform parent, MarkerManager m, bool inLayout, Action<string> toast)
        {
            this.m = m;
            this.toast = toast;
            var bg = UIKit.NewPanel(parent, inLayout ? UIKit.Card : UIKit.Overlay, "MarkerEditor");
            Root = bg.rectTransform;
            var col = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            UIKit.Setup(col, 16, new RectOffset(28, 28, 24, 28));
            col.childForceExpandWidth = true;

            var head = UIKit.NewRow(Root, 16);
            nameField = UIKit.NewInput(head.transform, "Tên bảng", UIKit.Body);
            nameField.textComponent.fontStyle = FontStyle.Bold;
            UIKit.Layout(nameField, flexW: 1);
            nameField.onEndEdit.AddListener(Rename);
            if (!inLayout)
            {
                UIKit.NewButton(head.transform, "Sơ đồ", UIKit.Chip, () => m.ShowLayout = true);
                UIKit.NewButton(head.transform, "Xong", UIKit.Brand, m.StopEditing);
            }

            var seg = UIKit.NewRow(Root, 12);
            for (int i = 0; i < 2; i++)
            {
                int k = i;
                var b = UIKit.NewButton(seg.transform, k == 0 ? "Model" : "Mã QR", UIKit.Chip, () => { tab = k; Refresh(); });
                UIKit.Layout(b, flexW: 1);
                tabs[k] = (Image)b.targetGraphic;
            }

            body = (RectTransform)UIKit.NewColumn(Root, 12, null, "Body").transform;
        }

        public void Refresh()
        {
            var b = m.EditingBoard;
            if (b == null) return;
            if (!nameField.isFocused) nameField.SetTextWithoutNotify(b.name);
            for (int i = 0; i < 2; i++) tabs[i].color = i == tab ? UIKit.Brand : UIKit.Chip;
            var s = tab == 0
                ? $"{b.id}|M|{picking}|{m.SelectedPlacement}|{string.Join(",", b.placements.Select(p => p.id))}"
                : $"{b.id}|Q";
            if (s != shape)
            {
                shape = s;
                UIKit.Clear(body);
                updaters.Clear();
                if (tab == 0) BuildModelTab(b); else BuildQRTab();
            }
            foreach (var u in updaters) u(b);
        }

        void Rename(string v)
        {
            var b = m.EditingBoard;
            if (b == null || v == b.name) return;
            var nb = b.Clone();
            nb.name = v;
            m.UpdateBoard(nb);
        }

        // Tab model: chip các model trên bảng + thông số model đang chọn.
        void BuildModelTab(MarkerBoard b)
        {
            UIKit.Layout(UIKit.NewScroll(body, false, out var chips), minH: UIKit.TouchH);
            foreach (var p in b.placements)
            {
                var model = HouseCatalog.Model(p.modelID);
                bool sel = p.id == m.SelectedPlacement;
                string pid = p.id;
                var chip = UIKit.NewButton(chips, model != null ? UIKit.ModelLabel(model) : p.modelID,
                                           sel ? Color.yellow : UIKit.Chip, () => m.Select(pid), UIKit.Small);
                if (sel) chip.GetComponentInChildren<Text>().color = Color.black;
            }
            UIKit.NewButton(chips, picking ? "Đóng" : "+ Thêm model", UIKit.Brand, () => { picking = !picking; Refresh(); }, UIKit.Small);

            if (picking)
            {
                UIKit.NewLabel(body, "Chọn model để thêm vào bảng:", UIKit.Small, UIKit.Muted);
                UIKit.Layout(UIKit.NewScroll(body, false, out var cat), minH: UIKit.TouchH);
                foreach (var h in HouseCatalog.All)
                {
                    var hm = h;
                    UIKit.NewButton(cat, UIKit.ModelLabel(h), UIKit.Chip, () => { picking = false; m.AddPlacement(hm); }, UIKit.Small);
                }
            }

            if (b.placements.Find(p => p.id == m.SelectedPlacement) == null)
            {
                UIKit.NewLabel(body, b.placements.Count == 0
                    ? "Bảng chưa có model. Bấm \"Thêm model\" để chọn từ danh sách."
                    : "Chạm vào một model để chỉnh vị trí.", UIKit.Small, UIKit.Muted);
                return;
            }
            Axis("Ngang (X)", -0.5f, 0.5f, p => p.x, (p, v) => p.x = v, v => $"{Mathf.RoundToInt(v * 100)} cm");
            Axis("Dọc (Z)", -0.5f, 0.5f, p => p.z, (p, v) => p.z = v, v => $"{Mathf.RoundToInt(v * 100)} cm");
            Axis("Nâng (Y)", -0.1f, 0.3f, p => p.y, (p, v) => p.y = v, v => $"{Mathf.RoundToInt(v * 100)} cm");
            Axis("Xoay", 0, 360, p => p.yaw, (p, v) => p.yaw = Mathf.Round(v / 5) * 5, v => $"{Mathf.RoundToInt(v)}°");
            // thanh trượt theo log để dễ chỉnh trong khoảng 1:20 … 1:1000
            Axis("Tỉ lệ", -3, 0, p => Mathf.Log10(p.scale), (p, v) => p.scale = Mathf.Pow(10, v),
                 v => $"1:{Mathf.RoundToInt(1 / Mathf.Pow(10, v))}");
            var del = UIKit.NewRow(body);
            UIKit.Spacer(del.transform);
            UIKit.NewButton(del.transform, "Xoá model này", UIKit.Danger, m.RemoveSelected, UIKit.Small);
        }

        void Axis(string title, float min, float max, Func<Placement, float> get, Action<Placement, float> set, Func<float, string> fmt)
        {
            var s = UIKit.NewSlider(body, title, min, max, out var value);
            s.onValueChanged.AddListener(v => m.ModifySelected(p => set(p, v)));
            updaters.Add(b =>
            {
                var p = b.placements.Find(q => q.id == m.SelectedPlacement);
                if (p == null) return;
                float v = get(p);
                s.SetValueWithoutNotify(v);
                value.text = fmt(v);
            });
        }

        // Tab mã QR: kích thước thật + nội dung + xuất ảnh.
        void BuildQRTab()
        {
            var top = UIKit.NewRow(body, 24);
            top.childAlignment = TextAnchor.UpperLeft;
            var qr = UIKit.NewRaw(top.transform, 220);
            var info = UIKit.NewColumn(top.transform, 8);
            UIKit.Layout(info, flexW: 1);
            var est = UIKit.NewLabel(info.transform, "", UIKit.Small);
            var use = UIKit.NewButton(info.transform, "Dùng số này", UIKit.Brand, () => m.ApplyEstimate(m.EditingID), UIKit.Small);
            var auto = UIKit.NewToggle(info.transform, "Tự ước lượng kích thước mã", v => m.AutoScale = v);

            // cạnh mã: kéo chỉ đổi số hiển thị, ghi (chạy lại session) khi nhả tay
            var width = UIKit.NewSlider(body, "Cạnh mã QR", 1, 30, out var widthValue);
            width.onValueChanged.AddListener(v => widthValue.text = UIKit.F1(Round1(v)) + " cm");
            var hold = width.gameObject.AddComponent<PressWatch>();
            hold.Released = () =>
            {
                var cur = m.EditingBoard;
                float w = Round1(width.value);
                if (cur == null || Mathf.Approximately(w, cur.widthCm)) return;
                var nb = cur.Clone();
                nb.widthCm = w;
                m.UpdateBoard(nb, rerun: true);
            };

            var prow = UIKit.NewRow(body, 12);
            var payload = UIKit.NewInput(prow.transform, "Nội dung QR", UIKit.Small);
            UIKit.Layout(payload, flexW: 1);
            var apply = UIKit.NewButton(prow.transform, "Áp dụng", UIKit.Brand, () =>
            {
                var cur = m.EditingBoard;
                var t = payload.text.Trim();
                if (cur == null || t.Length == 0 || t == cur.payload) return;
                var nb = cur.Clone();
                nb.payload = t;
                m.UpdateBoard(nb, rerun: true);
            }, UIKit.Small);
            payload.onValueChanged.AddListener(t =>
            {
                var cur = m.EditingBoard;
                apply.interactable = cur != null && t.Trim().Length > 0 && t.Trim() != cur.payload;
            });

            var xrow = UIKit.NewRow(body);
            UIKit.Spacer(xrow.transform);
            UIKit.NewButton(xrow.transform, "Xuất ảnh PNG", UIKit.Chip, () =>
            {
                var cur = m.EditingBoard;
                if (cur != null) toast("Đã lưu " + QRMaker.SavePng(cur));
            }, UIKit.Small);

            string draftFor = null;   // chỉ nạp lại nội dung QR khi đổi bảng / payload đã lưu đổi
            updaters.Add(b =>
            {
                qr.texture = UIKit.QR(b);
                bool hasEst = m.EstimatedCm.TryGetValue(b.id, out var cm) && m.AutoScale;
                est.text = hasEst ? $"AR đo được: {UIKit.F1(cm)} cm" : "Đo cạnh ô QR đen (không tính viền trắng) bằng thước.";
                est.color = hasEst ? Color.white : UIKit.Muted;
                use.gameObject.SetActive(hasEst);
                auto.SetIsOnWithoutNotify(m.AutoScale);
                if (!hold.Held)
                {
                    width.SetValueWithoutNotify(b.widthCm);
                    widthValue.text = UIKit.F1(b.widthCm) + " cm";
                }
                if (draftFor != b.id + "|" + b.payload)
                {
                    draftFor = b.id + "|" + b.payload;
                    payload.SetTextWithoutNotify(b.payload);
                }
                apply.interactable = payload.text.Trim().Length > 0 && payload.text.Trim() != b.payload;
            });
        }

        static float Round1(float v) => Mathf.Round(v * 10) / 10;
    }
}
