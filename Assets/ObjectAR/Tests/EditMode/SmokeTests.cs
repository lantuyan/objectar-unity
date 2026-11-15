using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace ObjectAR.Tests
{
    public class SmokeTests
    {
        // MARK: - Model

        [Test]
        public void EveryCatalogModel_MakesNode_BottomAtOrigin()
        {
            foreach (var m in HouseCatalog.All)
            {
                var go = HouseCatalog.MakeNode(m.Id);
                try
                {
                    Assert.IsNotNull(go, $"{m.Id}: MakeNode null");
                    Assert.IsNotEmpty(go.GetComponentsInChildren<Renderer>(), $"{m.Id}: không có renderer");
                    var b = NodeUtil.BoundsIn(go.transform, go);
                    var size = b.size.x > b.size.y ? (b.size.x > b.size.z ? b.size.x : b.size.z) : (b.size.y > b.size.z ? b.size.y : b.size.z);
                    Debug.Log($"[Test] {m.Id}: size={b.size:F3} min={b.min:F3}");
                    Assert.That(size, Is.InRange(0.05f, 500f), $"{m.Id}: kích thước bất thường {b.size}");
                    // Laptop: đáy căn theo thân máy, nắp mở có thể lệch chút.
                    Assert.That(Mathf.Abs(b.min.y), Is.LessThan(Mathf.Max(0.005f, size * 0.02f)), $"{m.Id}: đáy không ở gốc ({b.min.y})");
                }
                finally { Object.DestroyImmediate(go); }
            }
        }

        // MARK: - JSON

        [Test]
        public void MarkerStore_JsonRoundTrip()
        {
            var boards = MarkerStore.Defaults();
            var back = MarkerStore.Parse(MarkerStore.ToJson(boards));
            Assert.AreEqual(boards.Count, back.Count);
            for (var i = 0; i < boards.Count; i++)
            {
                Assert.AreEqual(boards[i].id, back[i].id);
                Assert.AreEqual(boards[i].name, back[i].name);
                Assert.AreEqual(boards[i].payload, back[i].payload);
                Assert.AreEqual(boards[i].widthCm, back[i].widthCm);
                Assert.AreEqual(boards[i].placements.Count, back[i].placements.Count);
                for (var j = 0; j < boards[i].placements.Count; j++)
                {
                    Placement a = boards[i].placements[j], b = back[i].placements[j];
                    Assert.AreEqual(a.id, b.id);
                    Assert.AreEqual(a.modelID, b.modelID);
                    Assert.AreEqual(new[] { a.x, a.y, a.z, a.yaw, a.scale }, new[] { b.x, b.y, b.z, b.yaw, b.scale });
                }
            }
            StringAssert.IsMatch("^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$", new Placement().id);
        }

        /// Đúng định dạng JSONEncoder (.prettyPrinted, .sortedKeys) của bản Swift.
        const string SwiftJson = @"[
  {
    ""id"" : ""M1"",
    ""name"" : ""Khu dân cư"",
    ""payload"" : ""OBJECTAR:M1:khu-dan-cu"",
    ""placements"" : [
      {
        ""id"" : ""8E1F4C2A-6B5D-4F1E-9A3C-2D7B8E9F0A1B"",
        ""modelID"" : ""house-pho"",
        ""scale"" : 0.0080000003799796104,
        ""x"" : -0.075000002980232239,
        ""y"" : 0,
        ""yaw"" : 90,
        ""z"" : 0.059999998658895493
      }
    ],
    ""widthCm"" : 4.5
  },
  {
    ""id"" : ""M4"",
    ""name"" : ""Bảng trống"",
    ""payload"" : ""OBJECTAR:M4:bang-trong"",
    ""placements"" : [

    ],
    ""widthCm"" : 6
  }
]";

        [Test]
        public void MarkerStore_ParsesSwiftFormat()
        {
            var b = MarkerStore.Parse(SwiftJson);
            Assert.AreEqual(2, b.Count);
            Assert.AreEqual("Khu dân cư", b[0].name);
            Assert.AreEqual(4.5f, b[0].widthCm);
            var p = b[0].placements.Single();
            Assert.AreEqual("8E1F4C2A-6B5D-4F1E-9A3C-2D7B8E9F0A1B", p.id);
            Assert.AreEqual("house-pho", p.modelID);
            Assert.AreEqual(0.008f, p.scale, 1e-7f);
            Assert.AreEqual(-0.075f, p.x, 1e-7f);
            Assert.AreEqual(0.06f, p.z, 1e-7f);
            Assert.AreEqual(90f, p.yaw);
            Assert.IsEmpty(b[1].placements);
            Assert.AreEqual(6f, b[1].widthCm);
            // Ghi lại vẫn dùng đúng tên khoá.
            var json = MarkerStore.ToJson(b);
            foreach (var k in new[] { "id", "name", "payload", "widthCm", "placements", "modelID", "x", "y", "z", "yaw", "scale" })
                StringAssert.Contains($"\"{k}\":", json);
        }

        // MARK: - QR

        static string[] DefaultIds => MarkerStore.Defaults().Select(b => b.id).ToArray();
        static MarkerBoard Board(string id) => MarkerStore.Defaults().Find(b => b.id == id);

        [TestCaseSource(nameof(DefaultIds))]
        public void QR_DecodesBackToPayload(string id)
        {
            var board = Board(id);
            Assert.IsNotNull(QRMaker.Modules(board.payload));
            var tex = QRMaker.DisplayTexture(board);
            try
            {
                var lum = TopDownLuminance(tex);
                var src = new RGBLuminanceSource(lum, tex.width, tex.height, RGBLuminanceSource.BitmapFormat.Gray8);
                var r = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(src)),
                                                  new Dictionary<DecodeHintType, object> { [DecodeHintType.TRY_HARDER] = true });
                Assert.IsNotNull(r, "ZXing không đọc được");
                Assert.AreEqual(board.payload, r.Text);
            }
            finally { Object.DestroyImmediate(tex); }
        }

        /// ARKit/ARCore dò theo hình ảnh → ảnh tham chiếu phải trùng từng ô với mã đã in (sinh bởi CoreImage).
        [TestCaseSource(nameof(DefaultIds))]
        public void QR_MatchesPrintedPng(string id)
        {
            var board = Board(id);
            var dir = Path.Combine(Application.dataPath, "../QR-markers");
            var file = Path.Combine(dir, $"QR-{board.id}-{board.payload.Split(':').Last()}.png");
            Assert.IsTrue(File.Exists(file), $"Thiếu {file}");
            var png = new Texture2D(2, 2);
            Assert.IsTrue(png.LoadImage(File.ReadAllBytes(file)));
            var m = QRMaker.Modules(board.payload);
            var n = m.GetLength(0);
            var refTex = QRMaker.ReferenceTexture(board.payload);
            try
            {
                var printed = SampleModules(png, n, cropDark: true);
                AssertSame(printed, m, "QRMaker.Modules");
                AssertSame(printed, SampleModules(refTex, n, cropDark: false), "QRMaker.ReferenceTexture");
                var auto = QRMaker.Encode(board.payload);
                Debug.Log($"[Test] {board.id}: mask ZXing tự chọn {(Same(printed, auto) ? "trùng" : "KHÁC")} CoreImage");
            }
            finally
            {
                Object.DestroyImmediate(png);
                Object.DestroyImmediate(refTex);
            }
        }

        static void AssertSame(bool[,] expected, bool[,] actual, string what)
        {
            Assert.IsNotNull(actual, what);
            Assert.AreEqual(expected.GetLength(0), actual.GetLength(0), $"{what}: số module");
            var diff = 0;
            for (var y = 0; y < expected.GetLength(1); y++)
                for (var x = 0; x < expected.GetLength(0); x++)
                    if (expected[x, y] != actual[x, y]) diff++;
            Assert.AreEqual(0, diff, $"{what}: {diff} ô khác ảnh in");
        }

        static bool Same(bool[,] a, bool[,] b) =>
            b != null && a.GetLength(0) == b.GetLength(0) && a.Cast<bool>().SequenceEqual(b.Cast<bool>());

        /// Lấy mẫu tâm từng module (m[x, y], y = 0 hàng trên). cropDark: cắt theo vùng điểm tối
        /// (bề ngang = ô QR; bỏ dòng chú thích phía dưới bằng cách lấy cạnh vuông theo bề ngang).
        static bool[,] SampleModules(Texture2D tex, int n, bool cropDark)
        {
            var lum = TopDownLuminance(tex);
            int w = tex.width, h = tex.height, x0 = 0, y0 = 0, side = w;
            if (cropDark)
            {
                int x1 = -1;
                x0 = w; y0 = h;
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                        if (lum[y * w + x] < 128) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); }
                side = x1 - x0 + 1;
                Assert.AreEqual(0, side % n, $"cạnh {side}px không chia hết cho {n} module");
            }
            var ms = side / (float)n;
            var m = new bool[n, n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                    m[x, y] = lum[(y0 + (int)((y + 0.5f) * ms)) * w + x0 + (int)((x + 0.5f) * ms)] < 128;
            return m;
        }

        /// Độ sáng 8-bit, hàng 0 = trên cùng (texture Unity lưu từ dưới lên).
        static byte[] TopDownLuminance(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            var lum = new byte[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var c = px[(h - 1 - y) * w + x];
                    lum[y * w + x] = (byte)((c.r * 299 + c.g * 587 + c.b * 114) / 1000);
                }
            return lum;
        }
    }
}
