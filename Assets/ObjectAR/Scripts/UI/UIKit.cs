using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ObjectAR.UI
{
    /// Bộ dựng uGUI bằng code: theme tối, control cỡ chạm (≥ 88px ở chiều rộng tham chiếu 1080).
    public static class UIKit
    {
        public static readonly Color Brand = new Color(0.08f, 0.29f, 0.75f);
        public static readonly Color Bg = new Color(0.03f, 0.03f, 0.04f);
        public static readonly Color Card = new Color(0.12f, 0.12f, 0.13f, 0.97f);
        public static readonly Color Overlay = new Color(0.1f, 0.1f, 0.11f, 0.9f);
        public static readonly Color Glass = new Color(0f, 0f, 0f, 0.55f);
        public static readonly Color Chip = new Color(1f, 1f, 1f, 0.15f);
        public static readonly Color Field = new Color(1f, 1f, 1f, 0.1f);
        public static readonly Color Muted = new Color(1f, 1f, 1f, 0.6f);
        public static readonly Color Danger = new Color(0.78f, 0.2f, 0.2f);
        public static readonly Color Cyan = new Color(0.2f, 0.85f, 1f);

        public const float TouchH = 88f;
        public const int Tiny = 22, Small = 28, Body = 34, Title = 44;

        static Font font;
        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        static Sprite round;
        /// Sprite bo góc sinh bằng code, cắt 9 mảnh.
        public static Sprite Round => round != null ? round : (round = MakeRound(64, 22));

        static Sprite MakeRound(int size, int r)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // khoảng cách tới tâm cung góc gần nhất (giữa cạnh = 0)
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var c = new Vector2(Mathf.Clamp(p.x, r, size - r), Mathf.Clamp(p.y, r, size - r));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - Vector2.Distance(p, c) + 0.5f) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        static readonly Dictionary<string, Texture2D> qrCache = new Dictionary<string, Texture2D>();
        /// Ảnh QR (có viền trắng) để hiển thị — cache theo nội dung.
        public static Texture2D QR(MarkerBoard b)
        {
            if (qrCache.TryGetValue(b.payload, out var t) && t != null) return t;
            t = QRMaker.DisplayTexture(b, 8);
            if (t != null) t.filterMode = FilterMode.Point;
            return qrCache[b.payload] = t;
        }

        public static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        // Không ghép Icon: Icon là emoji, font LegacyRuntime (Arial) không vẽ được emoji trên Android.
        public static string ModelLabel(HouseModel h) => h.Name;

        // MARK: - Khung & bố cục

        public static RectTransform NewRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        /// Gán kích thước cho layout group cha (-1 = không đặt).
        public static LayoutElement Layout(Component c, float minW = -1, float minH = -1, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1)
        {
            if (!c.TryGetComponent(out LayoutElement le)) le = c.gameObject.AddComponent<LayoutElement>();
            if (minW >= 0) le.minWidth = minW;
            if (minH >= 0) le.minHeight = minH;
            if (prefW >= 0) le.preferredWidth = prefW;
            if (prefH >= 0) le.preferredHeight = prefH;
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static void Setup(HorizontalOrVerticalLayoutGroup g, float spacing, RectOffset pad = null)
        {
            g.spacing = spacing;
            g.padding = pad ?? new RectOffset();
            g.childControlWidth = g.childControlHeight = true;
            g.childForceExpandWidth = g.childForceExpandHeight = false;
        }

        public static HorizontalLayoutGroup NewRow(Transform parent, float spacing = 16, RectOffset pad = null, string name = "Row")
        {
            var g = NewRect(parent, name).gameObject.AddComponent<HorizontalLayoutGroup>();
            Setup(g, spacing, pad);
            g.childAlignment = TextAnchor.MiddleLeft;
            return g;
        }

        public static VerticalLayoutGroup NewColumn(Transform parent, float spacing = 16, RectOffset pad = null, string name = "Column")
        {
            var g = NewRect(parent, name).gameObject.AddComponent<VerticalLayoutGroup>();
            Setup(g, spacing, pad);
            g.childForceExpandWidth = true;
            return g;
        }

        /// Khoảng trống co giãn trong hàng (đẩy phần sau sang phải).
        public static void Spacer(Transform parent) => Layout(NewRect(parent, "Spacer"), flexW: 1);

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }
        }

        // MARK: - Control

        public static Image NewPanel(Transform parent, Color c, string name = "Panel", bool rounded = true)
        {
            var img = NewRect(parent, name).gameObject.AddComponent<Image>();
            img.color = c;
            if (rounded)
            {
                img.sprite = Round;
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        public static Text NewLabel(Transform parent, string s, int size = Body, Color? color = null,
                                    TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var t = NewRect(parent, "Text").gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = color ?? Color.white;
            t.supportRichText = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = s;
            return t;
        }

        public static RawImage NewRaw(Transform parent, float size)
        {
            var r = NewRect(parent, "Raw").gameObject.AddComponent<RawImage>();
            r.raycastTarget = false;
            Layout(r, minW: size, minH: size, prefW: size, prefH: size);
            return r;
        }

        /// Vùng bấm có nền (nội dung tự thêm).
        public static Button NewTappable(Transform parent, Color bg, UnityAction onClick, string name = "Button")
        {
            var img = NewPanel(parent, bg, name);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        /// Nút chữ, tự co theo nội dung, cao tối thiểu TouchH.
        public static Button NewButton(Transform parent, string s, Color bg, UnityAction onClick, int size = Body)
        {
            var b = NewTappable(parent, bg, onClick);
            var h = b.gameObject.AddComponent<HorizontalLayoutGroup>();
            Setup(h, 0, new RectOffset(28, 28, 8, 8));
            h.childAlignment = TextAnchor.MiddleCenter;
            NewLabel(b.transform, s, size, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Layout(b, minH: TouchH);
            return b;
        }

        public static Toggle NewToggle(Transform parent, string s, UnityAction<bool> onChange)
        {
            var row = NewRow(parent, 20, null, "Toggle");
            row.gameObject.AddComponent<Image>().color = Color.clear;   // cả hàng nhận chạm
            Layout(row, minH: TouchH);
            Layout(NewLabel(row.transform, s, Small), flexW: 1);
            var box = NewPanel(row.transform, Chip, "Box");
            Layout(box, minW: 64, minH: 64, prefW: 64, prefH: 64);
            var check = NewPanel(box.transform, Brand, "Check");
            check.raycastTarget = false;
            Stretch(check.rectTransform, 8, 8, 8, 8);
            var t = row.gameObject.AddComponent<Toggle>();
            t.targetGraphic = box;
            t.graphic = check;
            t.toggleTransition = Toggle.ToggleTransition.None;
            t.onValueChanged.AddListener(onChange);
            return t;
        }

        /// Hàng "tiêu đề — thanh trượt — giá trị". Thêm listener ở chỗ gọi.
        public static Slider NewSlider(Transform parent, string title, float min, float max, out Text value)
        {
            var row = NewRow(parent, 16, null, title);
            Layout(row, minH: TouchH);
            Layout(NewLabel(row.transform, title, Small), minW: 180, prefW: 180);

            var s = NewRect(row.transform, "Slider");
            s.gameObject.AddComponent<Image>().color = Color.clear;   // vùng chạm cao bằng cả hàng
            Layout(s, minH: TouchH, flexW: 1);
            var track = NewPanel(s, Chip, "Track");
            Bar(track.rectTransform);
            var fillArea = Bar(NewRect(s, "Fill Area"));
            var fill = NewPanel(fillArea, Brand, "Fill");
            fill.raycastTarget = false;
            Stretch(fill.rectTransform);
            // Slider kéo dãn tay cầm theo chiều cao Handle Area (anchor y 0→1) → cho vùng này cao 0, tay cầm = 56x56.
            var handleArea = Bar(NewRect(s, "Handle Area"));
            handleArea.offsetMin = new Vector2(28, 0);
            handleArea.offsetMax = new Vector2(-28, 0);
            var handle = NewPanel(handleArea, Color.white, "Handle");
            var hr = handle.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(0, 0.5f);
            hr.sizeDelta = new Vector2(56, 56);

            var sl = s.gameObject.AddComponent<Slider>();
            sl.fillRect = fill.rectTransform;
            sl.handleRect = hr;
            sl.targetGraphic = handle;
            sl.direction = Slider.Direction.LeftToRight;
            sl.minValue = min;
            sl.maxValue = max;

            value = NewLabel(row.transform, "", Small, Muted, TextAnchor.MiddleRight);
            Layout(value, minW: 130, prefW: 130);
            return sl;
        }

        // Dải ngang mảnh giữa thanh trượt, chừa 28px hai đầu cho tay cầm.
        static RectTransform Bar(RectTransform r)
        {
            r.anchorMin = new Vector2(0, 0.5f);
            r.anchorMax = new Vector2(1, 0.5f);
            r.offsetMin = new Vector2(28, -7);
            r.offsetMax = new Vector2(-28, 7);
            return r;
        }

        public static InputField NewInput(Transform parent, string placeholder, int size = Body)
        {
            var bg = NewPanel(parent, Field, "Input");
            Layout(bg, minH: TouchH);
            var text = NewLabel(bg.transform, "", size);
            Stretch(text.rectTransform, 20, 6, 20, 6);
            var ph = NewLabel(bg.transform, placeholder, size, Muted, style: FontStyle.Italic);
            Stretch(ph.rectTransform, 20, 6, 20, 6);
            var f = bg.gameObject.AddComponent<InputField>();
            f.targetGraphic = bg;
            f.textComponent = text;
            f.placeholder = ph;
            f.lineType = InputField.LineType.SingleLine;
            return f;
        }

        /// ScrollRect + Viewport (RectMask2D) + Content tự co theo layout.
        public static ScrollRect NewScroll(Transform parent, bool vertical, out RectTransform content, float spacing = 12)
        {
            var bg = NewPanel(parent, Color.clear, "Scroll", rounded: false);   // nhận kéo
            var vp = Stretch(NewRect(bg.transform, "Viewport"));
            vp.gameObject.AddComponent<RectMask2D>();
            content = NewRect(vp, "Content");
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            if (vertical)
            {
                content.anchorMin = new Vector2(0, 1);
                content.anchorMax = Vector2.one;
                content.pivot = new Vector2(0.5f, 1);
                Setup(content.gameObject.AddComponent<VerticalLayoutGroup>(), spacing);
                content.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
                fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            else
            {
                content.anchorMin = Vector2.zero;
                content.anchorMax = new Vector2(0, 1);
                content.pivot = new Vector2(0, 0.5f);
                var h = content.gameObject.AddComponent<HorizontalLayoutGroup>();
                Setup(h, spacing);
                h.childAlignment = TextAnchor.MiddleLeft;
                fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            content.sizeDelta = Vector2.zero;
            var sr = bg.gameObject.AddComponent<ScrollRect>();
            sr.viewport = vp;
            sr.content = content;
            sr.horizontal = !vertical;
            sr.vertical = vertical;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40;
            return sr;
        }
    }

    /// Theo dõi giữ/nhả control (thanh trượt cạnh mã chỉ ghi dữ liệu khi nhả tay).
    public sealed class PressWatch : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool Held { get; private set; }
        public Action Released;

        public void OnPointerDown(PointerEventData e) => Held = true;

        public void OnPointerUp(PointerEventData e)
        {
            Held = false;
            Released?.Invoke();
        }
    }
}
