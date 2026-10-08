# QA-PLAN — DSSTech MCP + CAD2BIM

**Campaign:** `DSSTech-MCP-CAD2BIM`  
**directive_id:** `DSSTECH-P0-PLAN-2026-09-18`  
**This box:** `P0-PLAN-DSSTECH-MCP-CAD2BIM`  
**Date:** 2026-09-18

---

## 1. This box — Q0 only

```yaml
qa_plan:
  this_box_tier: Q0
  this_box_batch: solo
  this_box_job: >
    Document completeness: every WAVE_CARD present; bars copied not weakened;
    write-class split explicit; no src diff; owners not UNKNOWN on code cards;
    S0 before S3; R4-PILOT ≠ R4-CATALOG; S7 observe-only.
  never_this_cycle: Q2/Q3 Revit product PASS for MCP/CAD (features absent)
```

**Q0 checklist (strategy completeness gate after log 033):**

| Check | Pass when |
|---|---|
| CHARTER exists | product_outcome, retained_bars (copy of 032), W0–W3, library map, runtime, phase_order, journeys A/B/C, CAD package, attempt_cap, stop_rules, CODE-box opening rule |
| WAVE_CARDS | every `phase_order` id except `P0-PLAN` |
| Each card YAML | fields in 032 `wave_card_requirements` |
| Owners | from `allowed_owners`; never UNKNOWN on code waves; S7 = `cad2bim-pdf` + OBSERVE_ONLY |
| `code_authorized` | `false` on every card |
| No `src/**` edit this cycle | git/workspace: production untouched |
| Bars | not weaker than 032 |
| R1 split / S0 before S1 / R4 split / S7 observe | reflected in cards |

**Not a product job.** `run_class: N/A`. CLI / `tests/*.py` **must not** be cited as Revit PASS.

---

## 2. Future Q2 mapping (CODE boxes — not this cycle)

Minimum from 032 + practical Layer B:

| Wave | qa_tier | q2_class | Layer B job (decisive) | Falsifier |
|---|---|---|---|---|
| R0a-TAB-DSSTECH | Q2 | PRODUCT_SPOT | Restart Revit; tab string **DSSTech**; click 5 QS/BOQ buttons | Tab still DSS.QSEngine; button missing; MCP/CAD panel appeared |
| R0b-MCP-PIPE-PING | Q2 | PRODUCT_SPOT | On → local pipe ping OK; Off → ping fail; toast “MCP đang bật” | On but ping fail; Off but ping OK; TCP Host; ChatGPT required |
| R1a-STDIO-CURSOR | Q2 | PRODUCT_SPOT | Cursor `tools/call ping` with `Dss.RevitMcp.exe` | Stdio ping OK while Off; exe references RevitAPI |
| R2-REGISTRY | Q1 | n/a | Layer A: Contracts no RevitAPI; Host no Cad2Bim parse | Host references Cad2Bim |
| R3-READ-TOOLS | Q2 | PRODUCT_SPOT | `list_types Walls`; `query_by_category` pageSize 101 errors; no dump tool | Dump tool; write in read tools |
| R4-PILOT-CREATE | Q2 | PRODUCT_SPOT | Cursor `create_batch` 5 primitives (pipe skip if no type) + **one Undo** | Multi-transaction Undo miss; Delete tool; full catalog |
| S0-SPEC | Q0 | n/a | Spec + 3 maps exist | S1 without checklist |
| S1-CAD-PARSE-PREFLIGHT | Q2 | PRODUCT_SPOT | Good link pass; bad INSUNITS/open poly/exploded block → **0 creates** | Fail still creates |
| S2-DATUM-SKIP-OR-CREATE | Q2 | PRODUCT_SPOT | Existing level/grid skip; missing create via Host | Duplicates; Structure calls Grid.Create |
| S2b-STEP-WIZARD | Q2 | PRODUCT_SPOT | CAD 5 buttons; preflight blocks Confirm; MCP Off CAD still opens | CAD disabled when Off; code-behind Wall.Create |
| S3-STRUCTURE | Q2 | PRODUCT_SPOT | One framing floor column→wall→beam→slab; snap <150 mm | RevitAPI in Structure csproj |
| S4-ARCH | Q2 | PRODUCT_SPOT | Skip S-WALL overlap <50 mm; door block; exploded door fail | Double wall; line-as-door |
| S5-MEPF | Q2 | PRODUCT_SPOT | Sized pipe creates; unparsed size fail item | 0 mm pipe; Pipe.Create in Mepf lib |
| R1b-HTTP-SSE-CHATGPT | Q2 | PRODUCT_SPOT | `127.0.0.1` + bearer ping; 401 bad token | `0.0.0.0`; secret in git |
| R4-CATALOG | Q2 | PRODUCT_SPOT | One extra category create; OUT tools absent | Rebar/stair/fitting/delete shipped |
| S6-SPEC-FREEZE | Q0 | n/a | VN spec vs dialog string table | Freeze before S2b |
| S7-PDF-BACKLOG | Q0 | n/a | Memo only; no PDF code path | Any PDF create pipeline |

**Always on any wave that touches `App.cs` or ribbon:** Cost & Norm + BOQ buttons still present (5 commands).

**Q3:** once at a later **product exit** box (not P0, not each micro-stage). Do not default full qualification per wave.

**REM** after measured failure: solo QA, same SHA.

---

## 3. Product fingerprint (future Q2/Q3)

```yaml
RUN_ENVIRONMENT_FINGERPRINT:
  git_SHA: <verify_sha>
  revit_host_year: 2024 | 2025 | 2026 | 2027
  revit_build: <Application.VersionBuild or About>
  addin_sha256: <SHA-256 of QSNormEngine.Revit.dll in Addins\<year>\QSNormEngine\>
  tab_string: DSSTech
  mcp_on: true | false
  permission_mode: Read | ReadCreate | ReadCreateModify
  os: win32
  timestamp: ISO-8601
```

Unavailable fields = explicit `null` + reason. Qualification fingerprint **must equal** product-default (MCP default **On**, permission **ReadCreate**, tab **DSSTech**). A run with MCP forced Off cannot claim “default On” PASS.

`EFFECTIVE_RUNTIME_CONFIG` (CAD waves): link path, Level name, layer-map file SHA, skipIfExists, dryRun, discipline.

---

## 4. Fixture policy

| Artifact | Git |
|---|---|
| Small DXF (closed poly + one INSERT + one open-poly negative) | Allowed if ≪ 50 MiB; still record **bytes + SHA-256** |
| Typical `.dwg` / `.rvt` | **Forbidden**. `BINARY-NOT-IN-GIT.md` beside intended path: bytes, SHA-256, regenerate/source |
| Screenshots of ribbon/wizard | Small PNG OK; no base64 dumps in markdown |
| `mcp-host.log` excerpts | Redact tokens |

Evidence root per CODE wave:

```text
<campaign>/evidence/<wave_id>/
  result.json
  claims.yaml
  evidence-index.json
```

---

## 5. Layer A vs Layer B

| Layer | This campaign | Failure tag |
|---|---|---|
| **A** | csproj refs, grep (no Host DWG parse, Contracts no RevitAPI, Cad modules no `Wall.Create`), YAML/schema of maps | `INFRA-REGRESSION` |
| **B** | Actual Revit host: tab, ping, create_batch, wizard, preflight | `PRODUCT-UX-FAIL` |

Layer A **never** overrides Layer B. Green CLI / `tests/reference_smoke_test.py` is **engineering only** — **never** Revit product PASS for adapter/UI/MCP/CAD claims.

Trusted harness: first in-Revit Q2 for a host year = full spot job; later waves on same SHA+host may `verify_claims` + 1–3 samples **except** R0b/R4-PILOT/S1 which stay PRODUCT_SPOT.

---

## 6. Crash / hard-fail

Uncaught add-in exception on the claimed command, Host hanging > 30 s on ping, or `ExternalEvent` never running (queue deadlock) → `qa_tag: PRODUCT-RUNTIME-FAIL`, `verdict: FAIL`. Secondary metrics from that session contaminated.

---

## 7. Attempt accounting

`NOT_MEASURED` (Revit not installed, ChatGPT UI unavailable but curl measured, fixture missing) does **not** consume cap. Valid product miss → `FALSIFIED` / `PRODUCT_FAILURE_RUN`.

P0-PLAN cap = 1 (process). Future code default = 3; R0a = 2.
