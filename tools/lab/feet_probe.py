# feet_probe.py [n]: first n feet outfits -> bone names, submesh materials and how much of each submesh is foot / calf weighted
import sys, os, json, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
jobs = json.load(open("WORK/wristacc/lab/feetscan/jobs.json", encoding="utf-8")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
for fid, rec in list(jobs.items())[:int(sys.argv[1])]:
    env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        mesh = mo.read(); h = MeshHandler(mesh); h.process(); names = [p.split("/")[-1] for p in t["_bonePaths"]]
        bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float64)
        fb = [i for i, n in enumerate(names) if "Foot" in n or "Toe" in n]; cb = [i for i, n in enumerate(names) if "Calf" in n]
        fw = np.where(np.isin(bi, fb), bw, 0).sum(1); cw = np.where(np.isin(bi, cb), bw, 0).sum(1)
        mats = [deref(r, m_, objs) for m_ in rt["m_Materials"]]; mn = [m.peek_name() if m else "?" for m in mats]
        subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
        print(f"{rec['name'][:40]:40} {mesh.m_Name[:30]:30} v{len(fw)} " + " | ".join(f"{mn[i] if i < len(mn) else '?'}: {len(s)} tris foot {(fw[s].mean(1) > 0.35).mean():.2f} calf {(cw[s].mean(1) > 0.35).mean():.2f}" for i, s in enumerate(subs)))
    print("  bones:", [n.replace("Base Human", "") for n in names if "Foot" in n or "Toe" in n or "Calf" in n])
