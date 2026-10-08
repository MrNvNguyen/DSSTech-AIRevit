# Evidence and Footer Contracts

Read with `SKILL.md` when producing or adjudicating campaign evidence.

## 1. Machine SSOT

Each evidence-producing wave ships:

```text
<evidence-root>/
  result.json
  claims.yaml
  evidence-index.json
```

Required common fields:

```yaml
schema: <versioned schema>
campaign: <id>
wave_id: <id>
verify_sha: <git SHA>
artifact_manifest_sha: <SHA or n/a>
product_runtime_fingerprint_sha: <SHA or n/a>
behavior_change: true | false
authority: PRODUCT | DIAGNOSTIC | PROCESS
run_class: INVALID_ENVIRONMENT_RUN | PRODUCT_FAILURE_RUN | PRODUCT_PASS_RUN | N/A
attempt_outcome: PASS | FALSIFIED | NOT_MEASURED | N/A
bars_changed: false
```

Logs summarize; they do not duplicate raw timelines or metric tables.

Never commit secrets, large `.rvt`, extra SQLite dumps, or large binaries under evidence.
Record size + SHA + regenerate path when a binary artifact exists locally.

## 2. Environment fingerprint

See `QA-PROTOCOL.md` §5–6. Unavailable fields are explicit `null` with reason.

## 3. Footer contracts

Every agent end-of-turn includes exactly one footer block.

### lead-dev

```yaml
LEAD_FOOTER:
  wave_id:
  lever:
  verify_sha:
  self_test: PASS | FAIL
  behavior_change: true | false
  rollback:
  evidence_root:
  defer_qa: true | false
  next_agent: qa-agent | lead-dev | technical-advisor
  attempt_claimed: false
```

### qa-agent

```yaml
QA_FOOTER:
  wave_id:
  verify_sha:
  qa_tier:
  run_class:
  attempt_outcome:
  falsifier_measured: true | false
  product_fingerprint_equivalent: true | false
  product_evidence_paths: []
  engineering_evidence_paths: []
  verdict: PASS | FAIL | PARTIAL
  next_agent: technical-advisor
```

### technical-advisor

```yaml
TECHNICAL_FOOTER:
  wave_id:
  directive_id:
  local_verdict: APPROVE-HANDOFF | BLOCK-DEV | SELF-REPAIR | REPORT-UP-STRATEGY | ADVANCE_SHORT
  open_gaps: []
  next_agent: lead-dev | qa-agent | strategy-advisor | orchestrator-router
  never: PO-human
```

### strategy-advisor

```yaml
STRATEGY_FOOTER:
  strategyOutputType:
  directive_id:
  challenged_assumption:
  po_packet_precheck: PASS | FAIL | N/A
  next_agent: technical-advisor | orchestrator-router | PO-human | none
```

### orchestrator-router

```yaml
ORCH_FOOTER:
  dispatched:
  preflight: PASS | FAIL
  postcheck: PASS | FAIL
  stop_reason: null | <reason>
  next_agent:
```

## 4. Claims hygiene

```yaml
# claims.yaml (shape)
claims:
  - id:
    statement:
    evidence: []
    status: measured | inferred | hypothesis
```

Product PASS requires measured claims with product evidence paths. Inferred/hypothesis
claims cannot carry a Success packet alone.

CLI expected-code match is product evidence for **Core** claims. It is engineering-only
for **Revit adapter/UI/install** claims.
