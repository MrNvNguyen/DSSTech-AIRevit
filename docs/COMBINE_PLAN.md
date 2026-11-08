# MEPF Smart Combine — kế hoạch hoàn thiện

Nguồn: kế hoạch V2.0 (bổ sung Fitting & Connection Rules) anh gửi ngày 08/11/2026, đối chiếu với code hiện có.
Nguyên tắc giữ nguyên: không viết lại module đang chạy, không thêm ribbon riêng, mỗi bước có self-test và thử trong Revit.

## 1. Hiện trạng (Sprint 0 — rà soát code)

| Thành phần | File | Đang làm được | Thiếu so với kế hoạch |
|---|---|---|---|
| Né nhau (nút **Combine**) | `ModelRevit/CombineCommands.cs` `MepAvoidCommand`, `MepCombine.Detour` | Chọn vùng va chạm, chọn loại phải né, đi dưới/trên, 4 cút mỗi chỗ, gộp các vật cản sát nhau | Phân loại chỉ theo Category (`MepCombine.Kind`: mọi Pipe là "Ống nước"); một khe hở chung; vật cản tính bằng bounding box; không có thứ tự ưu tiên; không preview, không báo PASS/FAIL |
| Nâng sát dầm | `MepRaiseCommand` | Nâng cả mạng nối liền tới đáy dầm/sàn − 50 mm, bỏ qua mạng ống dốc | Khe hở cố định 50 mm; không tính bảo ôn; không kiểm tra va chạm sau khi nâng |
| Tạo phụ kiện khi dựng từ CAD | `MepFromCad.Join / Elbow / Tap`, `HvacFlex` | Cút, tê, chữ thập, côn thu, gót giày ống gió, độ dốc thoát nước | Phụ kiện lấy theo Routing Preferences, không có lớp kiểm tra loại/góc; nhánh thoát nước đang nối bằng tê theo góc CAD (thường 90°), chưa có chữ Y / tê chếch 45° |
| Kiểm tra va chạm | `ClashCommands.cs` | Nhóm A/B, link, BCF, Navisworks, Excel | Chưa dùng chung bảng khe hở theo cặp hệ thống |
| Detour 4 cút | `MepCombine.DetourOnce` | Bỏ qua ống dốc | Với thoát nước (nếu ống phẳng) vẫn tạo cút 90° → phải chặn theo rule |

## 2. Thứ tự ưu tiên mặc định (sửa được theo dự án)

Kết cấu trước tiên → **Thoát nước (DR)** → **Ống gió chính (DCT)** → **Điện chính / máng cáp (ECT)** → **PCCC (FP)** → **Nước lạnh / ĐHKK (CHW)** → **Cấp nước (CW)** → **Gas: Special** (hành lang riêng, không tự dời).

Hệ thống ưu tiên thấp né hệ thống ưu tiên cao. Ràng buộc kỹ thuật (độ dốc, hành lang gas, vùng phun sprinkler, góc phụ kiện) không bị thứ tự ưu tiên ghi đè.

## 3. Lộ trình theo bước nhỏ

Mỗi bước là một PR, chạy được ngay trong các nút hiện có.

| Bước | Nội dung | Vị trí code | Kết quả người dùng thấy |
|---|---|---|---|
| **C1. Phân loại hệ thống** | `MepSystemCode` 7 mã (DR, DCT, ECT, FP, CHW, CW, GAS) + Conduit; đọc theo System Classification, System Type, tên hệ thống, abbreviation, rồi Category. Không đọc được → REVIEW | Core: `Dss.Cad2Bim.Core/CombineRules.cs` (không phụ thuộc Revit); Revit: thay `MepCombine.Kind` | Hộp "Loại phải né" liệt kê đúng 7 hệ thống thay vì "Ống nước" chung |
| **C2. Profile ưu tiên + khe hở theo cặp** | JSON `%APPDATA%\DSSTech\combine-profile.json` (Default → Company → Project); bảng Pair Clearance (song song / giao cắt); kích thước tính cả bảo ôn | Core + cửa sổ Combine | Combine tự chọn hệ né theo ưu tiên; khe hở lấy theo cặp; Nâng sát dầm dùng khe hở của profile |
| **C3. Fitting & Connection Rules** | `FittingRule`, `ConnectionRule`, chuẩn hóa góc (góc đổi hướng và góc danh nghĩa); DR: cấm cút 90° ngang và tê vuông nhánh khi rule bắt buộc; tra Routing Preferences trước khi tạo; kiểm tra connector thật sự đã nối | Core rules + `MepFromCad.Join/Elbow`, `MepCombine.DetourOnce` | Thoát nước dùng chữ Y / tê chếch 45°, cút 45° (135°); không có phụ kiện đúng thì báo REVIEW, không tự thay |
| **C4. Preview & báo cáo** | Phân tích → bảng PASS / WARNING / FAIL / REVIEW → xác nhận → Apply trong một Transaction, kiểm tra sau Apply, lỗi thì rollback | Cửa sổ Combine mới (thay hộp thoại đơn giản) | Xem trước, chọn dòng để zoom, xuất Excel |
| **C5. Combine theo vùng** | Phạm vi: đang chọn / view / level; xếp tầng cao độ theo ưu tiên trong hành lang; giữ điểm neo (thiết bị, ống đứng, đầu nối), giữ độ dốc DR | `MepCombine` + Core route planner | Combine cả hành lang thay vì từng cặp |
| **C6. Hoàn thiện** | Khóa tuyến (Lock Position / Elevation / Route), hành lang gas, vùng phun sprinkler, không gian bảo trì | — | — |

### Trạng thái (08/11/2026)

Đã làm, chờ thử trong Revit:
- Core `Dss.Cad2Bim.Core/CombineRules.cs`: phân loại, profile JSON, khe hở theo cặp, chọn hệ né, kiểm tra cút/nhánh, đo khe hở song song / giao cắt.
- `MepRoutePlanner.DrainFittings`: thoát nước dựng từ CAD có chữ Y 45° và 2 cút 45°.
- Nút **Combine** mở cửa sổ mới `ModelRevit/CombineSmart.cs` (Phân tích, Xem, Áp dụng có kiểm tra sau dựng và hoàn tác từng tuyến, Xuất Excel; thứ tự ưu tiên và khóa sửa ngay trong cửa sổ).
- Nâng sát dầm dùng khe hở và bảo ôn của profile, bỏ qua mạng bị pin / hệ khóa.

- Phân loại đọc mã hệ của công ty trước (`MepSystemClassifier.CatalogCodes`: GAS = gas lạnh, CWS/HWS = cấp nước, FDS = thoát nước).
- Thứ tự ưu tiên và khóa theo **mã hệ của công ty** (`CombineProfile.DefaultPriority`: BWS, GWS… SAD, RAD… CWS, HWS, HWR), thêm LPG (gas đốt) và CND (ống luồn điện) ngoài danh mục. Nhóm DR/DCT/ECT/FP/CHW/CW chỉ còn dùng cho khe hở và quy tắc phụ kiện. File quy tắc bản 1.0 tự chuyển: nhóm → các mã của nhóm, "GAS" cũ (gas đốt) → LPG.
- Chế độ **Xếp tầng hành lang** (`CorridorStacker`): mạng ưu tiên cao sát dầm, mạng sau nằm dưới mạng ưu tiên cao hơn mà nó gặp trong mặt bằng (cách khe hở của cặp), máng cáp chừa 150 mm phía trên; thoát nước dốc / hệ khóa / mạng pin / mạng nối ngang vào thiết bị giữ nguyên; xuống dưới «đáy thấp nhất» thì REVIEW. Cả mạng dời cùng nhau, ống đứng co giãn theo; mất kết nối thì hoàn tác.

Chưa làm:
- Tự dời ngang tuyến song song (hiện chỉ báo WARNING).
- Không gian bảo trì thiết bị.

## 4. Kiểm thử

- Self-test Core (`tests/cad2bim-section-smoke`): phân loại (TC01–03), chọn hệ né theo ưu tiên (TC06), góc phụ kiện DR (TC11, TC15–16), thiếu rule → REVIEW (TC12).
- Thử trong Revit 2025 (anh): chạy Combine hai lần không tạo trùng (TC24), Undo một bước, ống dốc giữ nguyên.

## 5. Chưa chốt (cần anh duyệt)

- Giá trị khe hở mặc định từng cặp hệ thống (kế hoạch yêu cầu có nguồn; tham khảo TCVN 9207:2012 mục 6.16: cáp điện song song đường gas ≥ 400 mm).
- Thư viện family phụ kiện thoát nước của công ty (chữ Y, tê chếch 45°, cút 45°) để gán vào Routing Preferences.
