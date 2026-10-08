# N0 Formwork Spike Memo

> Auto-updated by `tools/formwork-spike/summarize-spike.py` — 2026-09-12 14:03 UTC

## Run metadata

| Field | Value |
|---|---|
| Campaign | N0-FORMWORK-SPIKE |
| Wave | N0-FORMWORK-SPIKE-ATTEMPT-1 |
| Run class | PRODUCT_FAILURE_RUN |
| Attempt outcome | FALSIFIED |
| Model | TEST FORMWORK |
| Revit host | 2025 build 25.4.10.45 |
| Structural count | 11 |

## Geometry timing (`get_Geometry` Fine)

| Batch | Elements timed | Solids | Total ms | Avg ms/elem | Status |
|---|---:|---:|---:|---:|---|
| 100 | 11 | 11 | 1.9 | 0.176 | MEASURED |
| 1000 | 11 | 11 | 1.6 | 0.142 | MEASURED |
| 10000 | 11 | 11 | 1.6 | 0.145 | MEASURED |

## Boolean difference (neighbor pairs)

| Metric | Value |
|---|---:|
| Status | MEASURED |
| Pairs attempted | 17 |
| Failed | 0 |
| Fail rate | 0.00% |
| Gate (≤ 5%) | **PASS** |
| Total ms | 8.4 |
| Avg ms/op | 0.49273529411764705 |

## Probe epsilon (joined beam–column)

| Metric | Value |
|---|---:|
| Status | MEASURED |
| Method | face_extrusion_difference |
| Hand area (m²) | 0.18 |
| Computed area (m²) | 0.13499999999999313 |
| Error % | 25.00000000000382 |
| Element ids | 1659100, 1660193 |
| Reason | — |

## Decision bar (N0 gate)

- Boolean fail rate ≤ **5%** required before N1.
- Probe error on hand-calc pair should be ≤ **1%** (plan target); mark NOT_MEASURED until Revit run.

## Evidence paths

- `docs/evidence/formwork-spike/N0-FORMWORK-SPIKE/result.json`
- `docs/evidence/formwork-spike/N0-FORMWORK-SPIKE/claims.yaml`
- `docs/evidence/formwork-spike/N0-FORMWORK-SPIKE/evidence-index.json`
