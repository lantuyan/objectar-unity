using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;

namespace ObjectAR
{
    public partial class MarkerManager
    {
        // Cử chỉ: chạm chọn; khi đang chỉnh bảng: kéo 1 ngón trên mặt phẳng mã, pinch scale, xoay 2 ngón.
        bool blocked;           // ngón chạm bắt đầu trên UI → bỏ qua tới khi nhấc hết
        bool moved;
        Vector2 downPos;
        float downTime;
        Vector3? lastPan;
        Vector2 prevA, prevB;
        int prevCount;
        Vector3 lastMouse;

        const float TapSlop = 12f, TapTime = 0.35f;

        void HandleInput()
        {
            if (showLayout || cam == null) return;
            if (Input.touchCount > 0 || Input.touchSupported) HandleTouches();
            else HandleMouse();
        }

        void HandleTouches()
        {
            var n = Input.touchCount;
            for (var i = 0; i < n; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began && OverUI(t.fingerId)) blocked = true;
            }
            if (n == 0 || blocked)
            {
                if (n == 0) blocked = false;
                lastPan = null; prevCount = 0;
                return;
            }
            if (n == 1)
            {
                var t = Input.GetTouch(0);
                switch (t.phase)
                {
                    case TouchPhase.Began: PointerDown(t.position); break;
                    case TouchPhase.Moved: PointerMove(t.position); break;
                    case TouchPhase.Ended: PointerUp(t.position); break;
                    case TouchPhase.Canceled: lastPan = null; break;
                }
            }
            else
            {
                moved = true;   // nhiều ngón → không còn là chạm
                lastPan = null;
                Vector2 a = Input.GetTouch(0).position, b = Input.GetTouch(1).position;
                if (prevCount >= 2)
                {
                    var d0 = (prevB - prevA).magnitude;
                    if (d0 > 1f) Pinch((b - a).magnitude / d0);
                    Rotate(Vector2.SignedAngle(prevB - prevA, b - a));
                }
                prevA = a; prevB = b;
            }
            prevCount = n;
        }

        void HandleMouse()
        {
            var p = Input.mousePosition;
            if (Input.GetMouseButtonDown(0)) { blocked = OverUI(-1); if (!blocked) PointerDown(p); }
            else if (Input.GetMouseButton(0) && !blocked) PointerMove(p);
            else if (Input.GetMouseButtonUp(0) && !blocked) PointerUp(p);

            var scroll = Input.mouseScrollDelta.y;
            if (scroll != 0 && !OverUI(-1))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) Pinch(1 + scroll * 0.1f);
                else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.LeftCommand)) Rotate(scroll * 5f);
                else if (IsSimulated) orbitDist = Mathf.Clamp(orbitDist * (1 - scroll * 0.1f), 0.08f, 3f);
            }
            if (IsSimulated && Input.GetMouseButton(1))
            {
                if (!Input.GetMouseButtonDown(1))
                {
                    var d = p - lastMouse;
                    orbitYaw += d.x * 0.3f;
                    orbitPitch = Mathf.Clamp(orbitPitch - d.y * 0.3f, 5f, 89f);
                }
            }
            lastMouse = p;
        }

        static bool OverUI(int pointerId) =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);

        void PointerDown(Vector2 p)
        {
            downPos = p; downTime = Time.unscaledTime; moved = false;
            lastPan = null;
        }

        void PointerMove(Vector2 p)
        {
            if (!moved && (p - downPos).magnitude > TapSlop) moved = true;
            if (moved) Pan(p);
        }

        void PointerUp(Vector2 p)
        {
            if (!moved && Time.unscaledTime - downTime < TapTime) Tap(p);
            lastPan = null;
        }

        BoardNode EditingNode()
        {
            if (EditingID == null) return null;
            foreach (var t in tracked.Values) if (t.BoardID == EditingID) return t.Node;
            return null;
        }

        void Tap(Vector2 p)
        {
            var ray = cam.ScreenPointToRay(p);
            Tracked best = null;
            string pid = null;
            var bestDist = float.MaxValue;
            foreach (var t in tracked.Values)
            {
                var id = t.Node.HitTest(ray, out var d);
                if (id != null && d < bestDist) { best = t; pid = id; bestDist = d; }
            }
            if (best == null) return;
            if (EditingID == best.BoardID) { Select(pid); return; }
            var pl = Boards.Find(b => b.id == best.BoardID)?.placements.Find(x => x.id == pid);
            var m = pl != null ? HouseCatalog.Model(pl.modelID) : null;
            if (m != null) Toast = $"{m.Name} • tỉ lệ 1:{Mathf.RoundToInt(1 / pl.scale)}";
        }

        void Pan(Vector2 p)
        {
            var bn = SelectedPlacement != null ? EditingNode() : null;
            if (bn == null || !bn.PointOnBoard(cam.ScreenPointToRay(p), out var cur)) return;
            if (lastPan is Vector3 last)
            {
                var d = cur - last;
                ModifySelected(pl => { pl.x += d.x; pl.z -= d.z; });   // z Unity ngược z dữ liệu
            }
            lastPan = cur;
        }

        void Pinch(float f)
        {
            if (SelectedPlacement == null || EditingID == null) return;
            ModifySelected(p => p.scale = Mathf.Clamp(p.scale * f, 0.0005f, 1f));
        }

        /// deg > 0 = ngược chiều kim đồng hồ trên màn hình → model xoay ngược chiều kim đồng hồ nhìn từ trên.
        void Rotate(float deg)
        {
            if (SelectedPlacement == null || EditingID == null || deg == 0) return;
            ModifySelected(p => p.yaw = Mathf.Repeat(p.yaw + deg, 360f));
        }

        // MARK: - Giả lập (Editor không có AR)

        int simIndex;
        float orbitYaw, orbitPitch = 55f, orbitDist = 0.45f;

        /// Tắt nền camera AR, camera nhìn xuống bàn, sinh bảng như thể vừa quét.
        void StartSimulation()
        {
            IsSimulated = true;
            if (cam != null)
            {
                if (cam.TryGetComponent<ARCameraBackground>(out var bg)) bg.enabled = false;
                // TrackedPoseDriver sẽ ghi đè tư thế camera → tắt (tìm theo tên để khỏi tham chiếu Input System).
                foreach (var c in cam.GetComponents<Behaviour>())
                    if (c.GetType().Name.Contains("PoseDriver")) c.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.32f, 0.33f, 0.35f);
            }
            Debug.Log("[ObjectAR] Không có AR → chế độ giả lập (chuột phải: xoay, cuộn: zoom, phím 1–9: quét bảng)");
            RunSession();
            if (Boards.Count > 0) Toast = $"Giả lập: {Boards[simIndex].name}";
        }

        /// Sinh bảng thứ simIndex tại gốc, đi đúng code path "đã nhận mã" + khoá quét.
        void SimulateScan()
        {
            if (Boards.Count == 0) return;
            simIndex = Mathf.Clamp(simIndex, 0, Boards.Count - 1);
            var wp = WorldParent;
            Found("sim", Boards[simIndex].id, wp != null ? wp.position : Vector3.zero, wp != null ? wp.rotation : Quaternion.identity);
        }

        /// Phím 1–9 = quét bảng thứ n (bỏ bảng đang hiện).
        void SimulationKeys()
        {
            for (var i = 1; i <= 9 && i <= Boards.Count; i++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha0 + i)) continue;
                simIndex = i - 1;
                RunSession();
                Toast = $"Giả lập: {Boards[simIndex].name}";
                return;
            }
        }

        void LateUpdate()
        {
            if (!IsSimulated || cam == null) return;
            var wp = WorldParent;
            var target = wp != null ? wp.position : Vector3.zero;
            // Người xem đứng phía cạnh dưới mã (z dữ liệu dương = -z Unity).
            cam.transform.SetPositionAndRotation(
                target + Quaternion.Euler(orbitPitch, orbitYaw, 0) * new Vector3(0, 0, -orbitDist),
                Quaternion.Euler(orbitPitch, orbitYaw, 0));
        }
    }
}
