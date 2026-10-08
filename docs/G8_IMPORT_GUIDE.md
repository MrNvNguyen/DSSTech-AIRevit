# G8 import guide (DSS.QSEngine)

## Output file

**BOQ Extract → Export G8 CSV** produces `g8-cong-trinh.csv` mapped to G8 sheet **Công trình** (columns A,B,E,F,G,H,I,J,K,L,P — logic from row 6).

Implementation: `src/QSNormEngine.Core/G8BoqExporter.cs`.

## Preconditions

1. G8 installed (e.g. `C:\G8\G8Ver2020.exe`).
2. BOQ rows are **official-ready** (`CanIssueOfficially=true`, leaf code present).
3. Quantities and units match G8 `CauHinh\KieuDuToan.txt` expectations.

## Manual round-trip (product evidence)

1. In Revit: select representative elements → **BOQ Extract**.
2. Export **G8 CSV** (official rows only in exporter).
3. In G8: import CSV into dự toán template per G8 UI.
4. Spot-check 3–5 lines: mã định mức, ĐVT, khối lượng.
5. Save evidence under `docs/evidence/g8-roundtrip/` (screenshots + CSV SHA-256, no `.rvt` in git).

## Known limits

- Current export supports **CSV** and **Excel (.xlsx)** via ClosedXML (`BoqWorkbookExporter`).
- No automated G8 API — manual UI verification required.
- `SelectedQc1` / code-only paths must not be treated as issuable.

## CLI smoke (no Revit)

```powershell
dotnet exec src\QSNormEngine.Cli\bin\Release\net8.0\QSNormEngine.Cli.dll --boq
```
