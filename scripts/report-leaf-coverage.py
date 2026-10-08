#!/usr/bin/env python3
"""M0 leaf coverage report from Data/norm-engine-runtime.json.

Writes Markdown + CSV under docs/evidence/leaf-coverage/.
Does not embed large binaries; prints SHA-256 of outputs.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
from collections import Counter, defaultdict
from datetime import datetime, timezone


def sha256_file(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "--runtime",
        default=os.path.join("Data", "norm-engine-runtime.json"),
    )
    ap.add_argument(
        "--out-dir",
        default=os.path.join("docs", "evidence", "leaf-coverage"),
    )
    ap.add_argument("--tag", default="baseline", help="filename tag, e.g. baseline|after-m1")
    args = ap.parse_args()

    with open(args.runtime, encoding="utf-8") as f:
        data = json.load(f)

    families = data.get("workFamilies") or []
    variants = data.get("normVariants") or []
    index = {r["norm_code"]: r for r in (data.get("normIndex") or [])}
    resources = Counter(r["variant_code"] for r in (data.get("normResources") or []))

    wi_variants: dict[str, list[str]] = defaultdict(list)
    for v in variants:
        code = v["variant_code"]
        wi = (index.get(code) or {}).get("work_item_id") or ""
        wi_variants[wi].append(code)

    status_by_wi: dict[str, Counter] = defaultdict(Counter)
    official_potential = 0
    for wi, codes in wi_variants.items():
        for code in codes:
            st = (index.get(code) or {}).get("digitization_status") or "UNKNOWN"
            status_by_wi[wi][st] += 1
            if st.upper() in ("RESOURCE_READY", "APPROVED") or "APPROVED" in st.upper():
                official_potential += 1

    rows = []
    for fam in sorted(families, key=lambda x: x.get("work_item_id") or ""):
        wi = fam.get("work_item_id") or ""
        codes = sorted(wi_variants.get(wi, []))
        st = status_by_wi.get(wi, Counter())
        official = sum(
            n
            for s, n in st.items()
            if s.upper() in ("RESOURCE_READY", "APPROVED") or "APPROVED" in s.upper()
        )
        rows.append(
            {
                "work_item_id": wi,
                "prefix": fam.get("prefix") or "",
                "group_l2": fam.get("group_l2") or "",
                "resource_status": fam.get("resource_status") or "",
                "variant_count": len(codes),
                "official_leaf_count": official,
                "status_breakdown": "; ".join(f"{k}={v}" for k, v in sorted(st.items())),
                "sample_codes": "|".join(codes[:8]),
            }
        )

    wi_with_leaf = sum(1 for r in rows if r["variant_count"] > 0)
    os.makedirs(args.out_dir, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%MZ")
    csv_path = os.path.join(args.out_dir, f"coverage-{args.tag}.csv")
    md_path = os.path.join(args.out_dir, f"coverage-{args.tag}.md")

    with open(csv_path, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()) if rows else ["work_item_id"])
        w.writeheader()
        w.writerows(rows)

    selectable_status = Counter()
    for codes in wi_variants.values():
        for code in codes:
            selectable_status[(index.get(code) or {}).get("digitization_status") or "UNKNOWN"] += 1

    lines = [
        f"# Leaf coverage — `{args.tag}`",
        "",
        f"- Generated (UTC): `{stamp}`",
        f"- Runtime: `{args.runtime}`",
        f"- Source DB: `{data.get('sourceDatabase')}`",
        "",
        "## Summary",
        "",
        f"| Metric | Value |",
        f"|---|---|",
        f"| Work families | {len(families)} |",
        f"| Families with ≥1 selectable leaf | **{wi_with_leaf}** |",
        f"| Families with zero leaf | {len(families) - wi_with_leaf} |",
        f"| Selectable variants | {len(variants)} |",
        f"| Official-potential leaves (READY/APPROVED) | **{official_potential}** |",
        "",
        "### Status among selectable leaves",
        "",
        "| digitization_status | count |",
        "|---|---|",
    ]
    for s, n in sorted(selectable_status.items(), key=lambda x: (-x[1], x[0])):
        lines.append(f"| `{s}` | {n} |")

    lines += [
        "",
        f"CSV: `{os.path.basename(csv_path)}`",
        "",
        "## Families with leaves",
        "",
        "| work_item_id | variants | official | statuses |",
        "|---|---:|---:|---|",
    ]
    for r in rows:
        if r["variant_count"] == 0:
            continue
        lines.append(
            f"| `{r['work_item_id']}` | {r['variant_count']} | {r['official_leaf_count']} | {r['status_breakdown']} |"
        )

    lines += [
        "",
        "## Regenerate",
        "",
        "```bash",
        f"python scripts/report-leaf-coverage.py --tag {args.tag}",
        "```",
        "",
    ]
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))

    print(f"wrote {md_path} ({os.path.getsize(md_path)} bytes) sha256={sha256_file(md_path)}")
    print(f"wrote {csv_path} ({os.path.getsize(csv_path)} bytes) sha256={sha256_file(csv_path)}")
    print(f"summary: families={len(families)} with_leaf={wi_with_leaf} variants={len(variants)} official={official_potential}")


if __name__ == "__main__":
    main()
