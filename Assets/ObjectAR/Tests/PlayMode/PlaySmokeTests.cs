using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ObjectAR.Tests
{
    /// Chạy scene Main thật ở Play mode (Editor không có AR → chế độ giả lập) và chụp ảnh UI.
    /// Chụp cần GPU: tools/win.sh test-play (batchmode không -nographics). Ảnh ở Builds/play/.
    public class PlaySmokeTests
    {
        const string ScannedHint = "Đã nhận mã QR • bấm Quét lại để quét mã khác";
        static string ShotDir => Path.Combine(Application.dataPath, "../Builds/play");

        readonly List<string> problems = new();
        readonly List<string> warnings = new();

        void OnLog(string msg, string stack, LogType type)
        {
            if (type is LogType.Error or LogType.Exception or LogType.Assert) problems.Add($"{type}: {msg}");
            else if (type == LogType.Warning) warnings.Add(msg);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Application.logMessageReceived += OnLog;
            yield return SceneManager.LoadSceneAsync("Main");
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            foreach (var w in warnings) Debug.Log("[Play] warning: " + w);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator Main_SimulationFlow_AndUI()
        {
            // 1. Giả lập: tự "quét" M1.
            yield return WaitFor(() => MarkerManager.Instance != null && MarkerManager.Instance.ScanLocked, 6, "chưa khoá quét (giả lập)");
            var m = MarkerManager.Instance;
            Assert.IsTrue(m.IsSimulated, "IsSimulated");
            yield return WaitFor(() => Board("M1") != null, 3, "không thấy BoardNode M1");
            yield return new WaitForSeconds(0.6f);   // hết hiệu ứng phóng to
            Assert.Greater(Board("M1").GetComponentsInChildren<Renderer>().Length, 0, "M1 không có model");
            var canvas = Object.FindObjectsOfType<Canvas>().FirstOrDefault(c => c.name == "ObjectAR UI");
            Assert.IsNotNull(canvas, "không có Canvas UI");
            Assert.IsTrue(Texts(canvas).Any(t => t.text == ScannedHint), "thiếu dòng hint đã nhận mã");
            yield return Shot("1-main-M1");

            // 2. Quét lại → bảng kế tiếp.
            m.Restart();
            yield return WaitFor(() => Board("M2") != null && Board("M1") == null, 4, "Quét lại không sinh M2");
            yield return new WaitForSeconds(0.6f);

            // 3. Danh sách bảng (bấm nút thật trên top bar).
            var listBtn = canvas.GetComponentsInChildren<Button>().First(b => b.GetComponentsInChildren<Text>().Any(t => t.text == "Bảng marker"));
            listBtn.onClick.Invoke();
            yield return null;
            Assert.IsTrue(Find(canvas, "Boards").gameObject.activeSelf, "sheet danh sách không mở");
            yield return Shot("2-board-list");
            Find(canvas, "Boards").gameObject.SetActive(false);

            // 4. Chỉnh bảng M1 trên màn AR, rồi editor sơ đồ.
            m.StartEditing("M1");
            yield return null;
            yield return Shot("3-ar-editing-M1");
            m.ShowLayout = true;
            yield return null;
            Assert.IsTrue(Find(canvas, "Layout").gameObject.activeInHierarchy, "editor sơ đồ không hiện");
            yield return Shot("4-layout-editor-M1");
            m.ShowLayout = false;
            m.StopEditing();
            yield return null;
            Assert.IsNull(m.EditingID);
            Assert.IsFalse(Find(canvas, "Layout").gameObject.activeInHierarchy, "editor sơ đồ không đóng");

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        static BoardNode Board(string id) => Object.FindObjectsOfType<BoardNode>().FirstOrDefault(b => b.BoardID == id);
        static IEnumerable<Text> Texts(Canvas c) => c.GetComponentsInChildren<Text>();
        static Transform Find(Canvas c, string name) => c.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        static IEnumerator WaitFor(System.Func<bool> cond, float seconds, string what)
        {
            var t0 = Time.realtimeSinceStartup;
            while (!cond())
            {
                if (Time.realtimeSinceStartup - t0 > seconds) Assert.Fail($"{what} (sau {seconds}s)");
                yield return null;
            }
        }

        /// Chụp ở 1080x1920 dọc bất kể kích thước Game view của batchmode: tạm cho Canvas sang Screen Space Camera
        /// và render camera chính (cả nền 3D + UI) vào RenderTexture.
        static IEnumerator Shot(string name)
        {
            // WaitForEndOfFrame không chạy trong batchmode (render tay nên không cần); chờ vài frame cho layout/LateUpdate xong.
            for (var i = 0; i < 3; i++) yield return null;
            var cam = Camera.main;
            var canvas = Object.FindObjectsOfType<Canvas>().First(c => c.name == "ObjectAR UI");
            var rt = new RenderTexture(1080, 1920, 24);
            var (mode, wc) = (canvas.renderMode, canvas.worldCamera);
            cam.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane * 2;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var img = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            img.Apply();
            RenderTexture.active = prev;
            canvas.renderMode = mode;
            canvas.worldCamera = wc;
            cam.targetTexture = null;
            Object.Destroy(rt);
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), img.EncodeToPNG());
            Object.Destroy(img);
            Debug.Log($"[Play] shot {name} (Screen {Screen.width}x{Screen.height})");
        }
    }
}
