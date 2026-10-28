# feet_islands.py <n|fid...>: index-connected triangle islands (UV islands: no position weld) of feet outfits' LOD0 meshes, near
# the feet: per island tris, mean foot weight, share of verts with foot weight > 0.35, z range -> can a boot be cut as whole islands?
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, feet_combo as F
ids = sys.argv[1:] if len(sys.argv) > 2 or (len(sys.argv) == 2 and not sys.argv[1].isdigit()) else list(F.JOBS)[:int(sys.argv[1])]
def islands(T, nv):
    par = np.arange(nv)
    def f(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for a, b, c in T: ra, rb, rc = f(a), f(b), f(c); par[rb] = ra; par[f(rc)] = ra
    return np.array([f(a) for a in T[:, 0]])
for q in ids:
    fid = F.find_pants(q) or q; p = F.load_pants(fid); n = p["names"]
    fb = [i for i, x in enumerate(n) if "Foot" in x or "Toe" in x]; fw = np.where(np.isin(p["bi"], fb), p["bw"], 0).sum(1)
    T = np.concatenate(p["subs"]); comp = islands(T, len(p["P"])); z = p["P"][:, 2]
    rows = []
    for c in np.unique(comp):
        v = np.unique(T[comp == c])
        if fw[v].max() < 0.05: continue
        rows.append(((comp == c).sum(), fw[v].mean(), (fw[v] > 0.35).mean(), z[v].min(), z[v].max(), p["P"][v, 0].mean()))
    print(f"== {p['name'][:40]} {p['mesh']}: {len(np.unique(comp))} islands, touching the feet:")
    for r in sorted(rows, key=lambda r: -r[0])[:14]: print(f"  {r[0]:5} tris foot mean {r[1]:.2f} >0.35 {r[2]:.2f}  z {r[3]:.3f}..{r[4]:.3f}  x {r[5]:+.2f}")
