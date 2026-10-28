# Wrist frame of every hands model, in LForearm3 bone space (the watch slot's parent):
#   o = midpoint between the LForearm3 and LPalm vertex centroids, ax = forearm direction (towards the hand),
#   up = bone-space +Y made perpendicular to ax, r = median of the per-direction outer radius around ax at o.
import UnityPy, sys, os, json, numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler
SPT = "SPT"; W = SPT + "/EscapeFromTarkov_Data/StreamingAssets/Windows"
cust = json.load(open(SPT + "/SPT_Runtime/SPT_Data/database/templates/customization.json", encoding="utf-8"))
jobs = [(k, os.path.join(W, v["_props"]["Prefab"]["path"])) for k, v in cust.items()
        if v.get("_props", {}).get("BodyPart") == "Hands" and isinstance(v["_props"].get("Prefab"), dict) and v["_props"]["Prefab"].get("path")]
jobs += [("e02e6e5732bc3261f5d373bb", SPT + "/SPT_Runtime/user/mods/UnheardTattoos/bundles/Hands/hands_bt_3_unheard.bundle"),
         ("bde3ddec04fc4f9cf79010ae", SPT + "/SPT_Runtime/user/mods/UnheardTattoos/bundles/Hands/hands_pmc_cultist_unheard.bundle")]
out = {}; fails = []
for hid, path in jobs:
    try:
        env = UnityPy.load(path); paths = mesh = mt = None
        for o in env.objects:
            if o.type.name == "MonoBehaviour" and paths is None:
                t = o.read_typetree()
                if "_bonePaths" in t: paths = t["_bonePaths"]
            elif o.type.name == "Mesh" and mesh is None: mesh = o.read(); mt = o.read_typetree()
        names = [p.split("/")[-1] for p in paths]; f3, pm = names.index("Base HumanLForearm3"), names.index("Base HumanLPalm")
        h = MeshHandler(mesh); h.process()
        P = np.array(h.m_Vertices); bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights)
        dom = bi[np.arange(len(bi)), bw.argmax(1)]
        bp = mt["m_BindPose"][f3]; M = np.array([[bp[f"e{r}{c}"] for c in range(4)] for r in range(4)])
        Pb = (M @ np.c_[P, np.ones(len(P))].T).T[:, :3]
        a, b = Pb[dom == f3].mean(0), Pb[dom == pm].mean(0); ax = (b - a) / np.linalg.norm(b - a); o = (a + b) / 2
        up = np.array([0, 1.0, 0]); up -= ax * (up @ ax); up /= np.linalg.norm(up); side = np.cross(ax, up)
        rel = Pb - o; al = rel @ ax; perp = rel - np.outer(al, ax); rad = np.linalg.norm(perp, axis=1)
        ang = np.arctan2(perp @ side, perp @ up); sl = (np.abs(al) < 0.012) & (rad < 0.12)
        prof = [rad[sl & (np.abs((ang - k * np.pi / 18 + np.pi) % (2 * np.pi) - np.pi) < np.pi / 18)].max() for k in range(36) if (sl & (np.abs((ang - k * np.pi / 18 + np.pi) % (2 * np.pi) - np.pi) < np.pi / 18)).any()]
        out[hid] = dict(o=o.round(5).tolist(), ax=ax.round(5).tolist(), up=up.round(5).tolist(), r=round(float(np.median(prof)), 5), name=os.path.basename(path))
    except Exception as e:
        fails.append((hid, os.path.basename(path), str(e)[:80]))
json.dump(out, open("WORK/wristacc/wrist_frames.json", "w"), indent=1)
print("frames", len(out), "fails", len(fails)); [print(" FAIL", *f) for f in fails[:10]]
rs = sorted((v["r"], v["name"]) for v in out.values()); print("radius min", rs[0], "max", rs[-1])
for k in ["6436a4c2b6b75d4c89077fbd", "6654957791883f6c9f0f1f50", "bde3ddec04fc4f9cf79010ae"]: print(k, out.get(k))
