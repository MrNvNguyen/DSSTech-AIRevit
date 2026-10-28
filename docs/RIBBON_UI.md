# DSSTech ribbon — quy ước giao diện

Phong cách **A – Kỹ sư xanh–vàng** (chốt 2026-10-27).

## Màu

| Vai trò | Mã màu |
|---|---|
| Nét chính (navy) | `#1B3A5C` |
| Điểm nhấn (amber) | `#E9A23B` |
| Trạng thái bật | `#1F9D55` |
| Trạng thái tắt | `#8A949E` |

Icon: nét navy dày 2px trên khung 32×32, mỗi icon có đúng một mảng vàng. Có cả bản 32px và 16px trong `src/DSSTech.Revit/Assets/`.
Icon các bước CAD → BIM có số ①②③ trên bản 32px (bản 16px bỏ số vì quá nhỏ).

## Nhãn song ngữ

- **Nút to:** 2 dòng, `Tiếng Việt\nEnglish`, mỗi dòng nên ≤ 16 ký tự.
- **Nút nhỏ xếp chồng:** chỉ tiếng Việt; tên tiếng Anh nằm đầu tooltip, ví dụ "Tham số QS (QS Parameters) — …".
- **Mục trong danh sách xổ:** một dòng `Việt · English`.
- **Tooltip:** `ToolTip` = mô tả tiếng Việt; `LongDescription` = tiếng Anh (hiện khi giữ chuột lâu).
- **F1:** mọi nút trỏ tới `Help/HuongDan.html#<mục>`.

Bề rộng đo trên bản phác thảo: khoảng 1.035 px, vừa màn hình laptop 1366 px. Nếu ghi song ngữ cả nút nhỏ thì khoảng 1.185 px, vẫn vừa nhưng chật.

## Bố cục

| Panel | Nút |
|---|---|
| Thiết lập · Setup | Bảng điều khiển (to) · [Tham số QS / Tài khoản / Hướng dẫn] |
| CAD → BIM | [① Link mặt bằng / ② Trục & tầng] · ③ Dựng mô hình (to) · Dựng theo lô ▾ (Kiểm tra CAD, Kết cấu, Kiến trúc, Cơ điện) |
| Khối lượng · Quantity | Chọn định mức · Ván khuôn · Xuất BOQ · Bảng khối lượng |
| Trợ lý AI · AI | AI: Đang bật/Đã tắt (đổi icon + chữ theo trạng thái thật) · Kết nối AI |

Tên nội bộ (ID) của các nút cũ giữ nguyên. Riêng tên panel đã đổi, nên phím tắt người dùng tự gán cho nút cũ có thể phải gán lại.
