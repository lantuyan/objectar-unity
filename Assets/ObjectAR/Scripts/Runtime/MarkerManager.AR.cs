using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;

namespace ObjectAR
{
    public partial class MarkerManager
    {
        /// Bảng đang hiển thị. Holder neo trong không gian thế giới (con của TrackablesParent, không gắn vào
        /// ARTrackedImage) để model đứng yên khi mã ra khỏi khung hình / mất tracking.
        sealed class Tracked { public Transform Holder; public BoardNode Node; public string BoardID; }
        readonly Dictionary<string, Tracked> tracked = new();    // trackableId (giả lập: "sim") → bảng

        ARSession session;
        ARTrackedImageManager images;
        ARCameraManager cameraManager;
        XROrigin origin;
        Camera cam;
        readonly List<(string id, AddReferenceImageJobState job)> jobs = new();
        readonly List<Texture2D> refTextures = new();
        bool arReady;
        int sessionGen;            // tăng mỗi lần chạy lại session → bỏ kết quả bất đồng bộ cũ
        const float LockSettle = 1f;   // giây cho ARCore tinh chỉnh tư thế mã trước khi tắt dò ảnh

        Transform WorldParent => origin != null ? origin.TrackablesParent : null;

        void FindAR()
        {
            session = FindObjectOfType<ARSession>(true);
            images = FindObjectOfType<ARTrackedImageManager>(true);
            origin = FindObjectOfType<XROrigin>(true);
            cam = origin != null && origin.Camera != null ? origin.Camera : Camera.main;
            cameraManager = cam != null ? cam.GetComponent<ARCameraManager>() : null;
        }

        IEnumerator Boot()
        {
            StartCoroutine(ModelLibrary.Preload());
            if (images != null) images.trackedImagesChanged += OnTrackedImagesChanged;
            // Chờ ARSession sẵn sàng. Editor không có XR (chưa cài XR Simulation) → giả lập sau ~1s.
            var t0 = Time.realtimeSinceStartup;
            while (session != null && images != null)
            {
                var s = ARSession.state;
                if (s == ARSessionState.Unsupported) break;
                if (s >= (Application.isEditor ? ARSessionState.SessionInitializing : ARSessionState.Ready)) { arReady = true; break; }
                if (Application.isEditor && Time.realtimeSinceStartup - t0 > 1f) break;
                yield return null;
            }
            if (arReady) RunSession();
            else StartSimulation();
        }

        /// Xoá holder + bảng đã nhận, reset session và dựng lại thư viện ảnh để quét từ đầu.
        void RunSession()
        {
            foreach (var t in tracked.Values) NodeUtil.Kill(t.Holder.gameObject);
            tracked.Clear();
            visible.Clear();
            ScanLocked = false;
            sessionGen++;
            if (IsSimulated) SimulateScan();
            else if (arReady)
            {
                session.Reset();
                var lib = BuildLibrary();
                if (lib != null) { images.referenceLibrary = lib; images.enabled = true; }
            }
            Notify();
        }

        /// Thư viện ảnh runtime: mỗi bảng 1 ảnh QR, tên = boardID, cạnh thật = widthCm (ARKit bắt buộc có kích thước).
        MutableRuntimeReferenceImageLibrary BuildLibrary()
        {
            // Job đã chép dữ liệu ảnh nên huỷ texture cũ được ngay.
            foreach (var t in refTextures) NodeUtil.Kill(t);
            refTextures.Clear();
            jobs.Clear();
            try
            {
                if (images.CreateRuntimeLibrary() is not MutableRuntimeReferenceImageLibrary lib)
                {
                    Toast = "Thiết bị không hỗ trợ thêm mã QR lúc chạy";
                    return null;
                }
                foreach (var b in Boards)
                {
                    var tex = QRMaker.ReferenceTexture(b.payload);
                    if (tex == null) continue;
                    refTextures.Add(tex);
                    jobs.Add((b.id, lib.ScheduleAddImageWithValidationJob(tex, b.id, Mathf.Max(b.widthCm, 0.5f) / 100f)));
                }
                return lib;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Toast = "Không tạo được thư viện mã QR";
                return null;
            }
        }

        /// ARCore có thể từ chối ảnh ít đặc trưng → log trạng thái và báo Toast.
        void PollJobs()
        {
            for (var i = jobs.Count - 1; i >= 0; i--)
            {
                var (id, job) = jobs[i];
                var st = job.status;
                if (st.IsPending()) continue;
                jobs.RemoveAt(i);
                if (st.IsError())
                {
                    Debug.LogWarning($"[ObjectAR] Ảnh tham chiếu {id}: {st}");
                    Toast = $"Mã {id} không dùng được để dò ({st})";
                }
                else Debug.Log($"[ObjectAR] Ảnh tham chiếu {id}: {st}");
            }
        }

        void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs e)
        {
            foreach (var img in e.added) OnAdded(img);
            foreach (var img in e.updated) OnUpdated(img);
            // Đang khoá quét: giữ model tại chỗ dù AR bỏ ảnh (holder neo trong không gian thế giới).
            if (ScanLocked) return;
            foreach (var img in e.removed)
            {
                var key = img.trackableId.ToString();
                if (!tracked.TryGetValue(key, out var t)) continue;
                NodeUtil.Kill(t.Holder.gameObject);
                tracked.Remove(key);
            }
        }

        void OnAdded(ARTrackedImage img)
        {
            if (ScanLocked) return;
            var key = img.trackableId.ToString();
            if (Found(key, img.referenceImage.name, img.transform.position, img.transform.rotation)) Verify(key);
        }

        void OnUpdated(ARTrackedImage img)
        {
            if (!tracked.TryGetValue(img.trackableId.ToString(), out var t)) return;
            var ok = img.trackingState == TrackingState.Tracking;
            // Chỉ cập nhật vị trí khi đang thấy mã; khi mất mã, model đứng yên nhờ world tracking.
            if (ok)
            {
                var h = t.Holder;
                var k = Vector3.Distance(h.position, img.transform.position) > 0.05f ? 1f : 0.35f;   // làm mượt để model không rung
                h.SetPositionAndRotation(Vector3.Lerp(h.position, img.transform.position, k),
                                         Quaternion.Slerp(h.rotation, img.transform.rotation, k));
            }
            var changed = false;
            if (!visible.TryGetValue(t.BoardID, out var v) || v != ok) { visible[t.BoardID] = ok; changed = true; }
            var est = Mathf.Round(img.size.x * 1000f) / 10f;   // cm, 1 chữ số thập phân
            if (!estimatedCm.TryGetValue(t.BoardID, out var old) || old != est) { estimatedCm[t.BoardID] = est; changed = true; }
            if (changed) Notify();
        }

        /// Đã nhận mã boardID tại tư thế (thế giới) → dựng holder + bảng, khoá quét. Dùng chung cho giả lập.
        bool Found(string key, string boardID, Vector3 pos, Quaternion rot)
        {
            var b = Boards.Find(x => x.id == boardID);
            if (b == null) return false;
            var holder = new GameObject("holder").transform;
            holder.SetParent(WorldParent, false);
            holder.SetPositionAndRotation(pos, rot);
            var bn = BoardNode.Create(b, holder, EditingID == b.id ? SelectedPlacement : null);
            bn.Appear();
            tracked[key] = new Tracked { Holder = holder, Node = bn, BoardID = b.id };
            visible[b.id] = true;
            ScanLocked = true;
            if (!IsSimulated) StartCoroutine(StopDetection(sessionGen));
            Notify();
            return true;
        }

        /// Khoá quét: chạy tiếp world tracking nhưng tắt dò ảnh (sau LockSettle để ARCore tinh chỉnh tư thế).
        IEnumerator StopDetection(int gen)
        {
            yield return new WaitForSeconds(LockSettle);
            if (gen == sessionGen && ScanLocked && images != null) images.enabled = false;
        }

        void UpdateWarning()
        {
            if (showLayout || !arReady) return;
            var w = ARSession.state != ARSessionState.SessionInitializing ? null : ARSession.notTrackingReason switch
            {
                NotTrackingReason.None => null,
                NotTrackingReason.Initializing or NotTrackingReason.Relocalizing => "Đang khởi tạo…",
                NotTrackingReason.ExcessiveMotion => "Di chuyển chậm lại",
                NotTrackingReason.InsufficientFeatures or NotTrackingReason.InsufficientLight => "Thiếu chi tiết / ánh sáng",
                _ => "Tracking không khả dụng",
            };
            if (w == TrackingWarning) return;
            TrackingWarning = w;
            Notify();
        }

        /// Đọc nội dung QR bằng ZXing trên plane Y của ảnh camera để chắc chắn đúng mã
        /// (các mã QR khá giống nhau về hình, AR có thể nhận nhầm).
        async void Verify(string key)
        {
            if (cameraManager == null || !cameraManager.TryAcquireLatestCpuImage(out var image)) return;
            byte[] y;
            int w, h;
            using (image)
            {
                var plane = image.GetPlane(0);
                if (plane.pixelStride != 1) return;
                w = image.width; h = image.height;
                y = new byte[w * h];
                for (var r = 0; r < h; r++) NativeArray<byte>.Copy(plane.data, r * plane.rowStride, y, r * w, w);
            }
            var known = new Dictionary<string, string>();
            foreach (var b in Boards) if (!known.ContainsKey(b.payload)) known[b.payload] = b.id;
            var gen = sessionGen;
            HashSet<string> found;
            try { found = await Task.Run(() => Decode(y, w, h, known)); }
            catch (Exception e) { Debug.LogWarning($"[ObjectAR] Đọc QR lỗi: {e.Message}"); return; }

            if (this == null || gen != sessionGen || found.Count != 1 || !tracked.TryGetValue(key, out var t)) return;
            var real = found.First();
            var nb = Boards.Find(x => x.id == real);
            if (real == t.BoardID || nb == null) return;
            // AR nhận nhầm sang mã khác → đổi nội dung theo mã thật đọc được.
            visible.Remove(t.BoardID);
            t.BoardID = real;
            t.Node.Sync(nb, true, EditingID == real ? SelectedPlacement : null);
            visible[real] = true;
            Notify();
        }

        /// Chạy trên thread nền: các boardID có payload đọc được trong ảnh.
        static HashSet<string> Decode(byte[] y, int w, int h, Dictionary<string, string> known)
        {
            var src = new PlanarYUVLuminanceSource(y, w, h, 0, 0, w, h, false);
            var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.TRY_HARDER] = true };
            var results = new QRCodeMultiReader().decodeMultiple(new BinaryBitmap(new HybridBinarizer(src)), hints);
            var ids = new HashSet<string>();
            foreach (var r in results ?? Array.Empty<Result>())
                if (r?.Text != null && known.TryGetValue(r.Text, out var id)) ids.Add(id);
            return ids;
        }
    }
}
