#!/usr/bin/env python3
import json, unicodedata, math, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
d=json.loads((ROOT/'Data/norm-engine-runtime.json').read_text(encoding='utf-8'))
resdef={x['resource_id']:x for x in d['resources']}
base={}
for x in d['normResources']: base.setdefault(x['variant_code'],{})[x['resource_id']]=x['quantity']
rules=d['adjustmentRules']

def norm(x):
    s='' if x is None else str(x)
    s=''.join(c for c in unicodedata.normalize('NFD',s) if unicodedata.category(c)!='Mn').replace('đ','d').lower()
    return ' '.join(s.split())

def trigger(rule,p):
    actual=p.get(rule['trigger_param'])
    op=rule['trigger_operator'].upper(); exp=rule['trigger_value']
    if actual is None:return False
    if op=='EQ':return norm(actual)==norm(exp)
    if op=='IN':return norm(actual) in [norm(v) for v in exp.split('|')]
    return False

def scope(rule,code,parent):
    sc=rule['norm_scope']
    if sc in (code,parent):return True
    a,b=sc.split('.'); c,d=code.split('.')
    return a==c and b.endswith('0000') and b[0]==d[0]

def adjusted(code,parent,props):
    r=dict(base.get(code,{})); applied=[]
    for rule in rules:
        if not scope(rule,code,parent) or not trigger(rule,props): continue
        targets=[rule['target_resource_id']] if rule['target_resource_id'] in r else []
        if not targets and rule['action_type'].startswith('LAB_'):
            targets=[rid for rid in r if resdef.get(rid,{}).get('resource_type')=='LAB']
        if rule['action_type']=='RESOURCE_REMOVE':
            for rid in targets:r.pop(rid,None)
        elif 'MULTIPLIER' in rule['action_type']:
            for rid in targets:r[rid]*=rule['factor']
        applied.append(rule['adjustment_id'])
    return r,applied

fail=0
r,a=adjusted('AF.12230','AF.12200',{'ConcreteSupply':'ReadyMix','PlacementMethod':'Manual'})
if 'R-MIXER-250' in r or abs(r.get('R-LAB-G2',0)-2.84*.6)>1e-9:
    print('FAIL manual ready-mix',r,a);fail+=1
else:print('PASS manual ready-mix adjustment',a,'labor=',r.get('R-LAB-G2'))

r,a=adjusted('AF.32230','AF.32200',{'PumpType':'Mobile','PlacementMethod':'Pump'})
if abs(r.get('R-LAB-G2',0)-1.98*.9)>1e-9 or abs(r.get('R-PUMP-50',0)-.033*.8)>1e-9:
    print('FAIL mobile pump',r,a);fail+=1
else:print('PASS mobile pump adjustment',a,'labor=',r.get('R-LAB-G2'),'pump=',r.get('R-PUMP-50'))
sys.exit(1 if fail else 0)
