#!/usr/bin/env python3
"""Read N1 formwork result.json and print bars vs a-priori hand calc."""

from __future__ import annotations

import json
import sys
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
EVIDENCE_DIR = REPO_ROOT / "docs" / "evidence" / "formwork-n1" / "N1-FORMWORK-GEOMETRY"
RESULT_PATH = EVIDENCE_DIR / "result.json"
APRIORI_PATH = REPO_ROOT / "docs" / "evidence" / "formwork-n1" / "apriori-hand-calc.yaml"
MAX_ERROR_PCT = 1.0


def load_result() -> dict:
    if not RESULT_PATH.exists():
        print(f"Missing {RESULT_PATH}", file=sys.stderr)
        sys.exit(1)
    with RESULT_PATH.open(encoding="utf-8-sig") as f:
        return json.load(f)


def build_comparison_table(data: dict) -> str:
    rows = ["| Role | Element | Hand m² | Computed m² | Error % | Status |",
            "|---|---|---:|---:|---:|---|"]
    for row in data.get("comparisons", []):
        hand = row.get("hand_total_m2")
        comp = row.get("computed_total_m2")
        err = row.get("error_pct")
        rows.append(
            f"| {row.get('role', '—')} "
            f"| {row.get('element_id') or '—'} "
            f"| {hand if hand is not None else '—'} "
            f"| {comp if comp is not None else '—'} "
            f"| {f'{err:.3f}' if err is not None else '—'} "
            f"| {row.get('status', 'NOT_MEASURED')} |"
        )
    return "\n".join(rows)


def gate_verdict(data: dict) -> str:
    comparisons = data.get("comparisons", [])
    measured = [c for c in comparisons if c.get("status") in ("PASS", "FAIL")]
    if data.get("apriori_slab_pending"):
        return "NOT_MEASURED (slab apriori pending)"
    if not measured:
        return "NOT_MEASURED"
    if any(c.get("status") == "FAIL" for c in measured):
        return f"FAIL (>{MAX_ERROR_PCT:.1f}%)"
    return f"PASS (<={MAX_ERROR_PCT:.1f}%)"


def print_bars(data: dict) -> None:
    print("=== N1 Formwork Geometry Bars ===")
    print(f"run_class: {data.get('run_class')}")
    print(f"attempt_outcome: {data.get('attempt_outcome')}")
    print(f"elements_processed: {data.get('elements_processed')}")
    print(f"neighbor_pairs: {data.get('neighbor_pairs')}")
    print(f"apriori_slab_pending: {data.get('apriori_slab_pending')}")
    print(f"n1_gate: {gate_verdict(data)}")
    print(f"result: {RESULT_PATH}")


def main() -> None:
    data = load_result()
    print_bars(data)
    print("\n" + build_comparison_table(data))
    print(f"\napriori: {APRIORI_PATH}")


if __name__ == "__main__":
    main()
