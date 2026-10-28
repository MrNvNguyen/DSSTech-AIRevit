# N0 Formwork Spike Harness (OBSERVE_ONLY)

Isolated Revit add-in for **N0-FORMWORK-SPIKE** — measures `get_Geometry(Fine)` timing, `BooleanOperationsUtils.Difference` fail rate, and a probe-epsilon contact estimate on a joined beam–column pair.

Does **not** touch `src/DSSTech.Revit`, runtime JSON, or DSS.QSEngine ribbon.

## Prerequisites

- Revit 2025–2027 (default build target: **2025**)
- .NET SDK matching the host TFM (read from the host's `RevitAPI.runtimeconfig.json`; fallback `net8.0-windows` for 2025–2026, `net10.0-windows` for 2027)
- Pilot structural model locally (see `docs/evidence/formwork-spike/pilot-model-pointer.md`) — **never commit `.rvt`**

## Build

```powershell
cd d:\DEV\QSNormEngine_Revit
.\tools\formwork-spike\run-spike.ps1 -Year 2025
```

Optional flags:

- `-Install` — copy DLL + write `.addin` under `%ProgramData%\Autodesk\Revit\Addins\<year>\`
- `-Configuration Debug`
- `-Framework net10.0-windows` — when building for Revit 2027

## Install add-in (ProgramData)

```powershell
.\tools\formwork-spike\run-spike.ps1 -Year 2025 -Install
```

Restart Revit after install (or if DLL was locked, close Revit then re-run `-Install`). Command appears under **Add-Ins → External Tools → Formwork Spike N0**.

**If probe was NOT_MEASURED:** clear selection (or select a joined column+beam), re-run once — spike now uses face-extrusion probe and can auto-pick a joined pair.

## Run spike in Revit

1. Open the pilot model (joined beam + column for probe; broader model for timing scales).
2. *(Optional)* Pre-select exactly **two** joined probe elements (beam + column).  
   Or fill `element_ids` in `tools/formwork-spike/fixtures/hand-calc-beam-column.yaml` after first discovery run.
3. Run **Formwork Spike N0**.
4. Confirm dialog shows counts; result written to:

   `docs/evidence/formwork-spike/N0-FORMWORK-SPIKE/result.json`

## Summarize

```powershell
py -3 tools/formwork-spike/summarize-spike.py
```

Prints N0 bars to stdout and updates memo tables in `docs/evidence/formwork-spike/MEMO-TEMPLATE.md`.

## Hand-calc fixture

`tools/formwork-spike/fixtures/hand-calc-beam-column.yaml`:

- Column 400×400×3000 mm
- Beam 300×600×4000 mm
- Expected contact (column side ∩ beam end): **0.24 m²**

## Evidence layout

| Path | Role |
|---|---|
| `docs/evidence/formwork-spike/N0-FORMWORK-SPIKE/result.json` | Machine SSOT per run |
| `docs/evidence/formwork-spike/N0-FORMWORK-SPIKE/claims.yaml` | Hypothesis / status |
| `docs/evidence/formwork-spike/N0-FORMWORK-SPIKE/evidence-index.json` | Index + SHA pointers |
| `docs/evidence/formwork-spike/MEMO-TEMPLATE.md` | Human memo (filled by summarize script) |

## N0 gate (plan)

- Boolean fail rate **≤ 5%** on valid solids → proceed to N1 design
- Fail **> 5%** → stop; report before production Formwork code in `src/`

## Rollback

Remove `%ProgramData%\Autodesk\Revit\Addins\<year>\FormworkSpike.addin` and folder `FormworkSpike\`. No production add-in files are modified.
