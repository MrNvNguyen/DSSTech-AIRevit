# N0-FORMWORK-SPIKE — partial result memo (2026-09-12)

## Env
- Revit **2025** build `25.4.10.45`
- Model: `TEST FORMWORK.rvt` (Desktop; not in git)
- Structural instances: **11** (6 columns + 5 beams + 1 floor)

## Timing (`get_Geometry` Fine)
| Batch target | Elements timed | Solids | Total ms | Avg ms/elem |
|---:|---:|---:|---:|---:|
| 100 | 11 | 11 | 1.69 | 0.15 |
| 1000 | 11 | 11 | 1.45 | 0.13 |
| 10000 | 11 | 11 | 1.57 | 0.14 |

(Model smaller than 100 — scales 1k/10k capped at 11; **order-of-magnitude OK** for this pilot.)

## Boolean
- Pairs attempted: **17** · succeeded: **17** · failed: **0**
- **boolean_fail_rate = 0.0%** → gate **PASS** (≤ 5%)

## Probe epsilon
- First run: **NOT_MEASURED** — volume-overlap method invalid for abutting joined solids; selection pair had no overlap.
- Fix shipped: face-extrusion Difference probe + auto join discovery. **Needs one re-run.**

## Verdict (interim)
| Bar | Status |
|---|---|
| boolean_fail_rate | **PASS** (0%) |
| geometry timing | **MEASURED** (small model) |
| probe vs hand calc | **pending re-run** |

N1 remains **unauthorized** until probe measured and strategy adjudicates.
