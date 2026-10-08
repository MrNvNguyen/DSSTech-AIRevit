# WAVE_CARD — S2b-STEP-WIZARD

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: S2b-STEP-WIZARD
  product_problem: "User has no stepwise Preview→Confirm UI; CAD panel does not exist; MCP Off vs CAD-still-works is untestable in product chrome."
  measured_owner: cad2bim-wizard
  hypothesis: "A WPF wizard (pick link+level → preflight → type list → preview → confirm) plus ribbon CAD buttons (Link, Kiến trúc, Kết cấu, MEPF, Preflight), calling Host primitives in-process, is sufficient for journey A and journey C (MCP Off, CAD still runs)."
  prediction:
    metric: wizard_preview_confirm
    expected_range: "CAD panel 5 buttons visible; preflight fail blocks Confirm; preview counts create/skip/fail; Confirm runs one create_batch; MCP Off still enables CAD buttons; Cost & Norm + BOQ still present"
    written_before_measurement: true
  falsifier: "Confirm bypasses Host (direct Wall.Create in the Window code-behind); OR CAD disabled when MCP Off; OR S3–S5 discipline engines shipped in this lever; OR second tab."
  one_allowed_lever: "Wizard chrome + CAD ribbon panel + in-process Host calls. Discipline intent engines remain stubs (list types from parse only) until S3–S5."
  theoretical_upper_bound: "Closes journeys A/C chrome. Does not close STR/ARCH/MEPF object tables."
  contracts_at_risk:
    - cad_link_stepwise
    - mcp_off_kills_ai_not_ribbon_cad
    - preflight_fail_fast
    - create_via_category
    - qs_panels_kept
    - one_addin_one_tab
  qa_tier: Q2
  qa_batch: solo
  q2_class: PRODUCT_SPOT
  regression_scope:
    - "MCP On/Off still kills AI only"
    - "Cost & Norm + BOQ"
    - "W0 confirm write-back"
  attempt_number: 0/3
  stop_rule: "Stop if this wave implements full S3 column/beam mapping. Cap 3. Wizard must call Host."
  code_authorized: false
```

## Blueprint (needs S1+S2; CODE after authorize)

S3–S5 **fill** type runners. This wave ships the **shell**: pick → preflight → list kinds present on CAD → preview/confirm for kinds that already have Host primitives (datum + optionally walls if R4 shipped). Structure/Arch/Mepf buttons may open the wizard with a discipline filter and show “chưa có engine (S3/S4/S5)” for unmapped kinds — **honest empty**, not fake create.

### Ribbon (App.cs)

Tab `DSSTech`, **new panel `CAD`** (not in R0a):

| Button id | Label | Command |
|---|---|---|
| `DssCadLink` | `Link\nmặt bằng` | `CadLinkCommand` — `UIDocument.PromptForFamilyInstancePlacement` is **wrong**. Use Revit built-in: `PostCommand(RevitCommandId.LookupPostableCommandId(PostableCommand.LinkCAD))` or TaskDialog instructing Insert → Link CAD, then pick `ImportInstance`. Prefer **Prompt to select ImportInstance** after user linked. |
| `DssCadArch` | `Kiến trúc` | `CadWizardCommand` args discipline=Arch |
| `DssCadStr` | `Kết cấu` | discipline=Structure |
| `DssCadMepf` | `MEPF` | discipline=Mepf |
| `DssCadPreflight` | `Preflight` | `CadPreflightCommand` (S1) |

MCP Off **must not** `Enabled=false` these buttons. Hook `McpHostService.IsOn` only for MCP panel.

### WPF window (match ResultsWindow style: code-behind, no MVVM framework)

| File | Role |
|---|---|
| `QSNormEngine.Revit/Cad/CadWizardWindow.cs` | `Window`, title `DSSTech — CAD2BIM` |
| `QSNormEngine.Revit/Cad/CadWizardCommand.cs` | `[Transaction(TransactionMode.Manual)]` like Norm Selector |
| `QSNormEngine.Revit/Cad/CadLinkCommand.cs` | Select link + bind Level |
| `QSNormEngine.Revit/Cad/Wizard/WizardSession.cs` | Selected `ImportInstance` id, `Level` id, `CadGeometry`, `PreflightReport`, `LayerMap` (editable DataGrid) |

**Steps:**

0. **Trục & tầng** if `GetLevels`/`GetGrids` empty **or** user ticks “dựng từ CAD”. Preview datum ops → Confirm → Host. If datums exist → skip silently (S2).
1. Combo: CAD links in document (`FilteredElementCollector.OfClass(typeof(ImportInstance))`) + Levels.
2. Run preflight (S1). If `!ok` → Confirm disabled, show checklist VN.
3. List object kinds found (from layer map ∩ geometry). Checkboxes one kind at a time (CHARTER: từng loại).
4. **Preview**: table columns `status, kind, type, reason` (create/skip/fail). No Transaction.
5. **Confirm**: `create_batch` via Host dispatcher on this thread. Then refresh preview.

Mapping DataGrid: pattern / kind / sample layer from CAD. Save overlay later: `cad-layer-map.{disc}.project.json` path shown as text (write file S3+; this wave may keep in-memory).

### Host call

Same as S2: on ExternalCommand thread, `McpDispatcher.Execute` — **no second Wall.Create**.

### DoD (product-visible)

1. Ribbon panel **CAD** with five buttons; tab still **DSSTech**; Cost & Norm + BOQ remain.
2. Preflight fail → cannot Confirm.
3. Preview then Confirm on datum or a single R4 primitive from CAD if mapped; Undo still one step.
4. MCP Off → Cursor ping fail; **CAD Preflight still opens**.
5. Screenshot of wizard.

### Regression

Do not remove QS buttons. Do not open ports from Cad2Bim.

### code_authorized

`false` until `STRATEGY_AUTHORIZE_PHASE` for `S2b-STEP-WIZARD`.
