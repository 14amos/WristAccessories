# ring_scan.py: RINGS on every first-person hands outfit (user 2026-10-27: "scan all the hands we added for rings"; the 0.9 hand
# scan welded pieces by position, so a ring modelled into the hand mesh stayed inside the hand and no real ring was found).
# Per outfit (all_scan_hands.json: game, WTT-ContentBackport, ISB-Aishi, WTT-Artem): the skin mesh, its texture islands
# (index-connected triangles). A ring candidate = an island mostly on ONE finger (its 3 Digit bones, >= 80% of verts), >= 12
# triangles, that goes round that finger (>= 240 of 360 deg about the finger segment's axis) and is short along it (<= 14 mm).
# Out: lab/ringscan/report.json (+ printed list); geometry dedupe by rounded positions + material.
import os, sys, json, hashlib, collections; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, multiprocessing as mp, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
OUT = "WORK/wristacc/lab/ringscan"; os.makedirs(OUT, exist_ok=True)
SCAN = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
def islands(T, nv):
    par = np.arange(nv)
    def f(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for a, b, c in T: ra, rb, rc = f(a), f(b), f(c); par[rb] = ra; par[f(rc)] = ra
    return np.array([f(a) for a in T[:, 0]])
def one(hid):
    rec = SCAN[hid]; out = []
    try:
        env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
        for o in env.objects:
            if o.type.name != "MonoBehaviour": continue
            t = o.read_typetree()
            if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
            r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
            mo = deref(r, rt["m_Mesh"], objs) if r else None
            if mo is None: continue
            nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
            if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt)
        if best is None: return hid, out, "no skin"
        _, paths, mo, r, rt = best; mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
        names = [p.split("/")[-1] for p in paths]; P = np.array(h.m_Vertices, np.float64)
        bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float64); sm = bw.sum(1, keepdims=True); bw = np.where(sm > 1.5, bw / np.maximum(sm, 1e-9), bw)
        top = bi[np.arange(len(bi)), bw.argmax(1)]
        bind = lambda i: np.array([[mt["m_BindPose"][i][f"e{a}{b}"] for b in range(4)] for a in range(4)])
        org = lambda n: np.linalg.inv(bind(names.index(n)))[:3, 3] if n in names else None
        subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
        mats = [deref(r, m_, objs) for m_ in rt["m_Materials"]]; mn = [m.peek_name() if m is not None else "?" for m in mats]
        T = np.concatenate(subs); tsub = np.concatenate([np.full(len(s), i) for i, s in enumerate(subs)]); comp = islands(T, len(P))
        for c in np.unique(comp):
            sel = comp == c; v = np.unique(T[sel])
            if sel.sum() < 12: continue
            fing = collections.Counter()
            for x in top[v]:
                n = names[x]
                if "Digit" in n: fing[n[:-1]] += 1                                        # "Base HumanLDigit4" (finger, any segment)
            if not fing: continue
            fname, cnt = fing.most_common(1)[0]
            if cnt < 0.8 * len(v): continue
            # the segment the island sits on: the dominant bone; axis = that bone's origin -> the next segment's (or its own +x)
            seg = collections.Counter(names[x] for x in top[v]).most_common(1)[0][0]
            o0 = org(seg); nxt = seg[:-1] + str(int(seg[-1]) + 1); o1 = org(nxt)
            if o0 is None: continue
            ax = (o1 - o0) if o1 is not None else (o0 - org(seg[:-1] + str(int(seg[-1]) - 1)))
            ax /= np.linalg.norm(ax)
            Q = P[v] - o0; along = Q @ ax; rad = Q - np.outer(along, ax)
            e1 = rad.mean(0) * 0 + np.cross(ax, [0, 0, 1.0]); e1 = e1 / (np.linalg.norm(e1) + 1e-9) if np.linalg.norm(e1) > 1e-6 else np.cross(ax, [0, 1.0, 0]); e2 = np.cross(ax, e1)
            ang = np.degrees(np.arctan2(rad @ e2, rad @ e1)) % 360
            cover = len(np.unique((ang // 15).astype(int))) * 15
            length = float(np.ptp(along)) * 1000; ext = float(np.ptp(P[v], 0).max()) * 1000
            if cover < 240 or length > 14: continue
            sub = int(collections.Counter(tsub[sel]).most_common(1)[0][0])
            key = hashlib.md5(np.round(P[v] / 0.0003).astype(np.int32).tobytes() + mn[sub].encode()).hexdigest()[:10]
            out.append(dict(hid=hid, outfit=rec["name"], mesh=mesh.m_Name, finger=fname.replace("Base Human", ""), seg=seg.replace("Base Human", ""),
                            tris=int(sel.sum()), verts=int(len(v)), cover=cover, length_mm=round(length, 1), ext_mm=round(ext, 1), mat=mn[sub], key=key, island=int(c)))
        return hid, out, "ok"
    except Exception as e: return hid, out, "ERR " + repr(e)[:100]
if __name__ == "__main__":
    with mp.Pool(8) as pool: res = pool.map(one, list(SCAN), chunksize=2)
    found = [x for _, o, _ in res for x in o]; errs = [(SCAN[h]["name"], n) for h, _, n in res if n != "ok"]
    uniq = {}
    for x in found: uniq.setdefault(x["key"], dict(x, outfits=[]))["outfits"].append(x["outfit"])
    json.dump(dict(rings=list(uniq.values()), errors=errs), open(OUT + "/report.json", "w"), indent=1)
    print(len(res), "hands outfits,", len(found), "ring islands,", len(uniq), "distinct;", len(errs), "errors")
    for x in sorted(uniq.values(), key=lambda x: x["outfit"]):
        print(f"  {x['outfit'][:44]:44} {x['finger']:8} {x['seg']:9} {x['tris']:4} tris  round {x['cover']:3}  long {x['length_mm']:5} mm  across {x['ext_mm']:5} mm  {x['mat'][:28]}  (+{len(x['outfits']) - 1})")
