# WAVE_CARD — S2-DATUM-SKIP-OR-CREATE

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: S2-DATUM-SKIP-OR-CREATE
  product_problem: "Missing levels/grids block CAD; existing datums would duplicate if create always runs."
  measured_owner: cad2bim-datum
  hypothesis: "Reusing Host create_level/create_grid with skipIfExists (Level: name OR elevation±5 mm; Grid: name OR line <50 mm and <2°) from G-GRID+text / CSV / MCP is sufficient to skip existing datums and create only missing ones."
  prediction:
    metric: datum_skip_or_create
    expected_range: "File with Level 'Tầng 1' at 0: create same name/elev → skipped=1 created=0; empty file: G-GRID two lines + two names → 2 grids created; second run skipped=2"
    written_before_measurement: true
  falsifier: "Duplicates of same grid name; OR skip uses 10 mm for levels instead of ±5 mm name/elev; OR Structure module calls Grid.Create directly; OR Host parse of DWG."
  one_allowed_lever: "Datum skip-or-create via Host primitives + CAD G-GRID/CSV intent in Cad2Bim.Core (no wizard UI beyond existing preflight). Wizard step 0 is S2b."
  theoretical_upper_bound: "Closes datum contract. Does not close stepwise type wizard."
  contracts_at_risk:
    - level_grid_skip_if_exists
    - create_via_category
    - host_does_not_parse_dwg
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "Host skipIfExists for walls still 10 mm (do not overwrite with 50 mm)"
    - "Cost & Norm + BOQ"
  attempt_number: 0/3
  stop_rule: "Stop if lever builds walls. Cap 3. Must call Host create_* not Grid.Create in Cad2Bim.Structure."
  code_authorized: false
```

## Blueprint (needs S1 + R4-PILOT Host create for levels/grids)

If R4-PILOT not yet authorized, this card **blocks** (report-up) — do not implement a second `Level.Create` path.

### Classes

| Class | Role |
|---|---|
| `Dss.Cad2Bim.Core/Datum/GridFromCad.cs` | Lines on `G-GRID` + nearest text < 300 mm → `{ name, startMm, endMm }` |
| `Dss.Cad2Bim.Core/Datum/LevelFromCsv.cs` | CSV `name,elevationMm` (UTF-8) |
| `Dss.Cad2Bim.Core/Datum/LevelFromCadText.cs` | Text patterns `Tầng 1,0` / `Level,mm` |
| `QSNormEngine.Revit/Cad/DatumApplyCommand.cs` | Builds `CreateOperation[]` → `Host.Invoke create_batch` (in-process, not pipe) |
| Host `SkipIfExists` | **Same methods** as R4: Level ±5 mm / name; Grid 50 mm / 2° / name |

`Dss.Cad2Bim.Structure` must **not** own Grid.Create. Intent may live in Core; Structure S3 **consumes** existing grids.

### Skip rules (copy to Host; single implementation)

```text
Level exists if:
  NormalizedName equal (trim, ignore case) OR |elev_mm - existing| <= 5

Grid exists if:
  NormalizedName equal OR
  (Distance between infinite lines < 50 mm AND included angle < 2°)
  Segment overlap not required for skip (avoid duplicates of slightly shorter CAD lines)
```

Angle: `acos(clamp(dot(u,v),-1,1))` in degrees.

### Sources (priority)

1. MCP JSON `{ name, elevationMm }` / `{ name, start, end }` — already R4.
2. CSV path user picks.
3. CAD layer `G-GRID` + text.

Empty CAD grids + empty CSV + existing Revit datums → **silent skip** (nothing to create). Missing all → report “không có nguồn trục/tầng” — **not** a hard product fail (CHARTER: do not block the file).

### In-process Host call

Wizard/commands must not `new Transaction` + `Grid.Create`. Pattern:

```csharp
var ops = DatumPlanner.Plan(cad, csv, existingSnapshot);
await McpHostService.Instance.InvokeLocalAsync("create_batch", opsJson, uiApp);
```

`InvokeLocalAsync` enqueues the **same** `McpExternalEventHandler` path (or calls dispatcher if already on Revit thread — if command is already API thread, call `McpDispatcher.Execute` directly **without** Raise to avoid deadlock). Document: **ExternalCommand = already Revit thread → direct dispatcher. Pipe thread → Raise.**

### DoD (product-visible)

1. Template with levels/grids: run datum apply from CAD G-GRID matching those names → **0 created**, skip reasons visible.
2. Blank project: two CAD grids → **2 created** via Host transaction `"DSSTech MCP create"` (or `"DSSTech CAD confirm"` if you add an alias — one name, keep W1 class).
3. Second run → skip 2.
4. Cost & Norm + BOQ still present.

### Regression

Do not change wall skip 10 mm. Do not expand QS write-back.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `S2-DATUM-SKIP-OR-CREATE`.
