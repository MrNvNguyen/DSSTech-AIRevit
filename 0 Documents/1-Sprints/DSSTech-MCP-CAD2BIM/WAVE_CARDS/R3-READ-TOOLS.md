# WAVE_CARD — R3-READ-TOOLS

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: R3-READ-TOOLS
  product_problem: "AI cannot read document/levels/grids/types; would guess families and dump the model."
  measured_owner: mcp-read-tools
  hypothesis: "A bounded set of read tools (ping, get_document, get_levels, get_grids, get_view, list_categories, list_types{category}, query_by_category paginated) lets Cursor inspect a fixture without a full model dump."
  prediction:
    metric: read_tools_bounded
    expected_range: "All 8 tools appear in tools/list; query_by_category requires pageSize<=100; missing page token/limit dump path does not exist; list_types Walls returns ≥1 type on a stock architectural template"
    written_before_measurement: true
  falsifier: "A dump_model / get_all_elements tool exists; OR query returns unbounded; OR tools call Wall.Create; OR Cost & Norm missing."
  one_allowed_lever: "Host read tools only. No create_element. No CAD parse."
  theoretical_upper_bound: "Closes read surface for later create. Does not close W1 create."
  contracts_at_risk:
    - create_via_category
    - no_delete_first_create_waves
    - qs_panels_kept
    - qc1_not_official
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "ping On/Off"
    - "Cost & Norm + BOQ"
    - "No W1 writes in this wave"
  attempt_number: 0/3
  stop_rule: "Stop if a full-model dump ships. Cap 3. Read tools must not write."
  code_authorized: false
```

## Blueprint (CODE only after STRATEGY_AUTHORIZE_PHASE; needs R2)

### Classes (`Dss.McpHost/Tools/`)

| Class | Tool name | Revit API |
|---|---|---|
| `PingTool.cs` | `ping` | already R0b |
| `GetDocumentTool.cs` | `get_document` | `Document.Title`, `PathName`, `IsWorkshared`, `Application.VersionNumber` |
| `GetLevelsTool.cs` | `get_levels` | `new FilteredElementCollector(doc).OfClass(typeof(Level))` → `{ id, name, elevationMm }` |
| `GetGridsTool.cs` | `get_grids` | `OfClass(typeof(Grid))` → `{ id, name, startMm, endMm }` from `Grid.Curve` as `Line` |
| `GetViewTool.cs` | `get_view` | `uidoc.ActiveView` → `{ id, name, viewType, genLevelId? }` |
| `ListCategoriesTool.cs` | `list_categories` | allow-list of BuiltInCategory used by later create (not every BIC) |
| `ListTypesTool.cs` | `list_types` | argument `category` (name or `OST_*`) |
| `QueryByCategoryTool.cs` | `query_by_category` | paginated collector |

Register all on `HostBuiltInProvider` (`dss.host`).

### Units

Return millimetres. Convert with `UnitUtils.ConvertFromInternalUnits(x, UnitTypeId.Millimeters)` (Revit 2021+ `ForgeTypeId`; 2024–2027 OK). Never return feet in JSON.

### `list_categories` allow-list (this wave)

Return short name + BuiltInCategory string for at least:

`Levels`, `Grids`, `Walls`, `Floors`, `Ceilings`, `Doors`, `Windows`, `Rooms`, `StructuralColumns`, `StructuralFraming`, `StructuralFoundation`, `Pipes`, `PipeCurves`/`OST_PipeCurves`, `Ducts`, `CableTrays`, `LightingFixtures`, `MechanicalEquipment`, `ElectricalEquipment`, `PlumbingFixtures`, `Sprinklers`.

Do **not** enumerate every `BuiltInCategory` (noise).

### `list_types { category }`

- Resolve category → `Category.GetCategory(doc, bic)` / `OfCategory`.
- Walls: `WallType` (`OfClass(typeof(WallType))`).
- Floors: `FloorType`.
- Columns: `FamilySymbol` filtered `OST_StructuralColumns` (and architectural columns if needed).
- Pipes: `PipeType` + list `PipingSystemType` separately in result `{ types: [], systemTypes: [] }`.
- If category unknown → error `unknown_category`, do not guess.

**Rule for later create:** if `typeName` unresolved, caller **must** `list_types` then retry. Read tools exist so create (R4) can refuse to guess.

### `query_by_category` pagination

Arguments: `{ category, pageSize?: 50, pageToken?: string }`.

- `pageSize` default 50, **max 100**. Reject > 100.
- Order by `Id.IntegerValue` ascending.
- `pageToken` = last id as string.
- Return `{ items: [{ id, name, typeName, levelId? }], nextPageToken }`.
- **No** geometry dump, **no** parameter dump of entire model.

Forbidden tool names: `dump_model`, `get_all_elements`, `export_nwd`.

### Permission

Read tools allowed at `Read`, `ReadCreate`, `ReadCreateModify`. They must not open a `Transaction`.

### DoD (product-visible)

1. Cursor `tools/list` includes the eight names.
2. On a stock template: `get_levels` ≥ 1; `list_types` for `Walls` ≥ 1.
3. `query_by_category` with `pageSize: 101` errors.
4. Document unmodified (no new element ids vs pre-call snapshot) — Layer B.
5. Cost & Norm + BOQ still present.

### Regression

MCP Off still blocks tools/call. QS W0 unchanged.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `R3-READ-TOOLS`.
