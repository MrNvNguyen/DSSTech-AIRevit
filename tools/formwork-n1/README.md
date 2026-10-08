# Formwork N1 / N2 Measure Harness

Read-only measurement. Production Formwork sources are **linked** into `FormworkN1.AddIn.dll` (no dependency on installed `QSNormEngine.Revit.dll`).

## Install

1. **Close Revit** completely.
2. Double-click `tools\formwork-n1\install-n1.cmd` or:

```powershell
cd d:\DEV\QSNormEngine_Revit
powershell -NoProfile -ExecutionPolicy Bypass -File tools\formwork-n1\run-n1-measure.ps1 -Year 2025 -DevLink
```

3. Restart Revit → **Add-Ins → External Tools**:
   - **Formwork N1 Measure** — N1 regression / a-priori bar
   - **Formwork N2 Measure** — rules JSON + N2 evidence
   - **Formwork N3 Measure** — height-clip segments (column top must be above L2 slab)

## N2 — JSON toggle proof (no rebuild)

1. Ensure `Config/formwork-rules.json` has `"groundContactAutoDetect": false`.
2. Set env (optional): `QS_FORMWORK_RESULT_NAME=baseline-result.json`
3. Run **Formwork N2 Measure** → writes `docs/evidence/formwork-n2/...` or rules-toggle folder.
4. Edit **only** JSON: `"groundContactAutoDetect": true` (or remove Bottom from Beam faces). **Do not** `dotnet build`.
5. Set `QS_FORMWORK_RESULT_NAME=toggled-result.json` → run Measure again.
6. Compare:

```powershell
py -3 tools\formwork-n1\compare-n2-runs.py
```

## N1 summarize

```powershell
py -3 tools\formwork-n1\summarize-n1.py
```

## Rollback

Delete `C:\ProgramData\Autodesk\Revit\Addins\<year>\FormworkN1.addin` (+ folder `FormworkN1\` if present).
