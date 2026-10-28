# Tests

## Canonical harness (binding)

**CLI** is the authoritative regression harness for leaf-code selection:

```powershell
dotnet build src\DSSTech.Cli\DSSTech.Cli.csproj -c Release
dotnet exec src\DSSTech.Cli\bin\Release\net8.0\DSSTech.Cli.dll --smoke
dotnet exec src\DSSTech.Cli\bin\Release\net8.0\DSSTech.Cli.dll --boq
dotnet exec src\DSSTech.Cli\bin\Release\net8.0\DSSTech.Cli.dll --boq-xlsx out\boq-smoke.xlsx
```

Expected leaf codes: `tests/expected.json` (currently **59** fixtures).

Expected BOQ quantity + norm unit: `tests/expected-quantity.json`, checked by the same `--smoke` run (kg → t, m2 → 100m2).

BOQ integrity: `--boq` asserts Σ BIM quantity detail == grouped.

## Supporting smoke (non-binding)

- `reference_smoke_test.py` — legacy Python reference engine; **deprecated** for product claims; kept for cross-check during migration.
- `resource_adjustment_smoke_test.py` — adjustment rule sanity.

Use `py -3` on Windows.

## Product evidence (Revit)

CLI PASS ≠ Revit PASS. In-Revit claims require Layer B screenshots under `docs/evidence/`.
