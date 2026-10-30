# label.py <top> <side> <out.png> [hide ids comma]: the wrist of the top's LOD0, textured + islands numbered (4 views); hidden islands removed in row 3
import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from match import *
from PIL import Image, ImageDraw
top, side, out = sys.argv[1:4]; hide = set(int(x) for x in sys.argv[4].split(",")) if len(sys.argv) > 4 and sys.argv[4] else set()
bone = f"Human{side}Forearm3"
env = UnityPy.load(G + top); objs = {o.path_id: o for o in env.objects}
paths, mo, r, rt, objs = F.lod0(G + top)
mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process(); names = [p.split("/")[-1] for p in paths]
P = np.array(h.m_Vertices, float); N = np.array(h.m_Normals, float)[:, :3]; UV = np.array(h.m_UV0, float)[:, :2]
b = [i for i, n in enumerate(names) if n.endswith(bone)][0]; B = F.bindm(mt, b)
L = (np.c_[P, np.ones(len(P))] @ B.T)[:, :3]; NL = N @ B[:3, :3].T
subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]; texs = [F.tex_of(F.deref(r, m_, objs)) for m_ in rt["m_Materials"]]
near = (L[:, 0] > -0.13) & (L[:, 0] < 0.06) & (np.hypot(L[:, 1], L[:, 2]) < 0.1)
items = []; gid = 0
for s, T in enumerate(subs):
    isl = islands(T, len(P))
    for k in np.unique(isl):
        tt = T[isl == k]
        if near[tt].all(): items.append((gid, s, tt))
        gid += 1
def view(v, parts, lab):
    box = (-0.12, 0.12, -0.12, 0.12)
    img, idb, _ = F.render(parts, S=420, view=v, box=box, ids=True)
    im = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)); d = ImageDraw.Draw(im)
    if lab:
        for pi, (g, s, tt) in enumerate(items):
            ys, xs = np.nonzero(idb == pi)
            if len(xs) > 12: d.text((xs.mean() - 6, ys.mean() - 5), str(g), fill=(255, 255, 255)); d.text((xs.mean() - 5, ys.mean() - 4), str(g), fill=(0, 0, 0))
    return np.asarray(im)
C = np.array([-0.03, 0, 0]); rng = np.random.default_rng(2)
rows = [[], [], [], []]
cols = {g: tuple(rng.random(3)) for g, s, tt in items}
for a in (0, 90, 180, 270):
    c_, s_ = np.cos(np.radians(a)), np.sin(np.radians(a)); Rx = np.array([[1, 0, 0], [0, c_, -s_], [0, s_, c_]])
    Q = (L - C) @ Rx.T; NQ = NL @ Rx.T; v = (0, 1.0, 0)
    tex = [(Q, NQ, UV, [tt], [texs[min(s, len(texs) - 1)]], None) for g, s, tt in items]
    col = [(Q, NQ, UV, [tt], [None], cols[g]) for g, s, tt in items]
    kept = [(Q, NQ, UV, [tt], [texs[min(s, len(texs) - 1)]], None) for g, s, tt in items if g not in hide]
    rows[0].append(view(v, tex, False)); rows[1].append(view(v, col, True)); rows[2].append(view(v, kept, False)); rows[3].append(view(v, [(Q, NQ, UV, [tt], [texs[min(s, len(texs) - 1)]], None) for g, s, tt in items if g in hide] or [(Q[:3], NQ[:3], UV[:3], [np.zeros((0, 3), int)], [None], None)], False))
Image.fromarray(np.concatenate([np.concatenate(r_, 1) for r_ in rows], 0)).save(out)
print(mesh.m_Name, len(items), "islands near the wrist:", " ".join(f"{g}:{len(tt)}" for g, s, tt in items))
