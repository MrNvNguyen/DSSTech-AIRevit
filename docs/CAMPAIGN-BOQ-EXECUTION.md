# Campaign BOQ — Kế hoạch triển khai (multi-agent)

**Campaign:** QSNormEngine BOQ + G8-ready  
**SSOT kế hoạch:** `.cursor/plans/qsnormengine_feature_roadmap_3c413c6a.plan.md`  
**Trạng thái:** CODE_COMPLETE (A1–G1) · M0–M3+M5 Layer A DONE · **M4 Phase 2 DONE (AB→BD)** · Phase 6 memo DONE · G2 Layer B chờ người dùng

### Tiến độ nhanh (2026-09-12)

| Sóng | Trạng thái |
|------|------------|
| A1 classification_rule | DONE — CLI smoke + Python reference |
| A2 facts + quantity | DONE — Width bbox / Level / System / quantity candidates |
| A3 bim_mapping_rule | DONE — 53 rules · migrate + export |
| B ResultsWindow | DONE — Official/Qc1 · Gán giá trị QS (catalog) · write-back |
| C CLI harness | DONE — `--smoke` / `--boq` / `--boq-xlsx` (+ official G8 CSV) |
| D Ribbon | DONE — icons 16/32 · Cost & Norm + BOQ |
| E1 Setup Parameters | DONE — 45 QS_* · Group General · all disciplines |
| E2 Write-back | DONE — Manual · Official + Qc1 draft confirm |
| F BOQ Extract | DONE — 28 cột · grouping · Σ detail=group PASS |
| G1 G8 export | DONE — xlsx 3 sheets + CSV companion |
| G2 G8 round-trip | Layer A refreshed (7 Official lines) · Layer B manual checklist |
| **M0 coverage** | DONE — `docs/evidence/leaf-coverage/` |
| **M1 Official unlock** | DONE — 6 pilot leaves `RESOURCE_READY` |
| **M2 residual AE/AK** | DONE — 7 WI seed Qc1 |
| **M3 deepen** | DONE — paint bool EQ · wall/beam CONC · steel paint bands |
| **M4 Phase 2** | **AB→BD DONE** |
| **M5 QA gate** | Layer A PASS (58 fixtures) · Layer B Revit/G8 PENDING |
| **Phase 6 CAD** | Memo DONE — `docs/PHASE6-CAD-PDF-MEMO.md` |

**Evidence:** `docs/BOQ_EXPORT_SCHEMA.md` · `docs/G8_IMPORT_GUIDE.md` · `docs/evidence/g8-roundtrip/` · `docs/evidence/leaf-coverage/` · `docs/evidence/leaf-official-g8/` · `docs/PHASE6-CAD-PDF-MEMO.md`

## Mục tiêu sản phẩm

Pipeline: `Revit element → facts → WorkItem → mã lá TT38 → KL BOQ → gom nhóm → Excel 28 cột + G8 sheet Công trình → (tùy chọn) ghi QS_* sau xác nhận`

**Phạm vi:** khối lượng + mã + trạng thái + diễn giải. **Không** giá / VL-NC-M thành tiền.

## Authority & governance

| Hạng mục | Quyết định |
|---|---|
| Write-back | Chỉ sau nút **Ghi vào cấu kiện** (Sóng E2) |
| Qc1 / Official | UI tách rõ; BOQ official chỉ `SelectedApproved` |
| Rule master | SQLite ENGINE_v1 → runtime JSON (seed fallback khi chưa có DB) |
| Chuẩn BOQ | BOQ-Forma-AEC (28 cột) |
| Chuẩn G8 | G8 2020 Unicode `c:\G8`, sheet **Công trình** dòng 6+ |

**Lưu ý PO-governance:** E2 write-back cần cập nhật `.cursor/rules/engine-contracts.mdc` trước khi code (Sóng gov-update).

## Vai trò & luồng (multi-agent)

```text
strategy-advisor  → phê duyệt sóng / reject claim yếu / PO packet (Success/Ceiling/G2)
technical-advisor → WAVE_CARD + DoD + QA tier
lead-dev          → một lever / sóng
qa-agent          → CLI smoke + Revit Layer B + G8 round-trip evidence
orchestrator      → không escalate Option A/B/C lên PO
```

## QA matrix

| Sóng | Tier | Layer A | Layer B (Revit) | Product evidence |
|---|---|---|---|---|
| A1–A3 | Q1 | CLI 7 fixtures + reference_smoke | — | Leaf code không đổi |
| A2 | Q2 | + SampleInputs mới | Facts adapter spot | Quantity đúng đơn vị |
| B | Q2 | — | ResultsWindow screenshot | Official/Qc1 tách |
| C | Q1 | CLI-only harness | — | Bỏ Python duplicate |
| D | Q2 | — | Ribbon + icon | Tab DSS.QSEngine |
| E1 | Q2 | — | Setup params autobind | Params bound |
| F | Q3 | BoqGrouper unit tests | BOQ Extract full model | 28 cột + tổng KL |
| G1 | Q2 | G8BoqExporter unit | — | File .xlsx layout |
| G2 | Q3 | — | Manual G8 import | Screenshot G8 UI |
| E2 | Q3 | — | Write-back confirm | QS_NormCode trên element |

## MCP Revit

**Không có MCP Revit** trong workspace hiện tại (chỉ Notion/Slack cần auth).  
Layer B / G2 cần **bạn mở Revit + G8Ver2020.exe** thủ công; agent chuẩn bị add-in + file evidence path.

---

## WAVE_CARD — Sóng A1: `classification_rule`

```yaml
WAVE_CARD:
  wave_id: A1-classification-rule
  product_problem: "WorkItemClassifier hard-code C# — không governed, khó đồng bộ BOQ-Forma"
  measured_owner: WorkItemClassifier
  hypothesis: "Rule từ runtime JSON + ConditionEvaluator → cùng kết quả 7 fixture"
  prediction:
    metric: expected.json match rate
    expected_range: "7/7 PASS"
  falsifier: "Bất kỳ fixture nào đổi WorkItem hoặc leaf code"
  one_allowed_lever: classification_rule table/seed + classifier refactor
  qa_tier: Q1
  regression_scope: [reference_smoke_test.py, CLI fixtures]
  stop_rule: "2 measured FAIL liên tiếp trên cùng fixture"
```

**DoD:** migrate seed · export JSON · `WorkItemClassifier` đọc rule · 7/7 PASS.

---

## WAVE_CARD — Sóng A2: Facts + quantity

```yaml
WAVE_CARD:
  wave_id: A2-facts-quantity
  one_allowed_lever: RevitElementFactsFactory + quantity from bim_property_map.quantity_parameter
  qa_tier: Q2
  falsifier: "KL BOQ ≠ Revit schedule quantity trên fixture wall/column/rebar"
```

**DoD:** Description/Keynote/Level/Zone/System · sửa rule tường/thép · SampleInputs mới.

---

## WAVE_CARD — Sóng A3: `bim_mapping_rule`

```yaml
WAVE_CARD:
  wave_id: A3-bim-mapping-rule
  one_allowed_lever: Port default-qs-rules.ts → SQLite seed
  qa_tier: Q1
```

**Thứ tự classifier:** `bim_mapping_rule` → `classification_rule` → `bim_property_map`.

---

## WAVE_CARD — Sóng B: UI + coverage

- ResultsWindow: Official / Qc1 / Missing dashboard / VL-NC-M panel
- Config `workItemDefaults` + SampleInputs CLAD/TILE/PAINT/WATERPROOF

---

## WAVE_CARD — Sóng C: CLI harness

- Một smoke C# (`dotnet test` hoặc CLI batch) thay Python engine duplicate
- Giữ Python chỉ validate JSON schema nếu cần

---

## WAVE_CARD — Sóng D: Ribbon

- Icon + panel: Norm Selector | BOQ Extract | Setup Parameters

---

## WAVE_CARD — Sóng E1/E2

- E1: `SetupParametersCommand` autobind `.txt`
- E2: Transaction Manual + ghi `QS_NormCode`, `QS_NormStatus`, `QS_NormStamp` sau confirm

---

## WAVE_CARD — Sóng F: BOQ pipeline

**Files mới:** `BoqLineItem.cs`, `BoqAggregator.cs`, `BoqGrouper.cs`, `BoqExporter.cs`, `BoqExtractCommand.cs`, `BoqResultsWindow.cs`, `docs/BOQ_EXPORT_SCHEMA.md`

**DoD:** 28 cột · grouping modes · inspection sheet · Σ grouped = detail.

---

## WAVE_CARD — Sóng G: G8 export

**G1:** `G8BoqExporter.cs` (ClosedXML) — sheet Công trình A,B,E,F,G,H,I,J,K,L,P từ dòng 6 + audit sheets.  
**G2:** Round-trip `G8Ver2020.exe` + `docs/evidence/g8-roundtrip/`.

---

## WAVE_CARD — Sóng M: ánh xạ mã lá

```yaml
WAVE_CARD:
  wave_id: M-leaf-mapping
  product_problem: "BOQ/G8 trống Official vì leaf selectable toàn Qc1; 89 WI NoNormMatched"
  status: "M0–M3+M5 Layer A DONE; M4-AB DONE; Phase6 memo DONE; Layer B Revit/G8 = G2"
  deliverables:
    - scripts/report-leaf-coverage.py + docs/evidence/leaf-coverage/
    - migrate M1 RESOURCE_READY pilots + orphan demote
    - migrate M2 7 residual seeds (RESOURCE_QC1 provisional)
    - migrate M4-AB EXC/FILL/TRANS/ROCK pilots
    - migrate M4-BD 4 FIRE/HDD/WELL pilots
    - SampleInputs + tests/expected.json (58 fixtures)
    - docs/evidence/leaf-official-g8/ + g8-roundtrip Layer A
    - docs/PHASE6-CAD-PDF-MEMO.md
  dod_layer_a:
    - smoke 58/58
    - official-ready > 0 on --boq
    - G8 Cong trinh sheet filters Official
  out_of_scope:
    - inventing official TT38 VL/NC/M for residual/M4 pilots (provisional only)
    - Phase 2 deeper digitization beyond pilots without new signed box
```

## Thứ tự thực thi

```text
gov-update → A1→G1 DONE → G2 Layer B (user)
                ↓
         M0→M1→M2→M3→M5 Layer A DONE
                ↓
         M4-AB…BD DONE → Phase 6 memo DONE
```

## File SSOT sẽ tạo

| File | Sóng |
|---|---|
| `Config/classification-rules.seed.json` | A1, M2 |
| `scripts/migrate-engine-v1.py` (extend) | A1, A3, M1–M2 |
| `scripts/report-leaf-coverage.py` | M0 |
| `src/QSNormEngine.Core/BoqGrouper.cs` | F |
| `src/QSNormEngine.Core/G8BoqExporter.cs` | G |
| `docs/G8_IMPORT_GUIDE.md` | G |
| `docs/BOQ_EXPORT_SCHEMA.md` | F |
| `docs/ENGINE_DB_PATCHES.md` | M1–M2 |
| `docs/evidence/leaf-coverage/` | M0 |
| `docs/evidence/leaf-official-g8/` | M5 |
