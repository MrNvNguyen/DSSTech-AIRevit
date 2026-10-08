# Phase 6 — CAD/PDF → model (backlog memo)

**Status:** backlog research memo — **not** an open implementation box.  
**Plan todo:** `phase6-cad`.

## Product intent

Enable QS BOQ path when the source is **2D CAD / PDF** (or incomplete BIM): reconstruct enough geometry (grids, levels, host elements) so Norm Selector / BOQ Extract can run without a full native Revit authoring model.

## In scope (future campaign)

| Layer | Goal |
|---|---|
| Import | Bring CAD (DWG/DXF) or PDF underlays into a Revit host (linked or converted) |
| Structure | Detect / place **grids** + **levels** from drawing metadata or user pick |
| Hosts | Create minimal structural/architectural placeholders (walls, columns, floors, rooms) tied to levels |
| QS params | Bind `QS_*` + classify → leaf → BOQ/G8 as today |

## Out of scope (this memo)

- Full automatic drawing interpretation / OCR as production claim
- Cost engine (Phase 4)
- Replacing Revit as runtime host
- Writing large sample `.rvt` into git

## Suggested approach (when PO opens a box)

1. **Pilot package:** one typical floor plan PDF + matching DWG, known grid/level set.
2. **Manual-assisted first:** user confirms grids/levels; agent/add-in places hosts from polylines (not fully unsupervised).
3. **Reuse engine:** no new selection semantics — only feed `ElementFacts` compatible with Core.
4. **Evidence:** before/after BOQ on reconstructed vs native BIM pilot (CLI + Revit Layer B).

## Dependencies

- Phase 1–M Official/G8 path stable on native BIM (done for AF/AE/AK pilots).
- Shared parameters + write-back UX already available.
- M4 chapter coverage optional for earthworks CAD civil drawings.

## Exit criteria (when authorized)

- Documented workflow: CAD/PDF → Revit placeholders → Norm Selector Official/Qc1 counts → BOQ export.
- One fixture project regenerable without committing binaries (pointer + SHA only).

## Decision needed from PO

Open a signed Phase 6 box only if product priority is **non-BIM intake**. Until then keep backlog; do not implement CAD converters in-repo.
