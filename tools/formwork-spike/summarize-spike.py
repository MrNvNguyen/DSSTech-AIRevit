#!/usr/bin/env python3
"""Read N0 formwork spike result.json and print bars + update memo tables."""

from __future__ import annotations

import json
import sys
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
EVIDENCE_DIR = REPO_ROOT / "docs" / "evidence" / "formwork-spike" / "N0-FORMWORK-SPIKE"
RESULT_PATH = EVIDENCE_DIR / "result.json"
MEMO_PATH = REPO_ROOT / "docs" / "evidence" / "formwork-spike" / "MEMO-TEMPLATE.md"
BOOLEAN_GATE_PCT = 5.0


def load_result() -> dict:
    if not RESULT_PATH.exists():
        print(f"Missing {RESULT_PATH}", file=sys.stderr)
        sys.exit(1)
    with RESULT_PATH.open(encoding="utf-8-sig") as f:
        return json.load(f)


def fmt_rate(rate: float | None) -> str:
    if rate is None:
        return "NOT_MEASURED"
    return f"{rate * 100:.2f}%"


def gate_verdict(fail_rate: float | None) -> str:
    if fail_rate is None:
        return "NOT_MEASURED"
    return "PASS" if fail_rate * 100 <= BOOLEAN_GATE_PCT else "FAIL (>5%)"


def build_geometry_table(data: dict) -> str:
    rows = ["| Batch | Elements timed | Solids | Total ms | Avg ms/elem | Status |",
            "|---|---:|---:|---:|---:|---|"]
    for sample in data.get("geometry_timing", {}).get("samples", []):
        avg = sample.get("avg_ms_per_element")
        avg_s = f"{avg:.3f}" if avg is not None else "—"
        rows.append(
            f"| {sample.get('batch_size')} "
            f"| {sample.get('elements_timed', 0)} "
            f"| {sample.get('solids_found', 0)} "
            f"| {sample.get('total_ms', 0):.1f} "
            f"| {avg_s} "
            f"| {sample.get('status', 'MEASURED')} |"
        )
    return "\n".join(rows)


def build_memo(data: dict) -> str:
    boolean = data.get("boolean_timing", {})
    probe = data.get("probe_epsilon", {})
    fail_rate = boolean.get("fail_rate")
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")

    return f"""# N0 Formwork Spike Memo

> Auto-updated by `tools/formwork-spike/summarize-spike.py` — {now}

## Run metadata

| Field | Value |
|---|---|
| Campaign | {data.get('campaign', 'N0-FORMWORK-SPIKE')} |
| Wave | {data.get('wave_id', 'N0-FORMWORK-SPIKE-ATTEMPT-1')} |
| Run class | {data.get('run_class', 'unknown')} |
| Attempt outcome | {data.get('attempt_outcome', 'unknown')} |
| Model | {data.get('model_title', '—')} |
| Revit host | {data.get('revit_host_year', '—')} build {data.get('revit_build', '—')} |
| Structural count | {data.get('structural_element_count', 0)} |

## Geometry timing (`get_Geometry` Fine)

{build_geometry_table(data)}

## Boolean difference (neighbor pairs)

| Metric | Value |
|---|---:|
| Status | {boolean.get('status', 'NOT_MEASURED')} |
| Pairs attempted | {boolean.get('pairs_attempted', 0)} |
| Failed | {boolean.get('pairs_failed', 0)} |
| Fail rate | {fmt_rate(fail_rate)} |
| Gate (≤ {BOOLEAN_GATE_PCT:.0f}%) | **{gate_verdict(fail_rate)}** |
| Total ms | {boolean.get('total_ms', 0):.1f} |
| Avg ms/op | {boolean.get('avg_ms_per_op') or '—'} |

## Probe epsilon (joined beam–column)

| Metric | Value |
|---|---:|
| Status | {probe.get('status', 'NOT_MEASURED')} |
| Method | {probe.get('method') or '—'} |
| Hand area (m²) | {probe.get('hand_area_m2') or '—'} |
| Computed area (m²) | {probe.get('computed_area_m2') or '—'} |
| Error % | {probe.get('error_pct') if probe.get('error_pct') is not None else '—'} |
| Element ids | {', '.join(probe.get('element_ids') or []) or '—'} |
| Reason | {probe.get('reason') or '—'} |

## Decision bar (N0 gate)

- Boolean fail rate ≤ **{BOOLEAN_GATE_PCT:.0f}%** required before N1.
- Probe error on hand-calc pair should be ≤ **1%** (plan target); mark NOT_MEASURED until Revit run.

## Evidence paths

- `{RESULT_PATH.relative_to(REPO_ROOT).as_posix()}`
- `{EVIDENCE_DIR.relative_to(REPO_ROOT).as_posix()}/claims.yaml`
- `{EVIDENCE_DIR.relative_to(REPO_ROOT).as_posix()}/evidence-index.json`
"""


def print_bars(data: dict) -> None:
    boolean = data.get("boolean_timing", {})
    probe = data.get("probe_epsilon", {})
    fail_rate = boolean.get("fail_rate")

    print("=== N0 Formwork Spike Bars ===")
    print(f"run_class: {data.get('run_class')}")
    print(f"attempt_outcome: {data.get('attempt_outcome')}")
    print(f"structural_element_count: {data.get('structural_element_count')}")
    print(f"boolean_fail_rate: {fmt_rate(fail_rate)}")
    print(f"boolean_gate: {gate_verdict(fail_rate)}")
    print(f"probe_status: {probe.get('status')}")
    if probe.get("computed_area_m2") is not None:
        print(f"probe_computed_area_m2: {probe.get('computed_area_m2'):.4f}")
    if probe.get("error_pct") is not None:
        print(f"probe_error_pct: {probe.get('error_pct'):.2f}%")
    print(f"result: {RESULT_PATH}")


def main() -> None:
    data = load_result()
    print_bars(data)
    MEMO_PATH.parent.mkdir(parents=True, exist_ok=True)
    MEMO_PATH.write_text(build_memo(data), encoding="utf-8")
    print(f"memo: {MEMO_PATH}")


if __name__ == "__main__":
    main()
