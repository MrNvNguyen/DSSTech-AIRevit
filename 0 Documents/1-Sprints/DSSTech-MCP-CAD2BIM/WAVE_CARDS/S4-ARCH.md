# WAVE_CARD — S4-ARCH

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: S4-ARCH
  product_problem: "Architectural CAD walls/doors/rooms/finish floors cannot be built stepwise; risk of double wall on S-WALL."
  measured_owner: cad2bim-arch
  hypothesis: "Dss.Cad2Bim.Arch maps A-WALL (parallel pair default), A-DOOR blocks, rooms, finish floors to Host operations and skips walls overlapping existing S-WALL / structural walls within 50 mm; doors are blocks only."
  prediction:
    metric: arch_skip_swall_overlap
    expected_range: "A-WALL coincident with S-WALL <50 mm: arch wall skipped; A-DOOR insert hosts nearest wall <200 mm; exploded door → fail item not a door; Arch csproj no RevitAPI"
    written_before_measurement: true
  falsifier: "Arch Wall.Create in discipline lib; OR exploded lines become doors; OR S3+S5 bundled; OR overlap skip missing → double wall."
  one_allowed_lever: "Arch intent engine only. Doors as blocks. Skip S-WALL overlap <50 mm."
  theoretical_upper_bound: "Closes Arch envelope path. Not MEPF. Not stairs/curtain."
  contracts_at_risk:
    - cad_link_stepwise
    - create_via_category
    - host_does_not_parse_dwg
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "Structure elements remain; Arch must not delete them (W2 out)"
    - "Cost & Norm + BOQ"
  attempt_number: 0/3
  stop_rule: "Stop if curtain/complex stairs/full furniture catalog. Cap 3. No second create path."
  code_authorized: false
```

## Blueprint (after S3 recommended so S-WALL exists; can run Arch-only if no STR)

### Project

`src/Dss.Cad2Bim.Arch/` — no RevitAPI. `ArchIntentEngine`, `WallPairMerger`, `DoorBlockMapper`, `WindowBlockMapper`, `RoomMapper`, `FinishFloorMapper`, `CeilingMapper`, `ArchToolProvider` (`dss.cad2bim.arch`).

Register in `App.OnStartup`.

### Object table

| Revit | Valid CAD | Type | Logic |
|---|---|---|---|
| Tường KT | Parallel pair `A-WALL-*` **or** center polyline | Gap = thickness or `A-WALL-200` | Host `Walls` `structural: false`; height floor-to-floor |
| Door | **Block** `A-DOOR-*`, insertion = opening center, rotation = wall dir | Attr `W`/`H` or `A-DOOR-900x2100` | Host `Doors` `NewFamilyInstance` hosted; nearest wall < **200 mm** else fail item |
| Window | Block `A-WIND-*` | `A-WIND-1500x1400` + attr `SILL` default 900 | Host `Windows` |
| Room | Closed `A-ROOM` + text inside | name/number from text | Host `Rooms` + room separation lines if needed |
| Finish floor | Closed `A-FLOR-*` | `A-FLOR-TILE` | Host `Floors` non-structural — **do not** replace `S-SLAB` |
| Ceiling | `A-CLNG-*` | `A-CLNG-OFF-2700` | Host `Ceilings` |

**Parallel pair merge:** two lines same layer, distance ≈ thickness ± **5 mm**, included angle of directions < **3°** from parallel. Midline = wall axis.

**S-WALL overlap skip:** if proposed Arch wall axis is within **50 mm** of an existing **structural** wall axis (from model after S3 or already in doc), `skipped: overlap_swall`. Compare to **Revit** walls with `Wall.WallType.Kind` / structural parameter, not only CAD layer (wizard may run Arch first — then skip when STR runs? **Spec: run STR before Arch.** If Arch first, later STR does not delete Arch walls. Document order on dialog: “Nên chạy Kết cấu trước.”)

### Host primitives to add if missing

| Category | API |
|---|---|
| Doors | `NewFamilyInstance(xyz, symbol, hostWall, level, StructuralType.NonStructural)` |
| Windows | same, host wall, sill |
| Rooms | `doc.Create.NewRoom(level, uv)` + `NewRoomTag` optional |
| Room separation | `NewRoomBoundaryLines` |
| Ceilings | `Ceiling.Create` (API varies by year — verify 2024–2027; if 2024 differs, `#if` in **Host** only) |

### OUT

2D stairs, complex curtain, full `A-FURN` catalog (optional generic place = R4-CATALOG, not this lever).

### DoD (product-visible)

1. After STR walls, Arch `A-WALL` on same axis → skip overlap; unique `A-WALL` creates.
2. Door **block** places; exploded door → fail item, zero door instances.
3. No `Wall.Create` in Arch project.
4. Cost & Norm + BOQ still present. MEPF not claimed.

### Regression

Do not `Document.Delete` STR walls. MCP Off CAD works.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `S4-ARCH`.
