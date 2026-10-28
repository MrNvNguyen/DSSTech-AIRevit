# Revit build notes

The add-in project is `DSSTech.Revit`. C# namespaces and assembly names do not include a host year.

Autodesk still requires a per-year Add-in folder and a TFM that matches the host CLR. That mapping lives only in `scripts/revit-host.ps1`. When the host ships `RevitAPI.runtimeconfig.json`, its TFM wins (2025.5 / 2026.5 updates run on .NET 10); the table below is the fallback:

| Host | Default TFM |
|------|-------------|
| 2025 | `net8.0-windows` |
| 2026 | `net8.0-windows` |
| 2027 | `net10.0-windows` |

```powershell
# Every installed 2025–2027 host
.\scripts\build-revit.ps1

# One host
.\scripts\build-revit.ps1 -Year 2025

# Force a TFM
.\scripts\build-revit.ps1 -Year 2026 -Framework net10.0-windows
```

`scripts/install-revit.ps1` copies each `bin\<Configuration>\<year>\` output to `%APPDATA%\Autodesk\Revit\Addins\<year>\`.

When building `DSSTech.Revit` from Visual Studio, set MSBuild properties `RevitApiPath`, `RevitTargetFramework`, and `RevitHostLabel` (year) so output matches the install script.

Official references:
- https://www.autodesk.com/support/technical/article/caas/tsarticles/ts/7JiiE4UoWHaTjxzuF754mL.html
- https://help.autodesk.com/view/RVT/2025/ENU/?caas=caas/sfdcarticles/sfdcarticles/Revit--Requirements-for-products-affected-by-the-Microsoft--NET-10-transition.html

## No NuGet runtime dependency in the Revit add-in

The add-in reads `System.Text.Json` runtime data. SQLite is the governed master/source database but does not need to be opened directly by Revit. This reduces deployment issues from native SQLite binaries and assembly binding conflicts.

The JSON stack comes from the .NET 8/10 shared framework. Revit 2024 (.NET Framework 4.8) is not supported.
