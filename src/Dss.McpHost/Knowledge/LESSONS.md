# DSSTech — sổ tay lỗi đã gặp (CAD → Revit, MEP, Combine)

Mỗi mục: **Lỗi** đã gặp trên dự án thật → **Vì sao** → **Quy tắc** phải giữ. Áp dụng cho mọi trường hợp tương tự,
không chỉ bản vẽ đã gặp. Khi dựng model qua MCP (`create_element`, `create_batch`) cũng giữ đúng các quy tắc này.
Mã chủ đề trong ngoặc vuông dùng cho `get_lessons {"topic": "..."}`.

## [cad-read] Đọc bản vẽ CAD

- **Lỗi:** nét thiếu hoặc thừa giữa các lần chạy. **Vì sao:** hình học ImportInstance của Revit thay đổi giữa các lệnh.
  **Quy tắc:** đọc thẳng file DWG (ACadSharp). Revit khoá file DWG đang link: mở bằng `FileShare.ReadWrite`
  (hoặc copy ra file tạm), không mở độc quyền.
- **Lỗi:** polyline kín mất cạnh cuối; cung tròn trong polyline thành đường thẳng.
  **Quy tắc:** polyline kín đọc lại cạnh nối điểm cuối về điểm đầu; giữ bulge của từng đoạn (cung = bulge ≠ 0).
- **Lỗi:** nét trong block nằm sai layer. **Quy tắc:** nét layer 0 trong block thuộc layer của block insert.
- **Lỗi:** đường nét đứt (linetype) đọc thành nhiều đoạn rời. **Quy tắc:** nối các nét thẳng hàng cùng layer, khe ≤ 400 mm.
  **Nhưng** không áp dụng cho thang cáp: thanh ngang của hai thang đặt cạnh nhau thẳng hàng, nối lại sẽ thành một nét giả.
- **Lỗi:** hatch không đọc được, nét hatch bị coi là cạnh ống. **Quy tắc:** loại nét song song cách đều dày đặc (nét hatch).

## [cad-text] Chữ trên CAD

- **Lỗi:** nhãn "THANG CÁP 1200X100, H=2800" không được đọc → size sai, cao độ sai.
  **Vì sao:** bộ lọc nhãn bỏ chữ > 24 ký tự. **Quy tắc:** chữ dài có size (600x400, D100, DN50, Ø200), độ dốc (i=1%)
  hoặc cao độ (H=…) phải được giữ cho MEP (`CadGeometry.NoteTexts`), tách khỏi nhãn kết cấu để không gán nhầm làm mark dầm/cột.
- **Lỗi:** nhãn miệng gió (SAG 1200x150, EAG+OBD 450x450) bị lấy làm size ống gió. **Quy tắc:** bỏ nhãn của miệng gió / louver.
- **Lỗi:** "H=15M", "H=100M" (cột áp bơm) bị đọc thành cao độ. **Quy tắc:** cao độ là H=/BOT số 3–5 chữ số (mm) hoặc
  số thập phân có đơn vị m (H=+2.80); số nguyên kèm "M" là cột áp, bỏ.
- **Lỗi:** hai nhãn chồng nhau cạnh hai tuyến song song bị gán cùng một tuyến. **Quy tắc:** nhãn chồng ghép theo thứ tự:
  nhãn trên (hoặc trái) ↔ tuyến trên (hoặc trái); size đi kèm nhãn đó.
- Nhãn gán cho tuyến gần nhất (tính từ mép, không từ tim), không vượt quá đầu tuyến; tuyến không nhãn lấy size / cao độ
  của tuyến liền trước (qua cút, nối thẳng, thẳng qua tê, cuối cùng mới vào nhánh).

## [mep-elevation] Cao độ

- Dùng đúng tên của Revit: Đáy = Bottom Elevation (máng: Lower End Bottom Elevation), Tâm = Middle Elevation,
  Đỉnh = Top Elevation (máng: Upper End Top Elevation). Máng cáp, ống gió, ống nước đều có đáy/tâm/đỉnh, không gọi là "ống".
- Có cao độ ghi trên CAD thì dùng cho đoạn đó (mặc định H = cao độ đáy, hỏi người dùng nếu bản vẽ quy ước khác);
  đoạn không ghi mới dùng cao độ tự nhập / theo đáy dầm.
- Hai tuyến có thể chung vị trí mặt bằng nhưng khác cao độ (bản vẽ xuất từ model 3D): giao nhau trên mặt bằng ≠ nối với nhau.
  Chỉ nối khi đầu tuyến chạm thân / đầu tuyến kia.
- Hai đoạn khác cao độ gặp nhau tại một nút: đoạn cao hạ xuống gần nút (đoạn đứng + 2 cút), không để hở.
- Thoát nước không lấy cao độ ghi trên CAD: tính lùi từ điểm xả theo độ dốc.

## [mep-tray] Máng / thang cáp

- **Lỗi:** thang cáp chạy ngang, size 250 sai. **Vì sao:** các thanh ngang (cách 300 mm) bị đọc thành hai cạnh máng.
  **Quy tắc:** thang vẽ thật (ray + thanh ngang): ray là dải 2 nét cách ~25 mm; hai ray thuộc một máng khi có
  thanh ngang nối từ ray này sang ray kia, cách đều ≤ 700 mm suốt chiều dài; tim máng ở giữa, bề rộng = mép ngoài hai ray.
- **Lỗi:** chỗ tê, chữ thập, đổi cao độ ra hàng loạt mẩu máng chồng nhau. **Quy tắc:** bỏ đoạn thang < 600 mm hoặc < bề rộng,
  đoạn nằm phần lớn trên một đoạn dài hơn cùng phương, đoạn ngắn nằm trọn trong bề rộng một máng khác, mẩu đứng riêng
  < 2000 mm không có nhãn (là nét của phụ kiện).
- Service Type của máng ghi mã hệ (BW, MV, LV, ELV, FA). Không áp dụng bộ lọc thiết bị (block) cho máng: cút vẽ bằng block.

## [mep-duct] Ống gió (vẽ 2 nét)

- Hai cạnh song song → một tim; tim nằm giữa hai cạnh (không phải đường tim vẽ thêm ở giữa).
- Cạnh chung của hai hộp liền nhau, nét ngang miệng cút tròn là mối nối, không phải cạnh.
- Cút vẽ tròn: lấy bán kính theo bản vẽ. Góc cút làm tròn về số độ nguyên (Revit không có cút 58.37°).
- Thiết bị trên layer ống (miệng gió, FCU, VAV, louver ≤ 3200 mm) đặt bằng công cụ thiết bị, không dựng thành ống;
  ô ngắn đứng riêng không ghi size (lỗ thông gió qua tường) bỏ qua. Layer ống mềm (FLEX, ỐNG MỀM) để công cụ nối ống mềm.
- Côn thu lệch tâm: nối bằng transition giữa hai đầu, mỗi ống giữ đúng tuyến của nó.

## [mep-pipe] Ống nước / PCCC / thoát nước

- Ống vẽ 1 nét: ký hiệu van / phụ kiện (block) bỏ đi, tuyến liền qua chỗ đó; nhánh dừng sát ống chính vẫn nối vào.
- Thoát nước: dốc về điểm xả (i=..% ghi trên tuyến, không có thì theo mặc định); nhánh vào ống chính bằng chữ Y / tê chếch 45°,
  góc vuông thành 2 cút 45° (TCVN 4474:1987 điều 5.14); thoát nước không tự đổi tuyến lên xuống (mất dốc, tạo võng).

## [systems] Mã hệ thống

- Dùng mã hệ của công ty (danh mục màu hệ thống): SAD, RAD, OAD, FAD, EAD, KEAD, TEAD, SEAD, PAD; CHWS, CHWR, CTS, CTR, EP,
  CD, **GAS = ống gas lạnh (cấp và hồi)**; BW, MV, LV, ELV, FA; CWS, HWS, HWR, BWS, GWS, KP, RWP, VP; SP, FFH, FDR, FFD, FFP, FDS.
  Tên System Type dạng "ABBR - NameEn". Không tự đặt mã khác.
- Gas đốt (LPG) không có trong danh mục: mã LPG. Ống luồn điện: CND. Không gọi gas đốt là "GAS".
- Đọc mã theo: abbreviation → từ đầu tên System Type ("SAD - Supply Air", "M-SAD-…") → Service Type (máng) → tên; sau đó mới
  đoán theo từ khóa / classification. "Cấp nước lạnh" là cấp nước sinh hoạt (CWS), không phải nước lạnh Chiller.

## [combine] Combine / né va chạm

- Hệ ưu tiên thấp né hệ ưu tiên cao, theo thứ tự mã hệ trong quy tắc (mặc định thoát nước → ống gió → máng cáp → PCCC →
  Chiller/gas lạnh → cấp nước → CND → LPG). Ràng buộc kỹ thuật không bị thứ tự ưu tiên ghi đè: thoát nước giữ dốc,
  LPG khóa, phần tử bị pin không dời.
- Khe hở theo cặp hệ (gas đốt song song cáp điện ≥ 400 mm, TCVN 9207:2012 6.16); kích thước tính cả bảo ôn.
- Luôn: phân tích → xem trước → áp dụng; mỗi tuyến một transaction, kiểm tra lại kết nối sau khi dựng, mất kết nối thì hoàn tác
  tuyến đó và báo REVIEW. Dời cả mạng nối liền cùng lúc; ống đứng co giãn theo; mạng nối ngang vào thiết bị không tự dời.

## [revit-api] Revit API

- Fitting có thể bị Revit từ chối (đoạn quá ngắn, thiếu Routing Preferences): tạo ống trong một transaction, fitting theo từng
  lô riêng để lỗi một fitting không hoàn tác cả lệnh; ghi lỗi vào log.
- Sau khi một lô bị rollback, object Element đọc trước đó không còn hợp lệ: giữ ElementId, lấy lại bằng `doc.GetElement`.
- Không dò dầm/sàn cho từng điểm trên toàn model: chia ô mặt bằng rồi chỉ thử phần tử trong ô (bản vẽ lớn treo Revit).

## [debug] Khi kết quả sai

- Đọc `%LOCALAPPDATA%\DSSTech\mep-cad-input.txt` (nét và chữ đã đọc), `%LOCALAPPDATA%\DSSTech\mcp-host.log`
  (dòng "mep fitting:") và journal Revit mới nhất trước khi đoán từ ảnh chụp.
- Người dùng gửi bằng nút **Góp ý / Feedback** (zip có log, ảnh, file dump).
