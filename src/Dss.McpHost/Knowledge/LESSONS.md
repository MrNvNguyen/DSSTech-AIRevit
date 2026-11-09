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
- **Lỗi:** "BỆ MÁY H 200" (chữ trên xref kiến trúc) bị đọc thành cao độ 200 → ống gió dựng sát sàn.
  **Quy tắc:** H phải đi kèm "=", ":" hoặc "+" (H=2800, H:2800, H+2.80); "H 200" trần không phải cao độ. Chữ nói về
  bộ phận công trình (BỆ, ĐẾ, PAD, CỬA, BỂ, HỐ, SILL) không phải cao độ tuyến. Không có cao độ ghi đúng thì dùng
  cao độ tự nhập / theo đáy dầm.
- **Lỗi:** bản vẽ điện ghi cao độ máng dạng "FFL=+3.0M" (tính từ sàn hoàn thiện) → không được đọc. **Quy tắc:** FFL
  kèm "=", ":" hoặc "+" là cao độ tuyến; đơn vị M là mét kể cả số nguyên (FFL=+3M = 3000). Cao độ sàn tầng
  (FFL+16.150) vượt 15 m nên bị bỏ.
- **Lỗi (nguy cơ):** bản vẽ ĐHKK có xref điện, nhãn "THANG CÁP 1200X100, H=2800", "BUSWAY … H=3300" nằm cạnh ống gió.
  **Quy tắc:** nhãn cao độ chỉ gán cho tuyến đúng loại (nhãn có THANG/MÁNG CÁP, BUSWAY chỉ cho máng; có ỐNG GIÓ / DUCT
  chỉ cho ống gió) và đúng size (nhãn ghi size thì bề rộng / đường kính tuyến phải khớp trong 10 % hoặc 20 mm).
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
  **Nhưng** ống gió khác chiều cao đặt cùng đáy (hoặc cùng đỉnh) thì tâm lệch nhau là bình thường: nối bằng côn/tê,
  không tạo đoạn đứng (lỗi cũ: đoạn ống gió đứng 50 mm thừa ở chỗ đổi tiết diện). Chỉ hạ khi một đoạn có cao độ ghi trên CAD.
- Thoát nước không lấy cao độ ghi trên CAD: tính lùi từ điểm xả theo độ dốc.

## [mep-tray] Máng / thang cáp

- **Lỗi:** thang cáp chạy ngang, size 250 sai. **Vì sao:** các thanh ngang (cách 300 mm) bị đọc thành hai cạnh máng.
  **Quy tắc:** thang vẽ thật (ray + thanh ngang): ray là dải 2 nét cách ~25 mm; hai ray thuộc một máng khi có
  thanh ngang nối từ ray này sang ray kia, cách đều ≤ 700 mm suốt chiều dài; tim máng ở giữa, bề rộng = mép ngoài hai ray.
- **Lỗi:** chỗ tê, chữ thập, đổi cao độ ra hàng loạt mẩu máng chồng nhau. **Quy tắc:** bỏ đoạn thang < 600 mm hoặc < bề rộng,
  đoạn nằm phần lớn trên một đoạn dài hơn cùng phương, đoạn ngắn nằm trọn trong bề rộng một máng khác, mẩu đứng riêng
  < 2000 mm không có nhãn (là nét của phụ kiện).
- **Lỗi:** chỗ hai máng giao nhau cùng cao độ không có phụ kiện (chỗ có chỗ không). **Vì sao:** nét phụ kiện vẽ ở
  chỗ giao bị bỏ cùng các nét ngắn, hai tuyến chỉ còn cắt nhau trên mặt bằng. **Quy tắc:** máng cắt nhau cùng cao độ
  (hoặc một bên chưa ghi cao độ) thì nối: đầu tự do vượt qua máng kia ≤ bề rộng máng kia → tê; hai tuyến chạy tiếp
  → chữ thập. Khác cao độ thì để đi chồng, không nối.
- **Lỗi:** hai thang 1200 song song (2800 / 3100) đổi cao độ cho nhau ở góc cua → đoạn đứng 300 mm, Revit báo không đủ chỗ
  cho phụ kiện. **Vì sao:** người vẽ đặt cặp nhãn chồng không theo một thứ tự cố định. **Quy tắc:** một máng không đổi
  cao độ với máng bên cạnh ở góc cua; khi các cặp nhãn mâu thuẫn, chọn cách gán giữ cao độ liền mạch qua các nút nối,
  theo số đông các cặp nhãn.
- **Busway** vẽ 1 nét (polyline có global width, ví dụ 150), không vẽ tiết diện: tool riêng (Điện ▸ Busway), đọc single
  line theo tim nét (chỉ nét hở ngoài block; có nét có bề rộng thì chỉ lấy các nét đó), kích thước do người dùng nhập cho
  mọi đoạn, Service Type BW. Nhãn "Busway … H=3300" chỉ gán cho busway; máng, ống gió, ống nước không nhận nhãn busway,
  busway không nhận nhãn máng / ống gió. Tool máng không đổi.
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

## [structure] Kết cấu, móng

- **Lỗi:** đài / móng đơn ghi "ĐA-1", "ĐA-2" bị bỏ hết ("no pile-cap Type Mark label"). **Vì sao:** mã móng chỉ nhận
  Đ + số (Đ4, Đ2A). **Quy tắc:** mã móng gồm Đ + số (+ chữ), Đ + chữ + số (ĐA-1, ĐC2), M / MĐ / F + số (M1, MĐ-2, F3);
  không nhận D1 (dầm), C1 (cột). Đài cọc và móng đơn dùng chung một tool: type theo Type Mark trùng tên trên CAD,
  không có tên thì dùng type chọn trong cửa sổ.
- **Lỗi:** pick trúng nét Defpoints (nằm đè lên viền móng) → "Không suy ra cấu kiện trên layer 'Defpoints'".
  **Quy tắc:** layer pick được không ra cấu kiện thì thử các layer khác có nét ngay tại điểm pick (≤ 300 mm, gần nhất
  trước, bỏ Defpoints), ghi log layer đã dùng.

## [revit-api] Revit API

- Fitting có thể bị Revit từ chối (đoạn quá ngắn, thiếu Routing Preferences): tạo ống trong một transaction, fitting theo từng
  lô riêng để lỗi một fitting không hoàn tác cả lệnh; ghi lỗi vào log.
- Sau khi một lô bị rollback, object Element đọc trước đó không còn hợp lệ: giữ ElementId, lấy lại bằng `doc.GetElement`.
- Không dò dầm/sàn cho từng điểm trên toàn model: chia ô mặt bằng rồi chỉ thử phần tử trong ô (bản vẽ lớn treo Revit).

## [debug] Khi kết quả sai

- Đọc `%LOCALAPPDATA%\DSSTech\mep-cad-input.txt` (nét và chữ đã đọc), `%LOCALAPPDATA%\DSSTech\mcp-host.log`
  (dòng "mep fitting:") và journal Revit mới nhất trước khi đoán từ ảnh chụp.
- Người dùng gửi bằng nút **Góp ý / Feedback** (zip có log, ảnh, file dump). Zip được gửi thẳng lên web app Apps
  Script (`tools/feedback-apps-script`): mail về DSSTech và lưu trong thư mục Drive "DSSTech-GopY"; không gửi được thì
  zip nằm trên Desktop người dùng. Dòng "Tool:" trong gop-y.txt là tên nút trên ribbon (Panel ▸ Tool).
- Không gửi thẳng bằng SMTP: mật khẩu mail nhúng trong DLL thì ai cũng lấy ra được.
