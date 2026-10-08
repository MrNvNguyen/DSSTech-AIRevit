# WAVE_CARD — R0b-MCP-PIPE-PING

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: R0b-MCP-PIPE-PING
  product_problem: "No in-process MCP Host. AI cannot ping this add-in; user has no On/Off on the DSSTech tab."
  measured_owner: mcp-host
  hypothesis: "A NamedPipeServerStream plus one ExternalEvent-queued ping tool, started On at OnStartup with a ribbon toggle, is sufficient for local ping OK when On and ping fail when Off. ChatGPT HTTP is not required."
  prediction:
    metric: mcp_ping_on_off
    expected_range: "On: local ping returns ok+pid within 2s; Off: ping fails (connection refused or explicit mcp_off); toast/dialog 'MCP đang bật' at startup; mcp-host.log has start/stop lines"
    written_before_measurement: true
  falsifier: "On but ping fails on a valid idle Revit document; OR Off but ping still succeeds; OR Host parses DWG; OR HTTP/SSE/ChatGPT is required for this DoD; OR Cost & Norm / BOQ missing."
  one_allowed_lever: "McpHost pipe + ExternalEvent ping + ribbon MCP On/Off default On. No stdio exe (R1a). No HTTP (R1b). No create_*. No CAD."
  theoretical_upper_bound: "Closes Host ping On/Off. Does not close Cursor stdio or create_batch."
  contracts_at_risk:
    - mcp_default_on
    - mcp_off_kills_ai_not_ribbon_cad
    - host_does_not_parse_dwg
    - third_party_mcp_not_host
    - mcp_http_bind_localhost
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "Cost & Norm + BOQ still present"
    - "Tab still DSSTech (depends on R0a having shipped in a prior authorized box)"
    - "No second .addin"
  attempt_number: 0/3
  stop_rule: "Stop if ChatGPT/SSE is bundled into this lever. Stop if pipe binds a TCP port. Cap 3 measured attempts. Do not treat AUTOM8LABS as Host."
  code_authorized: false
```

## Blueprint (CODE only after STRATEGY_AUTHORIZE_PHASE for this box)

Depends on **R0a** having renamed the tab (separate authorize). If R0a not shipped, do not hide a tab rename inside this lever.

### Projects / classes to add

| Item | Notes |
|---|---|
| `src/Dss.McpContracts/Dss.McpContracts.csproj` | `net8.0;net48`. **No RevitAPI.** Types: `McpRequest`, `McpResponse`, `McpError`, `McpPermission` (`Read`, `ReadCreate`, `ReadCreateModify`), `McpSessionInfo`. Ping payload can live here. Full `IMcpToolProvider` may land in R2; R0b may use an internal `IMcpTool` stub — if so, R2 replaces the stub, does not fork a second registry. |
| `src/Dss.McpHost/Dss.McpHost.csproj` | Library, **same TFM/RevitAPI** as `QSNormEngine.Revit` (build inside `scripts/build-revit.ps1` loop with `RevitApiPath`, `RevitHostLabel`). `UseWPF` false unless ribbon helpers need it. |
| `src/Dss.McpHost/McpHostService.cs` | Start/Stop pipe; session file; log; permission default `ReadCreate`. |
| `src/Dss.McpHost/McpNamedPipeServer.cs` | `NamedPipeServerStream`. |
| `src/Dss.McpHost/McpRequestQueue.cs` | Concurrent queue + `TaskCompletionSource<McpResponse>` per request. |
| `src/Dss.McpHost/McpExternalEventHandler.cs` | `IExternalEventHandler`. |
| `src/Dss.McpHost/Tools/PingTool.cs` | Returns `{ ok, pid, revitYear, documentTitle, mcpOn: true }`. |
| `src/Dss.McpHost/McpRibbon.cs` | Panel **MCP**: buttons On/Off (or one toggle), later Quyền/Clients placeholders **disabled** or omitted until R1b/R2. Prefer **On/Off + status text** only this wave. |
| `src/Dss.McpHost/McpLog.cs` | `%LOCALAPPDATA%\DSSTech\mcp-host.log` (rolling 1 MB × 3). |
| `QSNormEngine.Revit/App.cs` | After existing panels: `McpHostService.Instance.OnStartup(application)` — **default Start()**. Wire MCP panel. `OnShutdown` → Stop. |
| `QSNormEngine.sln` | Add Contracts + Host (when CODE opens). |
| `QSNormEngine.Revit.csproj` | `ProjectReference` to `Dss.McpHost`. |

**Do not** add `Dss.RevitMcp.exe` here (R1a). **Do not** add Kestrel (R1b).

### Pipe protocol (so R1a is a thin forwarder)

- Name: `DSSTech.Mcp.{Environment.ProcessId}` → `\\.\pipe\DSSTech.Mcp.{pid}`.
- Direction: `InOut`. `PipeTransmissionMode.Byte`.
- Framing: **4-byte little-endian length** + UTF-8 JSON object (avoid NDJSON escaping fights). One request → one response. Max payload 1 MiB; reject larger.
- JSON body (R0b minimum):

```json
{ "jsonrpc": "2.0", "id": 1, "method": "tools/call", "params": { "name": "ping", "arguments": {} } }
```

Response: `{ "jsonrpc": "2.0", "id": 1, "result": { "ok": true, "pid": 1234, "documentTitle": "..." } }`

Also accept a non-MCP test helper `method: "dss.ping"` for a 20-line PowerShell client before the sidecar exists.

- ACL: `PipeSecurity` / `PipeAccessRule` current user only (`WellKnownSidType.BuiltinUsersSid` is too wide; use `WindowsIdentity.GetCurrent().User`).
- Loop: after each client disconnect, `WaitForConnection` again while On. Off: `Dispose` server; in-flight TCS complete with error `mcp_off`.

### ExternalEvent (Revit idle thread)

```text
OnStartup (Revit thread):
  _handler = new McpExternalEventHandler(_queue);
  _externalEvent = ExternalEvent.Create(_handler);  // required here
  _pipeThread = Task.Run(PipeLoop);

PipeLoop (background):
  read framed JSON
  var tcs = new TaskCompletionSource<McpResponse>(RunContinuationsAsynchronously);
  _queue.Enqueue(new McpWorkItem(request, tcs));
  _externalEvent.Raise();
  wait tcs.Task (timeout 30s)
  write framed response

Execute(UIApplication uiApp):
  while TryDequeue:
    if ActiveUIDocument is null → error "no_active_document" (ping may still return ok with documentTitle null — specify: ping is allowed without document, create_* is not)
    dispatch ping
    tcs.SetResult
```

Rebind: `ControlledApplication.DocumentOpened` / `DocumentClosing` / `ViewActivated` update `McpSessionInfo` only. **Do not Stop the pipe** on `.rvt` switch. Never store `Document` on the handler field.

### Ribbon + default On

- `OnStartup`: create panel `"MCP"` on tab `DSSTech`. Toggle **MCP On/Off**. Start host **before** returning `Result.Succeeded`.
- Toast: `TaskDialog` title `DSSTech` main instruction **“MCP đang bật”** content “AI có thể kết nối. Bấm MCP Off trên ribbon nếu không muốn.” Checkbox “Không hiện lại” → `Roaming\DSSTech\ui.json`. Do **not** block startup more than one dialog.
- Icon: green when On, grey when Off (`RibbonIcons` pattern; new `icon-mcp-on` / `icon-mcp-off` PNGs via `scripts/generate-ribbon-icons.ps1` if needed).
- Off: Stop pipe, grey icon, log `mcp stop`.

### Local ping client (QA, not a product exe)

PowerShell in evidence (not committed as a second add-in): open `NamedPipeClientStream(".", "DSSTech.Mcp.{pid}")`, send ping frame. Read `mcp-session.json`:

```json
{
  "pid": 1234,
  "pipeName": "DSSTech.Mcp.1234",
  "revitYear": 2026,
  "startedUtc": "...",
  "permission": "ReadCreate",
  "on": true
}
```

Path: `%LOCALAPPDATA%\DSSTech\mcp-session.json`. Sidecar (R1a) reads this file.

### DoD (product-visible)

1. Start Revit + add-in → dialog/toast **MCP đang bật**; ribbon MCP control shows On.
2. Local ping → **OK**.
3. Click Off → ping **fails**; icon grey.
4. Click On again → ping OK; new session file (pid same, token/nonce new if any).
5. `mcp-host.log` contains start/stop.
6. **No** ChatGPT, **no** `create_element`, **no** CAD buttons required this wave.
7. Cost & Norm + BOQ still work.

### Regression

QS commands unchanged. W0 write-back unchanged. Do not call `Wall.Create`. Do not reference AUTOM8LABS.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `R0b-MCP-PIPE-PING`.
