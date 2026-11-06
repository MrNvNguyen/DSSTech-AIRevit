# DSSTech MCP Host

## Transports

| Client | How |
|---|---|
| Cursor / Claude Desktop | stdio → `Dss.RevitMcp.exe` → Named Pipe `DSSTech.Mcp.{pid}` |
| ChatGPT | HTTP `http://127.0.0.1:{port}/mcp` + `Authorization: Bearer <token>` |

Token mới mỗi lần **MCP On**. Chỉ bind `127.0.0.1`. Không commit secret.

Session file: `%LOCALAPPDATA%\DSSTech\mcp-session.json` (`pipeName`, `httpUrl`, `on`).

## Tools (Host)

Read: `ping`, `get_document`, `get_levels`, `get_grids`, `get_view`, `list_categories`, `list_types`, `query_by_category` (pageSize ≤ 100).

Write (Read+Create): `create_element`, `create_batch` — một Transaction `"DSSTech MCP create"`, Undo 1 lần; mỗi op một SubTransaction, op lỗi tự rollback riêng. `skipIfExists` mặc định. `dryRun` mặc định khi > 20 ops (chỉ `dryRun:false` cấp batch mới tạo thật). `units`: `mm` (mặc định) hoặc `m`.

Không có delete/modify (`delete_element(s)` trả `forbidden_permission`). Quyền Host hiện cố định `ReadCreate`.

### Category tạo được và `fields`

Điểm là `[x,y,z]` mm. Tên type lấy từ `list_types`.

| Category | fields |
|---|---|
| Levels | `name`, `elevationMm` |
| Grids | `name`, `startMm`, `endMm` |
| Walls | `startMm`, `endMm`, `sourceType`, `thicknessMm?`, `level`, `topLevel?`, `heightMm?`, `structural?`, `locationLine?` |
| Floors | `loops: [[[x,y],…]]`, `floorType`, `level` |
| Ceilings | `loops`, `ceilingType`, `level`, `offsetMm?` |
| Doors / Windows | `familyType` \| `typeMark`, `wallId` \| `xyzMm` (cách tường < 200 mm), `level?`, `swingMm?`, `hingeMm?` |
| Rooms | `xyzMm`, `level`, `name?`, `number?` |
| StructuralColumns | `xyzMm`, `familyType` \| `widthMm`+`depthMm`, `sourceFamily?`, `rotationDeg?`, `baseLevel`, `topLevel?`, `mark?` |
| StructuralFraming | `startMm`, `endMm`, `familyType` \| `widthMm`+`depthMm`, `sourceFamily?`, `level` |
| StructuralFoundation | `xyzMm`, `familyType` \| `typeMark` \| `widthMm`+`depthMm` \| `diameterMm`, `kind?`, `level`, `embedMm?`, `rotationDeg?`, `mark?` |
| Pipes | `startMm`, `endMm`, `pipeType`, `systemType`, `level`, `diameterMm?` |
| Ducts | `startMm`, `endMm`, `ductType`, `systemType`, `level`, `widthMm`+`heightMm` \| `diameterMm` |
| CableTrays | `startMm`, `endMm`, `type`, `level`, `widthMm?` |
| MechanicalEquipment, ElectricalEquipment, PlumbingFixtures, LightingFixtures, Sprinklers, GenericModels | `xyzMm`, `familyType`, `level`, `rotationDeg?` |

Chưa hỗ trợ: stairs, rebar, fittings, in-place.

## Cursor mcp.json

```json
{
  "mcpServers": {
    "dsstech-revit": {
      "command": "C:\\Users\\<USER>\\AppData\\Roaming\\Autodesk\\Revit\\Addins\\2025\\DSSTech\\Dss.RevitMcp.exe"
    }
  }
}
```

## ChatGPT

1. Ribbon **MCP On/Off** → hiện URL + Bearer.  
2. Dán vào connector MCP remote.  
3. `POST /mcp` JSON-RPC `tools/call` name=`ping`.
