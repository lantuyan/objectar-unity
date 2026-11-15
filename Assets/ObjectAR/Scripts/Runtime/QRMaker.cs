using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace ObjectAR
{
    /// Sinh mã QR bằng ZXing, khớp từng ô với CIQRCodeGenerator của bản iOS (ARKit/ARCore dò theo hình ảnh).
    public static class QRMaker
    {
        static readonly Dictionary<string, bool[,]> cache = new();

        /// Mask CoreImage đã chọn cho các mã in sẵn trong QR-markers/. ZXing chấm điểm mask khác CoreImage
        /// (vd M5: ZXing chọn 7, CoreImage chọn 0) nên ghim lại để ảnh tham chiếu trùng ảnh đã in.
        static readonly Dictionary<string, int> printedMasks = new()
        {
            ["OBJECTAR:M1:khu-dan-cu"] = 1,
            ["OBJECTAR:M2:biet-thu"] = 1,
            ["OBJECTAR:M3:chung-cu"] = 7,
            ["OBJECTAR:M4:bang-trong"] = 2,
            ["OBJECTAR:M5:acer-nitro5"] = 0,
            ["OBJECTAR:M6:low-poly-building"] = 2,
        };

        /// Ma trận QR thuần, không viền: m[x, y], y = 0 là hàng trên cùng, true = ô đen. null nếu không mã hoá được.
        public static bool[,] Modules(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;
            if (cache.TryGetValue(payload, out var m)) return m;
            m = Encode(payload, printedMasks.TryGetValue(payload, out var k) ? k : null);
            if (m != null) cache[payload] = m;
            return m;
        }

        /// Mã hoá như CoreImage: ECL M, chia đoạn tối ưu (QR_COMPACT: alphanumeric + byte), byte UTF-8 không ECI.
        /// mask = null → để ZXing tự chọn.
        public static bool[,] Encode(string payload, int? mask = null)
        {
            var hints = new Dictionary<EncodeHintType, object>
            {
                [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M,
                [EncodeHintType.MARGIN] = 0,
                [EncodeHintType.QR_COMPACT] = true,
            };
            if (mask is int k) hints[EncodeHintType.QR_MASK_PATTERN] = k;
            // Mỗi byte UTF-8 thành 1 ký tự ISO-8859-1 → ZXing ghi đúng các byte UTF-8, không chèn ECI (như Data(payload.utf8)).
            var latin1 = new string(Encoding.UTF8.GetBytes(payload).Select(b => (char)b).ToArray());
            try
            {
                var bm = new QRCodeWriter().encode(latin1, BarcodeFormat.QR_CODE, 0, 0, hints);
                var m = new bool[bm.Width, bm.Height];
                for (var y = 0; y < bm.Height; y++)
                    for (var x = 0; x < bm.Width; x++)
                        m[x, y] = bm[x, y];
                return m;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ObjectAR] Không tạo được mã QR \"{payload}\": {e.Message}");
                return null;
            }
        }

        /// Ảnh tham chiếu cho ARTrackedImageManager: chỉ phần ô QR, phóng to sắc nét (RGBA32, readable).
        public static Texture2D ReferenceTexture(string payload, int px = 16) => Render(Modules(payload), px, 0, payload);

        /// Ảnh để hiển thị/in: QR + viền trắng 4 module.
        public static Texture2D DisplayTexture(MarkerBoard b, int px = 24) => Render(Modules(b.payload), px, 4, b.name);

        /// Ghi persistentDataPath/QR-<id>.png, trả đường dẫn (null nếu lỗi).
        public static string SavePng(MarkerBoard b)
        {
            var t = DisplayTexture(b);
            if (t == null) return null;
            try
            {
                var path = Path.Combine(Application.persistentDataPath, $"QR-{b.id}.png");
                File.WriteAllBytes(path, t.EncodeToPNG());
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ObjectAR] Không ghi được PNG: {e.Message}");
                return null;
            }
            finally { NodeUtil.Kill(t); }
        }

        static Texture2D Render(bool[,] m, int px, int margin, string name)
        {
            if (m == null) return null;
            int n = m.GetLength(0), side = (n + margin * 2) * px;
            var pixels = new Color32[side * side];
            Color32 black = new(0, 0, 0, 255), white = new(255, 255, 255, 255);
            for (var r = 0; r < side; r++)
            {
                var my = (side - 1 - r) / px - margin;   // hàng texture 0 = dưới cùng
                for (var c = 0; c < side; c++)
                {
                    var mx = c / px - margin;
                    pixels[r * side + c] = mx >= 0 && mx < n && my >= 0 && my < n && m[mx, my] ? black : white;
                }
            }
            var t = new Texture2D(side, side, TextureFormat.RGBA32, false)
            {
                name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            t.SetPixels32(pixels);
            t.Apply(false, false);
            return t;
        }
    }
}
