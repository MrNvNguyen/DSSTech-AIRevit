# QS-BIM Shared Parameters (DSS.QSEngine)

Geometry (volume/area/length) is read from Revit where possible. Construction-method and QS metadata use Shared Parameters bound by **Setup Parameters**.

## Binding rules (add-in)

| Setting | Value |
|---------|--------|
| Shared Parameter file | `Config/QS_SharedParameters.txt` (shipped with add-in) |
| Group parameter under | **General** (`GroupTypeId.General` / `PG_GENERAL`) |
| Instance / Type | **Instance** |
| Categories | Architecture + Structure + MEP (see `SharedParameterSetup.GetTargetCategories`) |

## Parameter list

| Parameter | Type | Canonical value / unit | Typical use |
|---|---|---|---|
| `QS_WorkItemId` | Text | e.g. `XD-PLASTER` | Manual classification override |
| `QS_ConstructionHeight` | Number | m from ±0.00 | Height/elevation band |
| `QS_Thickness` | Number | cm | Layer / wall thickness |
| `QS_ConcreteGrade` | Text | `B20`, `B25`, `B30` | Concrete spec |
| `QS_ConcreteSupply` | Text | `SiteMix`, `SiteBatchPlant`, `ReadyMix` | Supply adjustment |
| `QS_PlacementMethod` | Text | `Manual`, `Crane`, `Pump` | Concrete method (required for XD-CONC) |
| `QS_PumpType` | Text | `Static`, `Mobile` | Pump adjustment |
| `QS_SurfaceLocation` | Text | `Interior`, `Exterior` | Finish selector |
| `QS_TileArea` | Number | m² / tile | Tile leaf selector |
| `QS_CoatSystem` | Text | `1P1F`, `1P2F` | Paint selector |
| `QS_PrePutty` | Yes/No | true/false | Paint selector |
| `QS_FormworkSystem` | Text | `PlywoodFrame`, `Steel`, `Wood` | Formwork |
| `QS_FormworkMaterial` | Text | company dictionary | Formwork material |
| `QS_FormworkArea` | Number | m² | Formwork takeoff total (**visible**) |
| `QS_FormworkSide` | Number | m² | Side incl. vai/shoulder (**visible**) |
| `QS_FormworkBottom` | Number | m² | Bottom (**visible**) |
| `QS_FormworkDeduct` … `Stamp` / System / Material | — | — | Bound but **hidden** on Properties |
| `QS_InOutBuilding` | Text | `Interior` / `Exterior` / `Mixed` | Trong/ngoài nhà (P) |
| `QS_InOutSource` | Text | WallFunction / Room / … | Audit nguồn InOut |
| `QS_PaintThickness` | Number | mm | Road marking |
| `QS_BrickType` | Text | company dictionary | Masonry |
| `QS_MortarGrade` | Text | e.g. `M75` | Masonry / plaster |
| `QS_PipeMaterial` | Text | `PVC`, `HDPE`, `Steel`, … | MEP pipe family |
| `QS_JointType` | Text | company dictionary | Pipe fittings |
| `QS_SystemType` | Text | system code/name | MEP system |
| `QS_EquipmentType` | Text | Fan / AC / … | Mechanical equipment |
| `QS_NormCode` | Text | leaf code | Write-back |
| `QS_NormStatus` | Text | selection status | Write-back |
| `QS_NormStamp` | Text | ISO timestamp | Write-back |
| `QS_ElementType` | Text | Foundation / Column / Wall / … | Override inferred type (e.g. Blinding) |

## How to apply on a model

1. Restart Revit after install.
2. **Setup Parameters** → expect ~30 `QS_*` under **General** (includes N4 formwork area/side/bottom params; bind only — no auto write-back).
3. **Norm Selector** → **Gán giá trị QS**: chọn từ danh sách chuẩn (full catalog / theo WorkItem / chỉ Missing). Geometry (Height, Volume, Width bbox) lấy từ BIM; enum/discrete ghi QS_*.
4. Expect **Qc1** for most TT38 RESOURCE_QC rows; **Official** only when digitization APPROVED.
5. **Ghi vào cấu kiện** when Official > 0 (or choose draft Qc1 write if offered).

List-selectable fields live in `QsValueCatalog` (WorkItemId, ElementType, PlacementMethod, MEP, finishes, piles/road, …). Continuous numbers like free-form Height stay model-driven.

## Official issue note

`SelectedQc1` / `RESOURCE_QC*` never becomes Official. Write-back only persists Official-ready leaf codes. On typical Vietnamese models, most structural concrete is `RESOURCE_QC1` → expect **Qc1** after filling PlacementMethod, not Official — until digitization is APPROVED.

For demo write-back testing, use fixtures/elements that resolve to APPROVED status, or temporarily inspect `DigitizationStatus` in export JSON.

## Governance

Do not hard-code TT38 codes into families as the primary key. Prefer:

`category + geometry + QS_* metadata → WorkItem → NormCode`

`QS_WorkItemId` is an override only.
