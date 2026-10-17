# Đánh giá kỹ thuật DSSTech / QSNormEngine — 2026-10-17

Bản này là quan sát kỹ thuật sau khi đọc source, charter MCP/CAD2BIM và smoke hiện có. Không phải Success Packet, không mở scope mới, không đổi nghĩa `SelectedQc1` / write-back chính thức.

## Phạm vi đã kiểm

- Đọc `README.md`, `docs/ARCHITECTURE.md`, `docs/MCP_HOST.md`, solution và các project trong `src/`.
- Rà luồng ribbon, engine định mức, formwork, và luồng AI: stdio sidecar → named pipe / HTTP → `ExternalEvent` → tool Revit.
- Không thấy API key, bearer cố định, hay secret trong git. Token HTTP được sinh mỗi lần MCP On và không ghi vào session file.
- Linux không có Revit. Đã build được: `QSNormEngine.Core` (net8.0), `QSNormEngine.Cli`, `Dss.McpContracts`, `Dss.RevitMcp`, `Dss.Cad2Bim.Core`.
- `Dss.McpHost` và `QSNormEngine.Revit` dừng ở `EnsureRevitApi` vì không có `RevitAPI.dll`. Các sửa trong hai project đó được giữ nhỏ và bám API hiện có; chưa biên dịch trên máy này.
- Smoke đã chạy và PASS: `tests/reference_smoke_test.py`, `tests/resource_adjustment_smoke_test.py`, `tests/formwork_formula_smoke_test.py`, CLI `concrete-column-pump.json` (AF.32230) và `brick-wall.json` (AE.22110).

## 1. Kiến trúc và mục đích

Sản phẩm là add-in Revit của DSSTech, host 2024–2027, gồm hai năng lực đang sống chung một ribbon **DSSTech**:

1. **QS-BIM Norm Engine.** `Revit element → ElementFacts → WorkItem → mã lá định mức → điều chỉnh → hao phí VL/NC/M`. SQLite là master; `Data/norm-engine-runtime.json` là cache. Core không tham chiếu Revit API. Lệnh Norm Selector phân tích read-only; ghi `QS_*` chỉ sau người dùng xác nhận.
2. **MCP cho AI (Cursor / Claude / ChatGPT).** Add-in mở named pipe `DSSTech.Mcp.{pid}` và HTTP `127.0.0.1`. Sidecar `Dss.RevitMcp` nói MCP stdio với client và chuyển JSON-RPC vào pipe. Mọi gọi Revit API chạy trên `IExternalEventHandler`, không chạy trên thread pipe/HTTP.

Tách project hiện tại là đúng hướng:

| Project | Vai trò | Phụ thuộc Revit |
|---|---|---|
| `QSNormEngine.Core` | Chọn mã, BOQ, công thức ván khuôn thuần số | Không |
| `QSNormEngine.Cli` | Harness `ElementFacts` JSON | Không |
| `Dss.Cad2Bim.Core` | DXF, preflight, intent kết cấu / kiến trúc / MEPF | Không |
| `Dss.McpContracts` | JSON-RPC, frame pipe, quyền, DTO create | Không |
| `Dss.McpHost` | Hàng đợi, ExternalEvent, tool đọc/tạo | Có |
| `QSNormEngine.Revit` | Ribbon, facts adapter, UI, write-back, CAD confirm | Có |
| `Dss.RevitMcp` | Sidecar stdio, không nhúng Revit API | Không |

Điểm mạnh: create MCP nằm trong một `Transaction` `"DSSTech MCP create"` (một lần Undo); `skipIfExists` mặc định; batch trên 20 phần tử mặc định dry-run; pipe ACL theo user hiện tại; HTTP chỉ loopback. Write-back QS gọi `EnsureBound` bên trong transaction đang mở, không mở transaction lồng nhau.

## 2. Lỗi đã sửa

### Phân trang `query_by_category` bỏ một phần tử

Spec R3: `pageToken` là **id cuối của trang hiện tại**, trang sau lấy `id > token`. Code cũ gán token bằng `ids[pageSize]`, tức id **đầu của trang sau**, rồi lọc `>`. Id đó bị nhảy cóc. Sửa: token = `ids[pageSize - 1]`.

### `XYZ.Normalize()` trên vector dài 0

`CategoryDispatcher.LinesNear` gọi `Normalize()` trước khi kiểm tra độ dài. Revit ném exception với đoạn thẳng suy biến (lưới/tường start ≈ end) đúng lúc `skipIfExists`. `BeamSpansAlongX` (ván khuôn dầm và vai cột) cùng kiểu gọi trên `BasisX`. Đã kiểm tra độ dài trước; dầm suy biến rơi về so sánh bounding box như nhánh không có `LocationCurve`.

### Sidecar bỏ cuộc trước host

Host pipe/HTTP chờ 30 giây. `PipeClient.SendRequest` mặc định đọc 5 giây rồi trả `null`, sidecar dịch thành `mcp_off` trong khi Revit vẫn đang chạy tool (kể cả create). Timeout đọc sidecar nâng lên 35 giây để nhận được kết quả hoặc thông báo timeout của host.

### Request timeout vẫn bị thực thi

Hết giờ chờ, caller đã nhận timeout nhưng work item vẫn nằm trong queue. Lần `ExternalEvent` sau vẫn create. Item giờ bị đánh dấu abandon; handler không chạy tool nữa.

### `ExternalEvent.Raise` trong lúc `Execute` bị bỏ qua

Revit không xếp thêm một lượt chạy nếu `Raise` xảy ra khi handler đang chạy. Request đến đúng lúc đó ngồi trong queue đến lần gọi sau, hoặc đến timeout. Handler vét queue đến khi rỗng; `Idling` gọi `Raise` lại nếu queue còn phần tử. `Start` lỗi giữa chừng gọi `Stop` để không để `IsOn` với pipe/event dở.

### Batch > 20 bị một operation kéo thành create thật

Charter: hơn 20 operation thì dry-run, trừ khi **cờ dryRun của batch** là `false`. Điều kiện cũ còn tắt chốt an toàn nếu **bất kỳ** operation nào có `dryRun: false`, rồi commit cả những operation không đặt cờ. Đã bỏ lối tắt đó. Confirm CAD vẫn gửi `dryRun: false` ở cấp batch nên không bị chặn.

### Operation lỗi vẫn dính vào transaction

`CategoryDispatcher` nuốt exception và trả `failed`, nhưng element đã `Create` (ví dụ `Level.Create` xong, gán `Name` trùng và ném) vẫn nằm trong transaction và bị `Commit`. Mỗi operation giờ chạy trong `SubTransaction`: `failed` thì rollback operation đó, operation khác trong cùng Undo vẫn giữ. Lỗi ngoài dự kiến rollback cả transaction cha.

### `FamilySymbol.Activate` thiếu `Regenerate`

API yêu cầu regenerate sau `Activate` trước khi đặt instance. Cột, dầm, cửa/cửa sổ, family theo điểm và móng đều đi qua một helper.

### Norm write-back đếm sai

`written++` chạy cả khi cả ba `TrySetParam` thất bại, nên hộp thoại báo đã ghi cấu kiện dù param chưa bind. Giờ chỉ đếm cấu kiện có ít nhất một param ghi được; lần bỏ qua vẫn vào `skippedBinding`.

### HTTP

- So bearer bằng `CryptographicOperations.FixedTimeEquals` (độ dài token cố định).
- Từ chối body lớn hơn `McpFrame.MaxPayloadBytes` (1 MiB), kể cả khi không có `Content-Length`.

### Sidecar đọc `error.code` số

`JsonElement.GetString()` ném nếu `code` là số JSON-RPC. Host hiện gửi code dạng chuỗi, nhưng nhánh map lỗi vẫn vỡ và không trả response nếu một host response dùng code số. `GetString` chỉ gọi khi `ValueKind` là string. `TryGetHostError` không còn trả `JsonElement` của `JsonDocument` đã dispose.

## 3. Lỗi tìm thấy nhưng chưa sửa

| Việc | Vì sao chưa sửa |
|---|---|
| `skipIfExists` không được dùng ở cửa, cửa sổ, room, family-at-point, móng | Cần quy tắc “cùng cấu kiện” (host + điểm, hoặc tên room). Khớp sai sẽ bỏ create hợp lệ. Không bịa ngưỡng trong pass này. |
| `CreateFloor` coi mọi sàn cùng type trên cùng level là trùng | Đúng với sàn thứ hai trên một tầng thì sẽ không bao giờ tạo được. Sửa đúng cần so footprint, ngoài phạm vi vá nhỏ. |
| Không có `IFailuresPreprocessor` | Warning Revit vẫn có thể hủy cả transaction. Danh sách warning được nuốt là quyết định sản phẩm, không phải default an toàn để gắn vào. |
| Phân trang vẫn `ToElementIds()` toàn category rồi mới cắt trang | Đúng kết quả sau khi sửa token, nhưng model lớn vẫn cấp phát hết id. Đổi collector là việc hiệu năng, không phải vá một dòng. |
| `DispatchSync` / pipe `Wait` trên thread pool | Đúng khi gọi từ pipe hoặc HTTP. Nếu sau này một lệnh ribbon gọi `DispatchSync`, Revit thread sẽ deadlock vì `ExternalEvent` không chạy được. Chưa đổi chữ ký vì mọi caller hiện tại đều ở background. |
| Cửa sổ giữa “timeout” và lúc tool đã bắt đầu | Không hủy được API Revit đang chạy giữa `Execute`. Abandon chỉ chặn item chưa bắt đầu. |
| Parser DXF chỉ LINE / LWPOLYLINE / CIRCLE / INSERT / TEXT | ARC, POLYLINE, SPLINE, HATCH bị bỏ. Đó là giới hạn sản phẩm đã ghi trong class, không phải crash rõ. |
| `LayerMap` / intent CAD có cảnh báo null (`CS8604`) | Build `Dss.Cad2Bim.Core` ra 7 warning có sẵn. Sửa sẽ đổi cách đọc layer; chưa đụng vì không thuộc lỗi crash đã xác nhận. |
| Quyền MCP luôn `ReadCreate`, bật ngay `OnStartup` | Đúng charter R0b (default On) nhưng AI có thể create trước khi người dùng mở toggle. Đổi default là quyết định PO, không vá im. |
| HTTP là POST JSON-RPC, chưa phải MCP streamable HTTP / SSE | ChatGPT connector đời mới có thể không handshake. Làm đúng spec là một transport mới, không phải sửa vài dòng. |
| `QuantityFactor` chưa có `100m3` | Runtime hiện tại chỉ có `m3`, `m2`, `100m2`, `t`, `m`, `kg`, `each`. Thêm nhánh khi chưa có mã sẽ là suy đoán. |
| `SelectionStatus` serialize ra số (`0` = SelectedApproved) ở CLI | Đổi sang chuỗi phá contract JSON hiện có. Nên làm khi có test khóa schema. |
| Transaction write-back: `Commit` ném rồi `RollBack` có thể ném tiếp và che lỗi gốc | Formwork / InOut / filler vẫn theo mẫu cũ. Chỉ Norm write-back bị đụng vì bug đếm; không sửa lan. |

Không có secret cứng trong repo. Không có write-back norm/formwork tự chạy khi mở lệnh.

## 4. Đề xuất cải tiến

### Cao

1. **Quyền create không mặc định.** `OnStartup` chỉ `Read`. Create bật bằng ribbon hoặc một lần confirm, token HTTP xoay cùng lúc. Giảm rủi ro AI tạo element ngay khi Revit mở.
2. **Transport MCP đúng spec hiện hành.** Giữ pipe cho Cursor. HTTP cần streamable HTTP (hoặc SSE đúng bản ChatGPT đang dùng), từ chối path khác `/mcp`, và không `Wait` vô hạn trên thread pool — giới hạn số request đồng thời bằng một hàng đợi có trần.
3. **Hợp đồng tool ổn định, test được không cần Revit.** Tách parse/paginate/dry-run/quyền ra `Dss.McpContracts` hoặc một project host-free. Revit chỉ còn adapter. Pagination và chốt dry-run > 20 đáng có test thuần.
4. **`skipIfExists` một nghĩa cho mọi category.** Cùng helper khoảng cách / host, có test hình học giả. Floor không được “trùng” chỉ vì cùng level.
5. **Failure preprocessor có danh sách trắng.** Nuốt warning đã biết (tên trùng đã được skip, slightly off axis), để lỗi lạ làm rollback operation và trả `failed` với message Revit.

### Trung bình

1. Cache `RuntimeData` theo SHA file, đừng parse JSON lớn mỗi lần bấm Norm Selector / BOQ.
2. `query_by_category` không materialize toàn bộ id; thêm trần thời gian và log khi category quá lớn.
3. CLI và export JSON dùng `JsonStringEnumConverter` cho `SelectionStatus`, kèm fixture.
4. Mở rộng DXF (ARC, POLYLINE) trước khi coi CAD→BIM là đường chính; preflight nên nói rõ entity bị bỏ, không chỉ layer lạ.
5. Gỡ nullable warning ở `Dss.Cad2Bim.Core` — layer null đang lọt vào parser kiểu.
6. `NormWriteBack` và các write-back khác dùng chung một helper “set param + đếm + rollback an toàn”.
7. Đừng `ShowDialog` kết quả create dạng raw JSON. Tóm tắt created / skipped / failed.

### Thấp

1. `CreateRibbonTab` nuốt mọi exception, không chỉ “tab đã tồn tại”.
2. So token HTTP còn lộ độ dài nếu sau này token không còn cố định.
3. Named pipe `WaitForConnection` không nhận cancellation; `Stop` có thể chờ hết 2 giây.
4. Ghi chú đơn vị trong `QuantityFactor` khi gặp `100m3` / `100m` thay vì nhân im lặng.
5. `ElementId` thống nhất `Id.Value` (invariant culture) giữa factory, write-back và MCP để khỏi phụ thuộc `ToString()`.

## 5. Hướng kiến trúc AI → Revit

Luồng nên là một pipeline có cửa, không phải “model gọi thẳng Revit API”:

```text
AI client
  |  MCP tools/list + tools/call   (chỉ schema, không Revit type)
  v
Sidecar Dss.RevitMcp              (stdio, không Revit API)
  |  frame + session file
  v
Host gateway                      (auth, quyền, trần kích thước, timeout, audit)
  |  hàng đợi
  v
ExternalEvent / Idling            (đúng thread, đúng document)
  |  một transaction / một intent
  v
Tool adapter                      (read | plan | commit)
  |
  +-- ReadTools                   không Transaction
  +-- Plan                        thuần Dss.Cad2Bim.Core / QSNormEngine.Core
  +-- Commit                      CategoryDispatcher, SubTransaction từng op
```

Tách trách nhiệm:

- **AI không chọn API Revit.** Nó chỉ gọi tool có schema: `query_by_category`, `create_batch`, sau này `select_norm` nếu muốn. Tool norm nên nhận `ElementFacts` hoặc element id và trả leaf-code; phần tính mã nằm trong Core để CLI và Revit dùng chung.
- **Plan và Commit tách nhau.** CAD preview và dry-run là plan (không transaction). Commit chỉ sau confirm người dùng hoặc quyền `ReadCreate` cộng cờ `dryRun: false`. Batch lớn mặc định là plan.
- **Một document context.** Mọi tool lấy `ActiveUIDocument` trong `Execute`, từ chối khi null, không giữ `Element` qua lần nhàn rỗi. Id trả về là `long`, mở lại bằng `GetElement` trong transaction mới.
- **Lỗi là dữ liệu.** `failed` + reason, không nuốt rồi vẫn commit. SubTransaction từng operation giữ một lần Undo cho cả batch nhưng không giữ element của op lỗi.
- **Test.** Core và contract test trên Linux. Host test bằng fake `IMcpToolProvider` không cần Revit. Add-in chỉ còn test tay / journal trên máy có Revit. Smoke CLI không được gọi là product PASS cho adapter.

Không nên thêm server riêng, không nhét SQLite native vào add-in, không để AI ghi `QS_NormCode` / `QS_WorkItemId` ngoài đường write-back đã có confirm.

## 6. Kiểm chứng lần này

| Việc | Kết quả |
|---|---|
| `dotnet build` Core net8.0, Cli, McpContracts, RevitMcp | Thành công, 0 warning |
| `dotnet build` Cad2Bim.Core | Thành công, 7 warning null có sẵn |
| `dotnet build` Dss.McpHost | Thất bại: thiếu `RevitAPI.dll` |
| `QSNormEngine.Revit` | Không build trên Linux, cùng lý do |
| Ba smoke Python + hai sample CLI | PASS, mã lá khớp `tests/expected.json` |
