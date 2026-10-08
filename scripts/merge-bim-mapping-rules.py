#!/usr/bin/env python3
"""Merge governed BIM mapping rules into norm-engine-runtime.json."""
import json
import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_RUNTIME = ROOT / "Data" / "norm-engine-runtime.json"
DEFAULT_SEED = ROOT / "Config" / "bim-mapping-rules.seed.json"


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--runtime", default=str(DEFAULT_RUNTIME))
    p.add_argument("--seed", default=str(DEFAULT_SEED))
    args = p.parse_args()
    runtime = json.loads(Path(args.runtime).read_text(encoding="utf-8"))
    seed = json.loads(Path(args.seed).read_text(encoding="utf-8"))
    runtime["bimMappingRules"] = seed.get("rules", [])
    Path(args.runtime).write_text(json.dumps(runtime, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(f"Merged {len(runtime['bimMappingRules'])} BIM mapping rules into {args.runtime}")


if __name__ == "__main__":
    main()
