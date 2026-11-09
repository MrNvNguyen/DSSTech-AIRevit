---
name: dsstech-lessons
description: Lessons learnt fixing DSSTech's CAD → Revit modelling (MEP from CAD, cable trays, ducts, pipes, drainage, elevations, CAD text and labels, company system codes, Combine / clash avoidance, Revit API fitting failures). Use before changing or debugging MepFromCad, MepRoutePlanner, CadLinkText, Combine or the MCP create tools, when a user reports a modelling error (Góp ý / Feedback zip, screenshots), and after every fix to record the new lesson.
---

# DSSTech — lessons learnt

The lessons live in one file, shipped inside the add-in so the MCP tool `get_lessons` serves them on every user's
machine: **`src/Dss.McpHost/Knowledge/LESSONS.md`**. Read it before touching the modelling code; keep to its rules for
every case, not only the drawing that showed the error.

## Before changing modelling code

1. Read `src/Dss.McpHost/Knowledge/LESSONS.md` (all of it, or the topics that apply: `cad-read`, `cad-text`,
   `mep-elevation`, `mep-tray`, `mep-duct`, `mep-pipe`, `systems`, `combine`, `structure`, `revit-api`, `debug`).
2. Reproduce on the user's data before guessing from screenshots: `%LOCALAPPDATA%\DSSTech\mep-cad-input.txt`
   (strokes and texts read), `mcp-host.log`, the newest Revit journal, or the Feedback zip. The dump's texts only hold
   what the reader kept; read the DWG itself (ACadSharp, `FileShare.ReadWrite`) when a label seems missing.
3. Run the planner outside Revit on that data (a scratch console referencing `src/Dss.Cad2Bim.Core`, render to SVG) and
   check the network before building the add-in.

## After every fix (required)

1. Add the lesson to `LESSONS.md` under its topic (new topic: `## [id] Title`), as **Lỗi** → **Vì sao** → **Quy tắc**,
   written so it applies to other drawings, not only this one. Vietnamese, like the rest of the file.
2. Add a self-test for it in `src/Dss.Cad2Bim.Core/SectionFootprintSelfTest.cs` with a small synthetic drawing, so the
   error cannot come back unnoticed: `dotnet run --project tests/cad2bim-section-smoke -c Release` → `PASS: ok`.
3. Build (`scripts/build-revit.ps1`): the file is embedded in `Dss.McpHost.dll`, so the next install carries it.
