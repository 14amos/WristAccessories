# scan_pieces.py: every hands outfit's skin mesh split into connected pieces (welded by position); per piece: side, dominant
# bone, how far it wraps round the forearm axis (or its finger), axial span, material -> candidates for baked accessories
# (bracelets / bands at the wrist, rings on fingers). Input: tattoo/acc/all_scan_hands.json (paths). Output: pieces.json
import UnityPy, os, sys, json, numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
scan = json.load(open("WORK/tattoo/acc/all_scan_hands.json"))
CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
only = os.environ.get("ONLY", "").split(",") if os.environ.get("ONLY") else None
out = {}
for n_, (hid, v) in enumerate(sorted(scan.items())):
    if only and hid not in only: continue
    try:
        env = UnityPy.load(v["path"]); objs = {o.path_id: o for o in env.objects}; cands = []
        def deref(owner, pp):
            if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
            cab = os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower(); f = CABS.get(cab)
            if f is None: return None
            if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
            return DEP[f].get(pp["m_PathID"])
        for o in env.objects:
            if o.type.name != "MonoBehaviour": continue
            t = o.read_typetree()
            if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
            r = deref(o, t["_skinnedMeshRenderer"]); mo = deref(r, r.read_typetree()["m_Mesh"]) if r else None
            if mo is None: continue
            nm = mo.peek_name().lower()
            if "lod" in nm and "lod0" not in nm: continue
            mn = [(deref(r, m_).peek_name() if deref(r, m_) is not None else "?") for m_ in r.read_typetree()["m_Materials"]]
            cands.append((mo.read_typetree()["m_VertexData"]["m_VertexCount"], t["_bonePaths"], mo, mn))
        if not cands: raise RuntimeError("no skin")
        res = []
        for _, paths, mo, mnames in cands:
            mesh = mo.read(); mt = mo.read_typetree(); names = [p.split("/")[-1] for p in paths]
            h = MeshHandler(mesh); h.process()
            P = np.array(h.m_Vertices); bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights)
            if bw.ndim != 2 or len(bw) != len(P): res.append(dict(mesh=mo.peek_name(), verts=len(P), unskinned=True, mats=mnames)); continue
            subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
            T = np.vstack(subs); tsub = np.concatenate([np.full(len(s), i) for i, s in enumerate(subs)])
            key = np.round(P / 1e-5).astype(np.int64); _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.ravel()
            e = np.r_[T[:, [0, 1]], T[:, [1, 2]]]; e = inv[e]
            g = coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(inv.max() + 1,) * 2)
            lab = connected_components(g, directed=False)[1][inv]
            dom = bi[np.arange(len(bi)), bw.argmax(1)]; dname = np.array([names[d] if d < len(names) else "?" for d in dom])
            def bind(n):
                bp = mt["m_BindPose"][names.index(n)]; return np.array([[bp[f"e{r}{c}"] for c in range(4)] for r in range(4)])
            frames = {}
            for S in "LR":
                try:
                    M3 = bind(f"Base Human{S}Forearm3"); org = lambda n: (M3 @ np.linalg.inv(bind(n)))[:3, 3]
                    w = org(f"Base Human{S}Palm"); f3 = org(f"Base Human{S}Forearm3"); ax = (w - f3) / np.linalg.norm(w - f3)
                    frames[S] = (M3, w, ax)
                except ValueError: pass
            for p in np.unique(lab):
                sel = lab == p; nv = int(sel.sum())
                bones, cnt = np.unique(dname[sel], return_counts=True); top = bones[cnt.argmax()]
                S = "L" if "HumanL" in top else ("R" if "HumanR" in top else "?")
                mats = sorted({mnames[s] if s < len(mnames) else "?" for s in np.unique(tsub[np.isin(T, np.where(sel)[0]).any(1)])})
                rec = dict(mesh=mo.peek_name(), piece=int(p), verts=nv, bone=top.replace("Base Human", ""), side=S, mats=mats)
                if S in frames:
                    M3, w, ax = frames[S]; Q = (M3 @ np.c_[P[sel], np.ones(nv)].T).T[:, :3] - w
                    al = Q @ ax; perp = Q - np.outer(al, ax); e1 = np.cross(ax, [0, 0, 1.0]); e1 /= np.linalg.norm(e1); e2 = np.cross(ax, e1)
                    ang = np.arctan2(perp @ e2, perp @ e1); bins = np.unique(np.floor((ang + np.pi) / (2 * np.pi) * 36).astype(int) % 36); cov = len(bins) / 36
                    rec.update(along_mm=[round(float(al.min()) * 1000), round(float(al.max()) * 1000)], wrap=round(cov, 2),
                               r_mm=round(float(np.median(np.linalg.norm(perp, axis=1))) * 1000, 1), bins=int(sum(1 << int(x) for x in bins)))
                res.append(rec)
        out[hid] = dict(name=v["name"], pieces=res)
    except Exception as ex:
        out[hid] = dict(name=v["name"], error=str(ex)[:120]); print("FAIL", v["name"], str(ex)[:80], flush=True)
    if n_ % 50 == 0: print(n_, flush=True)
OUTF = "WORK/wristacc/tools/pieces.json"
if only and os.path.exists(OUTF): old = json.load(open(OUTF)); old.update(out); out = old
json.dump(out, open(OUTF, "w"), indent=0)
print("done", len(out))
