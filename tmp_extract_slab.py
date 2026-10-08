import json

path = r"C:\Users\NguyenNguyenVan\.cursor\projects\d-DEV-QSNormEngine-Revit\agent-transcripts\e42032f6-ba97-4533-a77a-75695ba1ba9e\e42032f6-ba97-4533-a77a-75695ba1ba9e.jsonl"
best = None
best_len = 0
with open(path, encoding="utf-8") as f:
    for i, line in enumerate(f, 1):
        if "SlabPlanFormwork.cs" not in line:
            continue
        try:
            o = json.loads(line)
        except Exception:
            continue
        content = o.get("message", {}).get("content")
        if not isinstance(content, list):
            continue
        for part in content:
            if not isinstance(part, dict):
                continue
            inp = part.get("input") or {}
            p = inp.get("path", "")
            if p.endswith("SlabPlanFormwork.cs") and "contents" in inp:
                c = inp["contents"]
                if len(c) > best_len and "SupportDeduct" in c and (
                    "Beam" in c or "beam" in c
                ):
                    best = (i, c)
                    best_len = len(c)

print("best line", best[0] if best else None, "len", best_len)
if best:
    out = r"d:\DEV\QSNormEngine_Revit\tmp_slab_plan_prev.cs"
    open(out, "w", encoding="utf-8").write(best[1])
    print("saved", out)
    print("--- preview ---")
    for j, l in enumerate(best[1].splitlines()[:100], 1):
        print(f"{j}|{l}")
