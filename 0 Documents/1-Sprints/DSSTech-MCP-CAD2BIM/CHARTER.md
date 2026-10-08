# CHARTER — DSSTech MCP + CAD2BIM

**Campaign:** `DSSTech-MCP-CAD2BIM`  
**Box (current CODE):** `R0a-TAB-DSSTECH`  
**directive_id:** `DSSTECH-R0A-2026-09-23`  
**authority:** `PO-HUMAN-WORK-ORDER-2026-09-23`  
**campaign_mode:** `SIGNED_CAMPAIGN`  
**code_authorized:** `true` — **current open box only** (`R0a-TAB-DSSTECH`)  
**Date:** 2026-09-23 (header patched; bars in §2 unchanged)  
**P0 (closed):** `P0-PLAN-DSSTECH-MCP-CAD2BIM` — `CLOSED_DOCS_COMPLETE` (`034`)  
**Source of truth (CODE open):** `docs/Agents Logs/036. advisor-strategy-dsstech-r0a-authorize-2026-09-23.md`  
**Prior docs authorize (spent):** `docs/Agents Logs/032. advisor-strategy-dsstech-cad2bim-plan-2026-09-18.md`  
**PO-intent envelope (unsigned paper):** `C:\Users\NguyenNguyenVan\.cursor\plans\revit_mcp_cad2bim_ba5e792a.plan.md`

This charter is **frozen**. Bars in §2 are **not** weakened. P0 was docs-only. CODE is open for the **current** box (`R0a` = rename only). Sequence in §6 is still **one lever per wave**. Standing advance (§10.1) is a PO-work-order rule so the team does not ask PO-human after every wave — it is **not** a self-executing mega-commit.

---

## 1. Product outcome (PO-visible)

**Later, after signed CODE boxes (not this box):** người dùng QS/BIM làm việc trên **một add-in, một tab `DSSTech`**:

| Panel | Người dùng thấy / làm được |
|---|---|
| **Cost & Norm** | Setup Parameters, Norm Selector — **giữ nguyên vai trò** |
| **BOQ** | Formwork Takeoff, BOQ Extract, Quantity Schedule — **giữ nguyên vai trò** |
| **MCP** | Mặc định **On**. AI (Cursor/Claude; sau này ChatGPT) đọc model và **create theo category Revit**. Off → AI mất cổng; ribbon CAD vẫn chạy tay. |
| **CAD** | Link DWG/DXF vào Level → preflight fail-fast → wizard từng loại (preview → confirm) cho Kết cấu / Kiến trúc / MEPF. Level/Grid đã có thì **skip**. |

Hai đường dựng **cùng primitive Host** (`create_element` / `create_batch`): (A) wizard CAD đọc link; (B) AI ra lệnh MCP (tọa độ + type + level) **không cần CAD**.

**P0 (closed):** outcome trên đã được **ghi thành campaign plan** (CHARTER + WAVE_CARDs + QA-PLAN).  
**Current CODE box (R0a):** ribbon tab **DSSTech** only. Không ship MCP/CAD. Không Success toàn sản phẩm. Không PO Visual.

Pilot ≠ 3 bộ môn xong: DoD Host create = 1 level + 1 grid + 1 wall + 1 column + 1 pipe (nếu template có pipe type) + Undo 1 lần + **một** mặt bằng CAD link (sau S1–S2b), không claim Arch+STR+MEPF complete.

---

## 2. Retained bars (copied from 032 — do not weaken)

```yaml
retained_bars:
  - one_addin_one_tab: DSS.QSEngine → DSSTech (no second .addin, no second tab)
  - qs_panels_kept: "Cost & Norm + BOQ commands remain; no removal/rename of Norm Selector / BOQ Extract / Formwork / Setup / Quantity Schedule"
  - mcp_default_on: true
  - mcp_off_kills_ai_not_ribbon_cad: true
  - host_does_not_parse_dwg: true
  - cad_does_not_open_ports: true
  - create_via_category: true
  - cad_link_stepwise: true
  - level_grid_skip_if_exists: true
  - preflight_fail_fast: true
  - core_revit_api_free: true
  - mcp_contracts_no_revit_api: true
  - third_party_mcp_not_host: true
  - qc1_not_official: true
  - no_qs_auto_write_expansion: true
  - qs_confirm_write_neq_cad_create: true
  - no_delete_first_create_waves: true
  - mcp_http_bind_localhost: "127.0.0.1; token per On; no secret in repo"
  - no_large_dwg_rvt_in_git: true
  - pilot_is_not_three_disciplines: true
```

Hard limits that stay closed even when CODE boxes open later:

- `SelectedQc1` / `SelectedCodeOnly` / `CanIssueOfficially=false` are **not** official issue.
- No QS_* **auto** write-back expansion. Existing confirm-write of `QS_NormCode` / `QS_WorkItemId` / `QS_Formwork*` is **W0**, not `Wall.Create`.
- `QSNormEngine.Core` remains Revit-API-free.
- Original SQLite snapshot immutable; runtime JSON generated.
- No large `.dwg` / `.rvt` in git.
- No second `.addin`.
- Third-party MCP on this machine (`AUTOM8LABS_Revit`, `revit-public-mcp`) are read-only and **cannot** be our Host.
- `docs/ROADMAP.md` Phase 6 remains backlog until a later signed box says otherwise. This campaign is **not** “open Phase 6”.
- `docs/PHASE6-CAD-PDF-MEMO.md` is research for QS intake from 2D — not MCP Host, not 3-discipline modeling. Do not implement this envelope by shipping “Phase 6”.

---

## 3. Write-class matrix

| Class | What | This campaign |
|---|---|---|
| **W0** QS confirm params | `QS_NormCode` / `QS_WorkItemId` / `QS_Formwork*` after user **Ghi** | **Retain existing UX.** No expansion. No auto-write on command open. Transaction names today: `"DSS.QSEngine write-back"` etc. — R0a renames prefix to `DSSTech`; semantics unchanged. |
| **W1** MCP/CAD **create** | `Level.Create` / `Grid.Create` / `Wall.Create` / `FamilyInstance` column / `Pipe.Create` / `create_batch` | **NEW.** Requires dryRun / preview / confirm (ribbon) **or** MCP permission `Read+Create` + `skipIfExists`. Distinct CODE box when opened. Default units **mm**. `dryRun: true` default when batch `operations.Count > 20`. |
| **W2** MCP modify/delete | Modify / Delete / Ungroup | **OUT** until a later signed box. First create waves: permission **Read+Create** only. Do not register `delete_*` / `ungroup_*`. |
| **W3** Official issue | Treating Qc1 as official | **FORBIDDEN.** |

Do not reuse `NormWriteBack` / `FormworkWriteBack` transactions to wrap `Wall.Create`. W1 gets its own `Transaction` name (`DSSTech MCP create` / `DSSTech CAD confirm`) and its own confirm/dryRun contract.

---

## 4. Library map (one .addin, several class libraries)

Solution today: `QSNormEngine.sln` = Core, Cli, Revit only. **New libraries are allowed.** A second `.addin` / second ribbon tab is not.

| Project | Role | RevitAPI? | Ports? | When (CODE box) |
|---|---|---|---|---|
| **`QSNormEngine.Revit`** | **Sole `.addin`**. Tab `DSSTech`. `IExternalApplication.OnStartup` creates Cost & Norm + BOQ (existing) and later MCP + CAD panels. Calls McpHost + Cad2Bim. Keeps QS/BOQ. Hosts WPF wizard. **ImportInstance geometry reader lives here** (needs RevitAPI). | yes | no | R0a+ |
| **`QSNormEngine.Core`** | Existing norm engine. **Untouched by this campaign** except not being a dump site for MCP/CAD. | **no** | no | — |
| **`QSNormEngine.Cli`** | Existing ElementFacts harness. Optional later: DXF preflight fixture (not this P0). | no | no | — |
| **`Dss.McpContracts`** | `IMcpToolProvider`, `McpTool`, `McpResult`, `CreateOperation`, `McpPermission`, JSON-RPC DTOs. **No RevitAPI. No Autodesk.** TFM: `net8.0;net48`. | **no** | no | R2 (types can land with R0b stub) |
| **`Dss.McpHost`** | Named pipe server, request queue, `IExternalEventHandler`, built-in `create_*` / read tools, ribbon MCP panel helpers, `mcp-host.log`, permission gate. **Host contains ZERO CAD parse.** Built with same `RevitApiPath` / host-year TFM as the add-in (`scripts/build-revit.ps1`). | yes | Named Pipe only (local). HTTP/SSE in R1b. | R0b+ |
| **`Dss.RevitMcp`** | Console **exe** sidecar. MCP JSON-RPC on **stdio** → forward to Named Pipe. No RevitAPI. Cursor + Claude Desktop use this exe. | no | client of pipe | R1a |
| **`Dss.Cad2Bim.Core`** | DXF parse (fixture / file) + `CadGeometry` DTO + **preflight** rules. **No port. No RevitAPI.** | no | **no** | S1 |
| **`Dss.Cad2Bim.Structure`** | Intent: CAD geom → `CreateOperation[]` (column→wall→beam→slab). **Calls Host `create_*` only.** No `Wall.Create` here. | no | no | S3 |
| **`Dss.Cad2Bim.Arch`** | Intent: walls/doors/rooms/finish floors. Skip overlap `S-WALL` < 50 mm. Doors as blocks. Host `create_*` only. | no | no | S4 |
| **`Dss.Cad2Bim.Mepf`** | Intent: centerline pipe/duct/tray + equipment blocks. Fail item if size unparsed. Host `create_*` only. | no | no | S5 |

**Forbidden dumps:** do not put MCP/CAD into `QSNormEngine.Core`. Do not make Cad2Bim open a socket. Do not make Host parse DWG/DXF/`ImportInstance`.

**Add-in identity (unchanged GUID):**

- File: `QSNormEngine.addin` (template `src/QSNormEngine.Revit/QSNormEngine.addin.template`)
- `AddInId`: `7B52A0F1-7C61-4C8B-A6A4-0AC7398341A2`
- `FullClassName`: `QSNormEngine.Revit.App`
- Display `Name` / `VendorDescription`: today `DSS.QSEngine` → R0a `DSSTech`
- Install dir stays `%APPDATA%\Autodesk\Revit\Addins\<year>\QSNormEngine\` (folder name may stay; tab string changes)
- Hosts: Revit **2024–2027** via `scripts/build-revit.ps1` (2024 `net48`, 2025/2026 `net8.0-windows`, 2027 `net10.0-windows`)

---

## 5. Runtime (commands → idle thread)

```text
AI client (stdio sidecar or HTTP/SSE)
    → JSON-RPC tool call
    → NamedPipeServerStream (127.0.0.1 equivalent: local pipe, current-user ACL)
    → McpRequestQueue.Enqueue + ExternalEvent.Raise()
    → IExternalEventHandler.Execute(UIApplication)   // Revit idle / API thread
    → permission check → tool dispatcher → Transaction (W1)
    → McpResult back through TaskCompletionSource → pipe → client
```

Rules:

1. **`ExternalEvent.Create(handler)` only on Revit thread** (`OnStartup`). Pipe reader thread may only `Raise()`.
2. **Never cache `Document`** across files. Each `Execute` uses `uiApp.ActiveUIDocument?.Document`. Subscribe `DocumentOpened` / `DocumentClosing` / `ViewActivated` to **rebind** session metadata; pipe **stays On** across `.rvt` switch.
3. **MCP default On** at `IExternalApplication.OnStartup` (after ribbon). Toast / TaskDialog once: **“MCP đang bật”**. User can Off immediately.
4. **Token per On** (R1b HTTP; R0b pipe may use a session nonce in `%LOCALAPPDATA%\DSSTech\mcp-session.json` for sidecar discovery). Never commit secrets.
5. **HTTP bind `127.0.0.1` only** (R1b). No tunnel. No `0.0.0.0`.
6. **Off** closes listener, rejects new clients, greys ribbon icon, invalidates token. CAD ribbon commands do not check MCP On.
7. CAD wizard and MCP create share **one** Host dispatcher. Discipline modules emit `CreateOperation`; they do not call Revit create APIs.

Pipe name: `DSSTech.Mcp.{pid}` → `\\.\pipe\DSSTech.Mcp.{pid}` so two Revit instances do not collide.

---

## 6. phase_order (from 032)

| # | wave_id | Box kind when opened | Notes |
|---|---|---|---|
| 0 | **P0-PLAN** | THIS BOX — docs only | Charter + cards. No `src/**`. |
| 1 | R0a-TAB-DSSTECH | **OPEN CODE** (`DSSTECH-R0A-2026-09-23`) | **Rename only.** No MCP/CAD buttons. |
| 2 | R0b-MCP-PIPE-PING | CODE later | Pipe + ExternalEvent ping. Default On. No ChatGPT. |
| 3 | R1a-STDIO-CURSOR | CODE later | `Dss.RevitMcp.exe` stdio → pipe. |
| 4 | R2-REGISTRY | CODE later | `IMcpToolProvider`. Host has zero CAD parse. |
| 5 | R3-READ-TOOLS | CODE later | ping/document/levels/grids/view/categories/types/query paginated. |
| 6 | R4-PILOT-CREATE | CODE later | 5 primitives + Undo. **Not** full catalog. |
| 7 | S0-SPEC | CODE/docs later | Spec + 3 layer-map drafts **before** S1/S3. |
| 8 | S1-CAD-PARSE-PREFLIGHT | CODE later | Read link geom + fail-fast. **No create.** |
| 9 | S2-DATUM-SKIP-OR-CREATE | CODE later | Level/Grid skip or create. Same skipIfExists as Host. |
| 10 | S2b-STEP-WIZARD | CODE later | WPF preview→confirm. CAD panel buttons. Calls Host. |
| 11 | S3-STRUCTURE | CODE later | column→wall→beam→slab. Host `create_*` only. |
| 12 | S4-ARCH | CODE later | Skip S-WALL overlap. Doors as blocks. |
| 13 | S5-MEPF | CODE later | Centerline only. Unparsed size → fail item. |
| 14 | R1b-HTTP-SSE-CHATGPT | CODE later | After R0b+R1a. **Not** a R0b bar. |
| 15 | R4-CATALOG | CODE later | Remaining categories. Stairs/rebar/fittings **OUT**. |
| 16 | S6-SPEC-FREEZE | docs later | Printable VN freeze **after** wizard shape is specified. |
| 17 | S7-PDF-BACKLOG | OBSERVE_ONLY | Memo only. No code path. |

**R1 split:** R1a stdio (Cursor/Claude) vs R1b HTTP/SSE (ChatGPT).  
**S0 before S1.**  
**R4-PILOT ≠ R4-CATALOG.**  
**S7 observe-only.**

---

## 7. User journeys

### (A) Ribbon CAD wizard (MCP On or Off — CAD still works)

1. Mở file Revit (nên có Level + Grid; thiếu thì bước 0).
2. Ribbon **DSSTech → CAD → Link mặt bằng**: user links DWG (`ImportInstance` / CAD link) Origin-to-Origin hoặc Shared Coordinates, gán Level.
3. **Preflight**: fail-fast checklist (INSUNITS, polyline hở, block nổ, layer lạ, bbox vs grid > 1 m nếu đã có grid). Sai → **không tạo**, hiện checklist tiếng Việt.
4. Wizard: chọn link + Level → danh sách loại → **một loại**: Preview (count create/skip/fail) → Confirm → Host `create_batch` một Transaction.
5. Thứ tự đề xuất: Trục/tầng (nếu thiếu) → Kết cấu → Kiến trúc → MEPF. User có thể chạy từng nút **Kết cấu / Kiến trúc / MEPF**.
6. Mapping layer sửa được trên dialog; overlay `cad-layer-map.*.project.json` (future `Config/`, not this P0).

### (B) AI MCP without CAD

1. Revit chạy, MCP **On** (mặc định). Toast “MCP đang bật”.
2. Cursor: `mcp.json` trỏ `Dss.RevitMcp.exe` (R1a). Claude Desktop: cùng exe.
3. AI: `ping` → `get_document` → `list_types` nếu thiếu type → `create_batch` 5 primitives (R4-PILOT DoD). Không cần DWG.
4. `skipIfExists` mặc định true. `dryRun` mặc định true nếu > 20 operations.
5. Quyền `Read+Create`. Không Delete. User **Undo (Ctrl+Z)** một lần hoàn tác cả batch.

### (C) MCP Off, CAD still works

1. User bấm **MCP Off** (icon xám). Sidecar/HTTP ping **fail**. Token hết.
2. Nút CAD **Link / Preflight / Kiến trúc / Kết cấu / MEPF** vẫn enable. Wizard gọi Host primitives **in-process** (queue + ExternalEvent), không qua pipe.
3. Off **không** gỡ panel CAD. Off **không** đụng Cost & Norm / BOQ.

---

## 8. Practical CAD input package + fail-fast checklist

**Gói một tầng typical** (chỉ mặt bằng; không commit `.dwg` lớn):

```text
A-T1-PLAN.dwg         kiến trúc     → link Level Tầng 1
S-T1-FRAMING.dwg      dầm/cột/sàn   → cùng Level
S-T1-FOUNDATION.dwg   móng          → Level móng
M-T1-HVAC.dwg
P-T1-PLUMB.dwg
E-T1-POWER.dwg
F-T1-FIRE.dwg
```

Trục/tầng: **ưu tiên file Revit**. Nếu thiếu: layer `G-GRID` + text tên, và text/CSV cao độ (`Tầng 1,0`).

Fixture git policy: small DXF (closed polyline + one block + one open-polyline negative case) + **byte length + SHA-256** + `BINARY-NOT-IN-GIT.md`. No `.rvt` in git.

**Fail-fast (no create if any hard fail):**

| Check | Hard fail when |
|---|---|
| INSUNITS | Not mm (`INSUNITS=4` or documented equivalent). Scale would turn 400 mm column into 0.4 m. |
| Open polyline | Area objects (`S-SLAB-*`, `A-ROOM`, `S-FOOT-*`, `A-FLOR-*`) not `closed=1`. |
| Exploded block | Door/column/equipment expected as INSERT/block but present as exploded lines. |
| Unknown layer | Layer not in map and not `*-ANNO-*` / `G-GRID` / Defpoints. |
| Bbox vs grid | CAD bbox vs existing grid envelope > **1 m** (only if grids exist). |
| Units / 1 floor | Multiple storeys in one file without explicit Level bind. |

Warnings (may continue with tick): column vs grid > 150 mm; beam as double-line (STR option “đôi nét → tâm”).

Draft spec + layer maps: `SPEC-OUTLINE.md` this cycle. Production `docs/CAD_INPUT_SPEC.md` is **S0/S6**, not P0 product SSOT.

---

## 9. attempt_cap, stop_rules, QA mapping

```yaml
attempt_cap:
  P0-PLAN: 1                   # planning completeness; not a product attempt
  future_code_default: 3       # measured attempts per lever class
  R0a: 2
  note: NOT_MEASURED does not consume
```

**Stop rules:**

- Stop tactical CODE if no later `STRATEGY_AUTHORIZE_PHASE` for that box.
- Stop if a WAVE_CARD proposes Core+RevitAPI, Host parsing DWG, or CAD opening ports.
- Stop if a WAVE_CARD treats Qc1 as official or expands QS auto-write.
- Stop if one lever claims S3+S4+S5 together or R4 full catalog as R4-PILOT.
- Stop if second `.addin` or replacing QS panels.
- Stop grind: do not open MCP diagnostic spikes in P0.
- P0 incomplete cards → technical self-repair then report-up; do not start R0a code.
- Do not message PO-human from technical / lead / QA.

**QA (this box):** Q0 document completeness — every WAVE_CARD present; bars copied not weakened; W0–W3 explicit; no `src/**` diff; owners not UNKNOWN on code cards; S0 before S3; R4-PILOT ≠ R4-CATALOG; S7 observe-only.

**QA (future CODE, not this cycle):**

| Wave | Minimum |
|---|---|
| R0a | Q2 in-Revit: tab string `DSSTech`; 5 existing buttons still work |
| R0b | Q2: On → local ping OK; Off → ping fail |
| R1a | Q2: Cursor stdio `tools/call ping` |
| R4-PILOT | Q2: `create_batch` 5 primitives + Undo once |
| S1+ | Q2: preflight fail-fast (no create on INSUNITS / open polyline / exploded block) |
| Always | Cost & Norm + BOQ buttons still present |
| Never this cycle | Q2/Q3 Revit product PASS for MCP/CAD (features absent) |
| Never | CLI / `tests/*.py` as Revit PASS |

Product fingerprint (future Q2): Revit host year + build, add-in SHA (`QSNormEngine.Revit.dll`), tab string, MCP on/off, permission mode.

---

## 10. How later CODE boxes get opened

```text
P0-PLAN complete
  → technical report-up (this log 033)
  → strategy completeness gate (Q0)
  → STILL no lead-dev

To open R0a (or any later id):
  NEW STRATEGY_AUTHORIZE_PHASE
    authorized_box: R0a-TAB-DSSTECH   # example
    code_authorized: true             # that box only
    one WAVE_CARD already on disk is the contract
  → technical may ADVANCE_SHORT only inside an already authorized micro-stage
  → lead-dev one lever
  → qa-agent Q2
  → technical report-up
  → strategy adjudicate
```

**The sequence in §6 is not a mega-commit.** One lever per wave. “It is next in phase_order” does not let lead bundle the next wave into the current lever. Routine next-box open after a closed wave follows **§10.1 standing advance** (no PO packet). Strategy still issues a **new** `STRATEGY_AUTHORIZE_PHASE` at the gates in §10.1 (`R4-PILOT`, `S3`, `R1b`, 2× FALSIFIED, cap spent).

Identity / modeling-write scope beyond this PO-intent charter still cannot be widened (second add-in, Delete tools, Qc1-as-official, Core+RevitAPI) without PO **scope** checkpoint — that is strategy’s call, not technical’s.

### 10.1 Standing advance (added 2026-09-23 — does not weaken §2 / §6 / §9 bars)

PO-human work order 2026-09-23 (“tiếp tục triển khai đến khi hoàn thành”) opens **execution of this frozen charter**. It does **not** reuse `034` `STRATEGY_APPROVE_EXECUTION` (docs-only). It does **not** mean one commit ships MCP + three CAD disciplines.

**Exact rule** (also in `CAMPAIGN-STATE.md`):

After the current `authorized_box` is closed as **QA PASS** (or, for **R0a only**: process-advance on honest **`NOT_MEASURED`** when build+grep shows identity strings are `DSSTech`, the five existing commands remain registered, and no MCP/CAD panel or second tab was added — classify in-Revit UI as `NOT_MEASURED`; never treat that as whole-product Success), **technical-advisor may ADVANCE** to the next `phase_order` id that is **not** a strategy re-entry gate, set `authorized_box` + `code_authorized: true` for **that one box only**, and **lead-dev may implement that one WAVE_CARD lever**. **No new PO-human packet.** One lever per wave. Do not bundle.

**Strategy must re-enter** (new `STRATEGY_AUTHORIZE_PHASE` / adjudicate — still **not** a PO packet) **before**:

- `R4-PILOT-CREATE` (first create product claim)
- `S3-STRUCTURE` (first discipline create)
- `R1b-HTTP-SSE-CHATGPT` (network)

and **if** any wave is **FALSIFIED twice**, or **attempt_cap is spent**, or a bar / write-class / architecture change is proposed (including W2 delete, Qc1-as-official, Core+RevitAPI, Host DWG parse, S7 code).

`S7-PDF-BACKLOG` remains **OBSERVE_ONLY** (memo only; no PDF parser). `R4-CATALOG` stays a **separate** wave from `R4-PILOT`. Pilot ≠ three disciplines complete.

---

## Scope

**P0 (closed):** charter; `WAVE_CARDS/*.md`; `QA-PLAN.md`; `SPEC-OUTLINE.md`; log 033. No production code.

**R0a (current CODE):** identity rename only — see `WAVE_CARDS/R0a-TAB-DSSTECH.md`. Lead may edit the inventory on that card. **Out of R0a:** MCP/CAD panels, `Dss.*` projects, W2/W3, Core+RevitAPI, Host DWG parse, S7 parser, Success packet, PO-human message from technical/lead/QA.
