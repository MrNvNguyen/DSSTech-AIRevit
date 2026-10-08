# Leaf Official / G8 evidence (Sóng M5 — Layer A)

CLI harness after M1 Official unlock + M2 residual seeds.

## Claims (Layer A only — not Revit product PASS)

| Check | Result |
|---|---|
| `--smoke` | **19/19 PASS** (`tests/expected.json`) |
| `--boq` | Σ detail == group; **official-ready: 7** |
| `--boq-xlsx` | sheet `G8 Cong trinh` filters Official (`officialOnlyForG8=true`) |

## Artifacts (local `out/` — may be gitignored)

Do **not** commit large binaries. Record hashes of regenerate outputs:

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `out/boq-after-m.xlsx` | 13049 | `01a31795083257bcd37910ebdacb3f32d78007de8bf59d3f90c31a487cb2b132` |
| `out/boq-after-m-g8.csv` | 1734 | `2a5ff5097f204b24da2a12636f422637603f6d5cc1f6820d777fbb989972d635` |
| `out/boq-after-m-g8-official.csv` | 773 | `8fe9746bbca70d2316c28dc1cad569be414da46baba964747aa3dfc025df1ae3` |

Regenerate:

```bash
dotnet build src/QSNormEngine.Cli/QSNormEngine.Cli.csproj -c Release
dotnet exec src/QSNormEngine.Cli/bin/Release/net8.0/QSNormEngine.Cli.dll --smoke
dotnet exec src/QSNormEngine.Cli/bin/Release/net8.0/QSNormEngine.Cli.dll --boq
dotnet exec src/QSNormEngine.Cli/bin/Release/net8.0/QSNormEngine.Cli.dll --boq-xlsx out/boq-after-m.xlsx
```

## Layer B (Revit + G8 UI)

Pending user: Norm Selector Official count > 0 on pilot model + import `*-g8-official.csv` / xlsx sheet into G8Ver2020 — see `docs/evidence/g8-roundtrip/`.

## Official pilot codes

`AF.32230` · `AF.12230` · `AF.61432` · `AF.83111` · `AE.22110` · `AF.21210`
