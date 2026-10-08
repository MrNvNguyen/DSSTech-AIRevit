# Element quantity table schema (Q2)

Separate from the 28-column BOQ audit export. Source: `ElementQuantityExporter` + `Config/element-quantity-columns.json`.

## Groups

| Group | Default | Purpose |
|---|---|---|
| **1** | ON | PO 21 columns (Element_ID … Host ID) |
| **2** | OFF | Model trace (Family, Workset, Phase, …) |
| **3** | OFF | Location detail (Base/Top Level, elevations, Zone) |
| **4** | ON | Formwork detail (`QS_FormworkSide` … Contact IDs) |
| **5** | ON | Norm mapping (WorkItemId, NormCode, Official, …) |

Toggle groups by editing `enabledGroups` in config — no rebuild required (config is copied next to the add-in).

## Group 1 — 21 columns

`Element_ID · GUID · Category · Type · Mark · Level · QS_InOutBuilding · Vật liệu · Mác vật liệu · Chiều rộng (m) · Chiều cao (m) · Chiều dày (m) · Chiều dài (m) · Diameter (mm) · Description · Count · Area (m2) · Volume (m3) · Weight (kg) · QS_FormworkArea · Host ID`

## Export surfaces

- Excel sheet **Khối lượng cấu kiện** (via BOQ Excel export)
- CSV from BOQ results window → **Export Quantity CSV**
- Tab **Bảng khối lượng** in `BoqResultsWindow`

## Q3 — ViewSchedule in model

Ribbon **BOQ → Quantity Schedule** (or button on BOQ results window) creates/updates
`QS-BIM Khối lượng cấu kiện` (multi-category, idempotent by name).

Requires Setup Parameters so `QS_*` fields are schedulable. Native Mark/Type/Volume/Area
plus `QS_InOutBuilding`, `QS_FormworkArea`, `QS_HostId`, `QS_WeightKg`, `QS_MaterialGrade`.

## Related

- Facts: `RevitElementFactsFactory.AddQuantityTableFacts`
- Model fields: `BoqLineItem` Q1/Q2 properties
- Formwork SSOT: `docs/FORMWORK_RULES.md`
- Schedule builder: `src/QSNormEngine.Revit/Quantity/QuantityScheduleBuilder.cs`
