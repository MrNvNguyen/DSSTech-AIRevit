# BOQ export schema (DSS.QSEngine)

Audit export uses **29 columns** defined in `BoqExporter.ExportColumnOrder` (`src/DSSTech.Core/BoqExporter.cs`).

| # | Column | Source |
|---|--------|--------|
| 1 | STT | Row index |
| 2 | Bộ môn | Work family / discipline |
| 3 | Mã công tác BIM | Work item id |
| 4 | Mã định mức | Selected leaf code (official) or placeholder |
| 5 | Tên công tác TT 38 | Norm name from runtime index |
| 6 | Loại cấu kiện / Host | Host kind |
| 7 | Biện pháp | Placement / construction method |
| 8 | Kích thước danh nghĩa | Nominal dimensions |
| 9 | Dải hình học | Geometry band |
| 10 | Vật liệu / mác | Material label |
| 11 | Hệ MEPF | System code + name |
| 12 | System nguồn | Source system |
| 13 | Schedule / Type | Schedule or type name |
| 14 | Tầng / Level | Level |
| 15 | Zone | Zone |
| 16 | Type cấu kiện | Element type |
| 17 | ĐVT | Unit |
| 18 | Khối lượng BIM | Raw BIM quantity, in the BIM unit |
| 19 | Đơn vị BIM | Model unit of the BIM quantity (kg, m2, m3, count…) |
| 20 | Hệ số QS | BIM unit → norm unit (kg→t 0.001, m2→100m2 0.01; 1 when equal) |
| 21 | Khối lượng BOQ | BIM × QS factor |
| 22 | Số cấu kiện | Element count in group |
| 23 | Trạng thái | Mapping status (Vietnamese) |
| 24 | Thông số thiếu | Missing selector params |
| 25 | Ứng viên mã | Candidate norm codes |
| 26 | Description | Element description |
| 27 | Nguồn Category | Revit category |
| 28 | Element ID | Revit element ids |
| 29 | External ID | UniqueId list |

## Grouping modes

| Mode | Key fields |
|------|------------|
| Detail | One row per element |
| Code | Norm code + work item + unit + BIM unit + geometry bands |
| LevelType | Code key + level + type |
| Level | Code key + level |
| Type | Code key + type |

## Official issue rule

Rows with `CanIssueOfficially=false` (e.g. `SelectedQc1`, missing params, no valid unit conversion) are included in audit export but **excluded** from G8 “Công trình” CSV unless explicitly overridden in a future signed box.

## Regenerate

```powershell
dotnet build src\DSSTech.Cli\DSSTech.Cli.csproj -c Release
dotnet exec src\DSSTech.Cli\bin\Release\net8.0\DSSTech.Cli.dll --boq out\boq-smoke.csv
```
