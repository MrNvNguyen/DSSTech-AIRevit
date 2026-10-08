# WAVE_CARD — S5-MEPF

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: S5-MEPF
  product_problem: "MEP CAD centerlines cannot become pipes/ducts/trays; unparsed size would create 0 mm runs."
  measured_owner: cad2bim-mepf
  hypothesis: "Dss.Cad2Bim.Mepf maps one line = one run from P-*/M-DUCT-*/E-TRAY-* plus equipment blocks, fails the item when diameter/width cannot be parsed, and calls Host create_* only (no fittings catalog)."
  prediction:
    metric: mepf_unparsed_size_fails
    expected_range: "P-CW-D50 line → pipe created; layer P-CW without D## and no nearby text → item failed, zero 0 mm pipes; double-line duct without center tick → fail; Mepf csproj no RevitAPI"
    written_before_measurement: true
  falsifier: "Pipe.Create inside Mepf project; OR unparsed size still creates; OR full tee/elbow catalog; OR S3+S4 bundled."
  one_allowed_lever: "MEPF intent engine, centerline only, fail if size unparsed."
  theoretical_upper_bound: "Closes centerline MEPF. Not fittings/isometric/circuiting."
  contracts_at_risk:
    - cad_link_stepwise
    - create_via_category
    - host_does_not_parse_dwg
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "Arch/STR elements not deleted"
    - "Cost & Norm + BOQ"
    - "Pipe create from R4-PILOT still skipIfExists 10 mm"
  attempt_number: 0/3
  stop_rule: "Stop if fittings/flex/isometric in this lever. Cap 3. No CAD ports."
  code_authorized: false
```

## Blueprint (after S2b; Level+floors/ceilings preferred for offsets)

### Project

`src/Dss.Cad2Bim.Mepf/` — no RevitAPI. `MepfIntentEngine`, `PipeMapper`, `DuctMapper`, `CableTrayMapper`, `EquipmentBlockMapper`, `RiserMapper`, `MepfToolProvider` (`dss.cad2bim.mepf`).

### Object table

| Object | CAD | Size / system | Host |
|---|---|---|---|
| Pipe | Line/polyline `P-*` or `M-PIPE-*` | `P-CW-D50`, `P-SAN-D110` | `Pipes` — prefix `CW/HW/SAN/DR/GAS` → system type name map |
| Duct | Line `M-DUCT-SUP-*` / `RET-*` / `EXH-*` | `600x400` or `D315` in layer or text | `Ducts` |
| Cable tray | `E-TRAY-*` | `E-TRAY-300` | `CableTrays` |
| Equipment | Blocks `M-EQP-*` / `P-EQP-*` / `E-EQP-*` / `F-EQP-*` | family map by block name | place family; **no** connector wiring |
| Sprinkler / light | `F-SPK` / `E-LITE` | — | place on level/ceiling |
| Riser | Circle + text `RISER` same XY two levels | system + D | vertical pipe/duct |
| Fire pipe | `F-PIPE-D*` | as pipe | sprinkler system type |

**1 line = 1 run.** Do not extrude parallel pairs as walls.

**Z defaults** (wizard editable): domestic water **+300 mm** from floor; sanitary **−300 mm** from floor; supply air **−400 mm** from ceiling (`A-CLNG-OFF` or storeyHeight − 400). Layer suffix `-Z{mm}` overrides.

**Connect:** if two run endpoints < **10 mm**, Host may `doc.Regenerate` and let Revit auto-connect — **do not** place fittings catalog. If auto-connect fails, item warning `no_fitting` — still keep segments.

### Fail item (hard)

- Size not parsed → `failed: size_unparsed`. **Never** `Pipe.Create` with diameter 0.
- Double-line duct without tick “lấy trung tuyến” → `failed: need_centerline`.

### Host primitives to add if missing

| Category | API |
|---|---|
| Ducts | `Duct.Create(doc, systemTypeId, ductTypeId, levelId, start, end)` (`Autodesk.Revit.DB.Mechanical`) |
| CableTrays | `CableTray.Create(...)` (`Electrical`) |
| Equipment / lights | `NewFamilyInstance` |

MEP assemblies: Host project already references RevitAPI (includes MEP). Cad2Bim.Mepf still **no** RevitAPI.

### DoD (product-visible)

1. `P-T1-PLUMB` centerline with `P-CW-D50` → pipe in model after Confirm.
2. Line on `P-CW` without size → red fail row, **no** new pipe.
3. No `Pipe.Create` under `Dss.Cad2Bim.Mepf`.
4. Cost & Norm + BOQ still present. Fittings not claimed.

### Regression

R4 pipe skipIfExists. MCP Off CAD works.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `S5-MEPF`.
