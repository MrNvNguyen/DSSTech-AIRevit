import json

path = r"C:\Users\NguyenNguyenVan\.cursor\projects\d-DEV-QSNormEngine-Revit\agent-transcripts\e42032f6-ba97-4533-a77a-75695ba1ba9e\e42032f6-ba97-4533-a77a-75695ba1ba9e.jsonl"
# Find all Write versions with timestamps / key phrases
versions = []
with open(path, encoding="utf-8") as f:
    for i, line in enumerate(f, 1):
        if "SlabPlanFormwork.cs" not in line or "contents" not in line:
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
            if not str(inp.get("path", "")).endswith("SlabPlanFormwork.cs"):
                continue
            if "contents" not in inp:
                continue
            c = inp["contents"]
            flags = []
            if "union" in c.lower() or "Union" in c:
                flags.append("union")
            if "HOST_AREA" in c:
                flags.append("area")
            if "SupportDeduct" in c and "deduct" in c.lower():
                flags.append("deduct")
            if "Does not deduct" in c or "does not deduct" in c:
                flags.append("no-deduct")
            versions.append((i, len(c), ",".join(flags)))

for v in versions:
    print(v)
