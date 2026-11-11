# Nhận góp ý qua Google Apps Script

Nút **Thiết lập ▸ Góp ý** gửi nội dung + file zip (ảnh, log, file đọc CAD, journal) lên script này.
Script lưu zip vào thư mục Drive **DSSTech-GopY** và gửi mail về tài khoản chủ script.
Plugin không chứa mật khẩu mail nào; chỉ biết địa chỉ web app.

## Triển khai (một lần)

1. Vào <https://script.google.com> bằng tài khoản nhận góp ý ▸ **New project**, đặt tên "DSSTech Góp ý".
2. Xoá nội dung `Code.gs`, dán toàn bộ file [Code.gs](Code.gs) ▸ Save.
3. **Deploy ▸ New deployment** ▸ chọn loại **Web app**:
   - Execute as: **Me**
   - Who has access: **Anyone**
4. Bấm Deploy, cấp quyền (Gmail gửi mail, Drive tạo file). Google cảnh báo "app chưa xác minh" vì là script của chính mình:
   Advanced ▸ Go to … (unsafe) ▸ Allow.
5. Chép **Web app URL** (dạng `https://script.google.com/macros/s/…/exec`) vào `FeedbackWindow.Endpoint`
   trong `src/DSSTech.Revit/FeedbackCommand.cs`, rồi build lại.

Thử nhanh trên một máy mà không build: ghi URL vào `%LOCALAPPDATA%\DSSTech\feedback-url.txt` (file này ưu tiên hơn URL trong code).

Sửa script sau này: **Deploy ▸ Manage deployments ▸ Edit ▸ Version: New version**, để URL giữ nguyên.

## Giới hạn

- Zip quá 20 MB: plugin không gửi lên mà quay về cách cũ (mở mail soạn sẵn + file trên Desktop).
- Mail kèm zip khi ≤ 20 MB; lúc nào cũng có link file trên Drive.
- Gmail thường giới hạn khoảng 100 mail/ngày cho script.
- Gửi lỗi (mất mạng, script lỗi) → plugin quay về cách cũ, zip vẫn ở Desktop.
