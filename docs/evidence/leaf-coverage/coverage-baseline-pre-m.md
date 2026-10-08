# Leaf coverage — `baseline-pre-m`

- Generated (UTC): `2026-09-03T09:46Z`
- Runtime: `Data\norm-engine-runtime.json`
- Source DB: `Database_DinhMuc_QS_BIM_ENGINE_v1.sqlite`

## Summary

| Metric | Value |
|---|---|
| Work families | 98 |
| Families with ≥1 selectable leaf | **9** |
| Families with zero leaf | 89 |
| Selectable variants | 155 |
| Official-potential leaves (READY/APPROVED) | **0** |

### Status among selectable leaves

| digitization_status | count |
|---|---|
| `RESOURCE_QC1` | 139 |
| `RESOURCE_QC1_LEGACY` | 16 |

CSV: `coverage-baseline-pre-m.csv`

## Families with leaves

| work_item_id | variants | official | statuses |
|---|---:|---:|---|
| `XD-CLAD` | 8 | 0 | RESOURCE_QC1=8 |
| `XD-CONC` | 51 | 0 | RESOURCE_QC1=35; RESOURCE_QC1_LEGACY=16 |
| `XD-FORM` | 12 | 0 | RESOURCE_QC1=12 |
| `XD-MASON-BRICK` | 14 | 0 | RESOURCE_QC1=14 |
| `XD-PAINT` | 17 | 0 | RESOURCE_QC1=17 |
| `XD-PLASTER` | 6 | 0 | RESOURCE_QC1=6 |
| `XD-REBAR` | 42 | 0 | RESOURCE_QC1=42 |
| `XD-TILE` | 4 | 0 | RESOURCE_QC1=4 |
| `XD-WATERPROOF` | 1 | 0 | RESOURCE_QC1=1 |

## Regenerate

```bash
python scripts/report-leaf-coverage.py --tag baseline-pre-m
```
