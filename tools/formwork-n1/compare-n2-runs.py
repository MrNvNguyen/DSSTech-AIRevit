#!/usr/bin/env python3
"""Compare two N2 result.json files (baseline vs JSON-toggled, no rebuild)."""

from __future__ import annotations

import json
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
TOGGLE_DIR = REPO_ROOT / "docs" / "evidence" / "formwork-n2" / "rules-toggle"
BASELINE = TOGGLE_DIR / "baseline-result.json"
TOGGLED = TOGGLE_DIR / "toggled-result.json"
OUT = TOGGLE_DIR / "compare-summary.json"


def load(path: Path) -> dict:
    if not path.exists():
        print(f"Missing {path}", file=sys.stderr)
        sys.exit(1)
    with path.open(encoding="utf-8-sig") as f:
        return json.load(f)


def main() -> None:
    a = load(BASELINE)
    b = load(TOGGLED)
    by_a = {e["element_id"]: e for e in a.get("elements", [])}
    by_b = {e["element_id"]: e for e in b.get("elements", [])}
    deltas = []
    for eid in sorted(set(by_a) | set(by_b), key=lambda x: int(x) if str(x).isdigit() else 0):
        ea, eb = by_a.get(eid), by_b.get(eid)
        if ea is None or eb is None:
            continue
        db = eb.get("bottom_area_m2", 0) - ea.get("bottom_area_m2", 0)
        dt = eb.get("total_area_m2", 0) - ea.get("total_area_m2", 0)
        if abs(db) > 1e-9 or abs(dt) > 1e-9:
            deltas.append(
                {
                    "element_id": eid,
                    "role": eb.get("role") or ea.get("role"),
                    "delta_bottom_area_m2": db,
                    "delta_total_area_m2": dt,
                    "baseline_total_m2": ea.get("total_area_m2"),
                    "toggled_total_m2": eb.get("total_area_m2"),
                }
            )

    summary = {
        "schema": "formwork-n2-toggle-compare/1",
        "baseline": str(BASELINE.relative_to(REPO_ROOT)).replace("\\", "/"),
        "toggled": str(TOGGLED.relative_to(REPO_ROOT)).replace("\\", "/"),
        "baseline_ground_contact": a.get("ground_contact_auto_detect"),
        "toggled_ground_contact": b.get("ground_contact_auto_detect"),
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
