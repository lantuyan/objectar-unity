# Mã QR cho chế độ "QR Marker" (app Object AR)

| File | Bảng | Model hiển thị |
|---|---|---|
| QR-M1-khu-dan-cu.png | Khu dân cư | Nhà phố (trái), nhà cấp 4 (phải), cây (phía trên mã) |
| QR-M2-biet-thu.png | Biệt thự sân vườn | Biệt thự bên phải mã |
| QR-M3-chung-cu.png | Chung cư | Chung cư + 2 cây bên phải mã |
| QR-M4-bang-trong.png | Bảng trống | Chưa có model, dùng để thử editor |
| QR-M5-acer-nitro5.png | Laptop Acer Nitro 5 | Laptop Acer Nitro 5 (tỉ lệ 1:3) bên phải mã |
| QR-M6-low-poly-building.png | Toà nhà low-poly | Toà nhà low-poly bên phải mã |

Cách test:
1. Gửi ảnh sang điện thoại, mở ảnh toàn màn hình, tăng độ sáng tối đa.
2. Đặt điện thoại nằm phẳng trên bàn (cạnh có chữ hướng về phía bạn).
3. Trên iPad mở Object AR → chế độ "QR" → hướng camera vào mã.
4. Nếu model lệch so với mã: Bảng marker → chọn bảng → tab "Mã QR" → "Dùng số này"
   (hoặc đo cạnh ô QR đen bằng thước và nhập).

Sinh lại ảnh: `swift ObjectAR/scripts/make_qr.swift QR-markers`
