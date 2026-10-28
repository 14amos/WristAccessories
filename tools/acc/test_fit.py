# test_fit.py <hands bundle> <hands id> <out prefix> : stack accessories on both wrists with fit.py, clip-check against
# the posed sleeve (27 poses, ray from the forearm axis), check stacked items don't overlap, export meshes for render.
import sys, json, itertools, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
sys.path.insert(0, "WORK/tattoo/acc"); sys.path.insert(0, "WORK/wristacc/tools")
import fit
bundle, hid, outp = sys.argv[1], sys.argv[2], sys.argv[3]
BODY = len(sys.argv) > 4 and sys.argv[4] == "body"
OL = json.load(open("WORK/wristacc/outlines_body.json" if BODY else "WORK/wristacc/outlines.json"))[hid]
import os, glob
acc = {os.path.basename(f)[4:-5]: json.load(open(f)) for f in glob.glob("WORK/wristacc/acc/acc_*.json")}
plan = {"L": ["rolex", "chainlet", "paracord"], "R": ["prokill", "dogtag", "chain_gold"]}
if os.environ.get("PLAN"): plan = {k: v.split(",") for k, v in (x.split("=") for x in os.environ["PLAN"].split(";"))}
env = UnityPy.load(bundle); objs = {o.path_id: o for o in env.objects}; cands = []
for o in env.objects:                                   # every Skin -> its renderer -> its mesh (as build_outlines.py)
    if o.type.name != "MonoBehaviour": continue
    t = o.read_typetree()
    if "_bonePaths" not in t: continue
    r = objs.get(t["_skinnedMeshRenderer"]["m_PathID"]) if t.get("_skinnedMeshRenderer") else None
    mo = objs.get(r.read_typetree()["m_Mesh"]["m_PathID"]) if r else None
    if mo is None: continue
    nm = mo.peek_name().lower(); cands.append((("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm), t["_bonePaths"], mo))
cands.sort(key=lambda c: -c[0]); paths, mo = cands[0][1], cands[0][2]; mesh = mo.read(); mt = mo.read_typetree()
names = [p.split("/")[-1] for p in paths]; h = MeshHandler(mesh); h.process()
P = np.array(h.m_Vertices); bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights)
tris = np.vstack([np.array(s).reshape(-1, 3) for s in h.get_triangles()])
def rot(axis, deg):
    a = np.radians(deg); axis = axis / np.linalg.norm(axis); K = np.array([[0, -axis[2], axis[1]], [axis[2], 0, -axis[0]], [-axis[1], axis[0], 0]])
    return np.eye(3) + np.sin(a) * K + (1 - np.cos(a)) * K @ K
export = []
for S, items in plan.items():
    o = OL[S]; idx = lambda n: names.index(f"Base Human{S}{n}")
    def bind(n):
        bp = mt["m_BindPose"][idx(n)]; return np.array([[bp[f"e{r}{c}"] for c in range(4)] for r in range(4)])
    M3 = bind("Forearm3"); Mi = np.linalg.inv(M3)
    Pb = (M3 @ np.c_[P, np.ones(len(P))].T).T[:, :3]
    w, ax, e1, e2 = fit.frame(o); f3o = (M3 @ np.linalg.inv(bind("Forearm3")))[:3, 3]
    res = fit.stack([acc[n] for n in items], o)
    # posed sleeves (same poses as the outlines)
    poses = []
    for flex, dev, tw in itertools.product([-35, 0, 35], [-20, 0, 20], [-20, 0, 20]):
        T = {idx("Palm"): (rot(e2, flex) @ rot(e1, dev), w), idx("Forearm2"): (rot(ax, tw), f3o), idx("Forearm1"): (rot(ax, 2 * tw), f3o)}
        V = np.zeros_like(Pb)
        for k in range(bi.shape[1]):
            Vk = Pb.copy(); b = bi[:, k]
            for bone, (R, p) in T.items():
                m = b == bone; Vk[m] = (Pb[m] - p) @ R.T + p
            V += bw[:, k][:, None] * Vk
        poses.append(V)
    ranges = []
    for name, placed, rng, turns in res:
        V = np.vstack([p["pos"] for p in placed]); worst = np.full(len(V), np.inf)
        for PV in poses:
            al = (PV - w) @ ax; rad = np.linalg.norm((PV - w) - np.outer(al, ax), axis=1)
            Tt = tris[((al > rng[0] - 0.02) & (al < rng[1] + 0.02) & (rad < 0.12))[tris].all(1)]
            Aa, Bb, Cc = PV[Tt[:, 0]], PV[Tt[:, 1]], PV[Tt[:, 2]]; E1, E2 = Bb - Aa, Cc - Aa
            for i, v in enumerate(V[::3]):
                a_ = (v - w) @ ax; org = w + ax * a_; d = v - org; r_ = np.linalg.norm(d); d /= r_
                pp = np.cross(d, E2); det = (E1 * pp).sum(1); ok = np.abs(det) > 1e-12
                inv = np.where(ok, 1 / np.where(ok, det, 1), 0); ss = org - Aa
                uu = (ss * pp).sum(1) * inv; q = np.cross(ss, E1); vv = (q * d).sum(1) * inv; tt = (E2 * q).sum(1) * inv
                hit = ok & (uu >= 0) & (vv >= 0) & (uu + vv <= 1) & (tt > 0) & (tt < 0.12)
                gap = r_ - (tt[hit].max() if hit.any() else 0.0); worst[i * 3] = min(worst[i * 3], gap)
        wv = worst[np.isfinite(worst)]
        kinds = np.concatenate([np.array(m["kind"]) for m in acc[name]["meshes"]]); pids = np.concatenate([np.array(m["pid"]) for m in acc[name]["meshes"]])
        bad = np.where(np.isfinite(worst) & (worst < 0))[0]
        if len(bad): print(f"     clipping verts by (piece, kind): {sorted(set((int(pids[i]), int(kinds[i])) for i in bad))}, their h {np.round(np.concatenate([np.array(m['h']) for m in acc[name]['meshes']])[bad]*1000,1).tolist()}")
        print(f"{S} {name:10} axial {rng[0]*1000:6.1f}..{rng[1]*1000:6.1f} mm, turns {turns}, clip: {np.sum(wv < 0)} of {len(wv)} sampled verts, worst gap {wv.min()*1000:5.1f} mm")
        ranges.append((name, rng))
        for p in placed:
            pm = (Mi @ np.c_[p["pos"], np.ones(len(p["pos"]))].T).T[:, :3]
            export.append(dict(name=f"{S}_{name}_{p['mesh']}", pos=pm.tolist(), uv=np.array(p["uv"]).tolist(), tris=[[int(x) for x in np.concatenate([np.array(t) for t in p["tris"]])]]))
    cm = (Mi @ np.r_[w + ax * (o["start"] - 0.06), 1])[:3]; axm = Mi[:3, :3] @ ax; e1m = Mi[:3, :3] @ e1
    export.append(dict(name=f"{S}_frame", pos=[cm.tolist(), axm.tolist(), e1m.tolist()], tris=[[]]))
    for (n1, r1), (n2, r2) in zip(ranges, ranges[1:]):
        print(f"   {n1} vs {n2}: axial gap {(r1[0] - r2[1])*1000:.1f} mm")
json.dump(dict(hands=dict(pos=P.tolist(), uv=np.array(h.m_UV0).tolist(), tris=[tris.ravel().tolist()]), acc=export), open(outp + "_scene.json", "w"))
