# Quy cách CAD đầu vào — DSSTech CAD2BIM

Tài liệu in được cho người vẽ CAD. Engine **fail-fast**: sai spec thì không tạo đối tượng Revit.

## 1. File Revit

- Nên có Level + Grid trùng hệ tọa độ CAD.
- Thiếu: dựng từ layer `G-GRID` + text, CSV, hoặc lệnh MCP.
- Trùng tên / cao độ ±5 mm / lưới khoảng cách < 50 mm và góc < 2° → **bỏ qua** (không nhân bản).

## 2. Link

- DWG/DXF **link** vào model (không explode).
- Origin-to-Origin hoặc Shared Coordinates.
- **1 tầng / 1 file** mặt bằng; gán đúng Level.

## 3. Đơn vị

- **mm**. `INSUNITS=4`. 1 đơn vị CAD = 1 mm.
- Sai đơn vị → cột 400 thành 0,4 m → **chặn**.

## 4. Block

- Cửa, cột, thiết bị: giữ block (insertion + rotation + attribute). **Không nổ.**

## 5. Hình diện tích

- Polyline **đóng** (`closed=1`): sàn, phòng, đài móng, lát.

## 6. Chú thích

- Dim, hatch, text phụ: layer `*-ANNO-*`. Không dùng dim làm tường.

## 7. Text thuộc tính

- Gần đối tượng < 300 mm hoặc attribute block.

## 8. Gói file typical (một tầng)

```text
A-T1-PLAN.dwg
S-T1-FRAMING.dwg
S-T1-FOUNDATION.dwg
M-T1-HVAC.dwg
P-T1-PLUMB.dwg
E-T1-POWER.dwg
F-T1-FIRE.dwg
```

## 9. Fail-fast (không tạo nếu sai)

| Mã | Điều kiện |
|---|---|
| `insunits` | Không phải mm / `INSUNITS≠4` |
| `open_polyline` | Sàn/phòng/đài không đóng |
| `exploded_block` | Cửa/cột/TB đã nổ |
| `unknown_layer` | Layer lạ, không phải `*-ANNO-*` / `G-GRID` / Defpoints |
| `bbox_vs_grid` | Bao CAD lệch lưới sẵn > 1 m (chỉ khi đã có grid) |

## 10. Mapping layer

Ba file mặc định:

- `Config/cad-layer-map.str.json`
- `Config/cad-layer-map.arch.json`
- `Config/cad-layer-map.mepf.json`

User sửa trên dialog trước Preview. Overlay dự án: `cad-layer-map.*.project.json`.

## 11. Thứ tự dựng khuyến nghị

Trục/tầng (nếu thiếu) → Kết cấu → Kiến trúc → MEPF. Mỗi loại: Preview → Confirm.
