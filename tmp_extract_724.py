import json

path = r"C:\Users\NguyenNguyenVan\.cursor\projects\d-DEV-QSNormEngine-Revit\agent-transcripts\e42032f6-ba97-4533-a77a-75695ba1ba9e\e42032f6-ba97-4533-a77a-75695ba1ba9e.jsonl"
target = 724
with open(path, encoding="utf-8") as f:
    for i, line in enumerate(f, 1):
        if i != target:
            continue
        o = json.loads(line)
        for part in o.get("message", {}).get("content", []):
            if not isinstance(part, dict):
                continue
            inp = part.get("input") or {}
            if str(inp.get("path", "")).endswith("SlabPlanFormwork.cs") and "contents" in inp:
                open(r"d:\DEV\QSNormEngine_Revit\tmp_slab_plan_724.cs", "w", encoding="utf-8").write(inp["contents"])
                print("saved", len(inp["contents"]))
                # keywords
                c = inp["contents"]
                for kw in ["Union", "union", "Boolean", "crossing", "HashSet", "supportDeduct"]:
                    print(kw, c.count(kw))
        break
