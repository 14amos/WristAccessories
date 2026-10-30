# rcut.py <export.json> <out.png> <mesh key>...: per mesh one strip: kept (4 views) + cut (4 views), from the export tables
import sys, os, json; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from match import *
from PIL import Image
ex = json.load(open(sys.argv[1])); jobs = json.load(open("jobs.json")); strips = []
for key in sys.argv[3:]:
    e = ex[key]; path = G + jobs[e["job"]]["path"]; bone = f"Human{e['side']}Forearm3"
    ms = meshes(path); m = next(x for x in ms if f"{x['name']}|{len(x['P'])}" == key)
    paths, mo, r, rt, objs = F.lod0(path); texs0 = [F.tex_of(F.deref(r, m_, objs)) for m_ in rt["m_Materials"]]
    if True:
        B = m["bind"][[i for i, n in enumerate(m["names"]) if n.endswith(bone)][0]]; P = m["P"]; L = (np.c_[P, np.ones(len(P))] @ B.T)[:, :3]
        NL = np.zeros_like(L); subs = m["subs"]
        for T in subs:                                                            # face normals summed per vertex
            fn = np.cross(L[T[:, 1]] - L[T[:, 0]], L[T[:, 2]] - L[T[:, 0]]); np.add.at(NL, T.ravel(), np.repeat(fn, 3, 0))
        NL /= np.linalg.norm(NL, axis=1, keepdims=True) + 1e-12; UV = np.zeros((len(P), 2)); texs = [None] * len(subs)
        near = (L[:, 0] > -0.13) & (L[:, 0] < 0.06) & (np.hypot(L[:, 1], L[:, 2]) < 0.1); import base64; cut = set(np.frombuffer(base64.b64decode(e["tris"]), "<i4").tolist())
        kept, gone = [], []; off = 0
        for s, T in enumerate(subs):
            idx = np.arange(len(T)); nr = near[T].all(1); c = np.isin(off + idx, list(cut))
            kept.append(T[nr & ~c]); gone.append(T[nr & c]); off += len(T)
        C = np.array([-0.03, 0, 0]); row = []
        for rows in (kept, gone):
            for a in (0, 90, 180, 270):
                c_, s_ = np.cos(np.radians(a)), np.sin(np.radians(a)); Rx = np.array([[1, 0, 0], [0, c_, -s_], [0, s_, c_]])
                Q = (L - C) @ Rx.T; NQ = NL @ Rx.T
                row.append(F.render([(Q, NQ, UV, rows, texs, None)], S=220, view=(0, 1.0, 0), box=(-0.12, 0.12, -0.12, 0.12)))
        strips.append(np.concatenate(row, 1))
Image.fromarray((np.clip(np.concatenate(strips, 0), 0, 1) * 255).astype(np.uint8)).save(sys.argv[2])
