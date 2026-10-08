#!/usr/bin/env python3
"""N6 Layer A — formwork formula smoke (no Revit). Mirror FormworkFormula.cs / FORMWORK_RULES.md."""

from __future__ import annotations

import sys


def column_or_wall_side(b: float, h: float, height: float) -> float:
    if b <= 0 or h <= 0 or height <= 0:
        return 0.0
    return 2.0 * (b + h) * height


def beam_bottom(b: float, length: float) -> float:
    return b * length if b > 0 and length > 0 else 0.0


def beam_side_stem(h: float, length: float) -> float:
    return 2.0 * h * length if h > 0 and length > 0 else 0.0


def slab_side(peri: float, t: float) -> float:
    return peri * t if peri > 0 and t > 0 else 0.0


def near(a: float, b: float, eps: float = 1e-9) -> bool:
    return abs(a - b) <= eps


def main() -> int:
    cases = [
        ("column 400x400x3m side", column_or_wall_side(0.4, 0.4, 3.0), 4.8),
        ("wall 0.2x2.0x16m side", column_or_wall_side(0.2, 2.0, 16.0), 70.4),
        ("beam bottom 0.3x5", beam_bottom(0.3, 5.0), 1.5),
        ("beam side stem 0.45x5", beam_side_stem(0.45, 5.0), 4.5),
        ("slab side peri 20 t=0.15", slab_side(20.0, 0.15), 3.0),
    ]
    failed = 0
    for name, got, want in cases:
        ok = near(got, want)
        status = "PASS" if ok else "FAIL"
        print(f"  [{status}] {name}: got={got} want={want}")
        if not ok:
            failed += 1

    # Element quantity group-1 header count (contract with ElementQuantityExporter)
    group1 = [
        "Element_ID", "GUID", "Category", "Type", "Mark", "Level", "QS_InOutBuilding",
        "Vật liệu", "Mác vật liệu", "Chiều rộng (m)", "Chiều cao (m)", "Chiều dày (m)",
        "Chiều dài (m)", "Diameter (mm)", "Description", "Count", "Area (m2)",
        "Volume (m3)", "Weight (kg)", "QS_FormworkArea", "Host ID",
    ]
    if len(group1) != 21:
        print(f"  [FAIL] group1 column count={len(group1)} want=21")
        failed += 1
    else:
        print("  [PASS] quantity table group1 = 21 columns")

    if failed:
        print(f"formwork_formula_smoke_test: {failed} failed")
        return 1
    print("formwork_formula_smoke_test: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
