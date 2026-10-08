#!/usr/bin/env python3
"""Export runtime JSON from the master SQLite database. No external packages required."""
import sqlite3, json, os, argparse

p=argparse.ArgumentParser()
p.add_argument('db')
p.add_argument('out')
a=p.parse_args()
con=sqlite3.connect(a.db); con.row_factory=sqlite3.Row; cur=con.cursor()
def rows(t): return [dict(r) for r in cur.execute(f'SELECT * FROM {t}')]
res_counts={r['variant_code']:r['cnt'] for r in cur.execute('SELECT variant_code,COUNT(*) cnt FROM norm_resource GROUP BY variant_code')}
idx=rows('norm_index_final')
for r in idx:
    status=str(r.get('digitization_status') or '')
    # Preserve governed READY/APPROVED; only backfill missing RESOURCE_* tags.
    if res_counts.get(r['norm_code'],0)>0 and not status.upper().startswith('RESOURCE') and status.upper()!='APPROVED':
        r['digitization_status']='RESOURCE_QC1_LEGACY'
payload={
 'schemaVersion':'1.0','sourceDatabase':os.path.basename(a.db),
 'workFamilies':rows('work_family_final'),'normIndex':idx,'normVariants':rows('norm_variant'),
 'normConditions':rows('norm_condition'),'resources':rows('resource'),'normResources':rows('norm_resource'),
 'adjustmentRules':rows('adjustment_rule'),'bimPropertyMaps':rows('bim_property_map_final'),
 'selectorSchemas':rows('selector_schema_final'),'constructionMethods':rows('construction_method'),
 'parameterDictionary':rows('parameter_dictionary')}
def load_classification_seed():
    seed_path=os.path.join(os.path.dirname(os.path.dirname(__file__)),'Config','classification-rules.seed.json')
    if os.path.exists(seed_path):
        with open(seed_path,'r',encoding='utf-8') as f: return json.load(f).get('ruleSets',[])
    return []
try:
    sets=rows('classification_rule_set')
    conds=rows('classification_rule_condition')
    by_set={}
    for c in conds: by_set.setdefault(c['set_id'],[]).append({
        'paramKey':c['param_key'],'operator':c['operator'],'value1':c.get('value1'),
        'value2':c.get('value2'),'unit':c.get('unit') or '','conditionGroup':c.get('condition_group') or 1})
    payload['classificationRuleSets']=[{
        'setId':s['set_id'],'revitCategory':s['revit_category'],'workItemId':s['work_item_id'],
        'priority':s.get('priority') or 100,
        'conditions':by_set.get(s['set_id'],[])} for s in sets]
except sqlite3.OperationalError:
    payload['classificationRuleSets']=load_classification_seed()
def load_bim_mapping_seed():
    seed_path=os.path.join(os.path.dirname(os.path.dirname(__file__)),'Config','bim-mapping-rules.seed.json')
    if os.path.exists(seed_path):
        with open(seed_path,'r',encoding='utf-8') as f: return json.load(f).get('rules',[])
    return []
try:
    payload['bimMappingRules']=[{
        'ruleId':r['rule_id'],'revitCategory':r['revit_category'],'workItemId':r['work_item_id'],
        'priority':r.get('priority') or 100,'familyPattern':r.get('family_pattern'),
        'typePattern':r.get('type_pattern'),'systemCode':r.get('system_code'),
        'systemName':r.get('system_name'),'matchParameterNames':r.get('match_parameter_names'),
        'matchParameterValues':r.get('match_parameter_values'),'conditions':[]} for r in rows('bim_mapping_rule')]
except sqlite3.OperationalError:
    payload['bimMappingRules']=load_bim_mapping_seed()
with open(a.out,'w',encoding='utf-8') as f: json.dump(payload,f,ensure_ascii=False,separators=(',',':'))
print(f'Exported {a.out}')
