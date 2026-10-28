# ring_export.py <out dir> [min mm]: ring_scan.py's candidates (wider than min, default 14 mm) as handwear pieces (hw_ring_*.json,
# kind "ring", the format export_handwear.py writes): the island's triangles as a skinned mesh in its source mesh space, bones by
# name + bind poses, its material by bundle + mesh name. Also writes ringscan/picks.json for render_hw.py.
import os, sys, json, base64, collections; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
import ring_scan as RS
OUT = sys.argv[1]; MIN = float(sys.argv[2]) if len(sys.argv) > 2 else 14; os.makedirs(OUT, exist_ok=True)
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
def bundle_key(path):
    p = path.replace("\\", "/"); i = p.lower().find("assets/content/")
    if i >= 0: return p[i:]
    j = p.lower().find("/bundles/"); return p[j + 9:] if j >= 0 else p
rings = [x for x in json.load(open(RS.OUT + "/report.json"))["rings"] if x["ext_mm"] >= MIN]; picks = []
for x in rings:
    rec = RS.SCAN[x["hid"]]; env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = RS.deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = RS.deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
        if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt)
    _, paths, mo, r, rt = best; mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
    P = np.array(h.m_Vertices, np.float64); N = np.array(h.m_Normals, np.float64)[:, :3]; UV = np.array(h.m_UV0, np.float64)[:, :2]
    Tg = np.array(h.m_Tangents, np.float64) if h.m_Tangents is not None and len(h.m_Tangents) else np.tile([1, 0, 0, 1.0], (len(P), 1))
    bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float64); sm = bw.sum(1, keepdims=True); bw = np.where(sm > 1.5, bw / np.maximum(sm, 1e-9), bw)
    names = [p.split("/")[-1] for p in paths]; subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
    T = np.concatenate(subs); tsub = np.concatenate([np.full(len(s), i) for i, s in enumerate(subs)]); comp = RS.islands(T, len(P))
    sel = comp == x["island"]; keep = [T[sel & (tsub == i)] for i in range(len(subs))]
    used = np.unique(T[sel]); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
    ub = np.unique(bi[used][bw[used] > 0]); bmap = {int(b): i for i, b in enumerate(ub)}
    bi2 = np.vectorize(lambda q: bmap.get(int(q), 0))(bi[used]); bw2 = bw[used] * np.isin(bi[used], ub)
    bind = [[mt["m_BindPose"][int(b)][f"e{a}{c}"] for a in range(4) for c in range(4)] for b in ub]
    mats = [RS.deref(r, m_, objs) for m_ in rt["m_Materials"]]; mn = [m.peek_name() if m is not None else "?" for m in mats]
    tris = [(i, remap[k]) for i, k in enumerate(keep) if len(k)]
    side = x["finger"][0]; name = f"ring_{x['key']}_{side}"; fn = {"1": "thumb", "2": "index finger", "3": "middle finger", "4": "ring finger", "5": "little finger"}[x["finger"][-1]]
    out = dict(name=name, note=f"{x['outfits'][0]}: ring ({'left' if side == 'L' else 'right'} {fn})", kind="ring", side=side, bundle=bundle_key(rec["path"]), mesh=mesh.m_Name,
               bones=[names[int(b)] for b in ub], bind=bind, n=int(len(used)), pos=b64(P[used], "<f4"), nrm=b64(N[used], "<f4"), tan=b64(Tg[used], "<f4"), uv=b64(UV[used], "<f4"),
               bi=b64(bi2, "<i4"), bw=b64(bw2, "<f4"), subs=[dict(mat=mn[i] if i < len(mn) else "?", sub=int(i), tris=b64(t.ravel(), "<i4")) for i, t in tris],
               finger=x["finger"], src=dict(hid=x["hid"], seg=x["seg"], ext_mm=x["ext_mm"], length_mm=x["length_mm"], outfits=x["outfits"]))
    json.dump(out, open(f"{OUT}/hw_{name}.json", "w"))
    picks.append(dict(name=name, note=out["note"][:60], hid=x["hid"], side=side, kind="ring"))
json.dump(picks, open(RS.OUT + "/picks.json", "w"), indent=1); print(len(picks), "ring pieces written to", OUT)
