# WAVE_CARD — R1b-HTTP-SSE-CHATGPT

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: R1b-HTTP-SSE-CHATGPT
  product_problem: "ChatGPT cannot attach stdio to Revit; there is no localhost MCP HTTP/SSE with a per-On bearer."
  measured_owner: mcp-transport
  hypothesis: "HttpListener or Kestrel bound to 127.0.0.1 only, SSE MCP endpoint, bearer token generated each On and shown on the On dialog, is sufficient for ChatGPT (or a curl stand-in) to ping when On and fail when Off — without making HTTP a R0b bar."
  prediction:
    metric: localhost_sse_bearer
    expected_range: "GET/POST 127.0.0.1:<port>/mcp with Authorization Bearer <token> ping OK; wrong token 401; bind not 0.0.0.0; Off closes port; token not in repo"
    written_before_measurement: true
  falsifier: "Listens on 0.0.0.0; OR secret committed; OR R0b DoD rewritten to require ChatGPT; OR CAD opens this port."
  one_allowed_lever: "HTTP/SSE localhost + bearer UI. No new create categories. Not a R0b ping bar."
  theoretical_upper_bound: "Closes ChatGPT transport. Does not close R4-CATALOG."
  contracts_at_risk:
    - mcp_http_bind_localhost
    - mcp_default_on
    - mcp_off_kills_ai_not_ribbon_cad
    - cad_does_not_open_ports
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "R0b pipe ping still works without HTTP"
    - "R1a stdio still works"
    - "Cost & Norm + BOQ"
  attempt_number: 0/3
  stop_rule: "Stop if bind is non-localhost or ngrok is added. Cap 3. CAD modules must not start Kestrel."
  code_authorized: false
```

## Blueprint (after R0b + R1a; **not** on Host ping critical path)

R0b DoD remains: **local pipe ping**. This wave is additive.

### Classes

| Class | Role |
|---|---|
| `Dss.McpHost/Http/McpHttpServer.cs` | `HttpListener` prefix `http://127.0.0.1:{port}/` **or** in-process Kestrel `Listen(IPAddress.Loopback, port)` |
| `Dss.McpHost/Http/BearerToken.cs` | `RandomNumberGenerator` 32 bytes, Base64Url; **memory only** |
| `Dss.McpHost/Http/McpSseHandler.cs` | MCP Streamable HTTP / SSE as required by ChatGPT remote MCP (implement the revision Cursor/ChatGPT docs specify at CODE time; fallback: `POST /mcp` JSON-RPC + `GET /mcp/sse` events) |
| `Dss.McpHost/McpClientsDialog.cs` | Shows Cursor (pipe), Claude, ChatGPT URL + token + Copy buttons |

Prefer **HttpListener** first (no extra ASP.NET package in the add-in) unless SSE needs Kestrel. If Kestrel: do **not** add it to `QSNormEngine.Core`. Package on `Dss.McpHost` only.

### Bind + token

- `IPAddress.Loopback` only. Fail startup of HTTP if bind ≠ loopback.
- Port: ephemeral or `17832` if free; store in session JSON `httpUrl`.
- Token **new each On**. Off invalidates.
- Dialog on On (in addition to “MCP đang bật”):  
  `URL: http://127.0.0.1:17832/mcp`  
  `Token: ****` (show once, copy).  
  **Never** write token to `mcp-host.log` in full (log `tokenIssued=true`).
- ChatGPT connector: user pastes URL + bearer. No repo secret.

### AuthZ

Missing/wrong `Authorization: Bearer` → 401. CORS not needed for ChatGPT server-side; do not `Access-Control-Allow-Origin: *` on a tool endpoint.

### DoD (product-visible)

1. On → dialog shows URL + token; `curl`/`Invoke-WebRequest` from **same machine** ping OK.
2. Wrong token → 401.
3. Off → connection fail.
4. `netstat` shows `127.0.0.1:port` not `0.0.0.0`.
5. Pipe stdio ping still OK independently.
6. Cost & Norm + BOQ still present.

If ChatGPT account cannot be used in QA: Layer B = curl SSE/JSON-RPC; ChatGPT UI = `NOT_MEASURED` (do not consume attempt if curl measured the falsifier). Record honestly.

### Regression

CAD does not listen. QS panels unchanged.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `R1b-HTTP-SSE-CHATGPT`.
