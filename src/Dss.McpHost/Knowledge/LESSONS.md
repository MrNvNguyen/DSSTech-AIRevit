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

- **Lỗi:** PCCC báo tạo 1229 đoạn nhưng không thấy ống; lần chạy sau "đã có ống trùng vị trí". **Vì sao:** DWG vẽ xa
  gốc (x ≈ 78 880 000 mm). Khi link, Revit dời hình học về gần gốc nhưng `GetTotalTransform` không mang phần dời đó;
  đọc file DWG rồi nhân transform → ống nằm cách nét đang thấy ~78 km. **Quy tắc:** so nét đọc từ file với nét Revit
  trả về (cùng chiều dài, cùng hướng), lấy độ lệch được nhiều cặp đồng ý nhất và bù vào (`CadAlign.Offset`, log
  "cad align: … shifted"). Dấu hiệu: toạ độ trong dump / log "mep fitting" hàng chục triệu mm.

- **Lỗi:** đặt 849 đầu phun rất chậm. **Vì sao:** mỗi thiết bị đặt tự do gọi `doc.Regenerate()` để đọc hướng mặc định
  rồi xoay, và tạo lại `ReferenceIntersector` cho mỗi lần dò trần. **Quy tắc:** đọc hướng mặc định một lần cho mỗi type,
  dùng chung bộ dò mặt cho mỗi view + category trong một lần chạy; không regenerate trong vòng lặp hàng trăm phần tử.

- **Lỗi:** PCCC: tuyến chính (mạch vòng) trên layer SD-D150 dựng thành DN32/40/50/65. **Vì sao:** chữ "Dn40" của nhánh
  ghi sát tuyến chính được gán cho đoạn gần nhất; nhánh nằm trên SD-D40 mà layer đó không được chọn. **Quy tắc:** đoạn
  có size từ tên layer (SD-D150) chỉ nhận chữ cùng size; chữ khác size tìm đoạn khác. Báo người dùng chọn đủ layer
  SD-D25/32/40/50/65/150.

- **Quy tắc (thiết bị PCCC):** PCCC ▸ Tủ & thiết bị PCCC chỉ dựng phương tiện PCCC theo TCVN 3890: tủ / họng / cuộn
  vòi (600), FM200 / bình khí / van báo động / bơm / họng tiếp nước (0), bình xách tay / xe đẩy (0), tủ trung tâm (1200),
  nút ấn (1400), chuông / còi / đèn (2300), đèn sự cố / EXIT (2200); đặt tại điểm block, cao độ = level + cao lắp, xoay
  theo block. Đầu phun sprinkler và đầu báo khói / nhiệt ở Thiết bị trần (gắn theo trần), không ở tool PCCC.

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

- **Lỗi:** nhập đáy ống 2600, PCCC dựng đáy 2590,9 (DN150, tâm 2675). **Vì sao:** tâm tính bằng đường kính danh nghĩa
  (150/2) rồi làm tròn xuống 5 mm; đáy Revit đo theo đường kính ngoài 168,3. **Quy tắc:** cao độ đáy / đỉnh ống đo theo
  đường kính ngoài của segment trong type ống (Routing Preferences); cao độ nhập dùng đúng số, không làm tròn.

- **Lỗi:** PCCC: nhánh DN32 cắt qua ống chính DN150 không có fitting (log "failed to insert cross" ở hàng loạt nút
  4 nhánh). **Vì sao:** type ống không có cút chữ thập (Routing Preferences ▸ Crosses) hoặc không có cỡ 150×32. **Quy
  tắc:** chữ thập Revit từ chối → ống chính nối liền qua nút, hai nhánh gắn bằng tap; type không có tap → tê cho một
  nhánh, nhánh kia nối tay. Log "mep type …: junctions …; crosses …" cho biết type có gì.

- **Quy tắc (nối nhánh):** ống áp lực (cấp nước, PCCC) mặc định **không nối nhánh vào tuyến chính**: tuyến chính liền
  một ống qua chỗ rẽ (khác cỡ thì côn), nhánh dừng ở mép ống, để nối sau khi Combine dời tuyến. Tick "Nối nhánh vào tuyến
  chính" trong hộp thoại để nối ngay (tê / chữ thập / tap). Ống gió, máng, busway mặc định vẫn nối.
- **Lỗi (tuyến chính vẫn đứt ở chỗ chữ thập):** ở nút có nhánh DN32 lên và DN50 xuống, cặp nhánh cũng thẳng hàng như
  cặp DN150, chọn "cặp đối nhau nhất" thì lấy nhầm cặp nhánh làm tuyến chính, DN150 bị bỏ đứt từng khoang.
  **Cách sửa:** `MepRoutePlanner.MainPair` chọn trong các cặp thẳng hàng cặp **to nhất** (cỡ nhỏ hơn của hai ống).
- **Lỗi (danh sách type đài / móng đơn thiếu):** `FilterCapsAndFootings` lọc theo từ khoá trong tên (pile cap, đài,
  footing…), family đặt tên khác không hiện. **Cách sửa:** hiện mọi type Structural Foundation trong dự án, type có tên
  đài / móng đơn lên đầu, type giống cọc xuống cuối. Không lọc mất type theo tên khi người dùng phải tự chọn.
- **Lỗi (cọc trên trục xiên dim ra 49.973 thay vì 50):** `GridLineSnap` gộp các trục lệch nhau ≤ 0.05° vào một họ và đo
  vuông góc theo góc của trục đầu tiên; trục định vị cọc lệch góc chút ít nên khoảng cách thật tới nó không tròn 5 mm.
  **Cách sửa:** mỗi trục giữ phương riêng; cọc canh 5 mm vuông góc với **đúng trục định vị nó**. Họ trục chỉ dùng để
  tìm trục gần nhất. Bước canh lại cọc trong đài trên Revit (`SnapPileInCap`) cũng đo theo pháp tuyến của chính trục,
  không theo trục của đài.
- **Quy tắc (đầu phun):** đầu phun còn cổng ống hở tự nối vào ống nhánh PCCC phía trên (`SprinklerPipes.Connect`,
  vị trí do `SprinklerDropPlanner.Plan`): ngay dưới ống (≤ 25 mm) thì ống đứng thẳng lên + tap (type không có tap thì
  cắt ống, đặt tê); lệch sang bên thì tay ngang từ ống ra trên đầu phun + cút xuống; đầu phun cuối nhánh (qua đầu hở
  của ống) thì kéo ống tới đó + cút. Tầm tìm 1000 mm. Chạy sau «Đầu phun» (Thiết bị trần) và sau tuyến PCCC, nên dựng
  cái nào trước cũng được.
- **Lỗi (đầu phun không nối, 0/996):** đòi ống cao hơn đầu phun ≥ 100 mm; ống đáy 2800 nằm sát trên đầu phun (trần mặc
  định 2700) nên bị bỏ hết; đầu phun quay lên (ống ở dưới) cũng bị bỏ. Đầu phun nối mềm (`DauPhun_QuayXuong_NoiMem`) lệch
  ống ít thì tay ngang quá ngắn, Revit báo "angle too small or too large". **Cách sửa:** tìm ống theo hướng cổng nối
  của đầu phun (lên hoặc xuống); đầu phun nối mềm (tên có NoiMem / mềm / flex) hoặc cách mép ống < 100 mm thì nối bằng
  **Flex Pipe** từ tap trên ống nhánh (cần type ống mềm trong project); còn lại ống cứng như cũ.
- **Quy tắc (nối đầu phun — chỉ trong PCCC ▸ Nối đầu phun):** dựng tuyến PCCC và đặt đầu phun **không tự nối**. Lệnh nối
  cho chọn **ống cứng** (trần bê tông / trần hở: tê dưới ống nhánh + ống đứng DN25; lệch ống thì tay ngang từ hông ống +
  cút; đầu nhánh thì kéo ống + cút; côn thu tại đầu phun; tay ngang > 600 mm báo cần giá treo theo NFPA 13; tim ống cách
  đầu phun < 100 mm thì không nối cứng) hoặc **ống mềm** (trần thả / thạch cao: tê hông ống nhánh + nipple DN20, ống mềm
  DN20 ra ngang rồi xuống thẳng đầu phun, đoạn DN15 100 mm + côn DN20×DN15 cho đầu phun DN15). Chiều dài ống mềm chỉ
  700 / 1000 / 1200 / 1500 / 1800 mm (ống mềm listed tối đa 6 ft) và **mỗi công trình dùng 1–3 loại**
  (`SprinklerHose.PickSet`: đủ dài cho đầu xa nhất, ít hao nhất, thêm loại chỉ khi bớt hao ≥ 20%); chiều dài ghi vào
  Comments. **Lỗi trước (426/996 "failed to insert tee"):** đầu ống mềm đặt thẳng lên trục ống nhánh — Revit không cho
  tap / tê với Flex Pipe; phải có nipple ống cứng giữa ống nhánh và ống mềm.
  **Lỗi nối cứng (547/992; 166 "failed to insert transition", 32 "opposite direction"):** ống nhánh đáy 2800, đầu phun
  2700 — tim ống cách đầu phun ~100 mm, không chứa nổi ống đứng DN25 + côn thu + đoạn DN15 100 mm. **Sửa:** chỉ dùng
  DN25 + côn khi khoảng cách ≥ nửa ống nhánh + 120 + 100 mm; ít hơn thì ống đứng bằng cỡ đầu phun đi thẳng từ tê thu
  (reducing tee / tap). Chậm: một transaction cho mỗi đầu phun và đọc mọi ống cho mỗi đầu phun — nay gom 20 đầu phun một
  transaction (mỗi đầu phun một sub-transaction; lô bị Revit từ chối khi commit thì làm lại từng cái), và đầu phun chỉ
  xét ống trong tầm mặt bằng (đường ống lưu sẵn, cập nhật khi nối). Đầu phun không có ống: log ghi ống PCCC gần nhất
  (khoảng cách mặt bằng, cao độ) để biết vì sao.
- **Quy tắc (đầu phun thẳng hàng với ống nhánh — anh Nguyên):** trên một nhánh, đầu phun và ống nhánh phải thẳng hàng; đầu
  phun lệch tim ống vài chục mm (đặt theo CAD) sinh tay ngang rất ngắn → cút / tê không đặt được. «Nối đầu phun» dời đầu
  phun lệch ≤ 200 mm (sửa được trong hộp thoại, 0 = không dời) vào tim ống nhánh trên mặt bằng trước khi nối
  (`SprinklerDropPlanner.Aligns`); lệch xa hơn coi là tay ngang có chủ đích.
- **Tiêu chuẩn nối đầu phun (anh Nguyên gửi, tổng hợp từ ChatGPT — đối chiếu bản gốc trước khi trích điều khoản):**
  TCVN 7336:2021 (thiết kế, lắp đặt sprinkler) **không** có bảng bắt buộc vị trí nào dùng ống cứng / ống mềm — tool để
  người dùng chọn, **không tự chuyển sang ống mềm khi ống cứng vướng**. TCVN 13455:2022 (ống mềm kim loại nối đầu phun):
  inox, dài tối đa 3200 mm, đường kính trong ≥ 19,5 mm, áp làm việc ≥ 14 bar; cấu tạo thân ống, đai ốc, côn thu, ống nối.
  NFPA 13 (sửa đổi 2024): ống mềm trần thả tối đa 1,8 m, trần cứng 3,7 m; dùng bộ được chứng nhận, lắp theo hãng. Catalogue
  TYCO FH-1: dài 700 / 1000 / 1200 / 1500 / 1800 mm, đầu vào 1" (DN25), đầu ra 1/2" hoặc 3/4" — nên tool cho chọn cỡ ống
  mềm DN20 / DN25 (mặc định DN20 theo anh Nguyên). Chuỗi đúng: ống nhánh → tê / đầu lấy nhánh → inlet nipple → ống mềm →
  côn thu (thẳng hoặc 90°) → đầu phun; giá đỡ (reducer bracket, support bar) là hệ đỡ riêng, chưa dựng. Không ghép 2 ống
  mềm; ống mềm phải thoát hết nước về ống nhánh hoặc qua côn (không tạo điểm trũng); bán kính uốn, số lần uốn theo hãng.
  TCVN 7336 Đ5.5.22: đai giữ cách đầu phun cuối ≤ 0,9 m (ống ≤ DN25), ≤ 1,2 m (ống lớn hơn) — thuộc giá đỡ ống, chưa dựng.
- **Quy tắc (giá treo PCCC — PCCC ▸ Giá treo PCCC):** tool riêng, không chạy khi dựng tuyến. Mỗi ống nằm ngang
  (`HangerPlanner`): cách nhau ≤ 3,6 m (≤ DN32) / ≤ 4,5 m (≥ DN40) — số theo NFPA 13, đối chiếu TCVN 7336 Đ5.5.21; đầu
  tự do (đầu hở, cút xuống ống đứng / đầu phun cuối) có giá trong 0,9 m (≤ DN25) / 1,2 m (lớn hơn), đặt cách đầu 300 mm;
  cách phụ kiện ≥ 150 mm; ống giữa hai phụ kiện ngắn hơn 600 mm thì bỏ (ống hai bên đỡ). Family giá treo do người dùng
  chọn (đặt theo level); tham số đường kính và chiều dài ty (tới đáy dầm / sàn kể cả file link) ghi nếu family có. Chạy
  lại không đặt trùng (cùng family trong 100 mm). Tùy chọn giá đỡ tại đầu phun nối ống mềm.
- **Quy tắc (tổn thất ống mềm — PCCC ▸ Tổn thất ống mềm):** hãng công bố tổn thất ống mềm bằng **chiều dài tương đương**
  ống thép 1" Sch 40 (C=120), phụ thuộc chiều dài và số lần uốn (FM còn theo K đầu phun); ống inox gân sóng tổn thất
  hơn nhiều so với cùng chiều dài ống thẳng nên **không** tính theo chiều dài thật. Tool dùng
  số liệu có sẵn trong code (`HoseLossTool.BuiltIn`) từ catalogue anh Nguyên gửi: GS Fire Safety G-FLEX 25N/25B (UL bảng
  3–4, FM bảng 5) và DAEJIN DJ28B (UL, FM), DJ25UB (UL), DJ25B (UL, chỉ 1200/1500/1800); file
  `%APPDATA%\DSSTech\sprinkler-hose.json` thêm / thay dòng. Số tra theo hãng-model-chứng nhận, **đầu ra 1/2" hay 3/4"**
  (theo cỡ đầu phun) và chiều dài; số là ở **số lần uốn 90° tối đa** của bảng. ΔP = Hazen–Williams
  6,05·10⁵·Q^1,85/(C^1,85·d^4,87) × L_tđ, d = 26,6 mm, Q = K·√P (`HoseLoss`); thiếu dòng thì báo lỗi, không đoán. Cùng
  chiều dài, các model chênh nhau gấp 2–4 lần (DJ28B 700 = 18 ft, G-FLEX 700 = 33 ft, DJ25UB 1800 = 81 ft): phải chọn
  đúng model dùng ở công trình. Số liệu DAEJIN trên mạng (reseller) lệch nhau — chỉ dùng catalogue chính hãng.
- **Quy tắc (nối nhánh sau Combine — Combine ▸ Nối nhánh):** đầu ống nhánh còn hở nối vào ống chính cùng hệ (nhóm hệ +
  mã công ty), cỡ bằng hoặc lớn hơn, nhánh phải cắt ngang ống chính (không song song) và chỗ nối cách đầu ống chính ≥ cỡ
  nhánh + 100 mm. **Không đổi cao độ tuyến nào**: nhánh chỉ kéo dài / rút ngắn theo chính đường của nó (giữ dốc). Tiết diện
  nhánh nằm trong chiều cao ống chính → tap / tê bên hông; cao hơn / thấp hơn ≥ nửa ống chính + 1,5 cỡ nhánh + 50 mm → cút +
  ống đứng + tap / tê trên đỉnh / đáy (`BranchJoinPlanner`); lệch ít hơn → báo nối tay; thoát nước (DR, CND) thấp hơn ống
  chính → không nối (nước không chảy vào).
  **Lỗi lần chạy đầu (438 nối, 2 "failed to insert tee", 1 lệch 75 mm):** tap / tê ống tròn chỉ đặt được khi tim nhánh
  **trùng tim** ống chính — nhánh lệch cao vài chục mm (sau Combine) vẫn tính là "bên hông" nên Revit từ chối. **Sửa:** ống
  tròn chỉ nối hông khi lệch ≤ 2 mm (ống gió chữ nhật vẫn cho lệch trong mặt ống chính); lệch ít hơn mức cần cho ống đứng
  → **offset 2 cút 45°** sát ống chính (cút dưới cách mép ống chính 1,5 cỡ nhánh + 50 mm, đoạn xiên 45°, rồi vào hông
  ống chính ở cao độ ống chính) — tuyến nhánh giữ cao độ tới cút trên. Chậm: đọc hệ (Identify) và kích thước của mọi ống
  chính cho mỗi đầu nhánh — nay đọc một lần (cache) và bỏ qua ống xa hơn reach + 2 m trước khi đọc gì khác.
- **Lỗi (PCCC tầng hầm: 8 fitting không tạo được, lặp lại mọi lần chạy):** 3 kiểu, xem bằng harness trên dump.
  (1) 5 chỗ "family is connected in a network and can no longer keep the connectivity": góc DN150 có nhánh rẽ cách góc
  63–194 mm; cút DN150 cần ~230 mm mà đoạn góc→nhánh ngắn hơn. **Sửa:** dựng nút rẽ nhánh (≥ 3 ống, nối liền ống chính)
  **trước** các góc, để cút ăn vào ống chính đã liền. (2) Cút 95° (stub D65 dài 207 mm vẽ lệch 5,5°) — họ cút không nhận.
  (3) 2 côn "failed to insert transition": nhánh D40/D32 vẽ lệch 50 mm (2,6°) khỏi phương vuông ống chính, côn cần 2 ống
  thẳng hàng. **Sửa (`MepRoutePlanner.SquareUp`, mọi loại tuyến, trước `ReducersPast`):** nhánh lệch ≤ 8° → trượt nút tê
  dọc ống chính cho vuông (ống chính vẫn thẳng); chữ thập có 1 nhánh lệch → nhánh đó có tê riêng (ống chính là cặp thẳng
  hàng cỡ lớn nhất); góc lệch ≤ 8° khỏi 90° mà một đầu tự do → xoay đoạn tự do về 90°.
- **Quy tắc (côn giảm ống sprinkler):** chỗ đổi cỡ trên tuyến thẳng của PCCC (CAD đặt ngay tại đầu phun) dời 150 mm
  sang phía ống nhỏ (`MepRoutePlanner.ReducersPast`): đầu phun lấy nước từ ống lớn rồi mới giảm. Góc cua giữ côn tại cút.
- **Quy tắc (PCCC ▸ Tăng áp / hút khói):** dựng y như ĐHKK ▸ Ống gió (cùng `MepRun.Duct`: size, cao độ, fitting,
  cút tròn, ống mềm bỏ qua). Khác duy nhất: hộp thoại đưa hệ SEAD (hút khói), PAD (tạo áp) lên đầu danh sách System
  (`MepChoice.Codes`). Trước đây nhóm hệ để trống nên danh sách xếp theo tên, dễ chọn nhầm hệ cấp gió.
- **Quy tắc (thông báo khi xong):** lệnh dựng xong **không hiện báo cáo**; báo cáo đầy đủ ghi vào
  `%LOCALAPPDATA%\DSSTech\mcp-host.log` (dòng `report <tên lệnh>:`). Chỉ khi có dòng lỗi ("Không tạo được…", "Lỗi…",
  "lỗi N" với N > 0) mới hiện cửa sổ, và chỉ các dòng lỗi đó (`DoneReport.Show`, lọc bằng `ReportErrors.Lines`). Báo
  cáo mới phải viết lỗi bằng các cụm đó để người dùng thấy. Hướng dẫn trước khi chạy và lỗi chặn lệnh vẫn hiện như cũ.
- **Quy tắc (join cọc):** cọc join và **cắt** (switch join) cả đài lẫn bê tông lót và mọi khối móng / sàn nó xuyên qua
  quanh đài (`JoinPileIntoCap`), không chỉ riêng đài. Chạy lại lệnh Cọc áp dụng cho cọc đã có.

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
  line theo tim nét (chỉ nét hở ngoài block), kích thước do người dùng nhập cho mọi đoạn, Service Type BW. Nhãn
  "Busway … H=3300" chỉ gán cho busway; máng, ống gió, ống nước không nhận nhãn busway, busway không nhận nhãn máng /
  ống gió. Tool máng không đổi.
- **Lỗi:** busway thiếu đoạn so với CAD. **Vì sao:** khi layer có nét có bề rộng, tool chỉ lấy các nét đó, nên busway vẽ
  bề rộng 0 bị bỏ. **Quy tắc:** nét busway = nét hở ngoài block, có bề rộng **hoặc** dài ≥ 1 m; nét ngắn chỉ lấy khi chạm
  đầu một tuyến (đoạn chuyển hướng). Log ghi "busway strokes: … wide, kept …" để kiểm tra.
- **Lỗi:** dư đoạn đứng 1200 (2650→3150) nằm lơ lửng giữa các thang. **Vì sao:** thang 1200 H3100 kết thúc trên máng
  200 H2600; tool hạ thang xuống nút để nối tê, nhưng Revit không tê được nhánh rộng hơn ống chính ("failed to insert
  tee") nên đoạn đứng và cút bị bỏ lại. **Quy tắc:** ở nút khác cao độ, tuyến cao hơn chỉ hạ xuống khi không rộng hơn
  tuyến thấp nhất tại nút; rộng hơn thì giữ cao độ của nó (đi trên, không nối).
- **Lỗi:** busway thiếu đoạn chạy dưới thang cáp. **Vì sao:** trên DWG không vẽ busway ở đó (block thang có wipeout che,
  busway dừng ở mép thang rồi vẽ tiếp bên kia); nét anh thấy trong khung là nét thang. **Quy tắc:** busway: hai đầu hở
  thẳng hàng (lệch ≤ 100 mm), quay vào nhau, cách ≤ 6 m thì nối thẳng (log "bridged N gaps"). Kiểm tra DWG gốc bằng
  ACadSharp (toạ độ DWG = toạ độ dump + độ dời, so qua polyline có bề rộng).
- **Lỗi:** busway vẫn thiếu dù đã nối khe (log "bridged 2 gaps"). **Vì sao:** busway vẽ trên tim thang / máng đã dựng;
  bước "bỏ đoạn đã có" coi mọi Cable Tray song song, lệch < 30 mm, chênh < 1,5 m là trùng nên bỏ busway. **Quy tắc:**
  busway chỉ so với Cable Tray cùng Service Type (BW); máng không coi busway (BW) là máng đã có.
- **Lỗi:** busway bị chẻ thành nhiều khúc ngắn, đoạn đứng thừa, chỗ hở (lỗi phụ kiện ở cặp cao độ lệch 50 mm như
  3295/3345). **Vì sao:** busway đi chung nhánh cao độ với ống nước: uốn né từng dầm, mỗi đoạn một profile; đoạn có nhãn H
  và đoạn không nhãn khác cao độ. **Quy tắc:** mỗi mạng busway **một cao độ**, không uốn né dầm: cao độ ghi trên CAD
  (giá trị ghi nhiều nhất trong mạng) cho cả mạng; không ghi thì cao độ nhập, dầm/sàn thấp hơn thì hạ cả mạng.
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
- **Lỗi:** một tuyến ống chính PCCC DN150 dài 19 m mang System Type "Undefined" (System Name trống); nhánh và đầu phun
  không nối được vào tuyến đó. **Vì sao:** tuyến chính được làm liền qua chỗ rẽ (đoạn sau bị xóa và gộp vào đoạn trước),
  Revit có thể để ống gộp lại không còn system. **Quy tắc:** sau khi dựng tuyến từ CAD, và trước khi chạy «Nối nhánh» hoặc
  «Nối đầu phun», ống bị Undefined được gán lại system theo thứ tự: ống nối liền với nó qua fitting → system nhiều nhất
  của cùng type ống trong phạm vi lệnh → system đã chọn khi dựng. «Nối đầu phun» chỉ lấy theo ống nối liền, vì một type
  ống có thể dùng cho nhiều hệ.
  **Dùng `Pipe.SetSystemType` / `Duct.SetSystemType`, không set tham số System Type.** Mạch vòng DN150 gồm 24 ống chỉ
  nối với nhau qua cút: set tham số trả về thành công nhưng sau regenerate cả 24 ống vẫn Undefined. SetSystemType tạo
  hệ thống mới cho mạng chưa có hệ thống nào. Gán cho một ống thì cả mạng nhận theo, nên gán từng ống và regenerate sau mỗi
  lần, bỏ qua ống đã nhận.
  **Mạch vòng kín** (chỉ có ống và cút, không nối nhánh nào): system gán được trong transaction, đọc lại thấy có, nhưng
  commit xong Revit bỏ đi (log ghi 24/24, người dùng vẫn thấy Undefined; nối một nhánh vào thì mạch nhận system). Cách
  làm: tháo một mối nối của mạch, gán system, commit; transaction sau mới nối lại. Luôn kiểm tra System Type **sau khi
  commit**, không tin giá trị đọc trong transaction.
  Set System Type có thể không giữ: Revit trả về Undefined khi regenerate. Khi đó tháo ống khỏi các kết nối, gán system,
  rồi nối lại (trong sub-transaction). Ống vẫn Undefined được ghi log `mep system:` kèm các phần tử nó nối vào. Ống của lần
  dựng trước (lệnh bỏ qua vì đã có) cũng được kiểm tra, nhưng chỉ lấy system theo ống nối liền.
- **Lỗi:** họ đầu phun F-SPRI-DauPhun_QuayXuong_NoiMem có connector classification "Hydronic Supply". Khi nối vào ống
  chữa cháy, Revit báo "different system classification" và mạng ống có thể thành Undefined. **Quy tắc:** «Nối đầu phun»
  đổi connector ống của họ đầu phun sang classification của ống chữa cháy (Fire Protection Wet/Dry…), rồi load lại họ
  và giữ giá trị của các instance.
- **Lỗi:** 877 đầu phun có cổng nối cách tim ống 71 mm bị từ chối vì "không đủ chỗ tê cho ống cứng". Họ đầu phun có sẵn
  ống mềm, cổng nối nằm sát ống. **Quy tắc:** đầu phun sát ngay dưới ống nhánh được nối thẳng vào ống như Connect Into
  của Revit: tap (takeoff) từ cổng đầu phun vào ống; type ống không có tap thì cắt ống và đặt tê. Không cần ống đứng
  hay côn.
- **Lỗi:** 119 đầu phun "Dauphun-Quay len" báo không có ống nhánh, dù ống nằm ngay trên, cách 0 mm mặt bằng và cao hơn
  234 mm. **Vì sao:** đầu phun quay lên có cổng nối hướng xuống, nên phải đứng trên ống; lệnh đặt thiết bị trần lại đặt
  nó ở cao độ trần, tức dưới ống. **Quy tắc:** «Nối đầu phun» nâng đầu phun quay lên đang nằm dưới ống nhánh (lệch
  mặt bằng ≤ mức căn, tối thiểu 50 mm) lên trên ống: cổng nối cách tim ống max(100, OD/2 + 75) mm, đồng thời căn vào tim
  ống. Việc nâng nằm trong sub-transaction của đầu phun đó, nối không được thì hoàn tác.

- **Lỗi:** côn giảm 40-25 nằm ngay tại tê nhánh, vì bản vẽ CAD đổi cỡ ống chính đúng chỗ rẽ nhánh. **Quy tắc:** ống PCCC
  đổi cỡ thì côn giảm nằm sau đầu phun **và sau tê nhánh** 150 mm về phía ống nhỏ. Tại tê, ống chính nhỏ được cắt
  cách tê 150 mm và đoạn từ tê đến chỗ cắt lấy cỡ ống lớn. Nhánh rộng hơn ống chính nhỏ (tức là ống chính rẽ hướng)
  hoặc ống nhỏ ngắn hơn 250 mm thì giữ nguyên côn. Kiểm tra trên bản vẽ tầng hầm: 255 côn được dời (227 chỗ ở đầu
  phun, 28 chỗ ở tê).
  Khoảng 150 mm đo từ tim nhánh / đầu phun **đến chỗ côn bắt đầu thu nhỏ** (đầu to của côn), không đến tâm nút giảm.
  Revit đặt côn ngay tại mối nối, nên côn bắt đầu thu nhỏ trước điểm 150 mm. Sau khi đặt fitting, côn được trượt dọc
  ống cho đầu to nằm đúng ở nút đã dời; nối không giữ được thì hoàn tác và ghi log `mep fitting:`.

- **Lỗi:** ống thoát nước mưa (P-RWP, classification Sanitary) dựng bằng «Nước ▸ Cấp nước» thì không có độ dốc. Nó
  còn được dựng như ống cấp nước: dò dầm/sàn tầng trên và có 22 đoạn đứng né dầm tầng trên. **Quy tắc:**
  - Mỗi lệnh chỉ cho chọn hệ của nó. «Thoát nước» (và thoát nước ngưng) chỉ liệt kê hệ classification Sanitary / Vent.
    «Cấp nước» và PCCC không liệt kê các hệ đó. Project chưa phân loại hệ thì vẫn hiện tất cả.
  - Thoát nước treo dưới sàn của tầng đang đứng: mọi cao độ là **âm** so với level (cao độ tự nhập mặc định −500).
    Dầm/sàn cần né là của sàn tầng đó: dò từ level (+50 cho lớp hoàn thiện) xuống −(khoảng cách tới level dưới − 500).
    Không có level dưới thì dò xuống −3000. Không dò dầm tầng trên.

- **Lỗi:** ống thoát nước D110 ghi "D110-1%" nhưng dựng dốc 0,5%. **Vì sao:** lệnh chỉ đọc độ dốc từ chữ "i=..%", và
  đã lấy chữ "I=0,5%" trên layer xref kiến trúc (`XR TANG HAM$0$…Ky hieu`, ký hiệu dốc sàn). **Quy tắc:**
  - Đọc độ dốc trong chữ ghi tuyến (D110-1%, D200-0.5%-24.4M, D75-2%%%-5.4M). Chữ này ưu tiên hơn chữ "i=" đứng riêng.
  - Bỏ chữ "i=" nằm trên layer xref (tên có "$0$").
- **Lỗi:** sai hướng dốc khi một mạng xả về nhiều bể tự hoại. **Vì sao:** mỗi mạng chỉ nhận một điểm xả. **Quy tắc:**
  pick **mọi điểm cuối nơi ống xả** vào bể / hố ga (không pick điểm đầu tuyến), Esc để kết thúc. Mỗi điểm pick gắn vào
  đầu ống hở gần nhất trong vòng 1500 mm. Mỗi đoạn dốc về điểm xả gần nó nhất theo đường ống, nên điểm cao nhất nằm
  giữa các điểm xả.

- **Lỗi:** thoát nước có dốc thì 20 cút ("failed to insert elbow", cút 45° và 90°) và 6 tê/Y ("angle between them is too
  small or too large") không đặt được. **Vì sao:** lệnh chỉ dùng family cút / tê đứng đầu Routing Preferences của type
  ống. Family cút cố định 90° không làm được cút 45°, cũng không làm được cút giữa hai ống dốc. Tê 90° không làm được
  nhánh Y 45°. **Quy tắc:** Revit từ chối thì lần lượt thử các family cút / tê / Y khác đã load trong project (family có
  trong Routing Preferences trước, rồi family có tên gần nhất). Mỗi family được đặt tạm lên đầu Routing Preferences cho
  một fitting rồi gỡ ra. Không family nào làm được thì log ghi Pipe Fitting Angles và danh sách family cút đã load.

- **Quy tắc (cao độ thoát nước, theo yêu cầu người dùng):** cao độ được cố định tại **điểm đầu tuyến**, tức điểm cao
  nhất, xa điểm xả nhất theo đường ống. Tại đó đỉnh ống cách đáy dầm/sàn phía trên 150 mm (khe hở mặc định cho thoát
  nước). Mọi điểm khác nội suy theo độ dốc xuống các điểm xả. Không có dầm/sàn tại điểm đầu thì dùng cao độ tự nhập,
  cũng tính tại điểm đầu. Đoạn nào ở giữa tuyến chạm dầm sâu hơn thì báo dòng "Lỗi:" để người dùng xem lại, lệnh không
  tự hạ cả mạng.
- **Lỗi:** ống ghi "D110-1%-24.2M" dựng dốc 0,4092%. **Vì sao:** mạng có 5 điểm xả. Hai đầu đoạn ống dốc về hai điểm
  xả khác nhau, và lệnh lấy cao độ mỗi đầu theo điểm xả riêng, nên độ dốc thành số lẻ. **Quy tắc:** mọi đoạn giữ đúng độ
  dốc ghi trên bản vẽ, không nội suy giữa hai cao độ. Đoạn nằm giữa hai điểm xả giữ độ dốc tính từ đầu thấp; đầu cao bị
  lệch cao độ nên không nối được, và báo dòng "Lỗi:" để người dùng xem lại các điểm xả đã pick (thường do pick nhầm
  điểm đầu tuyến).
- **Lỗi:** sau quy tắc trên, các đoạn nối nhau lệch cao độ, và một ống 52,5 m dựng dốc 0,6983%. **Vì sao:** đầu cao của
  đoạn giữa hai điểm xả bị bỏ lệch; tê tại đó không tạo được ("not close enough"), nên lệnh gộp hai ống chính lệch dốc
  thành một ống, độ dốc thành trung bình. **Quy tắc:** mọi đoạn kết thúc đúng cao độ nút, nên mọi mối nối khớp nhau.
  Đoạn giữa hai điểm xả tách tại điểm cao nhất thành 2 ống, mỗi ống đúng độ dốc bản vẽ, dốc về hai phía. Báo dòng
  "Lưu ý:". Chỉ gộp hai ống chính qua tê khi thẳng hàng trong 3D trong 0,25° (1% và 2% lệch nhau 0,57° thì giữ riêng).
- **Lỗi:** tách đoạn tại đỉnh làm tuyến thẳng trên CAD bị gãy khúc cao độ (chữ V ngược giữa đoạn) — người dùng không
  chấp nhận. **Vì sao gốc:** mọi điểm xả bị ép cùng một cao độ; mạng nhiều điểm xả thì không thể vừa đúng dốc vừa khớp
  nút nếu không gãy đoạn. **Quy tắc:** mỗi đoạn dốc MỘT chiều (về điểm xả gần hơn theo đường ống), thẳng đúng độ dốc
  bản vẽ; cao độ truyền từng đoạn từ đỉnh xuống, nên các điểm xả có thể khác cao độ, và điểm cao (nước chia hai phía)
  nằm tại một nút, không bao giờ giữa đoạn. Chỉ đoạn khép mạch vòng không khớp mới báo "Lỗi:".
- **Lỗi:** đoạn nối ống chính với chỗ ống đầu tuyến nhập vào dốc ngược về phía đầu tuyến. **Vì sao:** chiều chảy lấy
  theo điểm xả gần nhất của từng nút, nên đoạn không mang nước từ đầu tuyến nào bị cho chảy về phía đầu tuyến.
  **Quy tắc:** đầu tuyến = đầu hở không pick làm điểm xả. Đoạn trên đường từ đầu tuyến về điểm xả gần nhất của nó thì
  chảy theo đường đó; đoạn còn lại chảy RA XA đầu tuyến gần nhất; không có đầu tuyến thì theo điểm xả gần nhất.

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
- Sau `LoadFamily` (load lại họ), các object Family / FamilyInstance đã đọc trước đó không còn hợp lệ: đọc
  `family.Name` thì lỗi "The referenced object is not valid" và lệnh dừng. Giữ ElementId, đọc tên trước khi load, lấy lại
  phần tử bằng `doc.GetElement`.
- Fitting thử trong SubTransaction bị từ chối rồi rollback: các Connector đọc trước đó không còn hợp lệ. Cút đã tạo
  bằng họ khác vẫn báo "referenced object is not valid" vì code đọc connector cũ sau đó. Đọc trước các giá trị cần
  (domain, gốc, cỡ), hoặc lấy lại connector theo id.
- Tên trong Revit (level, view, type) không được chứa \ : { } [ ] | ; < > ? ` ~. Tên tạm "~dsst …" làm cả lệnh Level
  lỗi "Name cannot include prohibited characters". Tên tạm dùng ký tự hợp lệ, và lọc ký tự cấm khỏi tên người dùng nhập.
- **Lỗi:** bảng Level có 2 tầng hầm + TẦNG 1–4, bấm "Thêm tầng" ra TẦNG 7. **Vì sao:** số tầng mới = số dòng + 1, tính
  cả tầng hầm. **Quy tắc:** tầng mới = số lớn nhất trong các tên "TẦNG n" + 1; tầng hầm không tính.
- **Lỗi:** level mới tạo ra type "No Head" (không có ký hiệu đầu level). **Vì sao:** `Level.Create` lấy type mặc định
  của project. **Quy tắc:** đổi sang type mà các level có sẵn đang dùng (ưu tiên type có head); không có level nào thì
  dùng một LevelType có head, không lấy "No Head".
- **Lỗi:** tool Grid không tạo trục xéo. **Vì sao:** trục xéo đặt tên chữ + số liền (B1…B6, C3…C5), mà mẫu tên trục chỉ
  nhận A, 1, 1A, 1.A, A-1, nên vòng trục bị bỏ (không có log). **Quy tắc:** nhận cả tên chữ + số (B1, C12). Hướng trục
  lấy theo nét, không giới hạn ngang/dọc. Kiểm tra lại bằng `%LOCALAPPDATA%\DSSTech\grid-capture.json` (PlanCapture.Load).
- Không dò dầm/sàn cho từng điểm trên toàn model: chia ô mặt bằng rồi chỉ thử phần tử trong ô (bản vẽ lớn treo Revit).

## [debug] Khi kết quả sai

- Đọc `%LOCALAPPDATA%\DSSTech\mep-cad-input.txt` (nét và chữ đã đọc), `%LOCALAPPDATA%\DSSTech\mcp-host.log`
  (dòng "mep fitting:") và journal Revit mới nhất trước khi đoán từ ảnh chụp.
- Người dùng gửi bằng nút **Góp ý / Feedback** (zip có log, ảnh, file dump). Zip được gửi thẳng lên web app Apps
  Script (`tools/feedback-apps-script`): mail về DSSTech và lưu trong thư mục Drive "DSSTech-GopY"; không gửi được thì
  zip nằm trên Desktop người dùng. Dòng "Tool:" trong gop-y.txt là tên nút trên ribbon (Panel ▸ Tool).
- Góp ý nằm trong `G:\My Drive\DSSTech-GopY` (Google Drive for desktop trên máy anh Nguyên): giải nén bằng Python
  `zipfile` (Expand-Archive của PowerShell 5.1 lỗi với tên file tiếng Việt trong thư mục con).
- Không gửi thẳng bằng SMTP: mật khẩu mail nhúng trong DLL thì ai cũng lấy ra được.

## Drainage flow read from the drawing (riser, floor)
- Lines carry no direction, so the flow comes from the plumbing rule: on upper floors WCs and floor drains run TO the riser (service shaft); on the lowest floor the riser foot runs OUT to manholes, septic tanks, grease traps.
- The riser end of a network: a "TR" / "OD" / "ỐNG ĐỨNG" note within 1.5 m, or another network's open end within 1 m (risers side by side), then the largest pipe, then the end farthest from tank notes (`MepRoutePlanner.DrainHead`).
- Lowest floor = no level below, or a BỂ / PHỐT / HỐ GA / TÁCH MỠ / XUYÊN MÓNG note within 5 m of another open end (a basement over a foundation level). Then every other open end is an outlet; else the riser is the one outlet (`DrainFlow`).
- Picking outlets is kept as a mode for drawings without notes; picking the riser per network overrides the guess.

## Pipe labels written as dimension text (D200-0.5%-23.6M)
- Symptom: a D200 run drawn "D200-0.5%-23.6M" came out at 1% (the default) in Revit.
- Cause 1: on many MEP plans the size-fall-length label is an aligned DIMENSION whose text is overridden; the words exist only as MTEXT inside the dimension's anonymous block (*D76). The DWG reader skipped Dimension entities, so 43 of 44 pipe labels on 30.4 tầng hầm were never read. Fix: read MText/Text with letters inside `Dimension.Block` (not its lines/arrows; plain measured numbers are skipped).
- Cause 2: a straight run is split at every tee where a branch joins, and the fall only carried on through plain joints, so the label reached one piece and the rest took the default; Revit then merged them into one pipe at a mixed slope. Fix: a run's own label (D..-x%) spreads to every piece straight on from it (within 2°, same size and layer) up to a piece with its own label. A lone i=x% does not spread this way.
- Check: dump probe with ACadSharp — list `Dimension.Block.Entities` MText; harness on the dump with those labels added.

## Lowest floor: many risers onto one collector
- Symptom: on tầng hầm a branch from shaft TKT29 sloped down INTO the shaft.
- Cause: the lowest-floor rule took one riser per network and made every other open end an outlet; but on the lowest floor several risers (TKT28, TKT29, TG3, TG4) come down onto one collector, which leaves only at the septic tank / foundation sleeve.
- Fix: on the lowest floor the outlets are the open ends within 5 m of a tank / manhole note (BỂ PHỐT, HỐ GA, CHỜ XUYÊN MÓNG) that have no riser note (TR, TKT29, TG3…) within 1.5 m; every other end is high. With no such note, the old rule stands.
- Trap: without its accents "BỆ MÁY" (machine plinth) reads "BE MAY", as "BỂ" does: the tank note now skips BE MAY / BE TONG.

## Vent (thông hơi) climbs the other way
- Rule from the user: a drain falls from the WC / floor drain (high) to the riser (low); a vent is the opposite: low at the branch ends (WC, floor drain), highest at the riser it climbs to, on every floor.
- Vent is told by the Revit piping system classed Vent, or its name / abbreviation (P-VP-Vent, Thông Hơi), or every layer drawn on (P - TH D110, HX, HR, THBP). A mix of TX and TH layers is not a vent.
- Build: the riser end is taken as the one "outlet", heights computed as for a drain, then turned over (highest - height), so the riser is the top and every run keeps its drawn fall. The top is set under the structure as before.

## Lowest floor: one exit per network
- Symptom: on tầng hầm a D200 collector fell toward a junction from both sides (two exits, water parting mid-network), and a pump-room D75 fell toward its THOÁT SÀN end.
- Causes: (1) every end within 5 m of a "tank" note was an exit, and the basement is covered with other pipes' "D140 CHỜ XUYÊN MÓNG" sleeve notes; (2) "BỂ NGẦM NƯỚC SH (510M3)" (clean-water tank) read as a septic tank; (3) floor-drain notes (THOÁT SÀN) were not seen as branch starts.
- Fix: one exit per network on the lowest floor. Ends by a riser note (TR, TKT29, TG3) within 1.5 m or a floor-drain note (THOÁT SÀN, PHỄU, FD, WC) within 3.5 m are high. Among the rest: nearest septic tank / manhole / pump pit note (BỂ PHỐT, HỐ GA, HỐ BƠM, TRẠM XỬ LÝ; not BỂ NƯỚC / BỂ NGẦM NƯỚC / BỂ PCCC / BỆ MÁY) within 10 m; else a sleeve note of the end's own size (D200 CHỜ XUYÊN MÓNG at a D200 end); else the largest pipe end farthest from the riser.

## Size label right on a pipe drawn on the wrong layer
- Symptom: a pipe labelled "D75" came out 90, one labelled "D110-1%" came out 140.
- Cause: the drafter drew them on "P - TR D90" / "P - TX D140"; a layer naming a size always won over labels (to keep a branch's DN32 written beside a DN100 main off the main).
- Fix: a label within 0.7 m of the run, of a near size (smaller / larger >= 0.6, e.g. 75/90, 110/140), wins over the layer. A far or very different label (Dn40 by SD-D150) still loses.
- Update (user rule): the label on the drawing comes first; the layer's size only where no label is written (many drawings name no size on the layer). A label of a near size (>= 0.6 ratio) by a run wins at the usual label reach and carries along that run on the same layer (through bends / joints, straight through tees) up to another label. Only a far-off size (Dn40 by SD-D150) still yields to the layer.
- Trap: size labels inside an xref of another discipline ("XR TANG HAM$0$…$0$VNBT-FF-TEXT" D65 of a fire main) sat nearer some drain pipes than their own labels and gave them Ø65. A size label counts only from the drawing itself or from the xref the pipes are drawn in.

## Drain heights from BOP notes at the run's ends
- User rule: drawings write the pipe bottom (BOP=-0.57, BOD=-0.49, from the level); use the BOP at the start and end of a run, not the ones along the middle.
- Read: BOP / BOD / BOT / IL / INV = value (metres if |v| < 30, else mm), from the raw text (DevicePlanner.Plain drops "=" and "-"). A note counts for an open end when it lies within 2 m of the network and its foot on the pipe is within 2.5 m (along the pipe) of that end.
- A BOP stacked under a sleeve note of another size ("D140 CHỜ XUYÊN MÓNG" over "BOP=-0.47", centres up to 3 m apart) is that sleeve's, not the pipe's.
- Build: a run with a BOP at both its two ends falls straight from the higher to the lower (that end is the outlet, slope reported); otherwise the BOP at the outlet (else the head, else any end) fixes the height and the drawn falls do the rest, other written BOPs off by more than 20 mm reported.

## Water supply: own systems, real outside diameter, reducers past the tee, under-beam off
- System list: "Cấp nước" listed every piping system (chiller CHWS, equalizer EP, drencher, sprinkler, hydronic). Pressure-pipe tools now list only their group (MepSystemCatalog.PipeInGroup): the code in the abbreviation or the name decides when the catalog knows it, else the Revit classification; drainage (Sanitary, Vent) never. Nothing matches: the whole list stays.
- Height: bottom typed 2700 came out 2697 (pipe D40, OD 40). The outside diameter came from the first segment rule of the pipe type, not the one Revit used for that size (or a size missing from the segment snapped to another). Fix: before planning, draw one pipe of each size, read RBS_PIPE_OUTER_DIAMETER, roll back (Setup.ProbeOuter; "mep pipe sizes" in the log).
- Reducers: water supply now reduces 150 mm past the branch tee like fire (ReducersPast), not at the junction (a reducer on a tee node with the branch stopped at the main's side made Revit refuse the fitting: "connected in a network and can no longer keep the connectivity").
- "Cao độ theo đáy dầm/sàn" is off when every MEP-from-CAD dialog opens (user rule); ticked by hand when runs should follow beams.