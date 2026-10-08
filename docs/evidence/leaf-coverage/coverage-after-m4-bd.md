# Leaf coverage — `after-m4-bd`

- Generated (UTC): `2026-09-12T10:42Z`
- Runtime: `Data\norm-engine-runtime.json`
- Source DB: `Database_DinhMuc_QS_BIM_ENGINE_v1.sqlite`

## Summary

| Metric | Value |
|---|---|
| Work families | 98 |
| Families with ≥1 selectable leaf | **47** |
| Families with zero leaf | 51 |
| Selectable variants | 198 |
| Official-potential leaves (READY/APPROVED) | **6** |

### Status among selectable leaves

| digitization_status | count |
|---|---|
| `RESOURCE_QC1` | 178 |
| `RESOURCE_QC1_LEGACY` | 14 |
| `RESOURCE_READY` | 6 |

CSV: `coverage-after-m4-bd.csv`

## Families with leaves

| work_item_id | variants | official | statuses |
|---|---:|---:|---|
| `MEP-CONDUIT` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-FIRE-CTRL` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-FITTING` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-HDD` | 2 | 0 | RESOURCE_QC1=2 |
| `MEP-HVAC-AC` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-HVAC-FAN` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-INSUL` | 4 | 0 | RESOURCE_QC1=4 |
| `MEP-LIGHT` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-LPS` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-PIPE-CI` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-PIPE-CONC` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-PIPE-COPPER` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-PIPE-PLASTIC` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-PIPE-STEEL` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-PLUMB-FIX` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-PROTECT` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-PUBLIC-LIGHT` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-SWITCH` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-VALVE` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-WELL` | 1 | 0 | RESOURCE_QC1=1 |
| `MEP-WIRE` | 1 | 0 | RESOURCE_QC1=1 |
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
| `XD-PILE-BORED` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-PILE-DRIVE` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-PILE-SHEET` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-PLASTER` | 6 | 0 | RESOURCE_QC1=6 |
| `XD-REBAR` | 42 | 1 | RESOURCE_QC1=41; RESOURCE_READY=1 |
| `XD-ROCK` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-ROOF` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-SCREED` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-SOIL-MIX` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-STEEL-ERECT` | 2 | 0 | RESOURCE_QC1=2 |
| `XD-STEEL-FAB` | 1 | 0 | RESOURCE_QC1=1 |
| `XD-TILE` | 4 | 0 | RESOURCE_QC1=4 |
| `XD-WATERPROOF` | 1 | 0 | RESOURCE_QC1=1 |

## Regenerate

```bash
python scripts/report-leaf-coverage.py --tag after-m4-bd
```
