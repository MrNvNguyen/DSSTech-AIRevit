#!/usr/bin/env python3
"""Compare two N3 result.json files (clipHeight false vs true)."""

from __future__ import annotations

import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
TOGGLE_DIR = REPO_ROOT / "docs" / "evidence" / "formwork-n3" / "rules-toggle"
BASELINE = TOGGLE_DIR / "baseline-result.json"
TOGGLED = TOGGLE_DIR / "toggled-result.json"
OUT = TOGGLE_DIR / "compare-summary.json"


def load(path: Path) -> dict:
    if not path.exists():
        print(f"Missing {path}", file=sys.stderr)
        sys.exit(1)
    with path.open(encoding="utf-8-sig") as f:
        return json.load(f)


def row_key(e: dict) -> str:
    seg = e.get("segment_index")
    return f"{e.get('element_id')}:{seg if seg is not None else '-'}"


def main() -> None:
    a = load(BASELINE)
    b = load(TOGGLED)
    by_a = {row_key(e): e for e in a.get("elements", [])}
    by_b = {row_key(e): e for e in b.get("elements", [])}
    deltas = []
    for key in sorted(set(by_a) | set(by_b)):
        ea, eb = by_a.get(key), by_b.get(key)
        if ea is None or eb is None:
            deltas.append({"row": key, "note": "row_added_or_removed"})
            continue
        dt = (eb.get("total_area_m2") or 0) - (ea.get("total_area_m2") or 0)
        dseg = (eb.get("segment_count") or 0) - (ea.get("segment_count") or 0)
        if abs(dt) > 1e-9 or dseg != 0 or bool(ea.get("clip_height_applied")) != bool(eb.get("clip_height_applied")):
            deltas.append(
                {
                    "row": key,
                    "delta_total_area_m2": dt,
                    "baseline_total_m2": ea.get("total_area_m2"),
                    "toggled_total_m2": eb.get("total_area_m2"),
                    "baseline_segment_count": ea.get("segment_count"),
                    "toggled_segment_count": eb.get("segment_count"),
                }
            )

    # Also flag if toggled has more rows for same element id
    ids_a = {e.get("element_id") for e in a.get("elements", [])}
    for eid in ids_a:
        na = sum(1 for e in a.get("elements", []) if e.get("element_id") == eid)
        nb = sum(1 for e in b.get("elements", []) if e.get("element_id") == eid)
        if na != nb:
            deltas.append({"element_id": eid, "baseline_rows": na, "toggled_rows": nb, "note": "row_count_changed"})

    summary = {
        "schema": "formwork-n3-toggle-compare/1",
        "baseline": str(BASELINE.relative_to(REPO_ROOT)).replace("\\", "/"),
        "toggled": str(TOGGLED.relative_to(REPO_ROOT)).replace("\\", "/"),
        "baseline_rules_sha256": a.get("rules_sha256"),
        "toggled_rules_sha256": b.get("rules_sha256"),
        "sha_changed": a.get("rules_sha256") != b.get("rules_sha256"),
        "elements_with_delta": len(deltas),
        "deltas": deltas,
        "json_toggle_bar": "PASS" if deltas else "FAIL",
    }
    TOGGLE_DIR.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"json_toggle_bar: {summary['json_toggle_bar']}")
    print(f"elements_with_delta: {len(deltas)}")
    print(f"wrote: {OUT}")


if __name__ == "__main__":
    main()
