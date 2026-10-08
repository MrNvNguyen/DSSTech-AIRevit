# WAVE_CARD — R1a-STDIO-CURSOR

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: R1a-STDIO-CURSOR
  product_problem: "Cursor/Claude cannot speak MCP stdio to Revit; only a raw pipe exists after R0b."
  measured_owner: mcp-transport
  hypothesis: "A Dss.RevitMcp.exe that speaks MCP JSON-RPC on stdio and forwards framed JSON to the R0b Named Pipe is sufficient for Cursor tools/call ping when Host is On, and fail when Off. Same exe works for Claude Desktop."
  prediction:
    metric: cursor_stdio_ping
    expected_range: "Cursor MCP dsstech-revit: initialize + tools/list includes ping + tools/call ping ok when Revit On; when Off, sidecar exits or returns error within 5s; Claude config uses the same command path"
    written_before_measurement: true
  falsifier: "Cursor cannot ping while Host On; OR ping succeeds while Host Off; OR sidecar references RevitAPI; OR ChatGPT SSE is required for this DoD."
  one_allowed_lever: "Stdio sidecar exe + mcp.json snippet. No HTTP/SSE. No new create tools."
  theoretical_upper_bound: "Closes Cursor/Claude transport. Does not close ChatGPT or create_batch."
  contracts_at_risk:
    - mcp_default_on
    - mcp_off_kills_ai_not_ribbon_cad
    - third_party_mcp_not_host
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "R0b On/Off still holds"
    - "Cost & Norm + BOQ still present"
  attempt_number: 0/3
  stop_rule: "Stop if lever adds Kestrel/ChatGPT. Cap 3. Sidecar must not load RevitAPI.dll."
  code_authorized: false
```

## Blueprint (CODE only after STRATEGY_AUTHORIZE_PHASE; needs R0b shipped)

### Projects / classes

| Item | Notes |
|---|---|
| `src/Dss.RevitMcp/Dss.RevitMcp.csproj` | `OutputType=Exe`, `TargetFramework=net8.0` (not `net8.0-windows` unless needed). **No RevitAPI.** ProjectReference `Dss.McpContracts` only. |
| `src/Dss.RevitMcp/Program.cs` | stdin/stdout MCP loop. |
| `src/Dss.RevitMcp/PipeClient.cs` | Connect `NamedPipeClientStream` using session file. |
| `src/Dss.RevitMcp/McpStdioServer.cs` | JSON-RPC: `initialize`, `notifications/initialized`, `tools/list`, `tools/call`, `ping` (MCP ping if spec requires). |
| `scripts/install-revit.ps1` | Copy `Dss.RevitMcp.exe` (+ deps) into `%APPDATA%\Autodesk\Revit\Addins\<year>\QSNormEngine\`. |
| `scripts/build-revit.ps1` | `dotnet publish` sidecar once (not per host year) OR build net8.0 and copy into each host output. Prefer **one** net8.0 exe copied to every host folder. |
| `QSNormEngine.sln` | Add exe project. |

### MCP protocol notes

Implement a **minimal MCP server** on stdio (newline-delimited JSON-RPC as MCP spec, **not** the length-prefix used on the pipe). Sidecar translates:

```text
stdin JSON-RPC (MCP) → PipeClient.SendFramed(same or mapped payload) → stdout JSON-RPC
```

Required methods this wave:

| Method | Behavior |
|---|---|
| `initialize` | `protocolVersion` (use `2024-11-05` or current Cursor-supported); `serverInfo.name = "DSSTech Revit MCP"`; `capabilities.tools = {}`. |
| `tools/list` | At least `{ name: "ping", description: "Health check the DSSTech Host" }`. Forward to Host if Host already lists tools; else sidecar can answer ping locally **after** a pipe ping so Off is detected. Prefer: always forward `tools/list` to Host so R2/R3 do not need sidecar updates. R0b Host must therefore answer `tools/list` (add in this wave if R0b only had raw ping — **allowed as part of transport**, keep Host tool set = ping only). |
| `tools/call` name=ping | Forward; map Host result to MCP `content: [{ type: "text", text: "..." }]`. |
| Host Off / no session file | Exit code 0 with JSON-RPC error `-32000 mcp_off`, or fail initialize. Do **not** hang > 5 s. Connect timeout 2 s. |

Discovery: `%LOCALAPPDATA%\DSSTech\mcp-session.json`. If missing: error “Revit DSSTech MCP is Off or Revit is not running.”

**Do not** start Revit from the sidecar.

### Cursor `mcp.json` snippet (path placeholder)

User file (do not commit secrets). Document in WAVE evidence / later `docs/MCP_HOST.md` (not this P0):

```json
{
  "mcpServers": {
    "dsstech-revit": {
      "command": "C:\\Users\\<USER>\\AppData\\Roaming\\Autodesk\\Revit\\Addins\\<YEAR>\\QSNormEngine\\Dss.RevitMcp.exe",
      "args": []
    }
  }
}
```

`<YEAR>` = 2024 \| 2025 \| 2026 \| 2027 matching the running host. Cursor does not need `env` if session file is used.

### Claude Desktop

Same exe:

```json
{
  "mcpServers": {
    "dsstech-revit": {
      "command": "C:\\Users\\<USER>\\AppData\\Roaming\\Autodesk\\Revit\\Addins\\<YEAR>\\QSNormEngine\\Dss.RevitMcp.exe",
      "args": []
    }
  }
}
```

File: `%APPDATA%\Claude\claude_desktop_config.json` (typical). Same command; no second binary.

### DoD (product-visible)

1. Revit On + Cursor configured with placeholder path filled → MCP server connected → **ping OK**.
2. MCP Off in ribbon → Cursor ping **fails** (error visible in MCP logs).
3. Claude Desktop using **same exe path** can ping (if Claude installed; if not, record `NOT_MEASURED` for Claude only — Cursor is the bar).
4. `Dss.RevitMcp.exe` has no `RevitAPI` reference (Layer A: `ildasm`/csproj).
5. Cost & Norm + BOQ still present. No ChatGPT URL required.

### Regression

R0b local pipe ping still works. QS panels unchanged.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `R1a-STDIO-CURSOR`.
