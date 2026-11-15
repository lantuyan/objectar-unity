# ObjectAR → Unity: porting contract

Nguồn gốc: app SwiftUI + ARKit + SceneKit tại `../ObjectAR/ObjectAR/*.swift` (QR-only, đã bỏ chế độ quét vật thể).
Đích: Unity **2022.3.17f1**, Built-in Render Pipeline, AR Foundation 5.2 (ARCore Android, ARKit iOS).
Tài liệu này là hợp đồng giữa các phần (Runtime / UI / Editor-tooling). Đổi API ở đây trước khi đổi code.

## Packages (Packages/manifest.json)
| package | version |
|---|---|
| com.unity.xr.arfoundation | 5.2.2 |
| com.unity.xr.arcore | 5.2.2 |
| com.unity.xr.arkit | 5.2.2 |
| com.unity.xr.management | 4.5.4 |
| com.unity.cloud.gltfast | 6.14.1 |
| com.unity.nuget.newtonsoft-json | 3.2.1 |
| com.unity.inputsystem | (do ARF kéo về) — Active Input Handling = **Both** |

QR encode/decode: **ZXing.Net 0.16.9**, `Assets/Plugins/ZXing/zxing.dll` (lib/netstandard2.0 từ NuGet).

## Thư mục
```
Assets/ObjectAR/
  Scripts/Runtime/   (asmdef ObjectAR.Runtime)  — dữ liệu, QR, model, AR, cử chỉ
  Scripts/UI/        (asmdef ObjectAR.UI, ref Runtime) — uGUI dựng bằng code
  Editor/            (asmdef ObjectAR.Editor, editor only) — ProjectSetup, SceneBuilder, BuildScript, AutoRefresh
  Resources/Models/  — *.glb (import bằng glTFast importer lúc editor → GameObject; load bằng Resources.Load<GameObject>("Models/<tên không đuôi>"))
  Tests/EditMode/    (asmdef, editor) — test khói
Assets/Scenes/Main.unity — dựng bởi SceneBuilder (không viết tay YAML)
QR-markers/          — PNG mã QR để in (copy từ app iOS)
```
Namespace: `ObjectAR` (Runtime), `ObjectAR.UI`, `ObjectAR.EditorTools`.
Font chữ: dùng **legacy `UnityEngine.UI.Text`** với `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` (dynamic, có dấu tiếng Việt). Không dùng TextMeshPro.

## Scene (SceneBuilder dựng, Runtime chỉ dùng FindObjectOfType)
- `AR Session` : ARSession, ARInputManager
- `XR Origin` : XROrigin (+ `Camera Offset/Main Camera`: Camera tag MainCamera, ARCameraManager, ARCameraBackground, TrackedPoseDriver (Input System)), ARTrackedImageManager (referenceLibrary = null, maxNumberOfMovingImages = 4, **enabled = false** lúc đầu; MarkerManager tự bật)
- `Directional Light`
- `EventSystem` : EventSystem + StandaloneInputModule
- `ObjectAR` : `ObjectAR.MarkerManager`, `ObjectAR.UI.ObjectARUI`

## Hệ toạ độ (QUAN TRỌNG)
Dữ liệu (markers.json, Placement) **giữ nguyên quy ước ARKit** của bản Swift: gốc = tâm mã QR, x → phải, z → cạnh **dưới** mã, y → pháp tuyến (lên). Đơn vị mét, yaw = độ.
Unity/AR Foundation: ARTrackedImage nằm trong mặt phẳng XZ, +y là pháp tuyến, +z hướng về cạnh **trên** ảnh (= lật z so với ARKit).
→ Chỉ `BoardNode` đổi hệ:
```
localPosition = (x, y, -z)
localRotation = Euler(0, -yaw, 0)
localScale    = scale
```
Model: SceneKit coi mặt trước model là +z (về phía cạnh dưới mã). glTFast nhập glTF bằng cách đảo trục X (kiểm chứng trong source glTFast) — tức = gương-z rồi xoay 180°. Nên trong Unity, chuỗi node của 1 model:
```
outer
 └ orient   localRotation = Euler(0, 180 - frontYawDeg, 0)
    └ content (bản instantiate glTF; với ModelInstance: localScale = unitScale) dời sao cho tâm-đáy bounds (của baseNode nếu có) về gốc
```
Laptop (ModelSpec): nắp `lid.localRotation = AngleAxis(-lidDeg, Vector3.forward)` (dấu đảo vì đảo trục X); màn hình = Cube mỏng đen (`(x1-x0) × 0.2 × (z1-z0)` đơn vị file) đặt tại `(-(x0+x1)/2, y, (z0+z1)/2)` trong toạ độ lid. Kiểm chứng bằng mắt nếu được (nắp mở về phía sau, màn hình quay về phía trước).

## Runtime API (namespace ObjectAR) — UI chỉ dùng những gì ở đây
```csharp
public class HouseModel { string Id, Name, File /*"house-cap4.glb"*/, Icon /*emoji/ký tự*/; float DefaultScale; string SpecID; float FrontYaw /*rad*/; }
public static class HouseCatalog { IReadOnlyList<HouseModel> All; HouseModel Model(string id); GameObject MakeNode(string id) /* null nếu lỗi */; }

[Serializable] public class Placement { string id /*UUID string*/; string modelID; float x, y, z, yaw /*độ*/, scale; Placement Clone(); }
[Serializable] public class MarkerBoard { string id, name, payload; float widthCm; List<Placement> placements; MarkerBoard Clone(); }
public static class MarkerStore { List<MarkerBoard> Load(); void Save(List<MarkerBoard>); List<MarkerBoard> Defaults(); }
   string ToJson(List<MarkerBoard>); List<MarkerBoard> Parse(string json);
   // Application.persistentDataPath/markers.json, JSON (Newtonsoft) cùng định dạng bản Swift (mảng, khoá: id,name,payload,widthCm,placements[id,modelID,x,y,z,yaw,scale]); PlayerPrefs "markers.knownDefaults".
public static class QRMaker {
   bool[,] Modules(string payload);                 // ZXing, ECL M, không viền, cache; m[x, y], y = 0 hàng trên, true = đen
   bool[,] Encode(string payload, int? mask = null); // ZXing thuần (QR_COMPACT), không cache — cho test
   Texture2D ReferenceTexture(string payload, int px = 16); // RGBA32, readable, chỉ ô QR — cho ARTrackedImageManager
   Texture2D DisplayTexture(MarkerBoard b, int px = 24);    // + viền trắng 4 module (chưa cần chú thích)
   string SavePng(MarkerBoard b);                   // ghi persistentDataPath/QR-<id>.png, trả đường dẫn
}

public class MarkerManager : MonoBehaviour {
   static MarkerManager Instance;
   event Action Changed;               // bắn khi BẤT KỲ state public nào đổi (UI vẽ lại)
   List<MarkerBoard> Boards;           // chỉ đọc từ UI; sửa qua method
   IReadOnlyDictionary<string,bool> Visible;      // boardID → đang tracking (có key = đã nhận)
   IReadOnlyDictionary<string,float> EstimatedCm;
   string EditingID; string SelectedPlacement;    // null = không
   MarkerBoard EditingBoard { get; }
   bool AutoScale { get; set; }        // đổi → chạy lại session (reset)
   string TrackingWarning;             // null = ổn
   string Toast;                       // UI tự ẩn sau 1.8s qua ClearToast(text)
   bool ScanLocked;                    // đã nhận mã → tắt dò ảnh, chỉ mở lại khi Restart()
   bool ShowLayout { get; set; }       // editor sơ đồ toàn màn hình (tạm dừng AR khi mở)
   bool IsSimulated;                   // Editor không có AR → chế độ giả lập (xem dưới)

   void Restart();
   void UpdateBoard(MarkerBoard b, bool rerun = false);   // rerun = reset session (đổi payload/kích thước)
   MarkerBoard AddBoard(); void DeleteBoard(string id); void RestoreDefaults();
   void AddPlacement(HouseModel m); void RemoveSelected(); void ModifySelected(Action<Placement> f);
   void StartEditing(string id); void StopEditing(); void Select(string placementId);
   void ApplyEstimate(string id); void ClearToast(string text);
}
```
Hành vi phải giữ y như Swift (đọc `MarkerManager.swift`): khoá quét sau mã đầu tiên; holder neo trong không gian thế giới (con của `XROrigin.TrackablesParent`), chỉ cập nhật khi `trackingState == Tracking`, làm mượt k=0.35 (nhảy >5cm thì k=1); bỏ qua remove khi đang khoá; Restart = huỷ holder, `ARSession.Reset()`, dựng lại runtime library (`CreateRuntimeLibrary` + `ScheduleAddImageWithValidationJob`), bật lại manager; xác minh nội dung QR bằng ZXing trên `XRCpuImage` (plane Y) — đọc nhầm thì đổi bảng; cử chỉ (chạm chọn / kéo 1 ngón trên mặt phẳng mã / pinch scale / xoay 2 ngón) chỉ khi đang chỉnh bảng và không chạm vào UI; tracking warning từ `ARSession.notTrackingReason`; tạm dừng AR khi ShowLayout.
**Không** vẽ khung quanh mã và **không** có nhãn tên bảng trong AR.

**Chế độ giả lập (Editor, không có thiết bị AR):** nếu sau ~1s `ARSession.state` không lên được Tracking/SessionTracking (Editor không cài XR Simulation), `IsSimulated = true`: tắt ARCameraBackground, đặt nền camera màu xám, đặt camera nhìn xuống bàn (orbit chuột phải kéo/scroll zoom), sinh bảng M1 tại gốc như thể vừa quét (đi đúng code path "đã nhận mã" + khoá quét). Restart trong giả lập → sinh bảng kế tiếp (M1→M2→…) để duyệt mọi model. Phím 1–9 = "quét" bảng thứ n (bỏ bảng đang hiện, kể cả khi đã khoá). Cử chỉ bằng chuột: trái = chạm/kéo, Shift+cuộn = scale, Ctrl/Cmd+cuộn = xoay.

## UI (namespace ObjectAR.UI) — `ObjectARUI : MonoBehaviour`
Dựng toàn bộ Canvas (Screen Space Overlay, CanvasScaler 1080x1920 match 0.5, safe area) bằng code, nghe `MarkerManager.Changed`. Port từ `MarkerView.swift` (top bar: "Bảng marker", nút "Quét lại"; dòng hint: "Hướng camera vào mã QR" / khi ScanLocked hoặc có bảng hiện: "Đã nhận mã QR • bấm Quét lại để quét mã khác"; tracking warning; toast giữa màn hình; danh sách bảng (sheet); MarkerEditor dưới cùng khi đang chỉnh) và `BoardLayoutEditor.swift` (sơ đồ 2D nhìn từ trên: z dữ liệu = xuống màn hình; kéo thả model, thanh trượt x/z/yaw/scale, thêm/xoá model, đổi tên/payload/kích thước mã, xuất PNG). Bỏ phần xem 3D nhỏ trong editor nếu quá tốn công (ghi chú lại). Màu brand (0.08, 0.29, 0.75), nền tối.

Ghi chú UI (đã port): `Scripts/UI/` — `UIKit` (factory control + sprite bo góc sinh bằng code), `ObjectARUI` (canvas, top bar, hint, toast), `BoardListSheet`, `MarkerEditorPanel` (dùng chung cho màn AR và editor sơ đồ), `BoardLayoutEditor` + `BoardMap` (sơ đồ 2D, kéo thả). Bỏ: xem 3D trong editor, pinch/xoay 2 ngón trên sơ đồ (thay bằng thanh trượt + nút zoom), ShareLink (thay bằng nút "PNG" → `QRMaker.SavePng` + toast đường dẫn). Gợi ý khi chỉnh bảng không hiện tên bảng.
UI giả định: `ClearToast`/setter `ShowLayout` bắn `Changed`; `HouseCatalog.MakeNode` trả GameObject active, có Renderer, dựng đồng bộ (UI đo bounds 1 lần/model để vẽ chân đế rồi `DestroyImmediate`).

## Windows build machine
Không commit IP. `tools/win.sh` đọc `tools/win.env` (gitignored): `WIN_HOST=ADMIN@<ip>`.
- `D:\Dev\UnityProject\Test_AR\TT_TGOSHI` — checkout cho người dùng mở trong Unity Editor (**Editor có thể đang mở — không kill, không chạy batchmode ở đây**). Cập nhật: `git fetch && git reset --hard origin/main` (giữ Library).
- `D:\Dev\UnityProject\Test_AR\TT_TGOSHI_ci` — checkout cho batchmode (compile, test, build APK).
- Unity: `C:\Program Files\Unity\Hub\Editor\2022.3.17f1\Editor\Unity.exe`.
Mac là nơi code; luồng: sửa trên Mac → commit → push GitHub → Windows fetch/reset → Unity batchmode → kéo log + file .meta mới sinh về Mac → commit.

### tools/win.sh (Mac → Windows)
```
tools/win.sh sync                 # fetch + reset --hard origin/main ở cả 2 checkout, in HEAD
tools/win.sh compile              # batchmode mở+thoát CI, báo lỗi compile
tools/win.sh batch SceneBuilder.Build   # -executeMethod (tên ngắn → ObjectAR.EditorTools.*)
tools/win.sh test                 # EditMode tests, in tổng kết + test fail
tools/win.sh apk [dest.apk]       # Builds/ObjectAR.apk (scp về nếu có dest)
tools/win.sh log [n]              # tail log batch gần nhất
tools/win.sh pull-meta            # kéo file mới/đổi (Assets/ ProjectSettings/ Packages/) từ CI về Mac (.meta, XR settings, scene)
tools/win.sh editor-log [n]       # tail Editor.log của Editor người dùng đang mở
tools/win.sh trigger setup|scene|refresh  # AutoRefresh trong Editor đang mở sẽ chạy lệnh này
```
Batch luôn chạy với `-buildTarget Android`. Editor đang mở có `AutoRefresh` ([InitializeOnLoad]): mỗi ~2s kiểm tra `.git/HEAD|refs/heads/main|ORIG_HEAD` đổi → `AssetDatabase.Refresh()` (cần Editor đã nạp script này một lần — lần đầu phải focus Editor).
Kiểm tra model: `tools/win.sh batch CIChecks.VerifyModels`.

## QR khớp ảnh in (CoreImage ↔ ZXing)
CIQRCodeGenerator chia đoạn tối ưu (`OBJECTAR:Mn:` alphanumeric + phần còn lại byte) → ZXing phải bật `QR_COMPACT` mới ra cùng codeword.
Mask: ZXing chấm điểm khác CoreImage (M5: ZXing chọn 7, ảnh in dùng 0) → `QRMaker` ghim mask của 6 bảng mặc định (bảng `printedMasks`).
Đã kiểm bằng ZXing Java 3.5.1: 6/6 trùng từng ô với `QR-markers/*.png`; test EditMode `QR_MatchesPrintedPng` kiểm lại với ZXing.Net.
Bảng người dùng tạo mới trong Unity in từ `SavePng` nên tự khớp. `QR-markers/` được copy vào gốc project Unity (test đọc `Application.dataPath/../QR-markers`).
