# feet_pieces.py [n]: welded pieces (by position) of the first n feet outfits' LOD0 meshes: tris, foot / calf share, height range
import sys, os, json, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
jobs = json.load(open("WORK/wristacc/lab/feetscan/jobs.json", encoding="utf-8")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
sel = sys.argv[2:] or list(jobs)[:int(sys.argv[1])]
for fid in sel:
    rec = jobs[fid]; env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        sc = ("lod0" in mo.peek_name().lower()) * 4
        if best is None or sc > best[0]: best = (sc, t, mo)
    _, t, mo = best; mesh = mo.read(); h = MeshHandler(mesh); h.process(); names = [p.split("/")[-1] for p in t["_bonePaths"]]
    P = np.array(h.m_Vertices, np.float64); bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float64)
    fb = [i for i, n in enumerate(names) if "Foot" in n or "Toe" in n]; cb = [i for i, n in enumerate(names) if "Calf" in n]
    fw = np.where(np.isin(bi, fb), bw, 0).sum(1); cw = np.where(np.isin(bi, cb), bw, 0).sum(1)
    T = np.concatenate([np.array(s).reshape(-1, 3) for s in h.get_triangles()])
    key = np.round(P / 0.0002).astype(np.int64); _, wid = np.unique(key, axis=0, return_inverse=True); wid = wid.ravel(); par = np.arange(wid.max() + 1)
    def f(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for a, b, c in T: ra, rb, rc = f(wid[a]), f(wid[b]), f(wid[c]); par[rb] = ra; par[f(rc)] = ra
    comp = np.array([f(wid[a]) for a in T[:, 0]])
    up = int(np.argmax(np.ptp(P, 0)))
    print(f"== {rec['name']} {mesh.m_Name} up axis {up} range {P[:, up].min():.2f}..{P[:, up].max():.2f}")
    rows = []
    for c in np.unique(comp):
        v = np.unique(T[comp == c]); rows.append(((comp == c).sum(), fw[v].mean(), cw[v].mean(), P[v, up].min(), P[v, up].max(), P[v, 0].mean()))
    for n, a, b, lo, hi, x in sorted(rows, key=lambda r: -r[0])[:12]: print(f"  {n:5} tris foot {a:.2f} calf {b:.2f} h {lo:+.3f}..{hi:+.3f} x {x:+.2f}")
