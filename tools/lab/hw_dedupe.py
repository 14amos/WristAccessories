# hw_dedupe.py [--apply]: hand items (hw_*.json kind "hand": gloves, other outfits' hands) that look the SAME (user 2026-10-27:
# "make sure there are no dupes in the gloves"): same geometry (rounded positions) + same texture pixels (each submesh's _MainTex
# from the source bundle, md5 of the decoded image) + same side. Keeps one per group: main mod first, then ISB, then Artem; the
# others are listed (and with --apply moved to BACKUPS/wristacc-hw-dupes with their icons).
import os, sys, json, glob, base64, hashlib, shutil, collections; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy
DIRS = [("main", "WORK/wristacc/acc"), ("ISB", "WORK/wristacc/addons/WristAccessories-ISB/acc"),
        ("Artem", "BACKUPS/wristacc-artem-removed-2026-10-27/source/WristAccessories-Artem/acc")]
SCAN = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
PATH = {}
for k, v in SCAN.items():
    p = v["path"].replace("\\", "/"); i = p.lower().find("assets/content/"); j = p.lower().find("/bundles/")
    PATH[p[i:] if i >= 0 else (p[j + 9:] if j >= 0 else p)] = v["path"]
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
TEXH = {}
def tex_hash(bundle_key, mat):
    k = (bundle_key, mat)
    if k in TEXH: return TEXH[k]
    h = "?"
    try:
        env = UnityPy.load(PATH[bundle_key]); objs = {o.path_id: o for o in env.objects}
        for o in env.objects:
            if o.type.name == "Material" and o.peek_name() == mat:
                t = o.read_typetree(); te = dict((kk, v) for kk, v in t["m_SavedProperties"]["m_TexEnvs"]).get("_MainTex")
                to = deref(o, te["m_Texture"], objs) if te else None
                if to is not None: im = to.read().image.convert("RGB"); im.thumbnail((256, 256)); h = hashlib.md5(np.asarray(im).tobytes()).hexdigest()[:12]
                break
    except Exception as e: h = "ERR"
    TEXH[k] = h; return h
rows = []
for tag, d in DIRS:
    for f in sorted(glob.glob(d + "/hw_*.json")):
        j = json.load(open(f))
        if j["kind"] != "hand": continue
        P = np.frombuffer(base64.b64decode(j["pos"]), "<f4").reshape(-1, 3)
        geo = hashlib.md5(np.round(P / 0.001).astype(np.int32).tobytes()).hexdigest()[:12]
        tex = "+".join(tex_hash(j["bundle"], s["mat"]) for s in j["subs"])
        rows.append(dict(tag=tag, file=f, name=j["name"], note=j["note"], side=j["side"], key=(geo, tex, j["side"])))
groups = collections.defaultdict(list)
for r in rows: groups[r["key"]].append(r)
order = {"main": 0, "ISB": 1, "Artem": 2}; drop = []
for g in groups.values():
    if len(g) < 2: continue
    g.sort(key=lambda r: (order[r["tag"]], r["note"])); keep = g[0]
    for r in g[1:]: drop.append((r, keep))
print(len(rows), "hand items,", len(groups), "distinct looks;", len(drop), "duplicates:")
for r, k in drop: print(f"  {r['tag']:5} {r['name']:24} {r['note'][:40]:40} = {k['tag']} {k['name']} ({k['note'][:30]})")
if "--apply" in sys.argv:
    B = "BACKUPS/wristacc-hw-dupes"; os.makedirs(B, exist_ok=True)
    for r, _ in drop:
        shutil.move(r["file"], B); ic = os.path.join(os.path.dirname(r["file"]), "icons", r["name"] + ".png")
        if os.path.exists(ic): shutil.move(ic, B)
    print("moved", len(drop), "to", B)
