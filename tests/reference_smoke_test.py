#!/usr/bin/env python3
import json, os, math, sys, unicodedata
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
DATA=json.loads((ROOT/'Data/norm-engine-runtime.json').read_text(encoding='utf-8'))
EXPECTED=json.loads((ROOT/'tests/expected.json').read_text(encoding='utf-8'))

idx={x['norm_code']:x for x in DATA['normIndex']}
variants={x['variant_code']:x for x in DATA['normVariants']}
conds={}
for c in DATA['normConditions']: conds.setdefault(c['variant_code'],[]).append(c)
maps={}
for m in DATA['bimPropertyMaps']: maps.setdefault(m['revit_category'],[]).append(m)
cls_sets=DATA.get('classificationRuleSets') or []
bim_rules=DATA.get('bimMappingRules') or []

def norm(s):
    s='' if s is None else str(s)
    s=''.join(c for c in unicodedata.normalize('NFD',s) if unicodedata.category(c)!='Mn').replace('đ','d').replace('Đ','D')
    return ' '.join(s.lower().split())

def num(x):
    try: return float(str(x).replace(',','.'))
    except: return None

def pattern_hit(text, pattern):
    if not pattern: return True
    t=norm(text)
    return any(norm(p) and norm(p) in t for p in str(pattern).replace(';','|').split('|'))

def classify(e):
    p=e.get('properties',{})
    ov=p.get('WorkItemOverride') or p.get('QS_WorkItemId')
    if ov: return ov
    cat=e['revitCategory']
    classify_text=norm(' '.join([
        e.get('familyName',''), e.get('typeName',''), e.get('materialName',''),
        str(p.get('MaterialName','')), str(p.get('ElementType',''))
    ]))
    facts={'_ClassifyText': classify_text, **p}

    # bim_mapping_rule (priority ascending)
    candidates=[]
    for r in bim_rules:
        if r.get('revitCategory')!=cat: continue
        fam=r.get('familyPattern'); typ=r.get('typePattern')
        ok=True
        if fam and not pattern_hit(e.get('familyName','')+' '+e.get('typeName',''), fam): ok=False
        if ok and typ and not pattern_hit(e.get('typeName',''), typ): ok=False
        if ok: candidates.append((r.get('priority') or 100, r.get('workItemId')))
    if candidates:
        candidates.sort(key=lambda x:x[0])
        return candidates[0][1]

    # classification_rule_set
    scored=[]
    for rs in cls_sets:
        if rs.get('revitCategory')!=cat: continue
        groups={}
        for c in rs.get('conditions') or []:
            groups.setdefault(c.get('conditionGroup') or 1, []).append(c)
        if not groups or any(all(evalc(c,facts) for c in g) for g in groups.values()):
            scored.append((rs.get('priority') or 100, rs.get('workItemId')))
    if scored:
        scored.sort(key=lambda x:x[0])
        return scored[0][1]

    return maps.get(cat,[{}])[0].get('default_work_item','')

def method_ok(v,p):
    mid=v.get('method_id') or ''
    if not mid: return True
    m=norm(p.get('PlacementMethod',''))
    if not m: return True
    if mid=='M-CONC-MANUAL': return 'manual' in m or 'thu cong' in m
    if mid=='M-CONC-CRANE': return 'crane' in m or 'can cau' in m
    if mid=='M-CONC-PUMP-STATIC': return 'pump' in m or 'bom' in m
    return True

def cat_ok(v,p):
    cid=v.get('category_id') or ''
    e=norm(p.get('ElementType',''))
    if cid=='WC-CONC-COL': return 'column' in e or 'cot' in e
    if cid=='WC-CONC-WALL': return 'wall' in e or 'tuong' in e
    return True

def evalc(c,p):
    k=c.get('paramKey') or c.get('param_key'); op=(c.get('operator') or 'EQ').upper(); exp=c.get('value1')
    if k not in p or p[k] in (None,''): return False
    a=p[k]
    an,en=num(a),num(exp)
    if an is not None and en is not None:
        if op in ('EQ','=','=='): return abs(an-en)<1e-8
        if op=='>': return an>en+1e-8
        if op=='>=': return an>=en-1e-8
        if op=='<': return an<en-1e-8
        if op=='<=': return an<=en+1e-8
    na, ne = norm(a), norm(exp or '')
    if op in ('EQ','=','=='):
        def as_bool(x):
            if isinstance(x, bool): return x
            s=norm(x)
            if s in ('1','true','yes','y','co'): return True
            if s in ('0','false','no','n','khong'): return False
            return None
        ab, eb = as_bool(a), as_bool(exp)
        if ab is not None and eb is not None: return ab==eb
        return na==ne
    if op=='IN': return na in [norm(x) for x in str(exp).replace(';','|').replace(',','|').split('|') if x]
    if op=='CONTAINS':
        return any(norm(x) and norm(x) in na for x in str(exp).replace(';','|').split('|') if x)
    if op=='NOT_CONTAINS':
        return not any(norm(x) and norm(x) in na for x in str(exp).replace(';','|').split('|') if x)
    return False

def select(e):
    wi=classify(e); p=dict(e.get('properties',{}))
    if 'ElementType' not in p:
        p['ElementType']={
            'OST_StructuralColumns':'Column','OST_Walls':'Wall','OST_Floors':'Floor',
            'OST_StructuralFraming':'Beam','OST_StructuralFoundation':'Foundation',
            'OST_Roofs':'Roof','OST_Ceilings':'Ceiling',
            'OST_Topography':'Excavation','OST_BuildingPad':'Fill','OST_Site':'Site','OST_Roads':'Road',
            'OST_Conduit':'Conduit','OST_CableTray':'Tray','OST_LightingFixtures':'Light',
            'OST_ElectricalFixtures':'Switch','OST_ElectricalEquipment':'Protect',
            'OST_MechanicalEquipment':'Equipment','OST_DuctCurves':'Duct',
            'OST_PipeCurves':'Pipe','OST_PipeFitting':'Fitting',
            'OST_PipeAccessory':'Valve','OST_PlumbingFixtures':'Fixture',
            'OST_PipeInsulations':'Insulation','OST_DuctInsulations':'Insulation',
            'OST_FireAlarmDevices':'FireAlarm'
        }.get(e['revitCategory'],'')
    if 'Thickness' in p and 'WallThickness' not in p: p['WallThickness']=p['Thickness']
    if 'WallThickness' in p and 'Thickness' not in p: p['Thickness']=p['WallThickness']
    if wi=='MEP-INSUL':
        if 'Thickness' in p and 'InsulationThickness' not in p: p['InsulationThickness']=p['Thickness']
        if 'InsulationThickness' in p and 'Thickness' not in p: p['Thickness']=p['InsulationThickness']
    codes={x['norm_code'] for x in DATA['normIndex'] if x.get('work_item_id')==wi}
    matches=[]
    for code,v in variants.items():
        if code not in codes or not method_ok(v,p) or not cat_ok(v,p): continue
        cs=conds.get(code,[])
        groups={}
        for c in cs: groups.setdefault(c.get('condition_group') or 1,[]).append(c)
        if not groups: matches.append((0,code)); continue
        for g in groups.values():
            if all(evalc(c,p) for c in g):
                matches.append((len(g),code)); break
    matches.sort(key=lambda x:(-x[0],x[1]))
    if not matches: return wi,None,[]
    best=matches[0][0]; winners=[c for n,c in matches if n==best]
    return wi, winners[0] if len(winners)==1 else None, winners

fail=0
for filename, expected in EXPECTED.items():
    e=json.loads((ROOT/'SampleInputs'/filename).read_text(encoding='utf-8'))
    wi, code, winners=select(e)
    ok=code==expected
    print(f"{filename}: workItem={wi} code={code} expected={expected} {'PASS' if ok else 'FAIL'} candidates={winners[:8]}")
    if not ok: fail+=1

# Runtime integrity
resource_codes={x['variant_code'] for x in DATA['normResources']}
missing=[c for c in resource_codes if c not in variants]
print(f"runtime: variants={len(variants)} conditions={len(DATA['normConditions'])} resources={len(DATA['normResources'])} orphanResourceVariants={len(missing)}")
if missing: fail+=1

# N4 formwork readonly — XD-FORM quantity maps
xd_form=[m for m in DATA['bimPropertyMaps'] if m.get('default_work_item')=='XD-FORM']
need={'OST_StructuralColumns','OST_Walls','OST_Floors','OST_StructuralFraming','OST_StructuralFoundation'}
got={m['revit_category'] for m in xd_form}
qp_ok=all(m.get('quantity_parameter')=='QS_FormworkArea' for m in xd_form if m['revit_category'] in need)
n4_ok=need.issubset(got) and qp_ok and len(DATA['bimPropertyMaps'])>=27
print(f"n4_xd_form_maps: count={len(xd_form)} cats={sorted(got & need)} qp_ok={qp_ok} bim_total={len(DATA['bimPropertyMaps'])} {'PASS' if n4_ok else 'FAIL'}")
if not n4_ok: fail+=1
sys.exit(1 if fail else 0)
