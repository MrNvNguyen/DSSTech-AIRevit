# Quy cách CAD đầu vào — DSSTech CAD2BIM (S6 freeze)

**Status:** frozen for pilot release 2026-09-24.  
**Source:** promoted from campaign SPEC-OUTLINE + CHARTER.

Xem nội dung đầy đủ tại: [CAD_INPUT_SPEC.md](CAD_INPUT_SPEC.md)

## Freeze notes

- Layer maps production: `Config/cad-layer-map.{str,arch,mepf}.json`
- Overlay dự án: `cad-layer-map.*.project.json` (wizard edits — future)
- Không đổi pattern S-COL / A-WALL / P-CW mà không bump `version` trong JSON
- PDF vector = S7 backlog only

## MCP create catalog (Host)

IN: Levels, Grids, Walls, Floors, Ceilings, Doors, Windows, Rooms, StructuralColumns, StructuralFraming, StructuralFoundation, Pipes, Ducts, CableTrays, Mechanical/Electrical/Plumbing/Lighting/Sprinkler equipment, GenericModels.

OUT: stairs sketch, rebar, in-place/mass/adaptive, full MEP fittings, Delete.
