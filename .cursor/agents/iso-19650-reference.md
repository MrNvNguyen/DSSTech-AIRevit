---
name: iso-19650-reference
model: composer-2.5-fast
description: >-
  Tham chiếu ISO 19650 / CDE naming cho tài liệu & deliverable QS-BIM —
  dùng kèm @technical-advisor hoặc @lead-dev. Model: Composer. Readonly.
readonly: true
---

# ISO 19650 Reference

Quick reference for CDE and documentation naming. Read when CDE or deliverable naming
is in scope (campaign docs, evidence folders, QS/BIM packages).

**Dispatch:** `@Task(iso-19650-reference)` · **cấm** `model=` · Composer từ frontmatter.

## Container Naming

```
[Project]-[Originator]-[Volume/System]-[Level/Location]-[Type]-[Role/Number]
```

| Segment | Example |
|---------|---------|
| Project | `QSBIM` |
| Originator | `QS`, `BIM`, `STR`, `ARC`, `MEP`, `DOC` |
| Volume/System | `BLDG-A`, `ZONE-01` |
| Level/Location | `L02`, `CORE` |
| Type | `DR`, `SK`, `RP`, `SC` |
| Role/Number | `GA-001` |

Example: `QSBIM-QS-ZONE-01-L02-RP-NORM-001` (norm-selection report / evidence pack)

## Status / Suitability

| Code | Meaning |
|------|---------|
| S0 | WIP |
| S1 | Suitable for Coordination (Shared) |
| S2 | Suitable for Information (Published) |
| A1–A4 | Approved for stage |
| B1–B2 | Partial sign-off |

## CDE states

```
WIP → Shared → Published → Archived
```

Map loosely to this repo:

- WIP: `0 Documents/1 - Thaoluan/**` and unsigned `docs/` notes
- Shared/Published: signed campaign under `1-Sprints` (when created) and governed `docs/` SSOT
- Do not treat research memos as Published product authority

## Metadata minimum

Revision · Status code · Originator · Classification · Created/Modified + author

## Flag

- Skip Shared before Published
- Missing originator/revision
- WIP edits in Published
- Inconsistent discipline codes across federated models
- Deliverables without EIR/PIR traceability
- Confusing CDE “Approved” with engine `SelectedApproved` / `CanIssueOfficially`
- Hard-coding TT38/norm codes into family names as the primary CDE identity
