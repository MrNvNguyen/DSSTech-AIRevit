# WAVE_CARD — S6-SPEC-FREEZE

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: S6-SPEC-FREEZE
  product_problem: "Draft spec/wizard copy may drift from the shipped wizard; CAD users lack a frozen Vietnamese printable contract."
  measured_owner: cad2bim-spec
  hypothesis: "After wizard shape is specified/shipped (S2b+), freezing docs/CAD_INPUT_SPEC.md plus dialog string table to match actual Preflight/Wizard copy is sufficient for a user-printable VN spec without reopening S3–S5 engines."
  prediction:
    metric: spec_dialog_parity
    expected_range: "Every preflight failure id in code has a VN sentence in the spec appendix; wizard step list matches S2b UI; no new create categories in this lever"
    written_before_measurement: true
  falsifier: "Freeze written before S2b wizard steps exist; OR freeze used to authorize Host parse; OR Phase 6 ROADMAP flipped open as a side effect."
  one_allowed_lever: "Printable VN spec freeze + dialog copy sync. No product create path change."
  theoretical_upper_bound: "Closes user-facing spec drift. Closes 0% of PDF vector."
  contracts_at_risk:
    - preflight_fail_fast
    - no_large_dwg_rvt_in_git
    - qs_panels_kept
  qa_tier: Q0
  qa_batch: solo
  q2_class: n/a
  regression_scope:
    - "Do not edit ROADMAP Phase 6 to open"
    - "Cost & Norm + BOQ copy prefixes remain DSSTech if R0a shipped"
  attempt_number: 0/3
  stop_rule: "Stop if this wave ships parsers. Must run after wizard shape specified (S2b at least). Cap 3."
  code_authorized: false
```

## Blueprint (after S2b; preferably after S3–S5 so object tables are real)

S0 = drafts **before** parse. S6 = **freeze** after UI exists. Do not invert.

### Files

| Path | Action |
|---|---|
| `docs/CAD_INPUT_SPEC.md` | Freeze VN: package names, fail-fast, per-discipline tables, skipIfExists numbers (5 mm / 50 mm / 2° / 10 mm / 150 mm / 200 mm door host) |
| `docs/MCP_HOST.md` | Optional: pipe name, mcp.json placeholder, Off vs CAD |
| `docs/CAD2BIM.md` | Optional: wizard steps screenshot pointers (no large binaries) |
| `src/QSNormEngine.Revit/Cad/CadDialogStrings.cs` | **Only if CODE authorized** — central VN strings; S6 may be docs-only if strings already match |

P0 must **not** write production `docs/CAD_INPUT_SPEC.md`. Lead writes it in S0 then S6 edits.

### Dialog copy (must match wizard)

- Preflight titles: “Không tạo — CAD chưa đạt spec”
- Checklist lines = spec appendix IDs (`insunits`, `open_polyline`, `exploded_block`, `unknown_layer`, `bbox_vs_grid`)
- MCP toast remains “MCP đang bật”
- Confirm button: “Tạo (một bước Undo)”

### DoD (product-visible)

Printed spec (PDF export by user, not committed) matches wizard. Q0: string parity table in evidence. No new ribbon. QS panels untouched.

### Regression

Do not claim ROADMAP Phase 6 open. Do not put `.dwg` in git.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `S6-SPEC-FREEZE`.
