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

Write (Read+Create): `create_element`, `create_batch` — một Transaction `"DSSTech MCP create"`, Undo 1 lần. `skipIfExists` mặc định. `dryRun` mặc định khi > 20 ops.

## Cursor mcp.json

```json
{
  "mcpServers": {
    "dsstech-revit": {
      "command": "C:\\\\Users\\\\<USER>\\\\AppData\\\\Roaming\\\\Autodesk\\\\Revit\\\\Addins\\\\2025\\\\QSNormEngine\\\\Dss.RevitMcp.exe"
    }
  }
}
```

## ChatGPT

1. Ribbon **MCP On/Off** → hiện URL + Bearer.  
2. Dán vào connector MCP remote.  
3. `POST /mcp` JSON-RPC `tools/call` name=`ping`.
