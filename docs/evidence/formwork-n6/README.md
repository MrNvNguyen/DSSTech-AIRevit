# Formwork N6 evidence

## Layer A — formula smoke (CODE)

```powershell
python tests/formwork_formula_smoke_test.py
```

Covers: column 400×400×3 → 4.8 m²; wall 200×2000×16 → 70.4 m²; beam/slab stem formulas; quantity group-1 = 21 columns.

**Scope:** formula only — not in-Revit PASS.

## Layer B — Revit (required for Sóng N PASS)

| # | Sample | Manual check | Engine | Δ% ≤ 2 |
|---|---|---|---|---|
| 1 | Interior column | | | ☐ |
| 2 | Edge column | | | ☐ |
| 3 | Primary beam | | | ☐ |
| 4 | Slab with opening | | | ☐ |
| 5 | Footing / đài | | | ☐ |

Procedure: Formwork Takeoff → export CSV → compare hand calc → screenshot Results + Project Browser schedule if Q3 used.

Screenshots / CSV: keep outside git if large; record SHA-256 here.

## Layer C — regression

Pilot model total formwork before/after rule change; `Confidence=Low` &lt; 5% of elements.
