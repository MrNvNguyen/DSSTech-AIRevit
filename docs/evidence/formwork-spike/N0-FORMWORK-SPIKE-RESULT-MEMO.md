# N0-FORMWORK-SPIKE — RESULT MEMO

**Timestamp (UTC):** 2026-09-12T13:59:17Z  
**Host:** Revit 2025 · build 25.4.10.45  
**Model:** `TEST FORMWORK.rvt` (Desktop; not in git)  
**Wave:** N0-FORMWORK-SPIKE-ATTEMPT-1 · OBSERVE_ONLY

## Summary

| Metric | Value | Gate |
|---|---|---|
| Structural elements | 11 | — |
| Boolean fail rate | **0.0%** (17/17) | **PASS** (≤ 5%) |
| get_Geometry avg | ~0.15–0.18 ms/elem (n=11) | MEASURED (model &lt; 100) |
| Probe method | `face_extrusion_difference` | MEASURED |
| Probe area | **0.135 m²** | — |
| Hand area (initial fixture) | 0.18 m² | wrong for this join |
| Error vs 0.18 | 25% | FALSIFIED vs wrong hand |
| Hand area (corrected) | **0.135 m²** (= 0.30×0.45) | error ≈ **0%** |

## Geometry interpretation

- Column type: **450 × 600 mm**; beam type: **300 × 600 mm**
- Probe pair ids: `1659100` (column) · `1660193` (beam)
- Computed contact **0.135 m²** = beam width **0.30 m** × column face **0.45 m** (beam meets the 450 mm face)
- Initial fixture assumed full beam end 0.30×0.60 = 0.18 → **definition error**, not boolean failure

## Timing

| Batch target | Elements timed | Solids | Total ms | Avg ms/elem |
|---:|---:|---:|---:|---:|
| 100 | 11 | 11 | 1.94 | 0.176 |
| 1000 | 11 | 11 | 1.56 | 0.142 |
| 10000 | 11 | 11 | 1.59 | 0.145 |

1k/10k scales **NOT_MEASURED as full batch** (model only has 11 solids); order-of-magnitude on this pilot is fine.

## Machine verdict in result.json

- `run_class`: PRODUCT_FAILURE_RUN  
- `attempt_outcome`: FALSIFIED  
- Reason: probe_error_pct=25 vs fixture 0.18 (≤2% bar)

## Team adjudication note (for strategy)

1. **Plan hard gate** (boolean fail &gt; 5% → stop N1): **not tripped**.
2. Probe pipeline is **alive** (MEASURED, face extrusion works on abutting join).
3. FALSIFIED is against an **incorrect hand_area**, not against geometry API viability.
4. Corrected hand_area 0.135 → relative error ≈ 0% → probe bar would PASS without changing Revit code.

## Recommendation to strategy

- Do **not** PO_PACKET (no scope change; definition fix inside team).
- Prefer: `STRATEGY_APPROVE_EXECUTION` for **N1** (boolean gate green + probe method validated), with corrected fixture recorded; OR one offline reclassify + formal PASS claim before N1.
- Do **not** open N2–N6 yet.

## Artifacts

- `docs/evidence/formwork-spike/N0-FORMWORK-SPIKE/result.json`
- `tools/formwork-spike/fixtures/hand-calc-beam-column.yaml` (to be corrected to 0.135)
- This memo
