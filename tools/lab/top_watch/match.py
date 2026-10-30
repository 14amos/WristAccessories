# match.py <top bundle rel> <hands id> <side L/R>: FP hands watch (watch submeshes) -> islands of the third-person top's meshes
import sys, os, json; sys.path.insert(0, "WORK/wristacc/lab")
import numpy as np, UnityPy, feet_combo as F
from UnityPy.helpers.MeshHelper import MeshHandler
G = "SPT/EscapeFromTarkov_Data/StreamingAssets/Windows/"
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "isl_fn.py")).read())
for c in ("cab-2e56e78b8e1bdf510c68d78dbceee33b", "cab-33a0b2cb07127ea283e12fdee93b7bd2"):   # pcsshirt mesh lives in a .skin_mesh file the CAB index lacks
    F.CABS[c] = G + "assets/content/characters/character/usec/usec5/top_usec_pcsshirt.skin_mesh"
SCAN = json.load(open("WORK/tattoo/acc/watch_scan.json"))
def meshes(path):
    env = UnityPy.load(path); objs = {o.path_id: o for o in env.objects}; out = []
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = F.deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree(); mo = F.deref(r, rt["m_Mesh"], objs)
        if mo is None: continue
        mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process(); names = [p.split("/")[-1] for p in t["_bonePaths"]]
        out.append(dict(name=mesh.m_Name, names=names, bind=[F.bindm(mt, i) for i in range(len(names))], P=np.array(h.m_Vertices, float),
                        bi=np.array(h.m_BoneIndices), bw=F.norm_w(np.array(h.m_BoneWeights, float)), subs=[np.array(s).reshape(-1, 3) for s in h.get_triangles()],
                        mats=[(F.deref(r, m_, objs).peek_name() if F.deref(r, m_, objs) else "?") for m_ in rt["m_Materials"]]))
    # variant meshes the game swaps onto the same renderer (WTT tops: _base / _CR / _AR skins): same bone list as a renderer of the bundle
    seen = {x["name"] for x in out}
    for o in env.objects:
        if o.type.name != "Mesh" or o.peek_name() in seen or not out: continue
        mesh = o.read(); mt = o.read_typetree(); nb = len(mt["m_BindPose"])
        ref = next((x for x in out if len(x["names"]) == nb), None)
        if ref is None: continue
        h = MeshHandler(mesh); h.process()
        out.append(dict(name=mesh.m_Name, names=ref["names"], bind=[F.bindm(mt, i) for i in range(nb)], P=np.array(h.m_Vertices, float),
                        bi=np.array(h.m_BoneIndices), bw=F.norm_w(np.array(h.m_BoneWeights, float)), subs=[np.array(s).reshape(-1, 3) for s in h.get_triangles()],
                        mats=ref["mats"])); seen.add(mesh.m_Name)
    return out
def local(m, bone):
    b = [i for i, n in enumerate(m["names"]) if n.endswith(bone)]
    if not b: return None
    B = m["bind"][b[0]]; return (np.c_[m["P"], np.ones(len(m["P"]))] @ B.T)[:, :3]
if __name__ == "__main__":
    top, hid, side = sys.argv[1:4]; bone = f"Human{side}Forearm3"
    hs = SCAN[hid]; hm = meshes(hs["path"])
    W = []
    for m in hm:
        ws = [s for s, mn in enumerate(m["mats"]) if any(x in mn.lower() for x in ("watch", "wach", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin", "casio", "clock", "glass"))]
        L = local(m, bone)
        if L is None or not ws: continue
        v = np.unique(np.concatenate([m["subs"][s].ravel() for s in ws])); W.append(L[v]); print("FP", m["name"], m["mats"], len(v), "watch verts")
    W = np.concatenate(W)
    for m in meshes(G + top):
        L = local(m, bone)
        if L is None: continue
        # forearm skin/sleeve points of the top near the FP watch, for the scale fit: per island centroid
        res = []
        for s, T in enumerate(m["subs"]):
            isl = islands(T, len(m["P"]))
            for k in np.unique(isl):
                tt = T[isl == k]; v = np.unique(tt); res.append((s, k, tt, v))
        print(m["name"], len(m["P"]), "verts", len(res), "islands", "FP watch bbox", W.min(0).round(3), W.max(0).round(3), "top near wrist bbox",
              L[np.linalg.norm(L - W.mean(0), axis=1) < 0.08].min(0).round(3), L[np.linalg.norm(L - W.mean(0), axis=1) < 0.08].max(0).round(3))
        break
def islist(top, side):
    bone = f"Human{side}Forearm3"; out = []
    for m in meshes(G + top):
        L = local(m, bone)
        if L is None: continue
        for s, T in enumerate(m["subs"]):
            isl = islands(T, len(m["P"]))
            for j, k in enumerate(np.unique(isl)):
                tt = T[isl == k]; v = np.unique(tt); out.append((m["name"], s, j, len(tt), L[v]))
    return out
