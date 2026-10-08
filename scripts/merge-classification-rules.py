#!/usr/bin/env python3
"""Merge governed classification rules into norm-engine-runtime.json."""
import json
import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RUNTIME = ROOT / "Data" / "norm-engine-runtime.json"
DEFAULT_SEED = ROOT / "Config" / "classification-rules.seed.json"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--runtime", default=str(DEFAULT_RUNTIME))
    p.add_argument("--seed", default=str(DEFAULT_SEED))
    args = p.parse_args()

    runtime_path = Path(args.runtime)
    seed_path = Path(args.seed)
    runtime = json.loads(runtime_path.read_text(encoding="utf-8"))
    seed = json.loads(seed_path.read_text(encoding="utf-8"))
    runtime["classificationRuleSets"] = seed.get("ruleSets", [])
    runtime_path.write_text(json.dumps(runtime, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"Merged {len(runtime['classificationRuleSets'])} classification rule sets into {runtime_path}")


if __name__ == "__main__":
    main()
