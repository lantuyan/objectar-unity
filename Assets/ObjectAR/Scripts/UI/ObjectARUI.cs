using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ObjectAR.UI
{
    /// Giao diện chính (port MarkerView): dựng toàn bộ Canvas bằng code, vẽ lại khi MarkerManager.Changed.
    public class ObjectARUI : MonoBehaviour
    {
        MarkerManager m;
        RectTransform canvasRect;
        readonly List<RectTransform> safeRects = new List<RectTransform>();
        Rect lastSafe;
        Vector2Int lastScreen;

        GameObject arRoot, layoutRoot;
        Text hint, warning, simHint;
        MarkerEditorPanel arEditor;
        BoardListSheet sheet;
        BoardLayoutEditor layout;

        RectTransform toastRect;
        Text toastText;
        string seenToast;
        Coroutine toastCo;

        void Start()
        {
            m = MarkerManager.Instance != null ? MarkerManager.Instance : GetComponent<MarkerManager>();
            if (m == null)
            {
                Debug.LogError("ObjectARUI: không tìm thấy MarkerManager");
                enabled = false;
                return;
            }
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Build();
            m.Changed += Refresh;
            Refresh();
        }

        void OnDestroy()
        {
            if (m != null) m.Changed -= Refresh;
        }

        void Update()
        {
            if (Screen.safeArea != lastSafe || Screen.width != lastScreen.x || Screen.height != lastScreen.y) ApplySafeArea();
        }

        // MARK: - Dựng

        void Build()
        {
            var go = new GameObject("ObjectAR UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) { layer = 5 };
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)go.transform;

            // màn AR
            var ar = SafeRect(canvasRect, "AR");
            arRoot = ar.gameObject;
            BuildTopBar(ar);
            BuildHint(ar);
            arEditor = new MarkerEditorPanel(ar, m, false, ShowToast);
            var er = arEditor.Root;
            er.anchorMin = Vector2.zero;
            er.anchorMax = new Vector2(1, 0);
            er.pivot = new Vector2(0.5f, 0);
            er.anchoredPosition = new Vector2(0, 16);
            er.sizeDelta = new Vector2(-48, 0);
            er.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // editor sơ đồ toàn màn hình (nền phủ cả ngoài vùng an toàn)
            var lbg = UIKit.NewPanel(canvasRect, UIKit.Bg, "Layout", rounded: false);
            UIKit.Stretch(lbg.rectTransform);
            layoutRoot = lbg.gameObject;
            layout = new BoardLayoutEditor(SafeRect(lbg.rectTransform, "Safe"), m, ShowToast);

            // sheet danh sách bảng
            var sroot = UIKit.Stretch(UIKit.NewRect(canvasRect, "Boards"));
            sheet = new BoardListSheet(sroot, SafeRect(sroot, "Safe"), m, ShowToast);

            BuildToast();
            ApplySafeArea();
        }

        RectTransform SafeRect(RectTransform parent, string name)
        {
            var r = UIKit.NewRect(parent, name);
            safeRects.Add(r);
            return r;
        }

        void ApplySafeArea()
        {
            lastSafe = Screen.safeArea;
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            if (lastScreen.x <= 0 || lastScreen.y <= 0) return;
            var size = new Vector2(lastScreen.x, lastScreen.y);
            var min = lastSafe.position / size;
            var max = (lastSafe.position + lastSafe.size) / size;
            foreach (var r in safeRects)
            {
                r.anchorMin = min;
                r.anchorMax = max;
                r.offsetMin = r.offsetMax = Vector2.zero;
            }
        }

        void BuildTopBar(RectTransform parent)
        {
            var bar = UIKit.NewPanel(parent, UIKit.Glass, "TopBar");
            var r = bar.rectTransform;
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(0, -16);
            r.sizeDelta = new Vector2(-48, 128);
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            UIKit.Setup(h, 16, new RectOffset(28, 20, 16, 16));
            h.childAlignment = TextAnchor.MiddleLeft;

            var title = UIKit.NewColumn(bar.transform, 6);
            title.childForceExpandWidth = false;
            title.childAlignment = TextAnchor.MiddleLeft;
            UIKit.Layout(title, flexW: 1);
            UIKit.NewLabel(title.transform, "QR Marker", UIKit.Title, style: FontStyle.Bold);
            UIKit.Layout(UIKit.NewPanel(title.transform, UIKit.Brand, "Underline", rounded: false), prefW: 140, prefH: 6);

            UIKit.NewButton(bar.transform, "Quét lại", UIKit.Chip, m.Restart);
            UIKit.NewButton(bar.transform, "Bảng marker", UIKit.Brand, () => sheet.Open());
        }

        void BuildHint(RectTransform parent)
        {
            var bg = UIKit.NewPanel(parent, UIKit.Glass, "Hint");
            bg.raycastTarget = false;   // không chặn cử chỉ AR
            var r = bg.rectTransform;
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(0, -160);
            r.sizeDelta = new Vector2(-48, 0);
            var col = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            UIKit.Setup(col, 8, new RectOffset(28, 28, 16, 16));
            col.childForceExpandWidth = true;
            bg.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            hint = UIKit.NewLabel(bg.transform, "", UIKit.Small, Color.white, TextAnchor.MiddleCenter);
            warning = UIKit.NewLabel(bg.transform, "", UIKit.Small, Color.yellow, TextAnchor.MiddleCenter, FontStyle.Bold);
            simHint = UIKit.NewLabel(bg.transform, "Giả lập (Editor): phím 1–9 để quét bảng thứ n", UIKit.Tiny, UIKit.Muted, TextAnchor.MiddleCenter);
        }

        void BuildToast()
        {
            var bg = UIKit.NewPanel(canvasRect, new Color(0, 0, 0, 0.75f), "Toast");
            bg.raycastTarget = false;
            toastRect = bg.rectTransform;
            var col = bg.gameObject.AddComponent<VerticalLayoutGroup>();
            UIKit.Setup(col, 0, new RectOffset(36, 36, 20, 20));
            col.childForceExpandWidth = true;
            bg.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            toastText = UIKit.NewLabel(bg.transform, "", UIKit.Small, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            bg.gameObject.SetActive(false);
        }

        // MARK: - Cập nhật

        void Refresh()
        {
            var e = m.EditingBoard;
            if (m.ShowLayout && e == null) m.ShowLayout = false;
            bool showLayout = m.ShowLayout;
            arRoot.SetActive(!showLayout);
            layoutRoot.SetActive(showLayout);

            if (showLayout) layout.Refresh();
            else
            {
                if (e != null)
                    hint.text = m.Visible.ContainsKey(e.id)
                        ? "Chạm chọn model • kéo 1 ngón để di chuyển • chụm để đổi kích thước • xoay 2 ngón để xoay"
                        : "Đang chỉnh bảng — hướng camera vào mã QR này để xem trực tiếp";
                else
                    hint.text = m.ScanLocked || m.Boards.Exists(b => m.Visible.ContainsKey(b.id))
                        ? "Đã nhận mã QR • bấm Quét lại để quét mã khác"
                        : "Hướng camera vào mã QR";
                warning.gameObject.SetActive(m.TrackingWarning != null);
                warning.text = "! " + m.TrackingWarning;   // Arial không có ⚠
                simHint.gameObject.SetActive(m.IsSimulated && !m.ScanLocked && e == null);
                arEditor.Root.gameObject.SetActive(e != null);
                if (e != null) arEditor.Refresh();
            }
            if (sheet.IsOpen) sheet.Refresh();

            if (m.Toast != seenToast)
            {
                seenToast = m.Toast;
                if (seenToast != null) ShowToast(seenToast);
            }
        }

        /// Toast giữa màn hình, tự ẩn sau 1.8s (toast của manager thì xoá qua ClearToast).
        void ShowToast(string text)
        {
            toastText.text = text;
            toastRect.gameObject.SetActive(true);
            toastRect.SetAsLastSibling();
            toastRect.sizeDelta = new Vector2(Mathf.Min(toastText.preferredWidth + 72, canvasRect.rect.width - 96), toastRect.sizeDelta.y);
            if (toastCo != null) StopCoroutine(toastCo);
            toastCo = StartCoroutine(HideToast(text));
        }

        IEnumerator HideToast(string text)
        {
            yield return new WaitForSecondsRealtime(1.8f);
            toastRect.gameObject.SetActive(false);
            toastCo = null;
            m.ClearToast(text);
        }
    }
}
