# ObjectAR (Unity)

Bản port sang Unity của app iOS **ObjectAR** (SwiftUI + ARKit + SceneKit): in mã QR, đưa camera vào mã → mô hình 3D
(nhà, cây, laptop…) hiện ra quanh mã theo bố cục của từng "bảng marker". Có trình chỉnh bảng (sơ đồ 2D, kéo thả model,
đổi tỉ lệ/xoay, đổi nội dung & kích thước mã, xuất PNG mã QR). Chạy được trên Android (ARCore) và iOS (ARKit).

- Unity **2022.3.17f1**, Built-in Render Pipeline, Android tối thiểu API 24, IL2CPP ARM64, OpenGLES3
- Hợp đồng kỹ thuật chi tiết (API, hệ toạ độ, hành vi phải giữ như bản Swift): [`docs/PORTING.md`](docs/PORTING.md)

## Packages

| Package | Phiên bản | Dùng cho |
|---|---|---|
| AR Foundation / ARCore / ARKit | 5.2.2 | nhận ảnh (mã QR) |
| XR Plug-in Management | 4.5.4 | bật ARCore/ARKit loader |
| glTFast (`com.unity.cloud.gltfast`) | 6.14.1 | nhập model `.glb` lúc editor |
| Newtonsoft Json | 3.2.1 | `markers.json` (cùng định dạng bản Swift) |
| ZXing.Net (DLL trong `Assets/Plugins/ZXing`) | 0.16.9 | sinh & đọc mã QR |

UI dựng bằng code với uGUI + font Arial có sẵn (`LegacyRuntime.ttf`), không dùng TextMeshPro.

## Chạy trong Editor (chế độ giả lập)

1. Mở project bằng Unity Hub (2022.3.17f1). Lần đầu nếu cần: menu **ObjectAR → Setup Project**, rồi **ObjectAR → Build Main Scene**.
2. Mở `Assets/Scenes/Main.unity`, bấm **Play**.
3. Editor không có thiết bị AR → sau ~1 giây app tự vào **chế độ giả lập**: bảng **M1** hiện ra ở gốc như vừa quét được mã.
   - Phím **1–9**: "quét" bảng thứ n.
   - Nút **Quét lại**: chuyển lần lượt M1 → M2 → … để xem mọi model.
   - **Chuột phải kéo**: xoay camera quanh bàn; **cuộn**: zoom.
   - Khi đang chỉnh bảng: **chuột trái** chạm chọn / kéo model, **Shift + cuộn** đổi tỉ lệ, **Ctrl/Cmd + cuộn** xoay.

Các bảng mặc định M1–M6 tương ứng mã in sẵn trong `QR-markers/` (in ra giấy để dùng với điện thoại).

## Build Android

- Trong Editor: menu **ObjectAR → Build Android APK** → `Builds/ObjectAR.apk`.
- Dòng lệnh / máy build: `tools/win.sh apk Builds/ObjectAR.apk` (xem dưới).
- Build chạy `ProjectSetup` trước (PlayerSettings, ARCore bắt buộc, shader luôn kèm), thiếu scene thì tự dựng.

iOS: menu **ObjectAR → Build iOS Xcode project** → `Builds/iOS`, cần máy có cài iOS Build Support và Mac + Xcode để ký/chạy.

## Quy trình Mac → GitHub → Windows

Code được viết trên Mac; máy Windows có Unity để compile, chạy test, build APK. `tools/win.sh` điều khiển máy Windows qua SSH,
đọc cấu hình từ `tools/win.env` (không commit, chứa `WIN_HOST=USER@host`).

```
sửa code trên Mac → git commit → git push (GitHub)
tools/win.sh sync        # Windows: git fetch + reset --hard origin/main ở cả 2 checkout
tools/win.sh compile     # batchmode trên checkout CI, báo lỗi compile
tools/win.sh test        # EditMode tests
tools/win.sh preview DIR # render ảnh kiểm tra hệ toạ độ từng bảng (cần GPU) và chép về DIR
tools/win.sh apk Builds/ObjectAR.apk
tools/win.sh pull-meta   # kéo .meta / scene / settings Unity mới sinh về Mac để commit
tools/win.sh trigger refresh   # Editor đang mở trên Windows tự Refresh (AutoRefresh)
```

Máy Windows có 2 checkout: một cho người dùng mở Editor (chỉ được fetch/reset, không chạy batchmode), một dành riêng cho batchmode.

## Cấu trúc

```
Assets/ObjectAR/
  Scripts/Runtime/   ObjectAR.Runtime — dữ liệu bảng (Markers), QRMaker, HouseCatalog/ModelInstance, BoardNode,
                     MarkerManager (.AR: luồng AR Foundation, .Input: cử chỉ + giả lập)
  Scripts/UI/        ObjectAR.UI — ObjectARUI (màn AR), BoardListSheet, MarkerEditorPanel, BoardLayoutEditor + BoardMap
  Editor/            ProjectSetup, SceneBuilder, BuildScript, CIChecks (VerifyModels, RenderPreview), AutoRefresh
  Resources/Models/  model .glb
  Tests/EditMode/    test khói (model, JSON, QR khớp ảnh in, đổi hệ toạ độ)
Assets/Plugins/ZXing/   zxing.dll
Assets/Scenes/Main.unity   dựng bởi SceneBuilder
Assets/link.xml         giữ ZXing / Newtonsoft khỏi IL2CPP stripping
QR-markers/             PNG mã QR để in (từ app iOS)
docs/PORTING.md         hợp đồng port
tools/win.sh            điều khiển máy build Windows
```

## Hạn chế đã biết

- **Tự ước lượng kích thước mã (AutoScale)**: AR Foundation không có tương đương `automaticImageScaleEstimationEnabled`
  của ARKit; công tắc chỉ khởi động lại phiên AR, kích thước ước tính lấy từ `ARTrackedImage.size`.
- **Mask QR được ghim**: ZXing chọn mask khác CoreImage (vd M5), nên `QRMaker` ghim mask cho 6 mã mặc định để ảnh tham chiếu
  trùng từng ô với ảnh đã in. Mã mới tạo trong Unity phải in từ PNG do app xuất (nút "PNG").
- **iOS**: chỉ build ra project Xcode; cần Unity có iOS Build Support và một máy Mac để build/ký.
- Font Arial không có emoji nên UI không hiện icon model; trình chỉnh sơ đồ không có xem 3D thu nhỏ và cử chỉ 2 ngón
  (thay bằng thanh trượt + nút zoom).
