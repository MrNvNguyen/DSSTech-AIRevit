# G8 round-trip evidence

Manual product evidence for **G2** (import BOQ export into G8). Automated Layer A dry-run is recorded below; Layer B requires Revit + G8 UI.

## Layer A dry-run (CLI — supporting, not product PASS)

```powershell
dotnet exec src\QSNormEngine.Cli\bin\Release\net8.0\QSNormEngine.Cli.dll --boq-xlsx out\boq-smoke.xlsx
```

| Artifact | Bytes | SHA-256 |
|----------|------:|---------|
| `out/boq-smoke.xlsx` | 14200 | `ae7040722899a232861bebee30b29b25bbc9f60d47c02432698eb34100521e24` |
| `out/boq-smoke-g8.csv` | 2325 | `4779431e407eb38990b6635e5d9c0154df3017e80c993d8b14e2e624e352e99f` |
| `out/boq-smoke-g8-official.csv` | 773 | `8fe9746bbca70d2316c28dc1cad569be414da46baba964747aa3dfc025df1ae3` |

Official-only CSV: **7** lines (pilot `RESOURCE_READY` leaves). `out/` is local regenerate output — do not commit binaries.

G8 install confirmed present at `c:\G8\G8Ver2020.exe` (Layer B still manual UI).

## Layer B checklist (product evidence)

1. Revit: selection with Official-ready rows (or Qc1 draft for write-back test).
2. **BOQ Extract** → Export BOQ Excel / G8 CSV (`*-g8-official.csv` preferred).
3. G8Ver2020: import sheet **Công trình** per `docs/G8_IMPORT_GUIDE.md`.
4. Spot-check 3 lines: mã định mức, ĐVT, khối lượng.
5. Fill table; screenshots optional (PNG only, no `.rvt`).

| Field | Value |
|-------|-------|
| Date | |
| Revit host | 2025 / 2026 / 2027 |
| Export file | |
| File bytes / SHA-256 | |
| G8 version | G8Ver2020 |
| Result | PASS / FAIL |
| Notes | |

## Screenshots

- `revit-boq-window.png`
- `g8-import-result.png`

Large binaries: see `BINARY-NOT-IN-GIT.md`.
