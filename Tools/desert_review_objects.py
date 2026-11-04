"""Desert review, per-object realism table (Docs/DESERT_REVIEW.md sections 12-13).

Reads Docs/REALISM_AUDIT_DesertOutpost.csv (written by FPSKitBatch.AuditRealism), maps each failure to a fault code and
a fix, and finds the generator line that defines each Town_ material. Re-run after every audit:
    python3 Tools/desert_review_objects.py > /tmp/objects.md
then replace sections 12-13 of Docs/DESERT_REVIEW.md with its output.
"""
import csv, re, subprocess, collections, sys
import os
ROOT=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
rows=list(csv.DictReader(open(ROOT+'/Docs/REALISM_AUDIT_DesertOutpost.csv')))

# where each generated material is defined
defs={}
def find_def(mat):
    if mat in defs: return defs[mat]
    import glob
    files=sorted(glob.glob(ROOT+'/Assets/Scripts/Editor/FPSKitTown*.cs'))+sorted(glob.glob(ROOT+'/Assets/Scripts/Editor/FPSKitDesert*.cs'))
    out=[]
    for f in files:
        for i,line in enumerate(open(f),1):
            if '"%s"'%mat in line: out.append('%s:%d:%s'%(f,i,line))
        if out: break
    out=[o for o in out if o and ('Mat(' in o or 'Flat(' in o or 'Material' in o or 'Weather' in o)] or [o for o in out if o]
    loc=''
    if out:
        f,l=out[0].split(':')[0:2]
        loc='`%s:%s`'%(f.replace(ROOT+'/',''),l)
    defs[mat]=loc; return loc

GROUPFILE={'Buildings':'FPSKitTownBuildings / Facade / Walls / Interior / Furniture / Shopfront','Edge':'FPSKitTownEdge','Landmarks':'FPSKitTownLandmarks',
 'Landmarks/LandmarkProps':'FPSKitTownLandmarks (props) + FPSKitTownAssets','Life':'FPSKitTownLife','Street':'FPSKitTownStreet','Roads':'FPSKitTownRoads',
 'Terrain':'FPSKitTownGround (terrain layers)','Apron':'FPSKitTownGround','Mountains':'FPSKitTownGround (BuildTownMountains)','Sky':'LevelTheme sky (FPSKitThemes)',
 'ShopCover':'FPSKitTownCover','Player':'weapon pack / ViewModelRig'}

# fault -> (code, fix)
RULES=[
 (r'edges perfectly sharp', 'EDGE', 'Bevel the hard edges: 1-3 cm chamfer on near geometry (add a chamfered box/extrude helper to FPSKitTownMesh and use it for frames, sills, kerbs, steps, slabs); far geometry: rounded-edge normals in the shader (bevel normal trick) instead of real polygons; imported models: Blender Bevel modifier (2 segments, 1-2 cm) + Weighted Normals, then re-export.'),
 (r'flat single colour', 'FLAT', 'Give it a real surface: a CC0 PBR set (ambientCG / Poly Haven) with albedo + normal + roughness/AO at world-metre UVs, tinted so the mean albedo matches the real material; no flat Color-only materials.'),
 (r'flat colour blocks|palette texture', 'PALETTE', 'Replace the palette (colour-swatch) texture with a real PBR texture set; for guns this is the planned weapon-pack purchase.'),
 (r'plain boxes', 'BOX', 'Replace plain boxes with profiled shapes: panelled doors, moulded sills and cornices, beam ceilings, rounded slab edges, real prop meshes. Keep plain boxes only as shadow/collision stand-ins.'),
 (r'repeats .* times across a face', 'TILE', 'Break the tiling: shared anti-tiling shader (stochastic/hex tiling or two-scale blend), macro variation noise at 8-20 m, grime/dirt toward the ground by vertex colour or height, a detail normal map.'),
 (r'albedo down to', 'DARK', 'Albedo too dark (below URP 0.01): raise the darkest texels / tint so p2 >= 0.02 and the mean sits inside the real range (tint = wanted colour / image mean).'),
 (r'albedo up to', 'BRIGHT', 'Albedo too bright (above URP 0.90): cap the texture/tint (albedoCap) so p98 <= 0.85.'),
 (r'albedo [\d\.]+ outside real', 'ALB', 'Albedo outside the real range for its kind: retint to the middle of the range given in the reason.'),
 (r'smoothness [\d\.]+ outside real', 'SMOOTH', 'Smoothness outside the real range: set it inside the range given (matte plaster/stone/sand 0.05-0.2; painted metal 0.3-0.5).'),
 (r'metallic [\d\.]+ on painted', 'METAL', 'Painted metal must be metallic 0 (the paint is a dielectric).'),
 (r'named as bare metal but metallic', 'METALQ', 'Decide: bare metal -> metallic 1, albedo 0.5-0.9; painted -> rename the material to say painted and set metallic 0.'),
 (r'smooth \([\d\.]+\) with no occlusion', 'AO', 'Smooth without an occlusion map: add an AO map (bake in Blender or use the pack AO) or lower smoothness; otherwise crevices glow.'),
 (r'normal map too weak', 'NRM', 'Normal map too weak: raise normal strength (bumpScale 1-2) or use the texture set\'s real normal map; generated grayscale normals need _NORMALMAP on.'),
 (r'no normal map on a near surface', 'NONRM', 'Add a normal map (it is seen up close).'),
 (r'texels per metre', 'TEXEL', 'Too few texels per metre for how close it is seen: use a 2K map or tile finer (keep >= 256 px/m near, 512 for things within 1 m).'),
 (r'low-poly for a gun', 'GUNPOLY', 'Held gun too low-poly: replace with the planned realistic weapon pack (20-60k tris, 2-4K PBR, separate mag/bolt/trigger), keeping ViewModelRig grip points and reload beats.'),
 (r'low-poly', 'POLY', 'Too few triangles for how close it is seen: higher-detail model or LOD0 from the source pack.'),
 (r'Unity primitive', 'PRIM', 'Unity primitive: replace with a modelled mesh (tyre with tread and sidewall, rim with spokes) or the source pack\'s own part.'),
 (r'not recognised from its name', 'NAME', 'The audit could not tell what it is: rename the material with its kind (e.g. _Plaster, _Fabric, _PaintedMetal) or add it to the audit name map, then re-run the audit so its albedo/smoothness are checked.'),
 (r'black hole', 'GLASS', 'Glass draws as a black hole: real glass (Docs/DESERT_GLASS.md): transparent, Fresnel reflection, dust, no shadow, and real space behind it (the real room, or a window room built behind it).'),
 (r'raking light', 'RAKE', 'Flat under raking light: add a subtle normal/height detail so low sun shows relief.'),
 (r'deeper than any real', 'SAT', 'Colour too saturated/deep for the material: desaturate so the weakest channel is >= 5 % of the strongest.'),
 (r'overlay over walls', 'OVER', 'An overlay (dark weathering strips): planned for removal; replace with the anti-tiling shader\'s grime instead.'),
 (r'panorama', 'SKY', 'Sky panorama is soft (11 px/deg vs 18 on 1080p): use an 8K HDRI or add a cloud layer/procedural sky detail.'),
 (r'URP.s albedo validation flags', 'VALID', 'URP albedo validation flags it: bring texels into 0.01-0.90.'),
]
CODE_ORDER=[r[1] for r in RULES]

def classify(text):
    out=[]
    for pat,code,fix in RULES:
        if re.search(pat,text): out.append(code)
    return out

FIX={code:fix for _,code,fix in RULES}
counts=collections.Counter(); screen=collections.Counter()
lines=[]
for r in rows:
    codes=[]; mats=collections.OrderedDict()
    for p in r['reasons'].split(' ; '):
        p=p.strip()
        if not p: continue
        m=re.match(r'\[(\w[\w ]*)\] [^:]+: ([^:]+?) \((\d+) %\): (.*)',p)
        if m:  # sub-material reason
            c=classify(m.group(4)); mats.setdefault(m.group(2),set()).update(c); codes+=c
        else:
            codes+=classify(p)
    codes=sorted(set(codes),key=CODE_ORDER.index)
    for c in codes: counts[c]+=1; screen[c]+=float(r['screen_pct'] or 0)
    obj=r['object']
    mat=obj.split(': ',1)[1] if ': ' in obj and obj.split(': ',1)[1].startswith('Town_') else ''
    where=GROUPFILE.get(r['group'],r['group'])
    if mat:
        d=find_def(mat[5:])
        if d: where=d
    elif r['source'] in ('imported model','generated prefab','imported character'):
        where='FPSKitTownAssets (material remap) / source asset'
    sub='; '.join('%s: %s'%(k,','.join(sorted(v,key=CODE_ORDER.index))) for k,v in mats.items() if v)
    lines.append((int(r['rank']),obj,r['group'],r['kind'],r['shape'],r['surface'],r['light'],r['overall'],float(r['screen_pct'] or 0),r['nearest_m'],','.join(codes),sub,where))

def md(s): return str(s).replace('|','\\|')
print('| Code | Objects | Combined screen share | Fix |\n|---|---|---|---|')
for c in CODE_ORDER:
    if counts[c]: print('| **%s** | %d | %.1f %% | %s |'%(c,counts[c],screen[c],FIX[c]))
print('\n## 13. Realism: every object\n')
print('Sorted by the audit rank (most seen first). Verdicts are Shape / Surface / Light / Overall. "Where" is the generator file and line that defines the material (generated surfaces), else the builder or asset step. Sub-materials of imported models are listed with their own codes.\n')
print('| # | Object | Group | Kind | Shape | Surface | Light | Overall | Screen % | Nearest m | Faults | Sub-materials | Where to fix |\n|---|---|---|---|---|---|---|---|---|---|---|---|---|')
for L in sorted(lines):
    print('| %d | %s | %s | %s | %s | %s | %s | **%s** | %.2f | %s | %s | %s | %s |'%(L[0],md(L[1]),md(L[2]),md(L[3]),L[4],L[5],L[6],L[7],L[8],L[9],L[10] or '-',md(L[11]) or '-',L[12]))
