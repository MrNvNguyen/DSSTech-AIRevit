# WAVE_CARD — S1-CAD-PARSE-PREFLIGHT

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: S1-CAD-PARSE-PREFLIGHT
  product_problem: "Linked CAD cannot be checked against spec; create would guess geometry."
  measured_owner: cad2bim-parse
  hypothesis: "Reading ImportInstance/CAD-link geometry into CadGeometry plus a PreflightEngine (INSUNITS, open polyline, exploded block, unknown layer, bbox vs grid >1 m if grids exist) fails fast with a Vietnamese checklist and never opens a create Transaction."
  prediction:
    metric: preflight_fail_fast
    expected_range: "Good DXF/link: preflight pass with entity counts; bad INSUNITS or open area polyline or exploded door block: status fail, createdElementCount=0; Host still has zero DWG parse types"
    written_before_measurement: true
  falsifier: "Preflight fail still creates walls; OR Core references RevitAPI; OR Host parses DWG; OR CAD module opens a TCP port."
  one_allowed_lever: "Parse + preflight only. No Level.Create / Wall.Create. No wizard chrome beyond a Preflight report if needed for measurement (prefer command that shows TaskDialog; CAD panel buttons are S2b)."
  theoretical_upper_bound: "Closes fail-fast gate. Closes 0% of stepwise create."
  contracts_at_risk:
    - preflight_fail_fast
    - host_does_not_parse_dwg
    - cad_does_not_open_ports
    - core_revit_api_free
    - mcp_contracts_no_revit_api
    - no_large_dwg_rvt_in_git
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "Cost & Norm + BOQ still present"
    - "Host create (if R4 shipped) unused by this command"
    - "QSNormEngine.Core still Revit-free"
  attempt_number: 0/3
  stop_rule: "Stop if this wave calls Wall.Create. Cap 3. Fixture must be small DXF + SHA, not a 50 MiB DWG in git."
  code_authorized: false
```

## Blueprint (needs S0 spec drafts; CODE after authorize)

Prefer **no CAD panel yet** (S2b). Measurement UX: existing pattern `IExternalCommand` behind a **temporary** button is OUT of R0a/R0b; for S1, either (a) MCP tool `cad_preflight` registered by a provider that only returns JSON, or (b) a command class compiled but **not** ribbon-bound until S2b. Prefer **(a) MCP + a debug command** `CadPreflightCommand` invoked from Revit Add-In Manager **or** ribbon **CAD / Preflight** only if S2b is not the sole panel owner.

**Practical split to keep bars:** S1 ships libraries + `CadPreflightCommand` **without** adding the full CAD panel (Link/KT/KC/MEPF). If a single **Preflight** button is required to measure in-Revit, it is this wave’s only new button — document it. **Do not** add Link/Kiến trúc/Kết cấu/MEPF here.

### Projects / classes

| Item | RevitAPI | Role |
|---|---|---|
| `src/Dss.Cad2Bim.Core/Dss.Cad2Bim.Core.csproj` | **no** | `CadGeometry`, `CadEntity` (Line, Arc, Polyline, Circle, Insert/Block, Text), `DxfParser`, `PreflightEngine`, `PreflightReport` |
| `src/Dss.Cad2Bim.Core/LayerMap.cs` | no | Load JSON maps (read from file path; default maps later Config) |
| `QSNormEngine.Revit/Cad/ImportInstanceGeometryReader.cs` | **yes** | Convert selected `ImportInstance` / CAD link to `CadGeometry` |
| `QSNormEngine.Revit/Cad/CadPreflightCommand.cs` | yes | Selection → reader → `PreflightEngine.Run` → TaskDialog VN |
| `Dss.Cad2Bim.Core` test fixture | no | Small DXF in `SampleInputs/cad/` + SHA in `BINARY-NOT-IN-GIT.md` if binary stays local |

**Host (`Dss.McpHost`) must not reference Cad2Bim.Core.** Optional: an `IMcpToolProvider` in the **add-in** (`QSNormEngine.Revit`) registers `cad_preflight` that calls Core — add-in may reference both Host and Core. That is not Host parsing DWG.

### ImportInstance geometry (Revit API)

1. User selects an `ImportInstance` (DWG link or import). `CADLinkType` / `ImportInstance.GetTypeId()`.
2. `Options { ComputeReferences = false, IncludeNonVisibleObjects = false, View = activeView }`.
3. `importInstance.get_Geometry(options)` → recurse `GeometryInstance.GetInstanceGeometry()` / `GetSymbolGeometry()`.
4. Map:
   - `Line` → CadLine (mm via `UnitConv`)
   - `Arc` → CadArc
   - `PolyLine` → CadPolyline (`IsClosed` if first≈last or API flag)
   - `Mesh` / `Solid` → ignore or warn `unsupported_geom`
   - Graphics style / layer: `geom.GraphicsStyleId` → `Category` / `ImportInstance` layer name via `GetLayerNames` / `GraphicsStyle.GraphicsStyleCategory.Name` (verify on host 2025–2027; if layer names unavailable, use `CADLinkType` + fallback DXF parse of `ExternalFileReference.GetPath()` — **file parse is Core DxfParser**, still not Host).
5. Blocks: `GeometryInstance` with transform → `CadInsert { insertionMm, rotationDeg, blockName }`. If DWG was exploded, inserts are missing and exploded lines appear → preflight **exploded_block** when map expects `A-DOOR-*` / `S-COL*` inserts and none found while door-like lines exist.

**DXF fixture path (no Revit):** `DxfParser.Parse(path)` for CLI/unit tests. Same `CadGeometry`. Do not add SQLite. Do not commit large DWG.

### PreflightEngine checks (fail-fast → no create)

| Id | Hard fail |
|---|---|
| `insunits` | Not mm / not 4. If DWG units unknown → fail (do not assume). |
| `open_polyline` | Entity on area layers (`S-SLAB-*`, `A-ROOM`, `S-FOOT-*`, `A-FLOR-*`, `A-CLNG-*`) not closed. |
| `exploded_block` | Expected INSERT for door/col/eqp patterns, zero inserts, line-only candidates. |
| `unknown_layer` | Layer not matching any map pattern and not `*-ANNO-*`, `G-GRID`, `Defpoints`, `0`. |
| `bbox_vs_grid` | If `doc` has ≥1 Grid: Cad bbox vs grid envelope > **1 m** in X or Y. If no grids: skip this check. |

Report: `{ ok, failures[], warnings[], countsByKind }` Vietnamese messages for TaskDialog. **`createdElementCount` must be 0.** No `Transaction`.

### DoD (product-visible)

1. Linked in-spec CAD (or fixture DXF via reader) → preflight **Pass** dialog with counts.
2. Same file with open slab polyline **or** INSUNITS wrong → **Fail**, checklist, **zero** new Revit elements.
3. Screenshot of fail dialog.
4. SHA of fixture DXF recorded; file not in git if > policy; pointer `BINARY-NOT-IN-GIT.md`.
5. Cost & Norm + BOQ still present.

### Regression

R4 create unused. Core (QS) untouched. Cad2Bim.Core has no socket types.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `S1-CAD-PARSE-PREFLIGHT`.
