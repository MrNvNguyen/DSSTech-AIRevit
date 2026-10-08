# WAVE_CARD — S3-STRUCTURE

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: S3-STRUCTURE
  product_problem: "Linked structural CAD cannot become columns/walls/beams/slabs through the wizard."
  measured_owner: cad2bim-structure
  hypothesis: "Dss.Cad2Bim.Structure maps CAD (column→wall→beam→slab) to Host CreateOperation with grid snap <150 mm and never calls Wall.Create itself; that is sufficient for one framing DWG floor after preview/confirm."
  prediction:
    metric: structure_host_only_create
    expected_range: "Order column then S-WALL then S-BEAM then S-SLAB; columns within 150 mm of grid intersection snapped; Cad2Bim.Structure.csproj has no RevitAPI; grep Structure folder has no Wall.Create"
    written_before_measurement: true
  falsifier: "Structure library calls Wall.Create/Floor.Create; OR Host parses DWG; OR Arch/MEPF shipped in same lever; OR open polyline still creates a floor."
  one_allowed_lever: "Structure intent engine + wizard runner for STR kinds. Not Arch. Not MEPF."
  theoretical_upper_bound: "Closes STR floor path. Does not close doors/pipes."
  contracts_at_risk:
    - cad_link_stepwise
    - create_via_category
    - host_does_not_parse_dwg
    - preflight_fail_fast
    - pilot_is_not_three_disciplines
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "Wizard still Preview→Confirm"
    - "Datum skip still ±5 mm / 50 mm 2°"
    - "Cost & Norm + BOQ"
  attempt_number: 0/3
  stop_rule: "Stop if rebar/complex stairs included. Stop if second Revit create path. Cap 3."
  code_authorized: false
```

## Blueprint (needs S2b + R4 create walls/columns; floors/framing may extend Host)

If Host cannot yet `Floor.Create` / `StructuralFraming`, this wave **may** add those **Host** primitives as part of the same CODE box **only if** they are category dispatcher extensions in `Dss.McpHost/Create/` (still one create path). Do **not** put `Floor.Create` in `Dss.Cad2Bim.Structure`.

### Project

`src/Dss.Cad2Bim.Structure/` — **no RevitAPI**. Reference `Dss.Cad2Bim.Core` + `Dss.McpContracts`.

| Class | Role |
|---|---|
| `StructureIntentEngine.cs` | `Plan(CadGeometry, LayerMap, DatumIndex) → CreateOperation[]` ordered |
| `ColumnMapper.cs` | Circle / closed rect / block `S-COL*` at **center** |
| `WallMapper.cs` | Center polyline or parallel pair `S-WALL-*` |
| `BeamMapper.cs` | Centerline `S-BEAM-*` |
| `SlabMapper.cs` | Closed `S-SLAB-*`; holes `S-SLAB-HOLE` |
| `FoundationMapper.cs` | `S-FOOT-*`, `S-PILE-D*`, `S-STRIP-*` (may be phase-2 inside S3 if time-box: **IN** for foot/pile/strip if Host has foundation create; else fail item `not_in_host_yet` — do not silent-skip) |
| `GridSnap.cs` | If distance to nearest grid intersection < **150 mm**, replace xyz with intersection; else warning `off_grid` (create unless user unticks “cho phép lệch”) |
| `StructureToolProvider.cs` | `ModuleId = "dss.cad2bim.structure"`; tools `cad_interpret_structure` returning operations JSON (**Host executes**) |

Register provider in `App.OnStartup` **after** Host: `registry.Register(new StructureToolProvider())`. Invoke still must end in Host create.

### Object table (CAD → Host fields)

| Revit | Valid CAD | Type / size | Host category |
|---|---|---|---|
| Column | Circle / closed rectangle / block `S-COL*` at center | `S-COL-400x400` or attr B×H; round `S-COL-D400` | `StructuralColumns` |
| Vách BTCT | Center polyline or parallel pair `S-WALL-*` | `S-WALL-200` / `300` | `Walls` + `structural: true` |
| Beam | Center line `S-BEAM-*` | `S-BEAM-300x600` or text `300x600` < 300 mm | `StructuralFraming` |
| Slab | Closed `S-SLAB-*` | `S-SLAB-150` | `Floors` + `structural: true`; holes inner loops |
| Isolated footing | Closed `S-FOOT-*` | `S-FOOT-1200x1800-H800` | `StructuralFoundation` |
| Pile | Circle `S-PILE-D*` | diameter | `StructuralFoundation` |
| Strip | Center `S-STRIP-*` | width×height | Wall foundation |

**OUT:** rebar, KC stairs, exploded 4-line columns without close/block.

**Blockers:** open slab → fail item (preflight should have hard-failed area layers). Beam double-line → option `doubleLineToCenter` (wizard tick) or fail item.

### Order

Planner **must** emit operations in **column → wall → beam → slab** (then foundations). Wizard “run one kind” still allowed; default **Kết cấu** button sequences kinds with confirm per kind (CHARTER từng loại) — do not one-shot all STR without per-type confirm.

### Host primitives to add if missing after R4-PILOT

| Category | API |
|---|---|
| StructuralFraming | `doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam)` |
| Floors | `Floor.Create(doc, loops, floorTypeId, levelId)` — `CurveLoop` from polyline mm |
| StructuralFoundation | `NewFamilyInstance` / `Hosted` per type |

Units mm. `list_types` if type unresolved.

### DoD (product-visible)

1. One linked `S-T1-FRAMING` (or fixture) → wizard Kết cấu → per type Preview/Confirm → columns/walls/beams/slabs appear.
2. Column 80 mm from grid intersection → snapped; column 200 mm → warning + created if tick on.
3. Engineering: no `Wall.Create` under `Dss.Cad2Bim.Structure`.
4. Cost & Norm + BOQ still present. Do **not** claim Arch/MEPF done.

### Regression

Preflight still blocks bad INSUNITS. MCP Off CAD still works.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `S3-STRUCTURE`.
