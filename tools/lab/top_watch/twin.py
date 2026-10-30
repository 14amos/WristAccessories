# twin.py <job A> <job B> <side>: B's islands whose vertices all lie within 0.5 mm of A's labelled watch islands (bone-local)
import sys, os, json; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from match import *
from scipy.spatial import cKDTree
import glob
a, b, side = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3]; jobs = json.load(open("jobs.json")); lab = {}
for f in glob.glob("labels_*.json"): lab.update(json.load(open(f)))
def isl(i):
    paths, mo, r, rt, objs = F.lod0(G + jobs[i]["path"]); mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process(); names = [p.split("/")[-1] for p in paths]
    P = np.array(h.m_Vertices, float); B = F.bindm(mt, [k for k, n in enumerate(names) if n.endswith(f"Human{side}Forearm3")][0]); L = (np.c_[P, np.ones(len(P))] @ B.T)[:, :3]
    out = []; gid = 0
    for T in [np.array(x).reshape(-1, 3) for x in h.get_triangles()]:
        s = islands(T, len(P))
        for k in np.unique(s): out.append((gid, L[np.unique(T[s == k])])); gid += 1
    return out
A = isl(a); ids = set(lab[str(a)]["ids"]); kd = cKDTree(np.concatenate([V for g, V in A if g in ids])); kk = cKDTree(np.concatenate([V for g, V in A if g not in ids]))
hit = [g for g, V in isl(b) if (kd.query(V)[0] < 0.0005).all() and (kd.query(V)[0] <= kk.query(V)[0]).all()]
print(",".join(map(str, hit)), len(hit), "of", len(ids))
