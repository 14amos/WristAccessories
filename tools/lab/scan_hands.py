# scan_hands.py: every first-person hands outfit (all_scan_hands.json) -> what it wears ON THE HAND (user 2026-10-26: rings on
# other clothing, the USEC Deadlift finger wraps, gloves): the hand's geometry = triangles whose vertices are mostly weighted to
# the Palm / Digit bones; split into pieces (welded by position); per side:
#   main = the biggest piece (the hand itself): GLOVE when its texture is not skin-coloured, else bare (tattoos etc.);
#   other pieces >= 12 triangles: RING when on one finger bone and <= 30 mm across, WRAP (tape, finger wraps) when not skin-
#   coloured, else other. Same mesh in several outfits (texture variants) = one geometry, all names listed.
# Out: lab/handscan/report.json + summary.txt (and OUT/handscan_summary.txt)
import os, json, ast, hashlib, collections, numpy as np, UnityPy, colorsys
from UnityPy.helpers.MeshHelper import MeshHandler
OUT = "WORK/wristacc/lab/handscan"; os.makedirs(OUT, exist_ok=True)
scan = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
def skin_like(rgb):
    h, s, v = colorsys.rgb_to_hsv(*rgb); h *= 360
    return (h < 50 or h > 340) and 0.12 < s < 0.7 and v > 0.22
geos = {}; rows = []
for hid, rec in scan.items():
    try:
        env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; cands = []
        for o in env.objects:
            if o.type.name != "MonoBehaviour": continue
            t = o.read_typetree()
            if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
            r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
            mo = deref(r, rt["m_Mesh"], objs) if r else None
            if mo is None: continue
            nm = mo.peek_name().lower()
            score = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm) * 1
            cands.append((score, t["_bonePaths"], mo, r, rt))
        if not cands: rows.append(dict(hid=hid, name=rec["name"], err="no skin")); continue
        cands.sort(key=lambda c: -c[0]); paths, mo, r, rt = cands[0][1], cands[0][2], cands[0][3], cands[0][4]
        mesh = mo.read(); h = MeshHandler(mesh); h.process(); P = np.array(h.m_Vertices, np.float64)
        geo = hashlib.md5(P.astype(np.float32).tobytes()).hexdigest()[:12]
        if geo in geos: geos[geo]["outfits"].append(rec["name"]); continue
        UV = np.array(h.m_UV0, np.float64)[:, :2]; bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float64)
        names = [p.split("/")[-1] for p in paths]
        handb = [i for i, n in enumerate(names) if "Palm" in n or "Digit" in n]
        hw = np.where(np.isin(bi, handb), bw, 0).sum(1)
        top = bi[np.arange(len(bi)), bw.argmax(1)]
        side = np.array(["L" if "HumanL" in names[t] else "R" if "HumanR" in names[t] else "?" for t in top])
        mats = [deref(r, m_, objs) for m_ in rt["m_Materials"]]
        subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
        # the textures, per submesh (its _MainTex)
        texs = []
        for mo_ in mats:
            img = None
            try:
                m = mo_.read(); te = dict((k, v.m_Texture) for k, v in m.m_SavedProperties.m_TexEnvs).get("_MainTex")
                to = deref(mo_, {"m_FileID": te.m_FileID, "m_PathID": te.m_PathID}, objs) if te is not None and te.m_PathID else None
                if to is not None: im = to.read().image.convert("RGB"); im.thumbnail((256, 256)); img = np.asarray(im).astype(float) / 255
                mn = m.m_Name
            except Exception: mn = "?"
            texs.append((mn, img))
        tris = []; tsub = []
        for i, s in enumerate(subs):
            keep = s[hw[s].mean(1) > 0.5]; tris.append(keep); tsub += [i] * len(keep)
        T = np.concatenate(tris) if tris else np.zeros((0, 3), int); tsub = np.array(tsub)
        # pieces: welded by position
        key = np.round(P / 0.0002).astype(np.int64); _, wid = np.unique(key, axis=0, return_inverse=True); wid = wid.ravel()
        par = np.arange(wid.max() + 1)
        def f(x):
            while par[x] != x: par[x] = par[par[x]]; x = par[x]
            return x
        for t_ in T:
            a, b, c = f(wid[t_[0]]), f(wid[t_[1]]), f(wid[t_[2]]); par[b] = a; par[f(c)] = a
        comp = np.array([f(wid[t_[0]]) for t_ in T]) if len(T) else np.zeros(0, int)
        pieces = []
        for c in np.unique(comp):
            sel = comp == c; tt = T[sel]; v = np.unique(tt)
            sd = collections.Counter(side[v]).most_common(1)[0][0]
            bones = collections.Counter(names[top[x]] for x in v); bone, bn = bones.most_common(1)[0]
            ext = float(np.ptp(P[v], 0).max())
            sub = int(collections.Counter(tsub[sel]).most_common(1)[0][0]); mn, img = texs[sub] if sub < len(texs) else ("?", None)
            col = None
            if img is not None:
                uv = UV[v] % 1.0; px = img[((1 - uv[:, 1]) * (img.shape[0] - 1)).astype(int), (uv[:, 0] * (img.shape[1] - 1)).astype(int)]
                col = np.median(px, 0).tolist()
            pieces.append(dict(side=sd, tris=int(sel.sum()), verts=int(len(v)), bone=bone, bone_share=round(bn / len(v), 2), ext_mm=round(ext * 1000, 1), mat=mn, col=col, skin=bool(col is not None and skin_like(col))))
        out = dict(geo=geo, hid=hid, mesh=mesh.m_Name, path=rec["path"], outfits=[rec["name"]], sides={})
        for sd in ("L", "R"):
            ps = sorted([p for p in pieces if p["side"] == sd], key=lambda p: -p["tris"])
            if not ps: continue
            main = ps[0]; extras = []
            for p in ps[1:]:
                if p["tris"] < 12: continue
                kind = "ring" if ("Digit" in p["bone"] and p["bone_share"] >= 0.8 and p["ext_mm"] <= 30) else "wrap" if not p["skin"] else "other"
                extras.append(dict(p, kind=kind))
            out["sides"][sd] = dict(hand="glove" if not main["skin"] else "bare", main=main, extras=extras)
        geos[geo] = out
        print(f"{hid} {mesh.m_Name[:36]:36} " + " | ".join(f"{sd}: {v['hand']} +{len(v['extras'])} ({', '.join(e['kind'] + '@' + e['bone'].replace('Base Human', '') for e in v['extras'])})" for sd, v in out["sides"].items()), flush=True)
    except Exception as e:
        rows.append(dict(hid=hid, name=rec["name"], err=repr(e)[:120])); print("FAIL", hid, repr(e)[:100], flush=True)
json.dump(dict(geos=geos, fails=rows), open(OUT + "/report.json", "w"), indent=1)
# summary
lines = [f"Hand scan: {len(geos)} hand geometries ({sum(len(g['outfits']) for g in geos.values())} outfits), {len(rows)} failed", ""]
for kind in ("glove", "ring", "wrap", "other"):
    lines.append(f"== {kind.upper()}")
    for g in geos.values():
        for sd, v in g["sides"].items():
            hits = [v["main"]] if kind == "glove" and v["hand"] == "glove" else [e for e in v["extras"] if e["kind"] == kind]
            for e in hits:
                lines.append(f"  {sd} {e['bone'].replace('Base Human', ''):12} {e['tris']:5} tris {e['ext_mm']:6.1f} mm  mat {e['mat'][:30]:30} {g['mesh'][:34]:34} = {g['outfits'][0]}{' (+' + str(len(g['outfits']) - 1) + ')' if len(g['outfits']) > 1 else ''}")
    lines.append("")
open(OUT + "/summary.txt", "w", encoding="utf-8").write("\n".join(lines)); open("OUT/handscan_summary.txt", "w", encoding="utf-8").write("\n".join(lines))
print("\n".join(lines[:4]))
