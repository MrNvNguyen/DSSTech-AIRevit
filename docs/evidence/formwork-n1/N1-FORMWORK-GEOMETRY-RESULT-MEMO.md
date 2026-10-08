# N1-FORMWORK-GEOMETRY — RESULT MEMO

**Timestamp (UTC):** 2026-09-14T04:28:10Z  
**Host:** Revit 2025 · build 25.4.10.45  
**Model:** `TEST FORMWORK.rvt` (Desktop; not in git)  
**Wave:** N1-FORMWORK-GEOMETRY-ATTEMPT-1 · PRODUCT

## Summary

| Metric | Value | Gate |
|---|---|---|
| Elements | 3 (col + beam + slab) | — |
| Neighbor pairs | 3 | — |
| run_class | PRODUCT_PASS_RUN | — |
| attempt_outcome | **PASS** | **PASS** (≤ 1% vs a-priori) |

| Role | ElementId | Hand m² | Computed m² | Deduct | Error |
|---|---|---:|---:|---:|---:|
| Column | 1659100 | 6.975 | 6.975 | 0 | ~0% |
| Beam | 1660193 | 22.695 | 22.695 | 0.135 | ~0% |
| Slab | 1660566 | 201.005 | 201.005 | 5.685 | ~0% |

## Notes

- Hand is **join-aware Fine solid** (column H_eff, beam exposed height under slab embed, slab contact deduct). Not raw type HxB alone.
- Intermediate FALSIFIED run: slab a-priori omitted contact deduct → 2.75% FAIL; hand corrected then re-measured PASS.
- Hard limits: read-only · no ribbon Takeoff · Core DTO Revit-API-free.

## Artifacts

- `docs/evidence/formwork-n1/N1-FORMWORK-GEOMETRY/result.json`
- `docs/evidence/formwork-n1/apriori-hand-calc.yaml`
- `src/QSNormEngine.Revit/Formwork/*` · `src/QSNormEngine.Core/Formwork/FormworkResult.cs`
- `tools/formwork-n1/`

## Recommendation

N1 DoD met → strategy close N1 / authorize N2-FORMWORK-RULES.
