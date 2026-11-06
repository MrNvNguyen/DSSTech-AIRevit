"""Reference implementation of Dss.AssetInfo.Core extraction, for validating
Config/asset-spec-rules.json on real data without Revit / .NET.

Mirrors the C# classes 1:1:
  SpecTextExtractor.Extract   -> extract()
  ModelTokens.From            -> model_tokens()
  PdfSpecMatcher.Match        -> match_pdf()

Usage:
  python -I tools/validate_rules.py excel <form.xlsx> [...]       # re-extract from "Quy cách" and compare with cells
  python -I tools/validate_rules.py pdf <models.txt> <file.pdf> [...] # models.txt: one "ID<TAB>model<TAB>family" per line
  python -I tools/validate_rules.py fixtures tests/Dss.AssetInfo.Core.Tests/Fixtures/reference_cases.json
"""
import json, re, sys, unicodedata
from pathlib import Path

RULES_PATH = Path(__file__).resolve().parent.parent / "Config" / "asset-spec-rules.json"


class Rules:
    def __init__(self, path=RULES_PATH):
        self.d = json.loads(Path(path).read_text(encoding="utf-8"))
        self.sep = self.d["separator"]
        self.prefixes = self.d["prefixes"]
        self.tail = self.d["valueTail"]
        self.maxlen = self.d["maxValueLength"]

    def fields(self, family, pdf=False):
        fam = self.d["families"].get(family, {"extraFields": [], "overrides": {}})
        out = []
        for f in self.d["commonFields"]:
            o = fam.get("overrides", {}).get(f["param"])
            out.append({**f, **o} if o else f)
        out = out + fam.get("extraFields", []) + self.d["tailFields"]
        if pdf:  # PDF: Vietnamese + English labels
            out = [{**f, "labels": f["labels"] + f.get("labelsEn", [])} for f in out]
        return out

    def family_by_sheet(self, sheet):
        for r in self.d["familyBySheet"]:
            if re.search(r["pattern"], sheet):
                return r["family"]
        return self.d["defaultFamily"]


def grab(rules, text, labels, unit, pdf=False):
    tails = [rules.tail] + ([rules.d["pdf"]["valueTailNoColon"]] if pdf else [])
    for pre in rules.prefixes:
        for lab in labels:
            for tail in tails:
                m = re.search(pre + lab + tail + rules.sep, text, re.I | re.S)
                if m:
                    v = re.sub(r"\s+", " ", m.group(1)).strip(rules.d.get("valueTrim", " .,;-"))
                    if v and re.search(r"\w", v) and len(v) <= rules.maxlen * 2:
                        return v[:rules.maxlen]
    if unit:
        m = re.search(unit, text)
        if m:
            return re.sub(r"\s+", " ", m.group(0)).strip()[:rules.maxlen]
    return ""


def extract(rules, text, family, pdf=False):
    text = (text or "").replace("\r", "")
    out = {}
    use_unit = not pdf or rules.d["pdf"].get("useUnitFallback", False)
    for f in rules.fields(family, pdf):
        v = grab(rules, text, f["labels"], f.get("unit") if use_unit else None, pdf)
        if v:
            out[f["param"]] = v
    return out


def norm_token(s):
    s = unicodedata.normalize("NFKD", s.upper())
    return re.sub(r"[^A-Z0-9]", "", s)


def model_tokens(model, min_len=4):
    """Tokens that identify a model in free text: whole model, single parts and
    adjacent pairs, each needing a letter AND a digit (so '4000' or 'UPS' alone never match)."""
    parts = [norm_token(p) for p in re.split(r"[\s/,;+]+|:", model or "")]
    parts = [p for p in parts if p]
    cands = ["".join(parts)] + parts + [a + b for a, b in zip(parts, parts[1:])]
    toks = []
    for t in cands:
        if len(t) >= min_len and re.search(r"\d", t) and re.search(r"[A-Z]", t) and t not in toks:
            toks.append(t)
    return toks


def pdf_pages(path):
    import pypdf
    r = pypdf.PdfReader(str(path))
    return [(p.extract_text() or "") for p in r.pages]


def match_pdf(rules, records, pdf_path):
    cfg = rules.d["pdf"]
    pages = pdf_pages(pdf_path)[: cfg["maxPagesPerFile"]]
    chars = sum(len(p.strip()) for p in pages)
    if not pages or chars / max(1, len(pages)) < cfg["minTextCharsPerPage"]:
        return {"file": str(pdf_path), "scanned": True, "proposals": []}
    all_tokens = {rid: model_tokens(m, cfg["minModelTokenLength"]) for rid, m, _ in records}
    all_tokens = {k: v for k, v in all_tokens.items() if v}
    props, links = [], {}
    for pi, page in enumerate(pages, 1):
        lines = [l for l in page.split("\n")]
        normlines = [norm_token(l) for l in lines]
        page_norm = "".join(normlines)
        on_page = {rid for rid, ts in all_tokens.items() if any(t in page_norm for t in ts)}
        models_on_page = {m.upper() for rid, m, _ in records if rid in on_page}
        for rid, model, family in records:
            toks = all_tokens.get(rid)
            if not toks or rid not in on_page:
                continue
            links.setdefault(rid, []).append(pi)
            hit = next((i for i, nl in enumerate(normlines) if any(t in nl for t in toks)), None)
            if hit is None or not cfg.get("proposeValues", False):
                continue
            a, b = max(0, hit - cfg["linesBefore"]), min(len(lines), hit + cfg["linesAfter"])
            window = "\n".join(lines[a:b])
            n = len(models_on_page)
            conf = "High" if n == 1 else ("Low" if n >= cfg["tableModelThreshold"] else "Medium")
            for p, v in extract(rules, window, family, pdf=True).items():
                props.append({"id": rid, "param": p, "value": v, "page": pi, "confidence": conf})
    return {"file": str(pdf_path), "scanned": False, "links": links, "proposals": props}


def cmd_excel(paths):
    from openpyxl import load_workbook
    rules = Rules()
    tot = agree = differ = new = 0
    diffs = []
    for p in paths:
        wb = load_workbook(p, read_only=True, data_only=True)
        for ws in wb.worksheets:
            rows = list(ws.iter_rows(values_only=True))
            prow = next((i for i, r in enumerate(rows[:10]) if sum(1 for v in r if isinstance(v, str) and re.fullmatch(r"TS_\w+", v)) >= 3), None)
            if prow is None:
                continue
            names = [v if isinstance(v, str) else "" for v in rows[prow]]
            fam = rules.family_by_sheet(ws.title)
            qc = names.index("TS_QuyCach") if "TS_QuyCach" in names else None
            for r in rows[prow + 1:]:
                if not r or not r[0] or qc is None:
                    continue
                got = extract(rules, r[qc] or "", fam)
                for prm, v in got.items():
                    tot += 1
                    if prm in names:
                        cell = r[names.index(prm)]
                        if cell in (None, ""):
                            new += 1
                        elif str(cell) == v:
                            agree += 1
                        else:
                            differ += 1
                            diffs.append((ws.title, r[0], prm, cell, v))
    print(f"values={tot} same_as_form={agree} differ={differ} not_in_form={new}")
    for d in diffs[:20]:
        print("  DIFF", d)


def cmd_pdf(models_file, pdfs):
    rules = Rules()
    recs = [tuple(l.rstrip("\n").split("\t")[:3]) for l in Path(models_file).read_text(encoding="utf-8").splitlines() if l.strip()]
    for p in pdfs:
        res = match_pdf(rules, recs, p)
        print(f"\n## {Path(p).name} scanned={res['scanned']} links={res.get('links', {})} proposals={len(res['proposals'])}")
        for x in res["proposals"][:25]:
            print("  ", x)


def cmd_fixtures(path):
    """Recompute 'expected' in tests/.../Fixtures/reference_cases.json after a rule change."""
    rules = Rules()
    p = Path(path)
    d = json.loads(p.read_text(encoding="utf-8"))
    for c in d["specCases"]:
        c["expected"] = extract(rules, c["text"], c["family"])
    for c in d["pdfCases"]:
        c["expected"] = extract(rules, c["text"], c["family"], pdf=True)
    for c in d["tokenCases"]:
        c["expected"] = model_tokens(c["model"], rules.d["pdf"]["minModelTokenLength"])
    p.write_text(json.dumps(d, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"updated {len(d['specCases'])} spec + {len(d['pdfCases'])} pdf + {len(d['tokenCases'])} token cases")


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "fixtures":
        cmd_fixtures(sys.argv[2])
    elif len(sys.argv) >= 3 and sys.argv[1] == "excel":
        cmd_excel(sys.argv[2:])
    elif len(sys.argv) >= 4 and sys.argv[1] == "pdf":
        cmd_pdf(sys.argv[2], sys.argv[3:])
    else:
        print(__doc__)
