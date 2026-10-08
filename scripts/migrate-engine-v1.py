#!/usr/bin/env python3
"""Create an engine-ready SQLite snapshot from Database_DinhMuc_QS_BIM_FINAL_v2.

Patches are idempotent and only address rule-engine consistency:
1) Exclusive AE.222 wall thickness band (>11 and <=33 cm).
2) ElementType conditions for AF.611-AF.615 rebar leaf codes.
3) Add missing norm_index_final rows for legacy resource-backed AF leaf variants.
4) Footing / beam-slab ElementType discriminators.
5) M1 Official unlock — promote smoke-verified pilot leaves to RESOURCE_READY;
   demote non-selectable RESOURCE_READY parent orphans.
6) M2 residual AE/AK — seed selectable variants + provisional QC1 resources.
7) M4 Phase 2 chapter AB — pilot EXC/FILL/TRANS/ROCK leaves + provisional resources.
8) M4 Phase 2 chapter AC — pilot DRIVE/BORED/SHEET/SOIL-MIX pile leaves + provisional resources.
9) M4 Phase 2 chapter AI — pilot STEEL-FAB / STEEL-ERECT leaves + provisional resources.
10) M4 Phase 2 chapter BA — pilot electrical/HVAC MEP leaves + provisional resources.
11) M4 Phase 2 chapter BB — pilot pipe/plumbing MEP leaves + provisional index where missing.
12) M4 Phase 2 chapter BC — pilot insulation (Bảo ôn) thickness leaves + provisional resources.
13) M4 Phase 2 chapter BD — pilot FIRE-CTRL / HDD / WELL leaves + provisional resources.
14) N4 formwork readonly — bim_property_map_final XD-FORM rows with QS_FormworkArea.
"""
import sqlite3, shutil, argparse, os
p=argparse.ArgumentParser()
p.add_argument('source')
p.add_argument('target')
a=p.parse_args()
if os.path.abspath(a.source)!=os.path.abspath(a.target): shutil.copy2(a.source,a.target)
con=sqlite3.connect(a.target); con.row_factory=sqlite3.Row; cur=con.cursor()

def next_cond_id():
    n=cur.execute("select max(cast(substr(condition_id,2) as integer)) from norm_condition").fetchone()[0] or 0
    return n+1

def next_nr_id():
    n=cur.execute("select max(cast(substr(norm_resource_id,3) as integer)) from norm_resource where norm_resource_id like 'NR%'").fetchone()[0] or 0
    return n+1

def ensure_variant(code, parent, name, unit, conditions):
    """Insert variant + conditions if missing. conditions: list of (param, op, v1, v2, unit)."""
    if not cur.execute('select 1 from norm_variant where variant_code=?',(code,)).fetchone():
        cur.execute('insert into norm_variant values (?,?,?,?,?,?,?,?)',
                    (code, parent, name, None, None, unit, 0, 0))
    n=next_cond_id()
    for param, op, v1, v2, unit in conditions:
        exists=cur.execute(
            "select 1 from norm_condition where variant_code=? and param_key=? and operator=? and ifnull(value1,'')=ifnull(?, '')",
            (code, param, op, v1)).fetchone()
        if not exists:
            cur.execute('insert into norm_condition values (?,?,?,?,?,?,?,?)',
                        (f'C{n:04d}', code, param, op, v1, v2, unit or '', 1)); n+=1

def clone_resources(src_code, dst_code):
    if cur.execute('select 1 from norm_resource where variant_code=?',(dst_code,)).fetchone():
        return
    n=next_nr_id()
    for r in cur.execute('select resource_id, quantity from norm_resource where variant_code=?',(src_code,)).fetchall():
        cur.execute('insert into norm_resource values (?,?,?,?)',
                    (f'NR{n:05d}', dst_code, r['resource_id'], r['quantity'])); n+=1

def ensure_resources_list(code, pairs):
    if cur.execute('select 1 from norm_resource where variant_code=?',(code,)).fetchone():
        return
    n=next_nr_id()
    for rid, qty in pairs:
        cur.execute('insert into norm_resource values (?,?,?,?)', (f'NR{n:05d}', code, rid, qty)); n+=1

def ensure_index(code, wi, label, parent, chapter='MEP-02', source=None):
    if cur.execute('select 1 from norm_index_final where norm_code=?',(code,)).fetchone():
        return
    src = source or 'https://datafiles.chinhphu.vn/cpp/files/vbpq/2026/7/38.2026.tt-bxd-pl3dmdtlapdathtkt_signed1.pdf'
    cur.execute('''insert into norm_index_final
      (norm_code,prefix,chapter,work_item_id,normalized_label,parent_inferred,hierarchy_level,digitization_status,official_source,qc_note)
      values (?,?,?,?,?,?,?,?,?,?)''', (
        code, code[:2], chapter, wi, label, parent, 3, 'CODE_INDEX_READY', src,
        'M4 BB provisional index row; replace with governed TT38 leaf when available.'))

# 1. Exclusive masonry band
n=next_cond_id()
for code in ['AE.22210','AE.22220','AE.22230','AE.22240']:
    if not cur.execute("select 1 from norm_condition where variant_code=? and param_key='WallThickness' and operator='>' and value1='11'",(code,)).fetchone():
        cur.execute('insert into norm_condition values (?,?,?,?,?,?,?,?)',(f'C{n:04d}',code,'WallThickness','>','11',None,'cm',1)); n+=1

# 2. Rebar element type discriminator
parent_map={'AF.61100':'Foundation','AF.61200':'MachineBase','AF.61300':'Wall','AF.61400':'Column','AF.61500':'Beam'}
for parent,etype in parent_map.items():
    for row in cur.execute('select variant_code from norm_variant where parent_norm_code=?',(parent,)).fetchall():
        code=row['variant_code']
        if not cur.execute("select 1 from norm_condition where variant_code=? and param_key='ElementType'",(code,)).fetchone():
            cur.execute('insert into norm_condition values (?,?,?,?,?,?,?,?)',(f'C{n:04d}',code,'ElementType','EQ',etype,None,'',1)); n+=1

# 3. Legacy resource-backed variants missing from norm index
rows=cur.execute('''
select v.*, (select count(*) from norm_resource nr where nr.variant_code=v.variant_code) res_count
from norm_variant v left join norm_index_final ni on ni.norm_code=v.variant_code
where ni.norm_code is null
''').fetchall()
for r in rows:
    code=r['variant_code']
    work_item='XD-CONC' if code.startswith('AF.') else ''
    chapter='XD-06' if code.startswith('AF.') else ''
    status='RESOURCE_QC1_LEGACY' if r['res_count'] else 'RULE_READY'
    cur.execute('''insert into norm_index_final
      (norm_code,prefix,chapter,work_item_id,normalized_label,parent_inferred,hierarchy_level,digitization_status,official_source,qc_note)
      values (?,?,?,?,?,?,?,?,?,?)''',(
       code,code[:2],chapter,work_item,r['variant_name'],r['parent_norm_code'],3,status,
       'https://datafiles.chinhphu.vn/cpp/files/vbpq/2026/7/38.2026.tt-bxd-pl2dmdtxaydung_signed1.pdf',
       'Engine migration v1: legacy resource-backed leaf code promoted to norm index; QS review required.'
    ))

# 4. Footing vs beam/slab disambiguation for WC-CONC crane/manual width leaves
n=next_cond_id()
for code in ['AF.11110','AF.11120','AF.11210','AF.11220','AF.21210','AF.21220']:
    if not cur.execute("select 1 from norm_condition where variant_code=? and param_key='ElementType'",(code,)).fetchone():
        cur.execute('insert into norm_condition values (?,?,?,?,?,?,?,?)',
                    (f'C{n:04d}',code,'ElementType','EQ','Foundation',None,'',1)); n+=1
for code in ['AF.22310','AF.22320','AF.12310','AF.12320']:
    if cur.execute("select 1 from norm_variant where variant_code=?",(code,)).fetchone() and \
       not cur.execute("select 1 from norm_condition where variant_code=? and param_key='ElementType'",(code,)).fetchone():
        cur.execute('insert into norm_condition values (?,?,?,?,?,?,?,?)',
                    (f'C{n:04d}',code,'ElementType','CONTAINS','Beam|Slab|Floor|San|Dam|Giang',None,'',1)); n+=1

# 5. M1 Official unlock — smoke-verified pilot leaves only (CONC/REBAR/FORM/MASON-BRICK)
PILOT_READY = [
    'AF.32230',  # concrete column pump static
    'AF.12230',  # concrete column manual ready-mix
    'AF.61432',  # rebar column
    'AF.83111',  # formwork slab
    'AE.22110',  # brick wall thin
    'AF.21210',  # foundation crane
]
for code in PILOT_READY:
    if cur.execute('select 1 from norm_variant where variant_code=?',(code,)).fetchone() and \
       cur.execute('select 1 from norm_resource where variant_code=?',(code,)).fetchone():
        cur.execute('''update norm_index_final
            set digitization_status='RESOURCE_READY',
                qc_note='M1 Official unlock: smoke-verified leaf; QS may further promote to APPROVED.'
            where norm_code=?''', (code,))

# Demote non-selectable parent codes that were RESOURCE_READY without being variants
for code in ['AF.12100', 'AF.12200', 'AF.32100', 'AF.32200']:
    if not cur.execute('select 1 from norm_variant where variant_code=?',(code,)).fetchone():
        cur.execute('''update norm_index_final
            set digitization_status='CODE_INDEX_READY',
                qc_note='Parent band index only; selectable children carry RESOURCE_* status.'
            where norm_code=? and digitization_status='RESOURCE_READY' ''', (code,))

# 6. M2 residual AE/AK — minimal selectable leaves (provisional QC1 resources cloned from nearest pack)
# Do NOT promote these to RESOURCE_READY — VL/NC/M are placeholders pending TT38 pack QC.
residual_seeds = [
    # code, parent, name, unit, work_item, conditions, clone_from
    ('AE.88110', 'AE.88100', 'Xây tường AAC thẳng, dày ≤11cm, cao ≤6m', 'm3', 'XD-MASON-AAC',
     [('WallThickness', '<=', '11', None, 'cm'), ('Height', '<=', '6', None, 'm'),
      ('ElementType', 'EQ', 'Wall', None, '')], 'AE.22110'),
    ('AE.11100', 'AE.11000', 'Xây đá tường/móng, band pilot ≤6m', 'm3', 'XD-MASON-STONE',
     [('Height', '<=', '6', None, 'm'), ('ElementType', 'CONTAINS', 'Wall|Foundation', None, '')], 'AE.22110'),
    ('AE.91000', 'AE.90000', 'Xây gạch chịu lửa, band pilot', 'm3', 'XD-MASON-REF',
     [('ElementType', 'EQ', 'Wall', None, '')], 'AE.22110'),
    ('AK.11200', 'AK.11000', 'Lợp mái band pilot', 'm2', 'XD-ROOF',
     [('ElementType', 'CONTAINS', 'Roof|Mai', None, '')], 'AK.51240'),
    ('AK.41100', 'AK.40000', 'Láng nền/sàn band pilot', 'm2', 'XD-SCREED',
     [('ElementType', 'CONTAINS', 'Floor|Slab|Screed|Lang', None, '')], 'AK.51240'),
    ('AK.61000', 'AK.60000', 'Trần band pilot', 'm2', 'XD-CEILING',
     [('ElementType', 'CONTAINS', 'Ceiling|Tran', None, '')], 'AK.21220'),
    ('AK.71100', 'AK.70000', 'Mộc trang trí/vách gỗ band pilot', 'm2', 'XD-CARPENTRY',
     [('ElementType', 'CONTAINS', 'Wall|Door|Floor|Wood|Carpentry|Moc', None, '')], 'AK.21220'),
]
for code, parent, name, unit, wi, conds, clone_from in residual_seeds:
    ensure_variant(code, parent, name, unit, conds)
    clone_resources(clone_from, code)
    cur.execute('''update norm_index_final
        set digitization_status='RESOURCE_QC1',
            work_item_id=?,
            normalized_label=?,
            qc_note='M2 residual seed: provisional VL/NC/M cloned from nearest pack; not Official-ready.'
        where norm_code=?''', (wi, name, code))
    cur.execute('''update work_family_final
        set resource_status='RESOURCE_PACK_V2_QC1',
            mapping_status='RULE_SCHEMA_READY'
        where work_item_id=?''', (wi,))

# 7. M4 Phase 2 chapter AB — pilot selectable leaves (provisional QC1; no official TT38 VL/NC/M in DB)
ab_seeds = [
    ('AB.12110', 'AB.12100', 'Đào đất band pilot (thủ công/máy nhẹ)', 'm3', 'XD-EARTH-EXC',
     [('ElementType', 'CONTAINS', 'Excavation|Earth|Cut|Dao|Site', None, '')],
     [('R-LAB-35', 1.2), ('R-MAT-OTHER', 2.0), ('R-MACH-OTHER', 1.0)]),
    ('AB.13100', 'AB.13000', 'Đắp/đầm đất cát band pilot', 'm3', 'XD-EARTH-FILL',
     [('ElementType', 'CONTAINS', 'Fill|Embankment|Pad|Dap|Dam', None, '')],
     [('R-LAB-35', 0.8), ('R-MAT-OTHER', 2.0), ('R-VIB-PLATE-1', 0.05)]),
    ('AB.41000', 'AB.40000', 'Vận chuyển đất đá band pilot', 'm3', 'XD-EARTH-TRANS',
     [('ElementType', 'CONTAINS', 'Haul|Transport|Van chuyen|Truck', None, '')],
     [('R-LAB-30', 0.3), ('R-TRUCK-25', 0.15), ('R-MACH-OTHER', 1.0)]),
    ('AB.51110', 'AB.51100', 'Khoan phá đá band pilot', 'm3', 'XD-ROCK',
     [('ElementType', 'CONTAINS', 'Rock|Stone|Da|Blast|Drill', None, '')],
     [('R-LAB-40', 1.5), ('R-MAT-OTHER', 3.0), ('R-MACH-OTHER', 2.0)]),
]
for code, parent, name, unit, wi, conds, res_pairs in ab_seeds:
    ensure_variant(code, parent, name, unit, conds)
    ensure_resources_list(code, res_pairs)
    cur.execute('''update norm_index_final
        set digitization_status='RESOURCE_QC1',
            work_item_id=?,
            normalized_label=?,
            qc_note='M4 AB pilot seed: provisional VL/NC/M; replace with TT38 pack before Official.'
        where norm_code=?''', (wi, name, code))
    cur.execute('''update work_family_final
        set resource_status='RESOURCE_PACK_V2_QC1',
            mapping_status='RULE_SCHEMA_READY'
        where work_item_id=?''', (wi,))

# 8. M4 Phase 2 chapter AC — pile / soil-mix pilots (provisional QC1)
ac_seeds = [
    ('AC.12410', 'AC.12400', 'Đóng/ép cọc band pilot (bê tông)', 'm', 'XD-PILE-DRIVE',
     [('PileType', 'EQ', 'Driven', None, ''),
      ('ElementType', 'CONTAINS', 'Pile|Foundation|Coc', None, '')],
     [('R-LAB-40', 0.8), ('R-MAT-OTHER', 2.0), ('R-CRANE-10', 0.05), ('R-MACH-OTHER', 1.0)]),
    ('AC.34510', 'AC.34500', 'Cọc khoan nhồi/Barrette band pilot', 'm', 'XD-PILE-BORED',
     [('PileType', 'EQ', 'Bored', None, ''),
      ('ElementType', 'CONTAINS', 'Pile|Foundation|Coc|Barrette', None, '')],
     [('R-LAB-40', 1.0), ('R-CONC-MORTAR', 0.15), ('R-MAT-OTHER', 2.0), ('R-MACH-OTHER', 2.0)]),
    ('AC.22100', 'AC.22000', 'Cừ/ván thép band pilot', 'm', 'XD-PILE-SHEET',
     [('PileType', 'EQ', 'Sheet', None, ''),
      ('ElementType', 'CONTAINS', 'Pile|Sheet|Foundation|Cu|Van thep', None, '')],
     [('R-LAB-35', 0.6), ('R-MAT-OTHER', 3.0), ('R-CRANE-10', 0.04), ('R-MACH-OTHER', 1.0)]),
    ('AC.41110', 'AC.41100', 'Xi măng đất/CFG band pilot', 'm', 'XD-SOIL-MIX',
     [('PileType', 'EQ', 'CFA', None, ''),
      ('ElementType', 'CONTAINS', 'Pile|Foundation|SoilMix|CFG|DSM', None, '')],
     [('R-LAB-35', 0.7), ('R-CEMENT', 50.0), ('R-MAT-OTHER', 2.0), ('R-MACH-OTHER', 1.5)]),
]
for code, parent, name, unit, wi, conds, res_pairs in ac_seeds:
    ensure_variant(code, parent, name, unit, conds)
    ensure_resources_list(code, res_pairs)
    cur.execute('''update norm_index_final
        set digitization_status='RESOURCE_QC1',
            work_item_id=?,
            normalized_label=?,
            qc_note='M4 AC pilot seed: provisional VL/NC/M; replace with TT38 pack before Official.'
        where norm_code=?''', (wi, name, code))
    cur.execute('''update work_family_final
        set resource_status='RESOURCE_PACK_V2_QC1',
            mapping_status='RULE_SCHEMA_READY'
        where work_item_id=?''', (wi,))

# 9. M4 Phase 2 chapter AI — structural steel fab/erect pilots (provisional QC1)
ai_seeds = [
    ('AI.11110', 'AI.11100', 'Gia công cấu kiện thép band pilot', 'kg', 'XD-STEEL-FAB',
     [('SteelActivity', 'EQ', 'Fabrication', None, ''),
      ('ElementType', 'CONTAINS', 'Column|Beam|Member|Brace|Steel|Thep', None, '')],
     [('R-LAB-40', 0.05), ('R-STEEL-ROUND', 1.02), ('R-WELD-ROD', 0.02), ('R-WELDER-23', 0.01), ('R-MAT-OTHER', 1.0)]),
    ('AI.61110', 'AI.61100', 'Lắp dựng cột thép band pilot', 'kg', 'XD-STEEL-ERECT',
     [('SteelActivity', 'EQ', 'Erection', None, ''),
      ('ElementType', 'EQ', 'Column', None, '')],
     [('R-LAB-40', 0.04), ('R-CRANE-16', 0.02), ('R-MAT-OTHER', 1.0), ('R-MACH-OTHER', 1.0)]),
    ('AI.61120', 'AI.61100', 'Lắp dựng dầm/khung thép band pilot', 'kg', 'XD-STEEL-ERECT',
     [('SteelActivity', 'EQ', 'Erection', None, ''),
      ('ElementType', 'CONTAINS', 'Beam|Framing|Brace|Member', None, '')],
     [('R-LAB-40', 0.045), ('R-CRANE-16', 0.025), ('R-MAT-OTHER', 1.0), ('R-MACH-OTHER', 1.0)]),
]
for code, parent, name, unit, wi, conds, res_pairs in ai_seeds:
    ensure_variant(code, parent, name, unit, conds)
    ensure_resources_list(code, res_pairs)
    cur.execute('''update norm_index_final
        set digitization_status='RESOURCE_QC1',
            work_item_id=?,
            normalized_label=?,
            qc_note='M4 AI pilot seed: provisional VL/NC/M; replace with TT38 pack before Official.'
        where norm_code=?''', (wi, name, code))
    cur.execute('''update work_family_final
        set resource_status='RESOURCE_PACK_V2_QC1',
            mapping_status='RULE_SCHEMA_READY'
        where work_item_id=?''', (wi,))

# 10. M4 Phase 2 chapter BA — electrical / HVAC pilots (provisional QC1)
ba_seeds = [
    ('BA.11100', 'BA.11000', 'Quạt band pilot', 'each', 'MEP-HVAC-FAN',
     [('EquipmentType', 'EQ', 'Fan', None, '')],
     [('R-LAB-35', 0.2), ('R-MAT-OTHER', 2.0), ('R-MACH-OTHER', 0.5)]),
    ('BA.12100', 'BA.12000', 'Điều hòa cục bộ band pilot', 'set', 'MEP-HVAC-AC',
     [('EquipmentType', 'CONTAINS', 'SplitAC|VRV|FCU|AC|DieuHoa', None, '')],
     [('R-LAB-40', 0.5), ('R-MAT-OTHER', 3.0), ('R-MACH-OTHER', 1.0)]),
    ('BA.13100', 'BA.13000', 'Đèn band pilot', 'each', 'MEP-LIGHT',
     [('ElementType', 'CONTAINS', 'Light|Luminaire|Den|Fixture', None, '')],
     [('R-LAB-30', 0.15), ('R-MAT-OTHER', 2.0)]),
    ('BA.14100', 'BA.14000', 'Ống/máng bảo hộ dây band pilot', 'm', 'MEP-CONDUIT',
     [('ElementType', 'CONTAINS', 'Conduit|Tray|Ong|Mang', None, '')],
     [('R-LAB-30', 0.05), ('R-MAT-OTHER', 2.0)]),
    ('BA.16100', 'BA.16000', 'Dây dẫn/cáp band pilot', 'm', 'MEP-WIRE',
     [('ElementType', 'CONTAINS', 'Wire|Cable|Day|Cap', None, '')],
     [('R-LAB-30', 0.03), ('R-MAT-OTHER', 2.0)]),
    ('BA.17100', 'BA.17000', 'Công tắc/ổ cắm band pilot', 'each', 'MEP-SWITCH',
     [('DeviceType', 'CONTAINS', 'Switch|Outlet|Socket|CongTac|OCam', None, '')],
     [('R-LAB-30', 0.1), ('R-MAT-OTHER', 1.5)]),
    ('BA.18200', 'BA.18000', 'Aptomat 1 pha band pilot', 'each', 'MEP-PROTECT',
     [('DeviceType', 'CONTAINS', 'Breaker|Aptomat|MCB|Protect', None, '')],
     [('R-LAB-35', 0.2), ('R-MAT-OTHER', 2.0)]),
    ('BA.19100', 'BA.19000', 'Chống sét/tiếp địa band pilot', 'var', 'MEP-LPS',
     [('ComponentType', 'CONTAINS', 'LPS|Earthing|Ground|ChongSet|TiepDia', None, '')],
     [('R-LAB-35', 0.3), ('R-MAT-OTHER', 2.0)]),
    ('BA.15100', 'BA.15000', 'Cột đèn công cộng band pilot', 'var', 'MEP-PUBLIC-LIGHT',
     [('ComponentType', 'CONTAINS', 'Pole|StreetLight|CotDen|PublicLight', None, '')],
     [('R-LAB-40', 0.4), ('R-MAT-OTHER', 3.0), ('R-CRANE-10', 0.02)]),
]
for code, parent, name, unit, wi, conds, res_pairs in ba_seeds:
    ensure_variant(code, parent, name, unit, conds)
    ensure_resources_list(code, res_pairs)
    cur.execute('''update norm_index_final
        set digitization_status='RESOURCE_QC1',
            work_item_id=?,
            normalized_label=?,
            qc_note='M4 BA pilot seed: provisional VL/NC/M; replace with TT38 pack before Official.'
        where norm_code=?''', (wi, name, code))
    cur.execute('''update work_family_final
        set resource_status='RESOURCE_PACK_V2_QC1',
            mapping_status='RULE_SCHEMA_READY'
        where work_item_id=?''', (wi,))

# 11. M4 Phase 2 chapter BB — pipes / fittings / valves / plumbing (provisional QC1)
# Provisional index for WI that had family but no BB leaf in master index.
ensure_index('BB.21010', 'MEP-PIPE-PLASTIC', 'Ống nhựa band pilot', 'BB.21000')
ensure_index('BB.31010', 'MEP-PIPE-CI', 'Ống gang band pilot', 'BB.31000')
ensure_index('BB.61010', 'MEP-VALVE', 'Van/phụ kiện band pilot', 'BB.61000')
ensure_index('BB.72010', 'MEP-PLUMB-FIX', 'Thiết bị vệ sinh band pilot', 'BB.72000')

bb_seeds = [
    ('BB.11110', 'BB.11100', 'Ống bê tông band pilot', 'm', 'MEP-PIPE-CONC',
     [('PipeMaterial', 'CONTAINS', 'Concrete|BeTong|RC', None, '')],
     [('R-LAB-35', 0.08), ('R-MAT-OTHER', 2.0), ('R-MACH-OTHER', 0.5)]),
    ('BB.21010', 'BB.21000', 'Ống nhựa band pilot', 'm', 'MEP-PIPE-PLASTIC',
     [('PipeMaterial', 'CONTAINS', 'PVC|HDPE|PPR|Plastic|Nhua', None, '')],
     [('R-LAB-30', 0.05), ('R-MAT-OTHER', 2.0)]),
    ('BB.31010', 'BB.31000', 'Ống gang band pilot', 'm', 'MEP-PIPE-CI',
     [('PipeMaterial', 'CONTAINS', 'CastIron|CI|Gang', None, '')],
     [('R-LAB-35', 0.07), ('R-MAT-OTHER', 2.0)]),
    ('BB.51000', 'BB.50000', 'Ống đồng band pilot', 'm', 'MEP-PIPE-COPPER',
     [('PipeMaterial', 'CONTAINS', 'Copper|Dong', None, '')],
     [('R-LAB-35', 0.06), ('R-MAT-OTHER', 2.5)]),
    ('BB.89300', 'BB.89000', 'Ống thép band pilot', 'm', 'MEP-PIPE-STEEL',
     [('PipeMaterial', 'CONTAINS', 'Steel|Thep|Galvanized|Inox', None, '')],
     [('R-LAB-35', 0.07), ('R-MAT-OTHER', 2.0), ('R-WELD-ROD', 0.01)]),
    ('BB.74100', 'BB.74000', 'Phụ tùng ống band pilot', 'each', 'MEP-FITTING',
     [('ElementType', 'CONTAINS', 'Fitting|Elbow|Tee|Reducer|Cut|Con', None, '')],
     [('R-LAB-30', 0.1), ('R-MAT-OTHER', 1.5)]),
    ('BB.61010', 'BB.61000', 'Van/phụ kiện band pilot', 'each', 'MEP-VALVE',
     [('ElementType', 'CONTAINS', 'Valve|Van|Accessory', None, '')],
     [('R-LAB-35', 0.15), ('R-MAT-OTHER', 2.0)]),
    ('BB.72010', 'BB.72000', 'Thiết bị vệ sinh band pilot', 'each', 'MEP-PLUMB-FIX',
     [('ElementType', 'CONTAINS', 'Fixture|Toilet|Sink|Lavatory|Urinal|ThietBi', None, '')],
     [('R-LAB-35', 0.25), ('R-MAT-OTHER', 2.0)]),
]
for code, parent, name, unit, wi, conds, res_pairs in bb_seeds:
    ensure_variant(code, parent, name, unit, conds)
    ensure_resources_list(code, res_pairs)
    cur.execute('''update norm_index_final
        set digitization_status='RESOURCE_QC1',
            work_item_id=?,
            normalized_label=?,
            qc_note='M4 BB pilot seed: provisional VL/NC/M; replace with TT38 pack before Official.'
        where norm_code=?''', (wi, name, code))
    cur.execute('''update work_family_final
        set resource_status='RESOURCE_PACK_V2_QC1',
            mapping_status='RULE_SCHEMA_READY'
        where work_item_id=?''', (wi,))

# 12. M4 Phase 2 chapter BC — insulation / bảo ôn (MEP-INSUL) thickness pilots
bc_seeds = [
    ('BC.13100', 'BC.13000', 'Bảo ôn ống – lớp 25 mm band pilot', 'm2', 'MEP-INSUL',
     [('InsulationThickness', 'EQ', '25', None, 'mm')],
     [('R-LAB-30', 0.12), ('R-MAT-OTHER', 2.0)]),
    ('BC.13200', 'BC.13000', 'Bảo ôn ống – lớp 30 mm band pilot', 'm2', 'MEP-INSUL',
     [('InsulationThickness', 'EQ', '30', None, 'mm')],
     [('R-LAB-30', 0.14), ('R-MAT-OTHER', 2.2)]),
    ('BC.13300', 'BC.13000', 'Bảo ôn ống – lớp 50 mm band pilot', 'm2', 'MEP-INSUL',
     [('InsulationThickness', 'EQ', '50', None, 'mm')],
     [('R-LAB-35', 0.18), ('R-MAT-OTHER', 2.5)]),
    ('BC.13400', 'BC.13000', 'Bảo ôn ống – lớp 100 mm band pilot', 'm2', 'MEP-INSUL',
     [('InsulationThickness', 'EQ', '100', None, 'mm')],
     [('R-LAB-35', 0.25), ('R-MAT-OTHER', 3.0)]),
]
for code, parent, name, unit, wi, conds, res_pairs in bc_seeds:
    ensure_variant(code, parent, name, unit, conds)
    ensure_resources_list(code, res_pairs)
    cur.execute('''update norm_index_final
        set digitization_status='RESOURCE_QC1',
            work_item_id=?,
            normalized_label=?,
            qc_note='M4 BC pilot seed: provisional VL/NC/M; replace with TT38 pack before Official.'
        where norm_code=?''', (wi, name, code))
    cur.execute('''update work_family_final
        set resource_status='RESOURCE_PACK_V2_QC1',
            mapping_status='RULE_SCHEMA_READY'
        where work_item_id=?''', (wi,))

# 13. M4 Phase 2 chapter BD — fire control / HDD / well pilots
bd_seeds = [
    ('BD.42400', 'BD.42000', 'Thiết bị PCCC/báo động band pilot', 'each', 'MEP-FIRE-CTRL',
     [('DeviceType', 'CONTAINS', 'FireAlarm|Detector|Smoke|Heat|MCP|CallPoint|BaoChay|BaoDong', None, '')],
     [('R-LAB-35', 0.2), ('R-MAT-OTHER', 2.0)]),
    ('BD.11100', 'BD.11000', 'Khoan ngầm HDD đặt ống HDPE band pilot', 'm', 'MEP-HDD',
     [('ProductType', 'CONTAINS', 'HDPE|Pipe|Ong', None, '')],
     [('R-LAB-40', 0.15), ('R-MAT-OTHER', 2.0), ('R-MACH-OTHER', 1.0)]),
    ('BD.12100', 'BD.12000', 'Khoan ngầm HDD đặt cáp band pilot', 'm', 'MEP-HDD',
     [('ProductType', 'CONTAINS', 'Cable|Cap|Wire', None, '')],
     [('R-LAB-40', 0.15), ('R-MAT-OTHER', 2.0), ('R-MACH-OTHER', 1.0)]),
    ('BD.22210', 'BD.22200', 'Khoan giếng đập cáp band pilot', 'm', 'MEP-WELL',
     [('DrillMethod', 'CONTAINS', 'CablePercussion|DapCap|Percussion|Cable', None, '')],
     [('R-LAB-40', 0.2), ('R-MAT-OTHER', 2.0), ('R-MACH-OTHER', 1.5)]),
]
for code, parent, name, unit, wi, conds, res_pairs in bd_seeds:
    ensure_variant(code, parent, name, unit, conds)
    ensure_resources_list(code, res_pairs)
    cur.execute('''update norm_index_final
        set digitization_status='RESOURCE_QC1',
            work_item_id=?,
            normalized_label=?,
            qc_note='M4 BD pilot seed: provisional VL/NC/M; replace with TT38 pack before Official.'
        where norm_code=?''', (wi, name, code))
    cur.execute('''update work_family_final
        set resource_status='RESOURCE_PACK_V2_QC1',
            mapping_status='RULE_SCHEMA_READY'
        where work_item_id=?''', (wi,))

con.commit();
print('engine db:',a.target)
print('variants:',cur.execute('select count(*) from norm_variant').fetchone()[0])
print('conditions:',cur.execute('select count(*) from norm_condition').fetchone()[0])
print('resources:',cur.execute('select count(*) from norm_resource').fetchone()[0])
seed_path=os.path.join(os.path.dirname(os.path.dirname(__file__)),'Config','classification-rules.seed.json')
if os.path.exists(seed_path):
    import json
    seed=json.load(open(seed_path,'r',encoding='utf-8'))
    cur.executescript('''
    CREATE TABLE IF NOT EXISTS classification_rule_set (
      set_id TEXT PRIMARY KEY,
      revit_category TEXT NOT NULL,
      work_item_id TEXT NOT NULL,
      priority INTEGER NOT NULL DEFAULT 100,
      active INTEGER NOT NULL DEFAULT 1
    );
    CREATE TABLE IF NOT EXISTS classification_rule_condition (
      condition_id TEXT PRIMARY KEY,
      set_id TEXT NOT NULL,
      param_key TEXT NOT NULL,
      operator TEXT NOT NULL,
      value1 TEXT,
      value2 TEXT,
      unit TEXT,
      condition_group INTEGER NOT NULL DEFAULT 1
    );''')
    cid=1
    for rs in seed.get('ruleSets',[]):
        cur.execute('INSERT OR REPLACE INTO classification_rule_set values (?,?,?,?,?)',
            (rs['setId'],rs['revitCategory'],rs['workItemId'],rs.get('priority',100),1))
        cur.execute('DELETE FROM classification_rule_condition WHERE set_id=?',(rs['setId'],))
        for c in rs.get('conditions',[]):
            cur.execute('INSERT INTO classification_rule_condition values (?,?,?,?,?,?,?,?)',
                (f'CR{cid:04d}',rs['setId'],c['paramKey'],c['operator'],c.get('value1'),c.get('value2'),
                 c.get('unit') or '',c.get('conditionGroup') or 1))
            cid+=1
    con.commit()
    print('classification_rule_sets:',cur.execute('select count(*) from classification_rule_set').fetchone()[0])
bim_seed_path=os.path.join(os.path.dirname(os.path.dirname(__file__)),'Config','bim-mapping-rules.seed.json')
if os.path.exists(bim_seed_path):
    import json
    bseed=json.load(open(bim_seed_path,'r',encoding='utf-8'))
    cur.executescript('''
    CREATE TABLE IF NOT EXISTS bim_mapping_rule (
      rule_id TEXT PRIMARY KEY,
      revit_category TEXT NOT NULL,
      work_item_id TEXT NOT NULL,
      priority INTEGER NOT NULL DEFAULT 100,
      family_pattern TEXT,
      type_pattern TEXT,
      system_code TEXT,
      system_name TEXT,
      match_parameter_names TEXT,
      match_parameter_values TEXT,
      active INTEGER NOT NULL DEFAULT 1
    );''')
    for r in bseed.get('rules',[]):
        cur.execute('''INSERT OR REPLACE INTO bim_mapping_rule
          (rule_id,revit_category,work_item_id,priority,family_pattern,type_pattern,system_code,system_name,match_parameter_names,match_parameter_values,active)
          values (?,?,?,?,?,?,?,?,?,?,?)''',(
           r['ruleId'],r['revitCategory'],r['workItemId'],r.get('priority',100),
           r.get('familyPattern'),r.get('typePattern'),r.get('systemCode'),r.get('systemName'),
           r.get('matchParameterNames'),r.get('matchParameterValues'),1))
    con.commit()
    print('bim_mapping_rules:',cur.execute('select count(*) from bim_mapping_rule').fetchone()[0])

# 14. N4 formwork readonly — XD-FORM quantity from QS_FormworkArea (additive; keep XD-CONC rows)
for cat, note in [
    ('OST_StructuralColumns', 'N4 formwork takeoff area (column)'),
    ('OST_Walls', 'N4 formwork takeoff area (wall)'),
    ('OST_Floors', 'N4 formwork takeoff area (slab)'),
    ('OST_StructuralFraming', 'N4 formwork takeoff area (beam)'),
    ('OST_StructuralFoundation', 'N4 formwork takeoff area (foundation)'),
]:
    exists = cur.execute(
        "select 1 from bim_property_map_final where revit_category=? and default_work_item=?",
        (cat, 'XD-FORM')).fetchone()
    if not exists:
        cur.execute(
            'insert into bim_property_map_final values (?,?,?,?,?)',
            (cat, 'XD-FORM', 'QS_FormworkArea',
             'FormworkArea|FormworkSystem|FormworkMaterial|ElementType', note))
con.commit()
print('bim_property_map_final:', cur.execute('select count(*) from bim_property_map_final').fetchone()[0])
con.close()
