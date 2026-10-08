# WAVE_CARD — R2-REGISTRY

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: R2-REGISTRY
  product_problem: "Host has no module registry; later CAD tools would be hardcoded into Host, violating host_does_not_parse_dwg."
  measured_owner: mcp-registry
  hypothesis: "An IMcpToolProvider registry on the Host, with Host built-in ping only this wave, lets CAD modules register later without Host gaining parse code."
  prediction:
    metric: registry_isolation
    expected_range: "tools/list = Host built-ins only; a test provider with ModuleId dss.test can register in debug and appear; Host project contains zero CadGeometry/Dxf/ImportInstance types"
    written_before_measurement: true
  falsifier: "Host csproj references Dss.Cad2Bim.*; OR tools are a hardcoded switch with CAD names; OR IMcpToolProvider lives in a project that references RevitAPI (Contracts must not)."
  one_allowed_lever: "IMcpToolProvider + Host registry. No CAD parse. No create catalog. No new ribbon CAD."
  theoretical_upper_bound: "Closes extension point. Does not close read/create UX."
  contracts_at_risk:
    - host_does_not_parse_dwg
    - mcp_contracts_no_revit_api
    - cad_does_not_open_ports
    - qs_panels_kept
  qa_tier: Q1
  qa_batch: wave:R2-REGISTRY
  q2_class: n/a
  regression_scope:
    - "ping On/Off still works (R0b/R1a)"
    - "Cost & Norm + BOQ still present"
  attempt_number: 0/3
  stop_rule: "Stop if registry wave ships Structure/Arch parse. Cap 3. Contracts must stay RevitAPI-free."
  code_authorized: false
```

## Blueprint (CODE only after STRATEGY_AUTHORIZE_PHASE; needs R0b)

### Contracts (no RevitAPI)

`src/Dss.McpContracts/IMcpToolProvider.cs`:

```csharp
namespace Dss.McpContracts;

public interface IMcpToolProvider
{
    string ModuleId { get; } // e.g. "dss.host", "dss.cad2bim.structure"
    IReadOnlyList<McpTool> Tools { get; }
    Task<McpResult> InvokeAsync(string tool, JsonElement args, McpCallContext ctx, CancellationToken ct);
}

public sealed record McpTool(string Name, string Description, string InputSchemaJson);

public sealed class McpCallContext
{
    public string? DocumentTitle { get; init; }
    public string? DocumentPath { get; init; }
    public McpPermission Permission { get; init; }
    public int RevitYear { get; init; }
    // Opaque. Host fills from UIApplication inside Execute. Cad modules must not need Document.
}

public sealed record McpResult(bool Ok, string? ErrorCode, string? Message, JsonElement? Data);
```

**No** `Autodesk.Revit` types on this interface. CAD modules (S3–S5) implement `IMcpToolProvider` by returning `CreateOperation` JSON that Host’s **built-in create dispatcher** executes — or they only expose `cad_interpret_*` that returns operations without touching Revit.

### Host registry classes

| Class | Role |
|---|---|
| `Dss.McpHost/McpToolRegistry.cs` | `Register(IMcpToolProvider)`; duplicate `tool.Name` → throw at startup (fail fast). |
| `Dss.McpHost/HostBuiltInProvider.cs` | `ModuleId = "dss.host"`; tools this wave: `ping` (+ `tools/list` internals). |
| `Dss.McpHost/McpDispatcher.cs` | `Execute` looks up name → provider.Invoke. Permission gate: unknown tool → `-32601`. |
| `QSNormEngine.Revit/App.cs` | `registry.Register(new HostBuiltInProvider())` at startup. **Do not** register Cad2Bim yet (projects may not exist). |

Later waves (do not implement now):

```text
registry.Register(new StructureToolProvider()); // S3
registry.Register(new ArchToolProvider());      // S4
registry.Register(new MepfToolProvider());      // S5
```

### Host contains ZERO CAD parse

Layer A grep on `src/Dss.McpHost/**`: no `ImportInstance`, `CadGeometry`, `Dxf`, `INSUNITS`, `S-COL`. Host create primitives (R4) are Revit category APIs, not DWG.

### `tools/list` mapping

Sidecar (R1a) forwards `tools/list` to Host:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/list",
  "params": {}
}
```

Host returns MCP-shaped `{ tools: [ { name, description, inputSchema } ] }`.

### DoD (product-visible)

1. Cursor `tools/list` shows **ping** (and only Host built-ins this wave).
2. Engineering: `Dss.McpContracts.csproj` has no Revit HintPath.
3. Engineering: `Dss.McpHost` has no project reference to `Dss.Cad2Bim.*`.
4. Cost & Norm + BOQ still on tab. Ping On/Off still holds.

Optional debug-only `dss.test` provider: **OUT of Release**; if used, `#if DEBUG` and must not ship.

### Regression

Do not break framed pipe. Do not add CAD ribbon.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `R2-REGISTRY`.
