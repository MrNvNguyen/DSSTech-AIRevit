# Development roadmap after MVP

## Phase 1 - current package (delivered in QSNormEngine_Revit)

- Core norm-selection engine + classification / bim_mapping rules.
- Runtime export from SQLite (`migrate-engine-v1` → `export-runtime-data`).
- Revit selection adapter (hosts 2024–2027) · ribbon **DSS.QSEngine**.
- Multi-element result grid · Gán giá trị QS (pick-lists) · Setup Parameters (45 QS_*).
- CSV/JSON + BOQ 28-col Excel + G8 Công trình export.
- AF/AE/AK resource-backed selection where digitized (Qc1 vs Official).
- CLI harness: `--smoke` (26 fixtures) · `--boq` · `--boq-xlsx` (+ official G8 CSV).
- Coverage: 20/98 WorkItem có leaf selectable; 6 Official pilots AF/AE.

## Phase 2 - database coverage
Digitize leaf conditions/resources for **AB + AC + AI + BA + BB + BC + BD (pilots started)** while retaining the same engine interfaces.

## Phase 3 - Revit production UX (partially delivered)

Done in campaign BOQ: missing-parameter dashboard, write-back after confirm, BOQ grouping.

Still backlog: dockable panel (non-modal), project defaults editor UI, schedule generation beyond BOQ Extract.

## Phase 4 - Cost engine
- Location/date price database.
- Material/labor/machine pricing.
- Cost aggregation by WBS/Zone/Level/Package.
- Comparison model quantity vs BOQ quantity.

## Phase 5 - governance
- QC2 approval workflow.
- Source/version/effective-date management.
- Superseded norms.
- Audit log for human overrides.

## Phase 6 - CAD/PDF intake (backlog)

Research memo: [docs/PHASE6-CAD-PDF-MEMO.md](PHASE6-CAD-PDF-MEMO.md). Not an open implementation box until PO signs scope.
