# Strategy and Governance Protocol

Repo-wide. Read with `SKILL.md` for strategy dispatch, PO-proxy adjudication, PO packets,
closure, or model fallback.

Product: **QSNormEngine** (Revit 2024–2027 norm selection). Not OneX/Forma.

## 1. Strategy as adversarial PO-proxy

`strategy-advisor` stands between the team and PO-human. Its job is to keep PO out of
technical ticks and to keep weak evidence out of PO packets.

```text
Team evidence / technical report
  → strategy adjudicates (raw evidence first)
    → TEAM_DIRECTIVE (continue without PO)
    → or STRATEGY_PO_PACKET (only Success / Ceiling / PO Visual / signed scope checkpoint)
```

## 2. Strategy trigger

Call strategy when **any** is true:

- authorize/close a box or phase;
- report-up after QA on a phase gate, checkpoint, or Q2+ product claim;
- replan or campaign pivot;
- change definition, bar, or architecture scope;
- evidence conflict across independent sources;
- Product Evidence vs claim mismatch;
- owner MEDIUM/UNKNOWN after a measurement wave;
- same symptom crossed two lever classes or three measured cycles;
- box cap spent;
- inconclusive / ceiling-partial route request;
- PO explicitly asks for strategic review.

Do **not** call strategy for:

- one already-approved root cause + one lever still inside that approval;
- QA scope approval alone;
- mechanical ADVANCE_SHORT inside an already strategy-approved micro-stage;
- pure CLI/python smoke green with no product claim.

## 3. Input order

```text
1. Current PO contract / North Star / authorized_box / original bars
2. Raw evidence: result.json, claims.yaml, CLI stdout, expected.json delta, Revit screenshots, export files
3. Strategy's own metrics and campaign-goal check
4. Technical report-up and lead/QA narrative for conflict/provenance only
```

Strategy must:

- challenge at least one team assumption;
- distinguish product failure from measurement failure (CLI vs Revit adapter);
- distinguish Ceiling of an arc/lever set from a product ceiling;
- reject sunk-cost routing;
- map the decision to Success, Ceiling, continue-in-cap, or PO-visible **scope** progress;
- reject all technical options when none materially closes the product gap;
- reject paths that violate hard limits (write-back, Qc1-as-official, original SQLite mutate, Core+Revit API).

## 4. Adjudication outputs

```yaml
STRATEGY_APPROVE_EXECUTION:
  # technical may open WAVE_CARD / lead may code under this directive
STRATEGY_REJECT_AND_DIRECT:
  # evidence or plan insufficient; team continues with explicit repair/next card
STRATEGY_HOLD_MEASURE:
  # observe-only / measurement repair; no behavior change; attempt not spent
STRATEGY_AUTHORIZE_PHASE:
  # open next phase inside signed order/route after measured prerequisites
STRATEGY_CLOSE_CEILING:
  # arc/lever ceiling (product_ceiling_claimed default false)
STRATEGY_PO_PACKET:
  # only Success | Ceiling | PO Visual | signed scope checkpoint
```

### Forbidden PO asks

Never put these in a PO packet:

- ack next technical phase;
- choose harness flag / rem instrumentation;
- tick Option A/B/C;
- approve incomplete measurement as progress;
- dump logs, git SHA chains, or ablation matrices as the main ask.

If those are the only remaining questions → `STRATEGY_REJECT_AND_DIRECT` or
`STRATEGY_HOLD_MEASURE`.

## 5. PO packet precheck

Before `STRATEGY_PO_PACKET`:

```yaml
PO_PACKET_PRECHECK:
  decisive_metrics_measured: true
  product_evidence_supports_claim: true
  run_class_valid_for_product_claim: true
  product_fingerprint_equivalent: true
  open_NOT_MEASURED_on_decision_metric: false
  technical_options_to_PO: false
```

Any false → reject packet; issue team directive instead.

## 6. ARG — Architecture Review Gate

```yaml
ARG:
  product_mapping: "Does the metric/lever improve what the QS/BIM user sees or can do?"
  definition: "Are bar and acceptance physically defined?"
  measured_owner: "Is the owner measured, co-dominant or distributed?"
  lever_viability: "Can the lever's upper bound materially close the gap?"
  contract_risk: "Does it threaten read-only, Qc1-not-official, sqlite-master, or Core-no-Revit?"
  qs_hard_limits: "No write-back? No Qc1-as-official? No hand-edit runtime JSON as master?"
  grind: "Has this class already consumed its measured cap?"
```

ARG fail → replan / definition box / Ceiling. Do not delegate another lever in the same
class.

## 7. PO governance

When a campaign has a PO-signed charter:

- team routes autonomously inside cap under strategy PO-proxy;
- do not send technical options, routine QA failure, or logs to PO;
- PO-human packets are only:
  1. Success;
  2. Ceiling;
  3. PO Visual / demo after QA PASS;
  4. a **scope** checkpoint explicitly named in the signed charter.

Open product scope questions stay with PO until signed — e.g. model write-back, official-vs-Qc1
UX, Phase 2 digitization order, construction-method defaults, cost engine.

### Ceiling meaning

Ceiling closes the current authorized arc/lever set because reasonable in-cap levers are
spent. It does not establish a permanent product ceiling unless the packet explicitly
proves and the PO accepts that broader scope.

```yaml
ceiling_scope:
  arc:
  lever_set:
  architecture_assumptions:
  product_ceiling_claimed: false | true
```

Default: `product_ceiling_claimed: false`.

### PO packet shape

One page, product language:

```yaml
goal_and_original_bars:
measured_outcome:
product_visible_result:
valid_attempts_spent:
residuals:
team_recommendation:
PO_scope_decision_needed:   # bar / envelope / accept ceiling — not tech ack
evidence_path:
```

No dump logs or technical option menu.

## 8. Model ladder

Only strategy receives an explicit high-tier `model=`:

| Tier | Model | Use |
|------|-------|-----|
| L0 | `composer-2.5-fast` | mechanical PO-proxy log/authorize |
| L1 | `cursor-grok-4.5-high-fast` | default strategy analysis |
| L2 | `claude-sonnet-5-thinking-high` or `gpt-5.6-terra-medium` | moderate conflict |
| L3 | `claude-opus-5-thinking-high` or `claude-fable-5-thinking-high` | major replan |

Do not default to L3.

Fallback on quota/rate-limit/unavailable/API/rejected slug:

```text
peer same tier → down-tier → omit model= (Cursor Auto)
```

Continue the task; record intended/actual/fallback. Stop only if Auto also fails.
Tactical agents always omit `model=`.

## 9. Telegram

After an allowed PO halt (`STRATEGY_PO_PACKET`), if notify script/guide exists, send a short
Vietnamese message:

```text
Kết quả: <product outcome>
PO cần: <one scope decision>
Chi tiết: <path>
```

No logs or jargon. Never Telegram for team-internal directives.
