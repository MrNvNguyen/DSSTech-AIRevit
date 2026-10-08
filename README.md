# QS-BIM Norm Selection Engine for Revit

MVP source package that consumes the **Database Định mức QS-BIM FINAL v2** and performs:

`Revit Object -> WorkItem -> Norm leaf-code -> Adjustment -> VL/NC/M resources`

The add-in source (`QSNormEngine.Revit`) is host-agnostic. Supported Revit hosts: **2024–2027**.

## Projects

- `QSNormEngine.Core`: Revit-independent selection/rule/resource engine.
- `QSNormEngine.Cli`: console harness for testing an `ElementFacts` JSON outside Revit.
- `QSNormEngine.Revit`: Revit add-in (ribbon command + results grid + CSV/JSON export).

## Data

- `Database/Database_DinhMuc_QS_BIM_FINAL_v2_original.sqlite`: unchanged QS database snapshot.
- `Database/Database_DinhMuc_QS_BIM_ENGINE_v1.sqlite`: engine-ready migrated snapshot used by runtime export.
- `Data/norm-engine-runtime.json`: runtime cache generated from SQLite.
- `Config/norm-engine.config.json`: engine behavior/project defaults.

## Build and install

Prerequisites:
1. One or more Revit 2024–2027 installs (API assemblies next to `Revit.exe`).
2. Visual Studio 2022+ or a `dotnet` SDK matching the host runtime (.NET Framework 4.8, .NET 8, or .NET 10 — see `docs/BUILD_NOTES.md`).

PowerShell discovers every installed host in range and builds/installs each:

```powershell
.\scripts\build-revit.ps1
.\scripts\install-revit.ps1
```

One host, or a host whose folder name is non-standard:

```powershell
.\scripts\build-revit.ps1 -Year 2025
.\scripts\build-revit.ps1 -RevitApiPath "D:\Autodesk\Revit" -Year 2026
.\scripts\install-revit.ps1 -Year 2025
```

Revit 2026.5+ runs on .NET 10; the default 2026 compile is still `net8.0-windows`. Override when needed:

```powershell
.\scripts\build-revit.ps1 -Year 2026 -Framework net10.0-windows
.\scripts\install-revit.ps1 -Year 2026
```

Or open `QSNormEngine.sln` and set `RevitApiPath` (and optionally `RevitTargetFramework` / `RevitHostLabel`) on `QSNormEngine.Revit`. Prefer the scripts so the output lands in `bin\<Configuration>\<year>\` for install.

## Use in Revit

1. Start Revit after installation.
2. Select one or more model elements.
3. Open ribbon **DSS.QSEngine -> Cost & Norm -> Norm Selector**.
4. Review WorkItem, NormCode, status, missing parameters and warnings.
5. Export result/resource CSV or JSON.

The current command is intentionally **read-only**: it does not modify the model.

## Critical behavior

- `RESOURCE_QC1` results can be calculated for validation, but are **not officially issuable** until QS review/approval.
- Concrete selection requires an explicit construction method unless configured otherwise.
- Finish objects can use `QS_WorkItemId` to override an ambiguous Revit category (e.g. a wall representing plaster rather than masonry/concrete).
- Runtime data is regenerated from SQLite with:

```bash
python scripts/export-runtime-data.py Database/Database_DinhMuc_QS_BIM_ENGINE_v1.sqlite Data/norm-engine-runtime.json
```

## Smoke test without Revit

When .NET 8 SDK is installed:

```powershell
dotnet run --project src\QSNormEngine.Cli -- SampleInputs\concrete-column-pump.json
dotnet run --project src\QSNormEngine.Cli -- SampleInputs\brick-wall.json
dotnet run --project src\QSNormEngine.Cli -- SampleInputs\interior-plaster.json
```

Expected leaf codes are documented in `tests/expected.json`.

## Validation performed in this package

Reference smoke tests currently cover:
- concrete column / static pump;
- concrete column / mobile pump;
- concrete column / manual + ready-mix;
- brick wall thickness band;
- interior plaster thickness;
- column rebar by diameter + construction height;
- plywood slab formwork by construction height;
- adjustment rules for ready-mix manual placement and mobile pump.

Run:

```bash
python tests/reference_smoke_test.py
python tests/resource_adjustment_smoke_test.py
```
