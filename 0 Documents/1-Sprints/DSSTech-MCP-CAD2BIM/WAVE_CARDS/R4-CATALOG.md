# WAVE_CARD — R4-CATALOG

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: R4-CATALOG
  product_problem: "AI/Host can only create the five pilot primitives; remaining PO-intent categories are missing."
  measured_owner: mcp-create-primitives
  hypothesis: "Extending CategoryDispatcher with remaining Arch/STR/MEPF/datum categories from the Cursor plan tables, still via create_element/create_batch, skipIfExists, Read+Create, is sufficient without stairs/rebar/full fittings/in-place/mass/adaptive."
  prediction:
    metric: catalog_remaining_in_out
    expected_range: "list_categories + create_element succeed for floors, ceilings, doors, windows, rooms, beams, foundations, ducts, trays, equipment, lights as types exist; tools for in-place/mass/adaptive/rebar/complex stairs/full MEP fittings are absent"
    written_before_measurement: true
  falsifier: "Rebar or complex stair sketch ships; OR Delete permission; OR this lever is merged into R4-PILOT historically; OR Cad2Bim duplicates create APIs."
  one_allowed_lever: "Remaining Host create_* categories. OUT: in-place, mass, adaptive, rebar, complex stairs, full MEP fittings."
  theoretical_upper_bound: "Closes Host catalog gap after pilot. Does not close S7 PDF."
  contracts_at_risk:
    - create_via_category
    - no_delete_first_create_waves
    - pilot_is_not_three_disciplines
    - qs_confirm_write_neq_cad_create
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "R4-PILOT 5 primitives + Undo still hold"
    - "Cost & Norm + BOQ"
    - "W2 still out"
  attempt_number: 0/3
  stop_rule: "Stop if OUT list is implemented. Cap 3. Do not weaken Read+Create."
  code_authorized: false
```

## Blueprint (after R4-PILOT; may follow S3–S5 Host extensions — **dedupe**)

If S3–S5 already added Floor/Beam/Duct/Door to Host, this wave **only** fills holes and documents the matrix. Do not fork a second dispatcher.

### IN (Host `create_element` categories)

From PO envelope tables, remaining after pilot (level, grid, wall, column, pipe):

**Arch:** Floors (finish), Ceilings, Doors, Windows, Rooms, Room separation, Generic Models / furniture (`xyz, familyType, level, rotationDeg`).

**STR:** Structural Framing, Structural Walls (wall + structural true — may already exist), Floors (structural), Foundations isolated/slab/wall, Piles.

**MEPF:** Ducts, Cable trays / conduits, Mechanical/Electrical/Plumbing/Sprinkler equipment, Lighting fixtures, Air terminals.

Railings = **OUT this wave** (plan said “sóng sau”). Flex ducts/pipes = OUT. Pipe fittings / accessories = OUT (R4b later). Stairs = OUT.

### OUT (must not exist as tools)

- In-place families / masses / adaptive components  
- Rebar (`Autodesk.Revit.DB.Structure.Rebar`)  
- Complex stairs (sketch runs)  
- Full MEP fittings (tee/elbow/transition catalogs, `NewTeeFitting`, etc.)  
- `delete_*`, `ungroup_*`, `modify_*` (W2)

### Classes

Extend `Dss.McpHost/Create/*.cs` one file per category group. Keep `skipIfExists` 10 mm (datum rules unchanged). `dryRun` > 20. `list_types` before guess.

Generic furniture: `NewFamilyInstance(xyz, symbol, level, NonStructural)` + `ElementTransformUtils.RotateElement` if `rotationDeg` set.

Conduits: `Conduit.Create` if API available on host years; else fail item `unsupported_host`.

### DoD (product-visible)

1. `tools/list` includes create for IN categories; **excludes** rebar/stair/fitting/delete.
2. Spot: one door **or** one duct **or** one beam from Cursor on a template that has types (pick one per QA session, not all).
3. Pilot five + Undo still work.
4. Cost & Norm + BOQ still present.

### Regression

Cad2Bim still only emits operations. No QS auto-write.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `R4-CATALOG`.
