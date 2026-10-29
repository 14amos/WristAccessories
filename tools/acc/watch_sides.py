# watch_sides.py: on which side of the left wrist every outfit wears its OWN watch (user 2026-10-29: Manimal's watch check should
# play the clip for the side the watch is really on, outfit by outfit). For every outfit in watch_scan.json (178 hands with a
# watch, game + mods): the watch submeshes in LForearm3 bone space, the wrist frame from the same hands mesh (palm, fingers,
# thumb = back of the hand), the face = the thickest run around the arm (as watches.py). Output: {hands id: degrees from the
# back of the hand} -> WristAccessories acc/watch_sides.json (WatchAnimsCompat picks Manimal's clip from it).
# Usage: <venv python> watch_sides.py <out json>
import sys, os, json, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

SCAN = json.load(open("WORK/tattoo/acc/watch_scan.json"))
WORDS = ("watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin", "shock")


def skins(env):
    objs = {o.path_id: o for o in env.objects}; out = []
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = objs.get(t["_skinnedMeshRenderer"]["m_PathID"])
        if r is None: continue
        rt = r.read_typetree(); mo = objs.get(rt["m_Mesh"]["m_PathID"])
        if mo is None: continue
        mats = [objs[m["m_PathID"]].peek_name() if m["m_PathID"] in objs else "?" for m in rt["m_Materials"]]
        out.append((t["_bonePaths"], rt, mo, mats))
    return out


def bone(paths, mt, name):
    names = [p.split("/")[-1] for p in paths]; bp = mt["m_BindPose"][names.index(name)]
    return np.array([[bp[f"e{r}{c}"] for c in range(4)] for r in range(4)])


res = {}; fails = []
for hid, sc in SCAN.items():
    try:
        env = UnityPy.load(sc["path"]); sk = skins(env)
        wr = [s for s in sk if any(any(w in m.lower() for w in WORDS) for m in s[3])]
        if wr:
            paths, rt, mo, mats = max(wr, key=lambda s: sum(any(w in m.lower() for w in WORDS) for m in s[3]))
            subs = [i for i, m in enumerate(mats) if any(w in m.lower() for w in WORDS)]
        else:                                   # materials in another bundle (ISB): the scan's own watch submeshes, by material count
            r0 = max(sc["renderers"], key=lambda r: len(r.get("watch_subs") or []))
            cand = [s for s in sk if len(s[3]) == len(r0["mats"])]
            if not cand or not r0.get("watch_subs"): fails.append((hid, "no watch renderer")); continue
            paths, rt, mo, mats = cand[0]; subs = r0["watch_subs"]
        mt = mo.read_typetree(); h = MeshHandler(mo.read()); h.process()
        M3 = bone(paths, mt, "Base HumanLForearm3")
        P = (M3 @ np.c_[np.array(h.m_Vertices), np.ones(len(h.m_Vertices))].T).T[:, :3]
        tri = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
        W = np.unique(np.concatenate([tri[i].ravel() for i in subs if i < len(tri)]))
        # wrist frame from the biggest hands mesh (same skeleton)
        mp, _, mmo, _ = max(sk, key=lambda s: s[2].read_typetree()["m_VertexData"]["m_VertexCount"])
        mmt = mmo.read_typetree(); hm = MeshHandler(mmo.read()); hm.process()
        Mm3 = bone(mp, mmt, "Base HumanLForearm3"); Pm = (Mm3 @ np.c_[np.array(hm.m_Vertices), np.ones(len(hm.m_Vertices))].T).T[:, :3]
        nm = [p.split("/")[-1] for p in mp]; bi = np.array(hm.m_BoneIndices); bw = np.array(hm.m_BoneWeights); dom = bi[np.arange(len(bi)), bw.argmax(1)]
        idx = lambda n: nm.index("Base Human" + n)
        org = lambda n: (Mm3 @ np.linalg.inv(bone(mp, mmt, "Base Human" + n)))[:3, 3]
        w = org("LPalm"); ax = w - org("LForearm3"); ax /= np.linalg.norm(ax)
        pc = Pm[dom == idx("LPalm")].mean(0)
        fd = Pm[np.isin(dom, [idx(f"LDigit{a}{b}") for a in (2, 3, 4) for b in (1, 2, 3)])].mean(0) - pc; fd /= np.linalg.norm(fd)
        td = Pm[np.isin(dom, [idx(f"LDigit1{b}") for b in (1, 2, 3)])].mean(0) - pc; td -= fd * (td @ fd); td /= np.linalg.norm(td)
        back = np.cross(fd, td); e1 = back - ax * (back @ ax); e1 /= np.linalg.norm(e1); e2 = np.cross(ax, e1)
        rel = P[W] - w; perp = rel - np.outer(rel @ ax, ax); rad = np.linalg.norm(perp, axis=1); ang = np.arctan2(perp @ e2, perp @ e1)
        nb = 36; b_ = np.floor((ang + np.pi) / (2 * np.pi / nb)).astype(int) % nb
        thick = np.array([np.ptp(rad[b_ == k]) if (b_ == k).sum() > 2 else 0.0 for k in range(nb)])
        k0 = int(thick.argmax()); face = -180 + 10 * (k0 + 0.5)
        res[hid] = round(float(face), 1)
        print(f"{face:7.1f}  {sc['name'][:70]}")
    except Exception as e:
        fails.append((hid, str(e)[:80]))
json.dump(res, open(sys.argv[1], "w"), indent=0)
print(f"{len(res)} outfits measured, {len(fails)} failed: {fails[:8]}")
