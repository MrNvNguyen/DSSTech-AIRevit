# ENGINE_DB_PATCHES

Patches applied by `scripts/migrate-engine-v1.py` onto `Database_DinhMuc_QS_BIM_ENGINE_v1.sqlite` (never edit the original SQLite).

| # | Patch | Purpose |
|---|---|---|
| 1 | AE.222 WallThickness `>11` | Exclusive band vs `<=11` |
| 2 | AF.611–AF.615 ElementType | Distinguish rebar host |
| 3 | Legacy AF index rows | Resource-backed leaves missing from index |
| 4 | AF footing / beam-slab ElementType | Disambiguate WC-CONC leaves |
| 5 | **M1 Official unlock** | Promote smoke pilots → `RESOURCE_READY`; demote parent orphans |
| 6 | **M2 residual AE/AK** | Seed 7 selectable leaves + provisional QC1 resources |
| 7 | **M4 chapter AB** | Seed EXC/FILL/TRANS/ROCK pilots + provisional resources |
| 8 | **M4 chapter AC** | Seed DRIVE/BORED/SHEET/SOIL-MIX pile pilots + provisional resources |
| 9 | **M4 chapter AI** | Seed STEEL-FAB / STEEL-ERECT pilots + provisional resources |
| 10 | **M4 chapter BA** | Seed 9 electrical/HVAC MEP pilots + provisional resources |
| 11 | **M4 chapter BB** | Seed 8 pipe/plumbing pilots (+ provisional index for 4 WI) |
| 12 | **M4 chapter BC** | Seed 4 insulation (Bảo ôn) thickness pilots for MEP-INSUL |
| 13 | **M4 chapter BD** | Seed 4 FIRE-CTRL / HDD / WELL pilots |

## M1 pilot codes (`RESOURCE_READY`)

`AF.32230` · `AF.12230` · `AF.61432` · `AF.83111` · `AE.22110` · `AF.21210`

## M2 residual seeds (`RESOURCE_QC1`)

| Code | WorkItem |
|---|---|
| AE.88110 | XD-MASON-AAC |
| AE.11100 | XD-MASON-STONE |
| AE.91000 | XD-MASON-REF |
| AK.11200 | XD-ROOF |
| AK.41100 | XD-SCREED |
| AK.61000 | XD-CEILING |
| AK.71100 | XD-CARPENTRY |

## M4 AB pilots (`RESOURCE_QC1`, provisional)

| Code | WorkItem |
|---|---|
| AB.12110 | XD-EARTH-EXC |
| AB.13100 | XD-EARTH-FILL |
| AB.41000 | XD-EARTH-TRANS |
| AB.51110 | XD-ROCK |

## M4 AC pilots (`RESOURCE_QC1`, provisional)

| Code | WorkItem |
|---|---|
| AC.12410 | XD-PILE-DRIVE |
| AC.34510 | XD-PILE-BORED |
| AC.22100 | XD-PILE-SHEET |
| AC.41110 | XD-SOIL-MIX |

## M4 AI pilots (`RESOURCE_QC1`, provisional)

| Code | WorkItem |
|---|---|
| AI.11110 | XD-STEEL-FAB |
| AI.61110 | XD-STEEL-ERECT (column) |
| AI.61120 | XD-STEEL-ERECT (beam/framing) |

## M4 BA pilots (`RESOURCE_QC1`, provisional)

| Code | WorkItem |
|---|---|
| BA.11100 | MEP-HVAC-FAN |
| BA.12100 | MEP-HVAC-AC |
| BA.13100 | MEP-LIGHT |
| BA.14100 | MEP-CONDUIT |
| BA.16100 | MEP-WIRE |
| BA.17100 | MEP-SWITCH |
| BA.18200 | MEP-PROTECT |
| BA.19100 | MEP-LPS |
| BA.15100 | MEP-PUBLIC-LIGHT |

## M4 BB pilots (`RESOURCE_QC1`, provisional)

| Code | WorkItem | Notes |
|---|---|---|
| BB.11110 | MEP-PIPE-CONC | existing index |
| BB.21010 | MEP-PIPE-PLASTIC | provisional index |
| BB.31010 | MEP-PIPE-CI | provisional index |
| BB.51000 | MEP-PIPE-COPPER | existing index |
| BB.89300 | MEP-PIPE-STEEL | existing index |
| BB.74100 | MEP-FITTING | existing index |
| BB.61010 | MEP-VALVE | provisional index |
| BB.72010 | MEP-PLUMB-FIX | provisional index |

## M4 BC pilots (`RESOURCE_QC1`, provisional)

| Code | WorkItem | Notes |
|---|---|---|
| BC.13100 | MEP-INSUL | 25 mm |
| BC.13200 | MEP-INSUL | 30 mm |
| BC.13300 | MEP-INSUL | 50 mm |
| BC.13400 | MEP-INSUL | 100 mm |

## M4 BD pilots (`RESOURCE_QC1`, provisional)

| Code | WorkItem | Notes |
|---|---|---|
| BD.42400 | MEP-FIRE-CTRL | fire alarm / detector |
| BD.11100 | MEP-HDD | HDD pipe/HDPE |
| BD.12100 | MEP-HDD | HDD cable |
| BD.22210 | MEP-WELL | cable-percussion well |

Regenerate:

```bash
python scripts/migrate-engine-v1.py Database/Database_DinhMuc_QS_BIM_FINAL_v2_original.sqlite Database/Database_DinhMuc_QS_BIM_ENGINE_v1.sqlite
python scripts/export-runtime-data.py Database/Database_DinhMuc_QS_BIM_ENGINE_v1.sqlite Data/norm-engine-runtime.json
python scripts/report-leaf-coverage.py --tag after-m4-bd
```
