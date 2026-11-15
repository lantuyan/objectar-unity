using System;
using UnityEngine;
using UnityEngine.UI;

namespace ObjectAR.UI
{
    /// Màn hình chỉnh bảng marker dạng sơ đồ, toàn màn hình (không cần camera). Bỏ phần xem 3D của bản Swift.
    sealed class BoardLayoutEditor
    {
        readonly MarkerManager m;
        readonly Text title;
        readonly BoardMap map;
        readonly MarkerEditorPanel editor;

        /// safe: khung vùng an toàn (đã có nền tối phía sau).
        public BoardLayoutEditor(RectTransform safe, MarkerManager m, Action<string> toast)
        {
            this.m = m;
            var col = safe.gameObject.AddComponent<VerticalLayoutGroup>();
            UIKit.Setup(col, 20, new RectOffset(24, 24, 16, 24));
            col.childForceExpandWidth = true;

            var head = UIKit.NewRow(safe, 16);
            var names = UIKit.NewColumn(head.transform, 2);
            UIKit.Layout(names, flexW: 1);
            UIKit.NewLabel(names.transform, "Chỉnh bảng marker", UIKit.Small, UIKit.Muted);
            title = UIKit.NewLabel(names.transform, "", UIKit.Title, style: FontStyle.Bold);
            UIKit.NewButton(head.transform, "Xem trong AR", UIKit.Chip, () => m.ShowLayout = false);
            UIKit.NewButton(head.transform, "Xong", UIKit.Brand, m.StopEditing);

            map = BoardMap.Create(safe, m);
            UIKit.Layout(map, minH: 400, flexH: 1);

            var scroll = UIKit.NewScroll(safe, true, out var content);
            UIKit.Layout(scroll, minH: 300, prefH: 780);
            editor = new MarkerEditorPanel(content, m, true, toast);
        }

        public void Refresh()
        {
            var b = m.EditingBoard;
            if (b == null) return;
            title.text = b.name;
            map.Show(b);
            editor.Refresh();
        }
    }
}
