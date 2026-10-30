# export.py <out.json> [job ...]: labels_*.json (LOD0 watch islands per top) -> per mesh of each top bundle (every LOD) the
# triangles to cut: {"<mesh name>|<vertex count>": {"top", "verts", "subs": [tris per submesh], "tris": base64 int32 global tri indices}}.
# Other LODs (decimated, the watch can be welded to the sleeve): a triangle goes when its 3 corners and centre all lie within TOLC
# (4 mm) of the cut LOD0 surface and nearer to it than to the kept one; plus whole islands with >= 80% of their vertices within
# 2.5 mm of it (and nearer to it).
import sys, os, json, glob, base64; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from match import *
from scipy.spatial import cKDTree
TOLC = float(os.environ.get('TOLC', '0.004'))
def samples(P, T):                                    # vertices + 6 points per triangle
    w = np.array([[1/3, 1/3, 1/3], [.5, .5, 0], [0, .5, .5], [.5, 0, .5], [.7, .15, .15], [.15, .7, .15], [.15, .15, .7]])
    return np.concatenate([P[np.unique(T)], (w[None] @ P[T]).reshape(-1, 3)]) if len(T) else np.zeros((0, 3))
labels = {}
for f in glob.glob(os.path.join(os.path.dirname(os.path.abspath(__file__)), "labels_*.json")): labels.update(json.load(open(f)))
jobs = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "jobs.json"))); out = {}
sel = [int(x) for x in sys.argv[2:]] or sorted(int(k) for k in labels)
for i in sel:
    lab = labels.get(str(i))
    if not lab or not lab["ids"]: print(i, "no watch ids - skipped"); continue
    side, ids = lab["side"], set(lab["ids"]); bone = f"Human{side}Forearm3"
    path = G + jobs[i]["path"]; lod0name = F.lod0(path)[1].peek_name(); ms = meshes(path)
    def loc(m): return local(m, bone)
    m0 = next(m for m in ms if m["name"] == lod0name); L0 = loc(m0)
    cut0, keep0, gid = [], [], 0; tris0 = []; off = 0
    for s, T in enumerate(m0["subs"]):
        isl = islands(T, len(m0["P"]))
        for k in np.unique(isl):
            idx = np.nonzero(isl == k)[0]
            nearw = ((L0[:, 0] > -0.13) & (L0[:, 0] < 0.06) & (np.hypot(L0[:, 1], L0[:, 2]) < 0.1))[T[idx]].all()   # label.py's wrist box
            if gid in ids and not nearw: print(i, f"island {gid} is not at the wrist - not cut")
            if gid in ids and nearw: cut0.append(T[idx]); tris0 += (off + idx).tolist()
            else: keep0.append(T[idx])
            gid += 1
        off += len(T)
    Sc = samples(L0, np.concatenate(cut0)); Sk = samples(L0, np.concatenate(keep0)); kc, kk = cKDTree(Sc), cKDTree(Sk)
    for m in ms:
        L = loc(m)
        if L is None: continue
        if m["name"] == lod0name: tris = sorted(tris0)
        else:
            tris = []; off = 0
            for s, T in enumerate(m["subs"]):
                Q = np.concatenate([L[T], L[T].mean(1, keepdims=True)], 1).reshape(-1, 3)      # 3 corners + centroid per triangle
                dc = kc.query(Q)[0].reshape(-1, 4); dk = kk.query(Q)[0].reshape(-1, 4)
                c = ((dc < TOLC) & (dc < dk)).all(1)
                isl = islands(T, len(m["P"]))                                                   # + whole islands mostly on the cut surface (the dial)
                for k in np.unique(isl):
                    idx = isl == k; V = np.unique(T[idx]); a = kc.query(L[V])[0]; b = kk.query(L[V])[0]
                    if ((a < 0.0025) & (a < b)).mean() >= 0.8: c |= idx
                tris += (off + np.nonzero(c)[0]).tolist(); off += len(T)
        key = f'{m["name"]}|{len(m["P"])}'
        if key in out: print(i, "SHARED MESH", key, "with job", out[key]["job"], "- same cut" if out[key]["tris"] == base64.b64encode(np.array(sorted(tris), "<i4").tobytes()).decode() else "- DIFFERENT CUT")
        out[key] = dict(top=jobs[i]["name"], job=i, side=side, verts=len(m["P"]), subs=[len(T) for T in m["subs"]], tris=base64.b64encode(np.array(sorted(tris), "<i4").tobytes()).decode())
        print(i, jobs[i]["name"], m["name"], f"{len(tris)} of {sum(len(T) for T in m['subs'])} tris cut")
json.dump(out, open(sys.argv[1], "w"), separators=(",", ":"))
