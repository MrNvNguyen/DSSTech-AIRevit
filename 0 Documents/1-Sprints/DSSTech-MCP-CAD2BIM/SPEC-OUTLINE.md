# SPEC-OUTLINE — CAD input + layer maps (P0 draft)

**Status:** sprint draft only. **Not** product SSOT. Do **not** treat as `docs/CAD_INPUT_SPEC.md`. Promote in wave **S0-SPEC**; freeze copy in **S6-SPEC-FREEZE**. Do **not** write `Config/cad-layer-map.*.json` this cycle.

**directive_id:** `DSSTECH-P0-PLAN-2026-09-18`

---

## 1. Skeleton — `docs/CAD_INPUT_SPEC.md` (Vietnamese, printable)

```markdown
# Quy cách CAD đầu vào — DSSTech CAD2BIM

## 1. File Revit
- Nên có Level + Grid trùng hệ tọa độ CAD.
- Thiếu: dựng từ layer G-GRID + text, CSV, hoặc MCP. Trùng tên / cao độ ±5 mm / lưới <50 mm <2° → bỏ qua (không nhân bản).

## 2. Link
- DWG/DXF link vào model (không explode).
- Origin-to-Origin hoặc Shared Coordinates.
- 1 tầng / 1 file mặt bằng; gán đúng Level.

## 3. Đơn vị
- mm. INSUNITS=4. 1 đơn vị CAD = 1 mm.

## 4. Block
- Cửa, cột, thiết bị: giữ block (insertion + rotation + attribute). Không nổ.

## 5. Hình diện tích
- Polyline đóng (closed=1): sàn, phòng, đài móng, lát.

## 6. Chú thích
- Dim, hatch, text phụ: layer *-ANNO-*. Không dùng dim làm tường.

## 7. Text thuộc tính
- Gần đối tượng < 300 mm hoặc attribute block.

## 8. Gói file typical
A-T1-PLAN.dwg
S-T1-FRAMING.dwg
S-T1-FOUNDATION.dwg
M-T1-HVAC.dwg
P-T1-PLUMB.dwg
E-T1-POWER.dwg
F-T1-FIRE.dwg

## 9. Fail-fast (không tạo nếu sai)
| Mã | Điều kiện |
| insunits | Không phải mm / INSUNITS≠4 |
| open_polyline | Sàn/phòng/đài không đóng |
| exploded_block | Cửa/cột/TB đã nổ |
| unknown_layer | Layer lạ, không phải *-ANNO-* / G-GRID |
| bbox_vs_grid | Bao CAD lệch lưới sẵn > 1 m (chỉ khi đã có grid) |

## 10. Mapping
User sửa trên dialog trước Preview. Overlay dự án:
cad-layer-map.str.project.json
cad-layer-map.arch.project.json
cad-layer-map.mepf.project.json
(future Config/ defaults; overlay cạnh file .rvt hoặc %LOCALAPPDATA%\DSSTech\projects\<hash>\)
```

---

## 2. Layer map — Structure (draft)

Future path: `Config/cad-layer-map.str.json`

```json
{
  "discipline": "structure",
  "version": 1,
  "lengthUnit": "mm",
  "rules": [
    {
      "pattern": "^G-GRID$",
      "kind": "Grid",
      "nameFrom": "nearestText"
    },
    {
      "pattern": "^S-COL-(\\d+)x(\\d+)$",
      "kind": "Column",
      "widthMm": "$1",
      "depthMm": "$2",
      "shape": "rect"
    },
    {
      "pattern": "^S-COL-D(\\d+)$",
      "kind": "Column",
      "diameterMm": "$1",
      "shape": "round"
    },
    {
      "pattern": "^S-COL",
      "kind": "Column",
      "sizeFrom": "blockAttrOrGeometry"
    },
    {
      "pattern": "^S-WALL-(\\d+)$",
      "kind": "StructuralWall",
      "thicknessMm": "$1",
      "structural": true
    },
    {
      "pattern": "^S-BEAM-(\\d+)x(\\d+)$",
      "kind": "Beam",
      "widthMm": "$1",
      "heightMm": "$2"
    },
    {
      "pattern": "^S-BEAM",
      "kind": "Beam",
      "sizeFrom": "nearestText"
    },
    {
      "pattern": "^S-SLAB-(\\d+)$",
      "kind": "Floor",
      "thicknessMm": "$1",
      "structural": true
    },
    {
      "pattern": "^S-SLAB-HOLE",
      "kind": "FloorHole"
    },
    {
      "pattern": "^S-FOOT-(\\d+)x(\\d+)-H(\\d+)$",
      "kind": "IsolatedFoundation",
      "widthMm": "$1",
      "depthMm": "$2",
      "heightMm": "$3"
    },
    {
      "pattern": "^S-PILE-D(\\d+)$",
      "kind": "Pile",
      "diameterMm": "$1"
    },
    {
      "pattern": "^S-STRIP-(\\d+)x(\\d+)$",
      "kind": "StripFoundation",
      "widthMm": "$1",
      "heightMm": "$2"
    }
  ],
  "ignoreLayerPatterns": ["^S-ANNO-", "^\\*-ANNO-", "^Defpoints$"]
}
```

---

## 3. Layer map — Architecture (draft)

Future path: `Config/cad-layer-map.arch.json`

```json
{
  "discipline": "arch",
  "version": 1,
  "lengthUnit": "mm",
  "wallPair": {
    "distanceTolMm": 5,
    "parallelAngleDeg": 3
  },
  "rules": [
    {
      "pattern": "^A-WALL-(\\d+)$",
      "kind": "ArchWall",
      "thicknessMm": "$1",
      "structural": false
    },
    {
      "pattern": "^A-WALL",
      "kind": "ArchWall",
      "thicknessFrom": "parallelPairGap",
      "structural": false
    },
    {
      "pattern": "^A-DOOR-(\\d+)x(\\d+)$",
      "kind": "Door",
      "widthMm": "$1",
      "heightMm": "$2",
      "requireBlock": true,
      "hostWallMaxMm": 200
    },
    {
      "pattern": "^A-DOOR",
      "kind": "Door",
      "sizeFrom": "blockAttrWH",
      "requireBlock": true,
      "hostWallMaxMm": 200
    },
    {
      "pattern": "^A-WIND-(\\d+)x(\\d+)$",
      "kind": "Window",
      "widthMm": "$1",
      "heightMm": "$2",
      "sillMmDefault": 900,
      "requireBlock": true
    },
    {
      "pattern": "^A-WIND",
      "kind": "Window",
      "sillFrom": "attrSILL",
      "sillMmDefault": 900,
      "requireBlock": true
    },
    {
      "pattern": "^A-ROOM$",
      "kind": "Room",
      "nameFrom": "textInsideLoop"
    },
    {
      "pattern": "^A-FLOR-",
      "kind": "FinishFloor",
      "structural": false
    },
    {
      "pattern": "^A-CLNG-OFF-(\\d+)$",
      "kind": "Ceiling",
      "offsetMm": "$1"
    },
    {
      "pattern": "^A-CLNG-",
      "kind": "Ceiling"
    }
  ],
  "skipOverlap": {
    "kind": "ArchWall",
    "against": "StructuralWall",
    "axisTolMm": 50
  },
  "ignoreLayerPatterns": ["^A-ANNO-", "^\\*-ANNO-", "^Defpoints$"]
}
```

---

## 4. Layer map — MEPF (draft)

Future path: `Config/cad-layer-map.mepf.json`

```json
{
  "discipline": "mepf",
  "version": 1,
  "lengthUnit": "mm",
  "centerlineOnly": true,
  "offsetsMm": {
    "coldWaterFromFloor": 300,
    "sanitaryFromFloor": -300,
    "supplyAirFromCeiling": -400
  },
  "rules": [
    {
      "pattern": "^P-CW-D(\\d+)$",
      "kind": "Pipe",
      "system": "Domestic Cold Water",
      "systemPrefix": "CW",
      "diameterMm": "$1"
    },
    {
      "pattern": "^P-HW-D(\\d+)$",
      "kind": "Pipe",
      "system": "Domestic Hot Water",
      "systemPrefix": "HW",
      "diameterMm": "$1"
    },
    {
      "pattern": "^P-SAN-D(\\d+)$",
      "kind": "Pipe",
      "system": "Sanitary",
      "systemPrefix": "SAN",
      "diameterMm": "$1"
    },
    {
      "pattern": "^P-DR-D(\\d+)$",
      "kind": "Pipe",
      "system": "Drainage",
      "systemPrefix": "DR",
      "diameterMm": "$1"
    },
    {
      "pattern": "^P-GAS-D(\\d+)$",
      "kind": "Pipe",
      "system": "Gas",
      "systemPrefix": "GAS",
      "diameterMm": "$1"
    },
    {
      "pattern": "^F-PIPE-D(\\d+)$",
      "kind": "Pipe",
      "system": "Fire Protection",
      "diameterMm": "$1"
    },
    {
      "pattern": "^M-PIPE-D(\\d+)$",
      "kind": "Pipe",
      "diameterMm": "$1"
    },
    {
      "pattern": "^M-DUCT-SUP-(\\d+)x(\\d+)$",
      "kind": "Duct",
      "system": "Supply Air",
      "widthMm": "$1",
      "heightMm": "$2"
    },
    {
      "pattern": "^M-DUCT-RET-(\\d+)x(\\d+)$",
      "kind": "Duct",
      "system": "Return Air",
      "widthMm": "$1",
      "heightMm": "$2"
    },
    {
      "pattern": "^M-DUCT-EXH-D(\\d+)$",
      "kind": "Duct",
      "system": "Exhaust Air",
      "diameterMm": "$1"
    },
    {
      "pattern": "^E-TRAY-(\\d+)$",
      "kind": "CableTray",
      "widthMm": "$1"
    },
    {
      "pattern": "^M-EQP-",
      "kind": "MechanicalEquipment",
      "requireBlock": true
    },
    {
      "pattern": "^P-EQP-",
      "kind": "PlumbingEquipment",
      "requireBlock": true
    },
    {
      "pattern": "^E-EQP-",
      "kind": "ElectricalEquipment",
      "requireBlock": true
    },
    {
      "pattern": "^F-EQP-",
      "kind": "FireEquipment",
      "requireBlock": true
    },
    {
      "pattern": "^F-SPK",
      "kind": "Sprinkler",
      "requireBlock": true
    },
    {
      "pattern": "^E-LITE",
      "kind": "LightingFixture",
      "requireBlock": true
    }
  ],
  "failIfSizeUnparsed": true,
  "ignoreLayerPatterns": ["^M-ANNO-", "^P-ANNO-", "^E-ANNO-", "^F-ANNO-", "^\\*-ANNO-", "^Defpoints$"]
}
```

---

## 5. Overlay (specify now, do not write Config)

- Defaults: three JSON files above in `Config/` when S0 CODE/docs box opens.
- Project overlay: `cad-layer-map.{str|arch|mepf}.project.json` merged **after** defaults (same `pattern` replaces). Wizard DataGrid edits write overlay, not the repo default.
- Regex: .NET, `CultureInvariant`, full layer name match (`^...$` already on patterns).
