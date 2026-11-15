using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ObjectAR
{
    /// Quản lý bảng marker: dữ liệu + trạng thái public cho UI. Phần AR ở MarkerManager.AR.cs,
    /// cử chỉ + giả lập ở MarkerManager.Input.cs.
    [DisallowMultipleComponent]
    public partial class MarkerManager : MonoBehaviour
    {
        public static MarkerManager Instance { get; private set; }

        /// Bắn khi bất kỳ state public nào đổi (UI vẽ lại).
        public event Action Changed;

        public List<MarkerBoard> Boards { get; private set; }
        public IReadOnlyDictionary<string, bool> Visible => visible;          // boardID → đang tracking (có key = đã nhận)
        public IReadOnlyDictionary<string, float> EstimatedCm => estimatedCm; // kích thước ước tính từ AR
        public string EditingID { get; private set; }
        public string SelectedPlacement { get; private set; }
        public MarkerBoard EditingBoard => EditingID == null ? null : Boards.Find(b => b.id == EditingID);
        public string TrackingWarning { get; private set; }
        /// Đã nhận mã QR → tắt dò ảnh, giữ nguyên model; chỉ dò lại khi bấm "Quét lại".
        public bool ScanLocked { get; private set; }
        /// Editor không có AR → chế độ giả lập.
        public bool IsSimulated { get; private set; }

        /// UI tự ẩn sau 1.8s qua ClearToast(text).
        public string Toast
        {
            get => toast;
            set { if (toast == value) return; toast = value; Notify(); }
        }

        /// AR Foundation không có tương đương automaticImageScaleEstimationEnabled của ARKit: giữ thuộc tính
        /// cho UI, đổi thì chạy lại session; kích thước ước tính luôn lấy từ ARTrackedImage.size.
        public bool AutoScale
        {
            get => autoScale;
            set { if (autoScale == value) return; autoScale = value; RunSession(); }
        }

        /// Editor sơ đồ che camera → tạm dừng AR cho mượt, mở lại khi quay về.
        public bool ShowLayout
        {
            get => showLayout;
            set
            {
                if (showLayout == value) return;
                showLayout = value;
                if (session != null && !IsSimulated) session.enabled = !value;
                if (value) TrackingWarning = null;
                Notify();
            }
        }

        readonly Dictionary<string, bool> visible = new();
        readonly Dictionary<string, float> estimatedCm = new();
        string toast;
        bool autoScale = true, showLayout;
        float saveAt = -1;

        static readonly Vector2[] slots =
            { new(1, 0), new(-1, 0), new(0, -1), new(0, 1), new(1, -1), new(-1, -1), new(1, 1), new(-1, 1) };

        void Awake()
        {
            Instance = this;
            Boards = MarkerStore.Load();
            FindAR();
        }

        void Start() => StartCoroutine(Boot());

        void Update()
        {
            HandleInput();
            if (IsSimulated) SimulationKeys();
            else { PollJobs(); UpdateWarning(); }
            if (saveAt >= 0 && Time.unscaledTime >= saveAt) SaveNow();
        }

        void OnApplicationPause(bool paused) { if (paused && saveAt >= 0) SaveNow(); }

        void OnDestroy()
        {
            if (saveAt >= 0) SaveNow();
            if (images != null) images.trackedImagesChanged -= OnTrackedImagesChanged;
            if (Instance == this) Instance = null;
        }

        void Notify() => Changed?.Invoke();

        /// Xoá mọi mã QR đã nhận + model đang hiển thị, chạy lại session để quét từ đầu.
        /// Giả lập: chuyển sang bảng kế tiếp.
        public void Restart()
        {
            EditingID = null; SelectedPlacement = null;
            estimatedCm.Clear();
            if (IsSimulated && Boards.Count > 0) simIndex = (simIndex + 1) % Boards.Count;
            RunSession();
            Toast = IsSimulated && Boards.Count > 0 ? $"Giả lập: {Boards[simIndex].name}" : "Hướng camera vào mã QR";
        }

        // MARK: - Dữ liệu

        public void UpdateBoard(MarkerBoard b, bool rerun = false)
        {
            var i = Boards.FindIndex(x => x.id == b.id);
            if (i < 0) return;
            Boards[i] = b;
            saveAt = Time.unscaledTime + 0.4f;   // gộp nhiều lần sửa liên tiếp (kéo thanh trượt) thành 1 lần ghi
            if (rerun) RunSession();
            else { RefreshNodes(b.id); Notify(); }
        }

        void SaveNow()
        {
            saveAt = -1;
            MarkerStore.Save(Boards);
        }

        public MarkerBoard AddBoard()
        {
            var n = Boards.Select(x => x.id.Length > 1 && int.TryParse(x.id.Substring(1), out var k) ? k : 0)
                          .DefaultIfEmpty(0).Max() + 1;
            var token = Guid.NewGuid().ToString("N").Substring(0, 6);
            var b = new MarkerBoard { id = $"M{n}", name = $"Bảng {n}", payload = $"OBJECTAR:M{n}:{token}" };
            Boards.Add(b);
            SaveNow();
            RunSession();
            return b;
        }

        public void DeleteBoard(string id)
        {
            Boards.RemoveAll(b => b.id == id);
            if (EditingID == id) EditingID = null;
            SaveNow();
            RunSession();
        }

        public void RestoreDefaults()
        {
            Boards = MarkerStore.Defaults();
            EditingID = null; SelectedPlacement = null;
            SaveNow();
            RunSession();
        }

        public void AddPlacement(HouseModel model)
        {
            var b = EditingBoard;
            if (b == null || model == null) return;
            // đặt lần lượt quanh mã: phải, trái, trên, dưới
            var s = slots[b.placements.Count % slots.Length] * (b.widthCm / 100 / 2 + 0.05f);
            var p = new Placement { modelID = model.Id, x = s.x, z = s.y, scale = model.DefaultScale };
            b.placements.Add(p);
            SelectedPlacement = p.id;
            UpdateBoard(b);
        }

        public void RemoveSelected()
        {
            var b = EditingBoard;
            if (b == null || SelectedPlacement == null) return;
            b.placements.RemoveAll(p => p.id == SelectedPlacement);
            SelectedPlacement = b.placements.LastOrDefault()?.id;
            UpdateBoard(b);
        }

        public void ModifySelected(Action<Placement> f)
        {
            var b = EditingBoard;
            var p = b?.placements.Find(x => x.id == SelectedPlacement);
            if (p == null) return;
            f(p);
            UpdateBoard(b);
        }

        public void StartEditing(string id)
        {
            EditingID = id;
            SelectedPlacement = Boards.Find(b => b.id == id)?.placements.FirstOrDefault()?.id;
            RefreshAll();
            Notify();
        }

        public void StopEditing()
        {
            EditingID = null; SelectedPlacement = null;
            ShowLayout = false;
            RefreshAll();
            Notify();
        }

        public void Select(string placementId)
        {
            SelectedPlacement = placementId;
            if (EditingID != null) RefreshNodes(EditingID);
            Notify();
        }

        /// Lấy kích thước AR ước tính làm kích thước thật của mã.
        public void ApplyEstimate(string id)
        {
            var b = Boards.Find(x => x.id == id);
            if (b == null || !estimatedCm.TryGetValue(id, out var cm)) return;
            b.widthCm = Mathf.Round(cm * 10) / 10;
            UpdateBoard(b, rerun: true);
            Toast = $"Đã đặt cạnh mã = {b.widthCm.ToString("0.0", CultureInfo.InvariantCulture)} cm";
        }

        public void ClearToast(string text) { if (toast == text) Toast = null; }

        void RefreshAll() { foreach (var b in Boards) RefreshNodes(b.id); }

        void RefreshNodes(string boardID)
        {
            var b = Boards.Find(x => x.id == boardID);
            if (b == null) return;
            var sel = EditingID == boardID ? SelectedPlacement : null;
            foreach (var t in tracked.Values)
                if (t.BoardID == boardID) t.Node.Sync(b, true, sel);
        }
    }
}
