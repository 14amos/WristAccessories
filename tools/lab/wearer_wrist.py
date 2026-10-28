# wearer_wrist.py <hands id> <side>: the wearer's own wrist as the plugin sees it - per 5 mm band from the wrist plane (Palm origin,
# toward the middle finger): triangles, skin share, hand-bone weight, and which island holds the palm (a glove). Lab probe.
import os, sys, json, colorsys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
import feet_combo as F
WID, S = sys.argv[1], sys.argv[2]
SCAN = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
env = UnityPy.load(SCAN[WID]["path"]); objs = {o.path_id: o for o in env.objects}; best = None
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    t = o.read_typetree()
    if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
    r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
    mo = deref(r, rt["m_Mesh"], objs) if r else None
    if mo is None: continue
    nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
    if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt)
_, paths, mo, r, rt = best; mt = mo.read_typetree(); h = MeshHandler(mo.read()); h.process(); names = [p.split("/")[-1] for p in paths]
WB = {n: np.array([[mt["m_BindPose"][i][f"e{a}{b}"] for b in range(4)] for a in range(4)]) for i, n in enumerate(names)}
P = np.array(h.m_Vertices, float); UV = np.array(h.m_UV0, float)[:, :2]; bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, float)
subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]; tex = [F.tex_of(deref(r, m_, objs)) for m_ in rt["m_Materials"]]
mnames = [(deref(r, m_, objs).peek_name() if deref(r, m_, objs) else "?") for m_ in rt["m_Materials"]]
o = lambda n: np.linalg.inv(WB[n])[:3, 3]; pc = o(f"Base Human{S}Palm"); fd = o(f"Base Human{S}Digit31") - pc; fd /= np.linalg.norm(fd)
handb = np.array([f"Human{S}" in n and ("Palm" in n or "Digit" in n) for n in names]); hw = (bw * handb[bi]).sum(1)
def skin(rgb):
    hh, ss, vv = colorsys.rgb_to_hsv(*rgb[:3]); hh *= 360
    return (hh < 45 or hh > 345) and 0.12 < ss < 0.62 and vv > 0.25
allT = np.vstack(subs); par = np.arange(len(P))
def fnd(x):
    while par[x] != x: par[x] = par[par[x]]; x = par[x]
    return x
k_ = np.round(P / 1e-5).astype(np.int64); _, inv = np.unique(k_, axis=0, return_inverse=True); inv = inv.ravel()
for a, b, c in inv[allT]: par[fnd(b)] = fnd(a); par[fnd(c)] = fnd(a)
print(WID, S, r.m_Name if hasattr(r, "m_Name") else "", mo.peek_name(), len(P), "verts, mats", mnames)
for si, T in enumerate(subs):
    if tex[si] is None or not len(T): continue
    img = tex[si]; uv = UV[T].mean(1) % 1; c = img[((1 - uv[:, 1]) * (img.shape[0] - 1)).astype(int), (uv[:, 0] * (img.shape[1] - 1)).astype(int)]
    sk = np.array([skin(x) for x in c]); dk = np.array([colorsys.rgb_to_hsv(*x[:3])[2] < 0.2 for x in c]); d = (P[T].mean(1) - pc) @ fd; isl = np.array([fnd(inv[t[0]]) for t in T]); thw = hw[T].mean(1)
    palm_isl = set(isl[thw > 0.9])
    for lo in np.arange(-0.08, 0.03, 0.01):
        m = (d >= lo) & (d < lo + 0.01)
        if m.any(): print(f" sub{si} d {lo*1000:+4.0f}: {m.sum():4d} tris, skin {sk[m].mean()*100:3.0f}%, dark {(dk[m]).mean()*100:3.0f}%, hand w {thw[m].mean():.2f}, in palm island {np.isin(isl[m], list(palm_isl)).mean()*100:3.0f}%")
