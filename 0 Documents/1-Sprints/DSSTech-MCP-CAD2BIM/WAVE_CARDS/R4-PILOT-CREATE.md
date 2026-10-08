# WAVE_CARD — R4-PILOT-CREATE

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: R4-PILOT-CREATE
  product_problem: "AI still cannot create even one wall/level/grid/column/pipe; W1 surface does not exist."
  measured_owner: mcp-create-primitives
  hypothesis: "create_element + create_batch (one Transaction), units mm, skipIfExists 10 mm, dryRun default when operations>20, permission Read+Create (no Delete), plus list_types-before-guess, is sufficient to create 1 level + 1 grid + 1 wall + 1 column + 1 pipe (if template has pipe type) from Cursor without CAD, undone with one Ctrl+Z."
  prediction:
    metric: create_batch_pilot_5
    expected_range: "5 created (or 4 + pipe skipped with reason no_pipe_type); one Undo removes all created in that batch; skipIfExists second call creates 0 duplicates; permission refuses delete_* if invoked"
    written_before_measurement: true
  falsifier: "Batch spans multiple Transactions that Undo cannot fully reverse; OR Delete tool ships; OR full Arch/STR/MEPF catalog ships in this lever; OR CAD parse is required; OR Qc1 treated official."
  one_allowed_lever: "Host create_element/create_batch for five primitives only. Not R4-CATALOG."
  theoretical_upper_bound: "Closes pilot W1 DoD. Does not close 3 disciplines or remaining categories."
  contracts_at_risk:
    - create_via_category
    - qs_confirm_write_neq_cad_create
    - no_delete_first_create_waves
    - no_qs_auto_write_expansion
    - qc1_not_official
    - pilot_is_not_three_disciplines
    - level_grid_skip_if_exists
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "W0 Norm/Formwork write-back still confirm-only"
    - "Cost & Norm + BOQ still present"
    - "Read tools still non-writing"
  attempt_number: 0/3
  stop_rule: "Stop if lever includes stairs/rebar/fittings/in-place. Stop if Delete. Cap 3. Do not claim 3 disciplines."
  code_authorized: false
```

## Blueprint (CODE only after STRATEGY_AUTHORIZE_PHASE; needs R3)

### Classes

| Class | Role |
|---|---|
| `Dss.McpContracts/CreateOperation.cs` | `{ opId, category, fields, skipIfExists, dryRun }` |
| `Dss.McpHost/Create/CreateElementTool.cs` | `create_element` |
| `Dss.McpHost/Create/CreateBatchTool.cs` | `create_batch` `{ operations: [] }` **one** `Transaction` |
| `Dss.McpHost/Create/CategoryDispatcher.cs` | switch on category → primitive |
| `Dss.McpHost/Create/LevelCreate.cs` | `Level.Create` |
| `Dss.McpHost/Create/GridCreate.cs` | `Grid.Create` |
| `Dss.McpHost/Create/WallCreate.cs` | `Wall.Create` |
| `Dss.McpHost/Create/ColumnCreate.cs` | `NewFamilyInstance` structural column |
| `Dss.McpHost/Create/PipeCreate.cs` | `Pipe.Create` — skip with `no_pipe_type` if template lacks types |
| `Dss.McpHost/Create/SkipIfExists.cs` | 10 mm element match; datum rules aligned with S2 |
| `Dss.McpHost/Create/UnitConv.cs` | mm ↔ internal feet |
| `Dss.McpHost/Create/DryRunPlanner.cs` | no Transaction; return would-create/skip/fail |

Optional aliases `create_wall`, `create_pipe`, … = sugar over `create_element`. Prefer one dispatcher.

**Transaction name:** `"DSSTech MCP create"` — **not** `"DSSTech write-back"`. Distinct from W0.

### Payloads (mm default)

```json
{
  "category": "Levels",
  "fields": { "name": "Tầng 1", "elevationMm": 0 },
  "skipIfExists": true
}
```

```json
{
  "category": "Grids",
  "fields": { "name": "A", "startMm": [0, 0, 0], "endMm": [10000, 0, 0] }
}
```

```json
{
  "category": "Walls",
  "fields": {
    "startMm": [0, 0, 0],
    "endMm": [4000, 0, 0],
    "wallType": "<exact WallType.Name from list_types>",
    "level": "Tầng 1",
    "heightMm": 3000,
    "structural": false
  }
}
```

```json
{
  "category": "StructuralColumns",
  "fields": {
    "xyzMm": [0, 0, 0],
    "familyType": "<FamilyAndType name>",
    "baseLevel": "Tầng 1",
    "topLevel": "Tầng 2"
  }
}
```

```json
{
  "category": "Pipes",
  "fields": {
    "startMm": [1000, 1000, 300],
    "endMm": [4000, 1000, 300],
    "systemType": "<PipingSystemType.Name>",
    "pipeType": "<PipeType.Name>",
    "level": "Tầng 1",
    "diameterMm": 50
  }
}
```

`units` field optional: `"mm"` (default) or `"m"` (multiply ×1000). Internal: `UnitUtils.ConvertToInternalUnits`.

### Revit API notes

- **Level:** `Level.Create(doc, elevationInternal)` then `level.Name = name` (name collide → catch, skipIfExists).
- **Grid:** `Grid.Create(doc, Line.CreateBound(p0, p1))` then `Name`. Line Z usually 0.
- **Wall:** `Wall.Create(doc, line, wallTypeId, levelId, heightInternal, 0, false, structural)`.
- **Column:** `doc.Create.NewFamilyInstance(xyz, symbol, baseLevel, StructuralType.Column)`. Symbol must `Activate()` if needed. Locate `FamilySymbol` by `Family.Name + " : " + symbol.Name` or exact `typeName`.
- **Pipe:** `Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, start, end)` (`MEP` API in `Autodesk.Revit.DB.Plumbing`). Requires pipe type **and** piping system type in template. If missing: **do not fail the whole batch** — item result `skipped: no_pipe_type` and DoD still PASSES if the other four exist. QA records whether pipe type was present.
- All inside `using (var tx = new Transaction(doc, "DSSTech MCP create")) { tx.Start(); ... tx.Commit(); }` for `create_batch`. `create_element` is a batch of 1.
- **dryRun:** if `operations.Count > 20` and `dryRun` omitted → treat `true`. Preview JSON only; `Transaction` not started.
- **Undo:** one Transaction ⇒ one Ctrl+Z. Do **not** `SubTransaction` per item unless still one outer Transaction (Undo still one step). Prefer no nested commit.

### skipIfExists

| Kind | Match |
|---|---|
| Level | same **name** OR elevation ± **5 mm** (same as S2) |
| Grid | same **name** OR line distance < **50 mm** and angle < **2°** |
| Wall / column / pipe | same category + resolved type + origin/line within **10 mm** |

Default `skipIfExists: true`. Result item: `{ status: created|skipped|failed, id?, reason? }`.

### Unresolved type

If `wallType` / `familyType` / `pipeType` does not resolve → item `failed: type_unresolved` and message “call list_types”. **Do not** pick the first type.

### Permission

Default `Read+Create`. `create_*` allowed. If method `delete_element` is called → `forbidden_permission` even if someone added the method (do **not** add it). No `Document.Delete`.

### DoD (product-visible)

From **Cursor**, **no CAD**:

1. `create_batch` of 1 level + 1 grid + 1 wall + 1 column + 1 pipe-if-type.
2. Elements visible in a 3D/plan view (screenshot).
3. **Undo once** → those elements gone.
4. Repeat batch with `skipIfExists` → 0 new ids for the same geometry.
5. Ribbon Cost & Norm + BOQ still work. W0 still confirm.
6. **Not** claimed: doors, ducts, stairs, 3 disciplines.

### Regression

Norm Selector does not auto-write. No QS_* in this Transaction.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `R4-PILOT-CREATE`.
