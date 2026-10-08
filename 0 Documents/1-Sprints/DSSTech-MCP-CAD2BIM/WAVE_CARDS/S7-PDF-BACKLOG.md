# WAVE_CARD — S7-PDF-BACKLOG

```yaml
WAVE_CARD:
  directive_id: DSSTECH-P0-PLAN-2026-09-18
  wave_id: S7-PDF-BACKLOG
  product_problem: "PDF vector intake is requested in long-term research but is not a Host/CAD2BIM create path this campaign."
  measured_owner: cad2bim-pdf
  hypothesis: "An observe-only backlog memo (no code path) is the only valid lever; PDF vector cannot close MCP/CAD DoD and must not be implemented under this envelope."
  prediction:
    metric: pdf_code_path_absent
    expected_range: "Memo exists; grep src/** has no PdfVector/PdfIntake create pipeline; no nuget PDFium/QuestPDF added for CAD2BIM"
    written_before_measurement: true
  falsifier: "Any PDF-to-Wall.Create path ships; OR this card used as code authorize; OR PHASE6 memo treated as Host."
  one_allowed_lever: OBSERVE_ONLY
  theoretical_upper_bound: "Closes 'forgot PDF' by recording backlog. Closes 0% of product create."
  contracts_at_risk:
    - pilot_is_not_three_disciplines
    - qs_panels_kept
    - no_large_dwg_rvt_in_git
  qa_tier: Q0
  qa_batch: solo
  q2_class: n/a
  regression_scope:
    - "No src/** edits"
    - "Cost & Norm + BOQ N/A (no install)"
  attempt_number: 0/n/a
  stop_rule: "Forever observe-only unless a later PO scope box. Never UNKNOWN owner on a CODE fix. Do not dispatch lead-dev."
  code_authorized: false
```

## Blueprint — OBSERVE_ONLY

**No classes. No csproj. No ports. No parser.**

When strategy later asks for the memo (this card’s only deliverable):

| Path | Content |
|---|---|
| `0 Documents/1-Sprints/DSSTech-MCP-CAD2BIM/BACKLOG-S7-PDF.md` | Why PDF vector ≠ DWG link; QS PHASE6 memo is BOQ placeholders not Host; dependencies (vector vs scan); not in R4/S3–S5 |

Related observation (not authority): `docs/PHASE6-CAD-PDF-MEMO.md`.

### DoD

Memo on disk. `code_authorized` still false. Product fingerprint N/A.

### Regression

Do not touch `src/**`.

### Owner

`cad2bim-pdf` + `OBSERVE_ONLY` (allowed exception to UNKNOWN).

### code_authorized

`false`. Sequence is not self-executing. A future PDF product needs **PO scope** + new box, not this wave flipping to code.
