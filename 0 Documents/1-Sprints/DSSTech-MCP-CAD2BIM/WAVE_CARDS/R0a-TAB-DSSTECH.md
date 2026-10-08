# WAVE_CARD — R0a-TAB-DSSTECH

```yaml
WAVE_CARD:
  directive_id: DSSTECH-R0A-2026-09-23
  wave_id: R0a-TAB-DSSTECH
  product_problem: "Ribbon tab still reads DSS.QSEngine; PO-intent product name is DSSTech. User cannot see the platform identity."
  measured_owner: ribbon-identity
  hypothesis: "Renaming the tab string, add-in Name/VendorDescription, and all user-visible DSS.QSEngine TaskDialog/Transaction/Window prefixes to DSSTech makes the loaded add-in show tab DSSTech while the five existing commands still open."
  prediction:
    metric: ribbon_tab_string
    expected_range: "exactly 'DSSTech'; panels 'Cost & Norm' and 'BOQ' still present; 5 pushbuttons still invoke Setup Parameters, Norm Selector, Formwork Takeoff, BOQ Extract, Quantity Schedule"
    written_before_measurement: true
  falsifier: "After install+Revit restart, ribbon tab is still DSS.QSEngine; OR any of the five buttons missing/broken; OR a second tab appears; OR MCP/CAD buttons appear (out of lever)."
  one_allowed_lever: "Rename identity strings only (tab + addin Name/VendorDescription + dialog/transaction prefixes). No MCP panel. No CAD panel. No new commands."
  theoretical_upper_bound: "Closes 100% of the tab-identity gap. Closes 0% of MCP/CAD."
  contracts_at_risk:
    - one_addin_one_tab
    - qs_panels_kept
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "Cost & Norm: Setup Parameters, Norm Selector"
    - "BOQ: Formwork Takeoff, BOQ Extract, Quantity Schedule"
    - "AddInId unchanged 7B52A0F1-7C61-4C8B-A6A4-0AC7398341A2"
    - "FullClassName remains QSNormEngine.Revit.App"
  attempt_number: 0/2
  stop_rule: "Stop after 2 measured attempts. Stop if lever adds MCP/CAD buttons or a second tab. Do not bundle R0b/MCP/CAD/create into this lever. Standing advance to R0b is technical+lead after this box closes — not this lever."
  code_authorized: true
```

## Blueprint (authorized 2026-09-23 — `DSSTECH-R0A-2026-09-23`)

### Lever boundary

**IN:** string identity. **OUT:** MCP/CAD empty panels, new PushButtonData, new projects, Host, pipe. Strategy: one lever = tab identity. Prefer **rename only**; MCP panel in R0b; CAD panel in S2b.

### Files to change (names)

| Path | Change |
|---|---|
| `src/QSNormEngine.Revit/App.cs` | `const string tab = "DSS.QSEngine"` → `"DSSTech"` (today line 10). Keep `CreateRibbonTab` try/catch. Do **not** add panels. |
| `src/QSNormEngine.Revit/QSNormEngine.addin.template` | `<Name>DSSTech</Name>`; `<VendorDescription>DSSTech</VendorDescription>`. Keep AddInId + FullClassName. |
| `src/QSNormEngine.Revit/QSNormEngine.Revit.csproj` | `<Product>` / `<AssemblyTitle>` `DSS.QSEngine` → `DSSTech` (identity only). |
| `scripts/install-revit.ps1` | Manifest here-string `<Name>` / `<VendorDescription>` currently hardcoded `DSS.QSEngine` (lines ~38–43). Must match template or tab in Add-Ins manager stays old. |
| `scripts/package-revit.ps1` | Same manifest + README “ribbon **DSS.QSEngine**” → **DSSTech**. Package folder prefix `DSS.QSEngine-Revit-v…` may stay or become `DSSTech-Revit-v…` — **do not** invent a second add-in file. |
| Dialog/transaction prefixes (grep `DSS.QSEngine` in `src/QSNormEngine.Revit/**/*.cs`, exclude `obj/`): | Replace user-visible title/prefix with `DSSTech`. Do not rename C# types (`NormSelectorCommand`, etc.). |

**Inventory (must all change or product still says DSS.QSEngine):**

- `App.cs` tab
- `FormworkResultsWindow.cs` Title + TaskDialog
- `BoqResultsWindow.cs` Title + MessageBox + Transaction `"DSS.QSEngine quantity schedule"`
- `FormworkScopeResolver.cs` TaskDialog
- `QuantityScheduleCommand.cs` TaskDialog + Transaction
- `FormworkTakeoffCommand.cs` TaskDialog
- `BoqExtractCommand.cs` TaskDialog
- `FormworkWriteBack.cs` Transaction `"DSS.QSEngine formwork write"`
- `SharedParameterSetup.cs` Transaction `"DSS.QSEngine bind QS parameters"`
- `AssignQsValuesDialog.cs` Title + MessageBox
- `FormworkScopeDialog.cs` Title
- `InOutWriteBack.cs` Transaction `"DSS.QSEngine assign InOut"`
- `QsParameterFiller.cs` Transaction `"DSS.QSEngine assign QS values"`
- `NormSelectorCommand.cs` TaskDialog
- `SetupParametersCommand.cs` TaskDialog
- `ResultsWindow.cs` Title + MessageBox
- `NormWriteBack.cs` Transaction `"DSS.QSEngine write-back"` → `"DSSTech write-back"` (**W0 unchanged**: still confirm-only param write)

Do **not** edit `obj/**` generated AssemblyInfo; rebuild will pick csproj Product.

### Revit API notes

- `UIControlledApplication.CreateRibbonTab(string)` — tab title is the string. If a leftover `DSS.QSEngine` tab exists from an old session, Revit may keep a ghost empty tab until restart with only the new add-in. QA: full Revit restart after `scripts/install-revit.ps1`.
- `GetRibbonPanels(tab)` / `CreateRibbonPanel(tab, "Cost & Norm")` — panel **names** stay English as today (`Cost & Norm`, `BOQ`). Do not rename panels (bar `qs_panels_kept` includes command names, not necessarily panel English labels — keep labels).
- PushButton ids stay `QSSetupParameters`, `QSNormSelector`, `QSFormworkTakeoff`, `QSBoqExtract`, `QSQuantitySchedule`.
- `IExternalApplication` still `QSNormEngine.Revit.App`. One `.addin` file `QSNormEngine.addin`.

### DoD (product-visible)

1. Revit host (same year as install) → ribbon tab **DSSTech**.
2. Two panels: **Cost & Norm** (2 buttons) + **BOQ** (3 buttons). Click each once: command starts (dialog or expected empty-selection TaskDialog). No unhandled exception.
3. Add-Ins dialog / `.addin` Name reads **DSSTech**.
4. **No** MCP panel, **no** CAD panel, **no** extra tab.
5. Write-back still requires confirm (W0). Qc1 still not official.

### Regression

Always: Cost & Norm + BOQ still on tab. Norm Selector still `TransactionMode.Manual`. No `src` MCP types.

### Build / install

Unchanged flow: `scripts/build-revit.ps1` then `scripts/install-revit.ps1`. No new csproj besides the Product string on the existing Revit project.

### code_authorized

`true` for this box only (`DSSTECH-R0A-2026-09-23`). Does not authorize R0b or any later wave as part of this lever.
