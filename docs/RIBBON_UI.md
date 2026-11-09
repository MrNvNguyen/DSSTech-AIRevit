# DSSTech ribbon — quy ước giao diện

Phong cách **A – Kỹ sư xanh–vàng** (chốt 2026-10-27).

## Màu

| Vai trò | Mã màu |
|---|---|
| Nét chính (navy) | `#1B3A5C` |
| Điểm nhấn (amber) | `#9A5B00` (sáng) · `#E9A23B` (tối) |
| Trạng thái bật | `#14803F` (sáng) · `#4CC784` (tối) |
| Trạng thái tắt | `#5F6B76` (sáng) · `#B4BEC8` (tối) |

Icon: nét navy dày 2px trên khung 32×32, mỗi icon có đúng một mảng vàng. Có cả bản 32px và 16px trong `src/DSSTech.Revit/Assets/`.
Icon module do `scripts/generate-module-icons.ps1` tạo. Màu trên đạt tương phản tối thiểu 3:1 với nền ribbon ở cả hai chế độ (ước lượng nền sáng `#F0F0F0`, nền tối `#3B4453`); chữ nhãn do Revit tự vẽ theo giao diện.

## Giao diện tối (Revit 2024+)

Mỗi icon có thêm bản `*-32-dark.png` / `*-16-dark.png`: nét `#D6E2EE` (xám xanh sáng) thay cho navy, mảng vàng dùng màu tối ở bảng trên.
`RibbonIcons.Load` tự chọn bản tối khi `UIThemeManager.CurrentTheme == UITheme.Dark`; `RibbonState.RefreshTheme()` chạy lại khi người dùng đổi theme (sự kiện `ThemeChanged`) nên không cần khởi động lại Revit.

## Nhãn song ngữ

- **Nút to:** 2 dòng, `Tiếng Việt\nEnglish`, mỗi dòng nên ≤ 16 ký tự.
- **Nút nhỏ xếp chồng:** chỉ tiếng Việt; tên tiếng Anh nằm đầu tooltip, ví dụ "Tham số QS (QS Parameters) — …".
- **Mục trong danh sách xổ:** một dòng `Việt · English`.
- **Tooltip:** `ToolTip` = mô tả tiếng Việt; `LongDescription` = tiếng Anh (hiện khi giữ chuột lâu).
- **F1:** mọi nút trỏ tới `Help/HuongDan.html#<mục>`.

## Bố cục

| Panel | Nút |
|---|---|
| Thiết lập · Setup | [Giới thiệu / Tài khoản / Hướng dẫn] · [Tham số QS / Góp ý] |
| Kiến trúc · Architecture | [Trục / Level] · Tường (to) · [Cửa / Cửa sổ] · [Khớp kết cấu / Lanh tô / Lỗ mở] · [Phòng / Hoàn thiện / Trần] |
| Kết cấu & móng · Structure | [Cột / Vách / Dầm] · [Sàn / Móng bè / Móng băng] · [Cọc / Đài, móng đơn] — đài và móng đơn chung một nút (type theo tên trên CAD) |
| MEPF | ĐHKK ▾ · Điện ▾ (Máng / thang cáp · Busway) · Nước ▾ · PCCC ▾ (to) · [Hệ thống / Thiết bị tường ▾ / Thiết bị trần ▾] |
| Combine | Combine · Clash Check (to) · [So sánh / Nâng sát dầm / Xung đột] |
| Khối lượng · Quantity | Chọn định mức (to) · [Ván khuôn / Xuất BOQ / Bảng khối lượng] |
| Tài sản · Assets | Tài sản (Asset Data): Form BIM Excel + catalogue PDF → tham số Type `TS_*`, ghi sau khi xác nhận |
| Trợ lý AI · AI | AI: Đang bật/Đã tắt (đổi icon + chữ theo trạng thái thật) · Kết nối AI |

Nút to chỉ dành cho công cụ chính của mỗi panel; công cụ phụ là nút nhỏ xếp chồng **có tên**. Tổng bề rộng ribbon phải vừa
màn hình 1920 px, nếu không Revit tự ẩn chữ của nút nhỏ (chỉ còn icon, khó nhận ra).

Mỗi nút module mở cửa sổ Dựng mô hình đúng loại đó, không có ô Loại và ô Layer (layer lấy từ nét pick trên bản vẽ); Bảng điều khiển vẫn mở cửa sổ đầy đủ như trước. Nút Link mặt bằng, Dựng mô hình và Dựng theo lô đã gỡ khỏi ribbon, lệnh phía sau giữ nguyên. Level chưa có logic dựng.

**Bản quyền:** mọi nút cần key đã kích hoạt (Thiết lập ▸ Tài khoản), trừ Giới thiệu, Tài khoản và Hướng dẫn. Chưa có key thì các nút mờ và AI không mở cổng. Key do `tools/DSSTech.KeyGen` cấp, ký bằng khoá bí mật của DSSTech (không nằm trong repo).

Tên nội bộ (ID) của các nút cũ giữ nguyên. Riêng tên panel đã đổi, nên phím tắt người dùng tự gán cho nút cũ có thể phải gán lại.
