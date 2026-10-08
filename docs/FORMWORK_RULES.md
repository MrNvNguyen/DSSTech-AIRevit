# Formwork rules (SSOT)

Canonical config: [`Config/formwork-rules.json`](../Config/formwork-rules.json).  
Reference algorithm: [`docs/formwork_logic_code.html`](formwork_logic_code.html) (OOG FormworkUtils + FaceUtil).

## Unified measure (Foundation / Column / Wall / Beam / Slab)

Same path for every Kind (`FaceOverlapFormwork`):

```
1. Gross  = Σ included planar face areas on element solid
            Column/Wall/Footing → Side
            Beam/Slab           → Side + Bottom
2. For each bbox-overlapping neighbor (Found/Col/Wall/Beam/Floor):
     for each host face × neighbor face:
       if same plane → extrude loops → boolean Intersect
       area = |V| / extrudeDist          # FaceUtil.OverlappingResult
3. Side   = max(0, grossSide   − Σ overlap on Side faces)
   Bottom = max(0, grossBottom − Σ overlap on Bottom faces)
4. GroundContact / footing → Bottom forced 0 when configured
```

Overlap is attributed per neighbor category (Found / Beam / Slab / Col / Wall) for the Warnings column — same split as OOG `AssignOverlapAreaIntoCategory`.

**Contact pairs:** Side↔Side, Bottom↔Top (slab/beam soffit on beam/column/wall top). Skip Bottom↔Bottom between beams/floors (false soffit overlap). Neighbors from `ElementIntersectsSolidFilter` (OOG IntersectingElements), bbox fallback.

**No HOST_AREA, no peri×H heuristics.** Geometry faces + coplanar overlap only.

## Kind faces (config)

| Kind | Faces | Notes |
|---|---|---|
| Column / Wall | Side | Full solid (clipHeight=false for OOG parity) |
| Beam | Side, Bottom | |
| Slab | Side, Bottom | Opening walls appear as Side faces on solid |
| Footing | Side | Bottom always 0 |
| Pile | skip | |

## Ownership / priority

Priority remains in JSON for audit only; face-overlap does **not** use winner/loser — each element deducts its own overlapping faces (OOG).
