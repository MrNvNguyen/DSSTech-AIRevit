# Leaf coverage — `after-m4-ab`

- Generated (UTC): `2026-09-04T07:49Z`
- Runtime: `Data\norm-engine-runtime.json`
- Source DB: `Database_DinhMuc_QS_BIM_ENGINE_v1.sqlite`

## Summary

| Metric | Value |
|---|---|
| Work families | 98 |
| Families with ≥1 selectable leaf | **20** |
| Families with zero leaf | 78 |
| Selectable variants | 166 |
| Official-potential leaves (READY/APPROVED) | **6** |

### Status among selectable leaves

| digitization_status | count |
|---|---|
| `RESOURCE_QC1` | 146 |
| `RESOURCE_QC1_LEGACY` | 14 |
| `RESOURCE_READY` | 6 |

CSV: `coverage-after-m4-ab.csv`

## Families with leaves

| work_item_id | variants | official | statuses |
|---|---:|---:|---|
| `XD-CARPENTRY` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-CEILING` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-CLAD` | 8 | 0 | RESOURCE_QC1=8 |
| `XD-CONC` | 51 | 3 | RESOURCE_QC1=34; RESOURCE_QC1_LEGACY=14; RESOURCE_READY=3 |
| `XD-EARTH-EXC` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-EARTH-FILL` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-EARTH-TRANS` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-FORM` | 12 | 1 | RESOURCE_QC1=11; RESOURCE_READY=1 |
| `XD-MASON-AAC` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-MASON-BRICK` | 14 | 1 | RESOURCE_QC1=13; RESOURCE_READY=1 |
| `XD-MASON-REF` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-MASON-STONE` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-PAINT` | 17 | 0 | RESOURCE_QC1=17 |
| `XD-PLASTER` | 6 | 0 | RESOURCE_QC1=6 |
| `XD-REBAR` | 42 | 1 | RESOURCE_QC1=41; RESOURCE_READY=1 |
| `XD-ROCK` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-ROOF` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-SCREED` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-TILE` | 4 | 0 | RESOURCE_QC1=4 |
| `XD-WATERPROOF` | 1 | 0 | RESOURCE_QC1=1 |

## Regenerate

```bash
python scripts/report-leaf-coverage.py --tag after-m4-ab
```
