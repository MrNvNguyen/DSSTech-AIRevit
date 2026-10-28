# QA Protocol

Read with `SKILL.md` when a wave has QA work.

Product: DSSTech Revit add-in (2025–2027) + CLI. Not Electron/APS.

## 1. Tier and batching

| Tier | Scope | Required work | Default batching |
|------|-------|---------------|------------------|
| Q0 | docs/schema/contract | schema/lint/consistency; no live Revit | many stages → one QA |
| Q1 | code/CLI/forensic | `verify_sha` + 1–3 claim samples; full rerun only on first-use/mismatch | 3–5 stages → one QA |
| Q2 | runtime/product spot | Layer A + bounded CLI and/or Revit product session | solo or same-config wave |
| Q3 | product exit | full signed DoD matrix | solo once at exit |

Plan/footer must include:

```yaml
qa_tier: Q0 | Q1 | Q2 | Q3
qa_batch: solo | wave:<id>
q2_class: ROUTING_SMOKE | PRODUCT_SPOT | MEASURE_FULL | n/a
```

Rules:

- Q0/Q1 wave-batch is default.
- Q3 is not grouped with another stage.
- REM after measured failure is solo.
- `defer_qa: true` means wait until the wave end; self-test still runs per lead stage.
- QA verifies the exact shipped SHA.

## 2. Trusted harness

| Harness state | QA |
|---------------|----|
| First use or runner changed | full independent run once |
| Trusted (≥1 independent PASS, runner unchanged) | `verify_claims` + 1–3 samples + SHA |
| Lead nonzero exit, SHA/claim conflict | full rerun |

CLI harness (when SDK present):

```powershell
dotnet run --project src\DSSTech.Cli -- SampleInputs\<fixture>.json
python tests/reference_smoke_test.py
python tests/resource_adjustment_smoke_test.py
```

Expected leaf codes: `tests/expected.json`.

## 3. Layer A and Layer B

| Layer | Purpose | Failure |
|-------|---------|---------|
| A | code, schema, CLI smoke, expected.json | `INFRA-REGRESSION` |
| B | actual product behavior (Revit add-in and/or CLI product job) | `PRODUCT-UX-FAIL` or product failure |

Layer A never overrides a Layer B failure.

### Q2 class

| Class | Use | Layer B budget |
|-------|-----|----------------|
| `ROUTING_SMOKE` | ribbon/command/error fences; no full model set | one select + command smoke |
| `PRODUCT_SPOT` | classifier/selection/adapter/UI risk | ≤1 valid product job on fixture + decisive evidence |
| `MEASURE_FULL` | metric/DoD gate | full result grid + export + status/official-ready counts |

Q2 must not grow into a full qualification matrix. Q3 owns full qualification.

CLI-only claims may complete Layer B without Revit. **In-Revit claims** (facts adapter, ribbon, results window, install) require a Revit 2025–2027 Layer B session — CLI green is not enough.

## 4. Product runtime policy

For Revit / CLI product claims:

1. Use documented SampleInputs or a dedicated fixture model when live Revit is required.
2. Prefer CLI expected-code evidence before a live Revit session when the owner is Core.
3. Do not commit `.rvt` or large binaries into evidence; record size + SHA + path.
4. Record Revit build, TFM (`net8.0-windows` / `net10.0-windows`), runtime JSON source, config flags.
5. One product session per QA run unless DoD requires otherwise.

Do not treat Cursor browser MCP as a substitute for Revit add-in verification.

## 5. Environment fingerprint

Every product qualification run records, when available:

```yaml
RUN_ENVIRONMENT_FINGERPRINT:
  git_SHA:
  app_version:          # VERSION file
  OS:
  revit_build:          # host year.build or null if CLI-only
  tfm:                  # net8.0-windows | net10.0-windows | net8.0 (CLI)
  runtime_json_source:
  runtime_json_sha:
  config_path:
  allowQc1Calculation:
  allowCodeOnlyResult:
  requireExplicitConstructionMethod:
  timestamp:
```

Unavailable fields are explicit `null` with a reason; do not invent values.

## 6. Effective runtime config

```yaml
EFFECTIVE_RUNTIME_CONFIG:
  runtime_data: Data/norm-engine-runtime.json
  config: Config/norm-engine.config.json
  allowQc1Calculation: true | false
  allowCodeOnlyResult: true | false
  requireExplicitConstructionMethod: true | false
  work_item_defaults: {}
  command: NormSelector | Cli
  dry_run: n/a
```

Qualification fingerprint must match product-default fingerprint for product claims.

## 7. Product evidence requirements

Minimum for a product PASS claim:

- selection summary: count by `SelectionStatus`; official-ready (`CanIssueOfficially`) count;
- for leaf-code claims: actual vs `tests/expected.json` (CLI) or named fixture element (Revit);
- for missing/ambiguous claims: listed `MissingParameters` / `CandidateCodes`;
- for Qc1 claims: proof UI/export does **not** treat them as official-ready;
- for export claims: CSV/JSON path + key columns;
- UI claim: screenshot of the claimed surface (results grid / dialog).

## 8. Crash / env hard-fail

```yaml
qa_tag: PRODUCT-RUNTIME-FAIL
verdict: FAIL
```

Triggers: uncaught Revit command exception, missing `norm-engine-runtime.json` at plugin path
when the command should run, add-in failed to load, or CLI crash on a documented SampleInput.

```yaml
qa_tag: INVALID_ENVIRONMENT
attempt_outcome: NOT_MEASURED
```

Triggers: no Revit 2025–2027 host when Layer B requires it; missing .NET SDK for CLI; wrong
TFM vs installed Revit.

Secondary metrics from a crash session are contaminated.

## 9. Attempt classification

```yaml
run_class: INVALID_ENVIRONMENT_RUN | PRODUCT_FAILURE_RUN | PRODUCT_PASS_RUN
attempt_outcome: PASS | FALSIFIED | NOT_MEASURED
falsifier_measured: true | false
```

`NOT_MEASURED` when Revit/SDK/runtime file missing prevents measuring the falsifier.
Valid product misses still count as `FALSIFIED` / `PRODUCT_FAILURE_RUN`.
