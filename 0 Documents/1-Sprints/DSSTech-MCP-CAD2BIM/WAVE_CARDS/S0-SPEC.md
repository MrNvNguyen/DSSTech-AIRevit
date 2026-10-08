# WAVE_CARD — S0-SPEC

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: S0-SPEC
  product_problem: "No fail-fast CAD contract; S1 would invent layer names and silently mis-scale."
  measured_owner: cad2bim-spec
  hypothesis: "A user-printable CAD_INPUT_SPEC outline plus three discipline layer-map JSON drafts (str/arch/mepf) written as campaign docs is sufficient for S1 preflight to have a checklist and for S3–S5 to share patterns. Production Config/ is not required this card if drafts live in sprint folder / docs as specified by the authorizing CODE box."
  prediction:
    metric: spec_checklist_complete
    expected_range: "docs/CAD_INPUT_SPEC.md (or sprint draft promoted) lists INSUNITS=4, closed polyline, blocks not exploded, *-ANNO-* separate, 1 floor per file; three JSON maps contain S-COL, A-WALL, P-CW patterns"
    written_before_measurement: true
  falsifier: "S1 authorized without a spec; OR maps live only in Host C#; OR production Config/ edited in P0; OR S6 freeze claimed before wizard shape exists."
  one_allowed_lever: "Spec + 3 layer-map drafts. No parse engine. No create. No Config/ as production this P0 (P0 already drafted SPEC-OUTLINE.md)."
  theoretical_upper_bound: "Closes 'no checklist' for fail-fast. Closes 0% of ImportInstance read."
  contracts_at_risk:
    - preflight_fail_fast
    - no_large_dwg_rvt_in_git
    - host_does_not_parse_dwg
    - qs_panels_kept
  qa_tier: Q0
  qa_batch: solo
  q2_class: n/a
  regression_scope:
    - "Do not edit Config/** as production in this wave unless a later CODE box explicitly opens Config"
    - "Cost & Norm + BOQ untouched"
  attempt_number: 0/3
  stop_rule: "Stop if this wave ships parsers or Wall.Create. Do not skip S0 to jump S3. Cap 3 doc revisions measured as Q0."
  code_authorized: false
```

## Blueprint

P0 already contains **drafts** in `SPEC-OUTLINE.md`. This CODE/docs box **promotes** them.

### Files to add (when this box is authorized)

| Path | Role |
|---|---|
| `docs/CAD_INPUT_SPEC.md` | Vietnamese, printable for CAD drafters. **Not** written in P0 as product SSOT. |
| `Config/cad-layer-map.str.json` | Only when CODE box allows Config. Until then keep drafts in sprint. |
| `Config/cad-layer-map.arch.json` | ditto |
| `Config/cad-layer-map.mepf.json` | ditto |
| `0 Documents/1-Sprints/DSSTech-MCP-CAD2BIM/SPEC-OUTLINE.md` | P0 draft — source for promotion |

**Do not** write `docs/CAD_INPUT_SPEC.md` in P0 (this file is the plan). Lead in S0 copies/edits from SPEC-OUTLINE.

### Spec must include (from PO envelope)

- File Revit: nên có Level + Grid; thiếu thì dựng (CAD `G-GRID` / CSV / MCP).
- DWG/DXF **link** vào model (Origin-to-Origin hoặc Shared Coordinates).
- **mm**, `INSUNITS=4`; 1 unit = 1 mm.
- **1 tầng / 1 file** mặt bằng, link đúng Level.
- Block cửa/cột/TB **không nổ**.
- Area = polyline **đóng** (`closed=1`).
- Dim/hatch trên `*-ANNO-*`.
- Text thuộc tính < 300 mm hoặc block attribute.
- Package names: `A-T1-PLAN.dwg`, `S-T1-FRAMING.dwg`, `S-T1-FOUNDATION.dwg`, `M-T1-HVAC.dwg`, `P-T1-PLUMB.dwg`, `E-T1-POWER.dwg`, `F-T1-FIRE.dwg`.
- Fail-fast table matching CHARTER §8.

### Layer maps

JSON array of `{ pattern, kind, ...capture fields }`. Regex .NET. User-editable later on wizard (S2b). Project overlay path specified: `cad-layer-map.{str|arch|mepf}.project.json` beside the Revit file or under `%LOCALAPPDATA%\DSSTech\projects\<hash>\` — **specify in spec**, do not invent a second Host parse.

Drafts: see `SPEC-OUTLINE.md` fenced JSON (P0).

### DoD (product-visible)

A CAD user can print `docs/CAD_INPUT_SPEC.md` and see the fail-fast table in Vietnamese. Three map files exist (sprint or Config per authorize). No new ribbon buttons required. QS panels untouched.

### Regression

No `src/**` required if this box is docs-only; if Config is opened, do not break existing `norm-engine.config.json`.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `S0-SPEC`. Sequence: S0 before S1.
