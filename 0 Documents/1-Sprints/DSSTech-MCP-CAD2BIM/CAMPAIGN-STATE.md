# CAMPAIGN-STATE — DSSTech MCP + CAD2BIM

**Updated:** 2026-09-24  
**Success claimed:** false (in-Revit Layer B still NOT_MEASURED until user smoke)

```yaml
authority: PO-HUMAN-WORK-ORDER-2026-09-23
campaign_mode: SIGNED_CAMPAIGN
code_authorized: false   # code arc for charter scope closed at build; reopen per residual
box_status: CHARTER_BUILD_COMPLETE_PENDING_REVIT_EVIDENCE

shipped:
  R0a: tab DSSTech
  R0b: Named Pipe + ExternalEvent ping + On/Off
  R1a: Dss.RevitMcp.exe stdio
  R1b: HTTP 127.0.0.1 /mcp + bearer per On + MCP Clients button
  R2/R3: registry + read tools
  R4-PILOT + R4-CATALOG: create_element/create_batch catalog (no stairs/rebar/fittings/delete)
  S0: docs/CAD_INPUT_SPEC.md + Config layer maps
  S1: Cad2Bim.Core DXF + Preflight
  S2: CadDatumCommand (G-GRID)
  S2b: Link help + Preflight + Structure/Arch/MEPF buttons (preview→confirm)
  S3/S4/S5: StructureIntentEngine / ArchIntentEngine / MepfIntentEngine → Host create_batch
  S6: docs/CAD2BIM.md + docs/MCP_HOST.md freeze notes
  S7: docs/CAD_PDF_BACKLOG.md OBSERVE_ONLY

build:
  host: 2025 Release PASS
  install: %APPDATA%\Autodesk\Revit\Addins\2025\QSNormEngine

residuals:
  - In-Revit product evidence (screenshot, ping, create Undo, CAD confirm)
  - Family type names in CAD maps often need project overlay (user edits)
  - Full fitting/stairs/rebar still OUT by design
  - HttpListener may require urlacl on some machines for non-admin
```
