# BOQ export schema (DSS.QSEngine)

Audit export uses **28 columns** defined in `BoqExporter.ExportColumnOrder` (`src/QSNormEngine.Core/BoqExporter.cs`).

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
| 18 | Khối lượng BIM | Raw BIM quantity |
| 19 | Hệ số QS | QS factor (default 1) |
| 20 | Khối lượng BOQ | BIM × QS factor |
| 21 | Số cấu kiện | Element count in group |
| 22 | Trạng thái | Mapping status (Vietnamese) |
| 23 | Thông số thiếu | Missing selector params |
| 24 | Ứng viên mã | Candidate norm codes |
| 25 | Description | Element description |
| 26 | Nguồn Category | Revit category |
| 27 | Element ID | Revit element ids |
| 28 | External ID | UniqueId list |

## Grouping modes

| Mode | Key fields |
|------|------------|
| Detail | One row per element |
| Code | Norm code + work item + unit + geometry bands |
| LevelType | Code key + level + type |
| Level | Code key + level |
| Type | Code key + type |

## Official issue rule

Rows with `CanIssueOfficially=false` (e.g. `SelectedQc1`, missing params) are included in audit export but **excluded** from G8 “Công trình” CSV unless explicitly overridden in a future signed box.

## Regenerate

```powershell
dotnet build src\QSNormEngine.Cli\QSNormEngine.Cli.csproj -c Release
dotnet exec src\QSNormEngine.Cli\bin\Release\net8.0\QSNormEngine.Cli.dll --boq out\boq-smoke.csv
```
