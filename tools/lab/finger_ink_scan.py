# finger_ink_scan.py: MARKS ON BARE FINGERS in every first-person hands outfit (user 2026-10-30: "other available finger tattoos",
# "bloody markings"). Per outfit (all_scan_hands.json, like ring_scan.py): the hands skin mesh; per finger (L/R 1..5) the triangles
# fully on that finger's Digit bones; per triangle 7 texels: skin (scan_hands-style hue test) / blood (red-dominant, dark) / ink
# (other non-skin). A finger is BARE when >= 60% of its texels are skin; candidates = bare fingers with 1.5..45% ink or >= 1% blood.
# Out: lab/fingerink/report.json (+ printed), deduped by texture + mesh.
import os, sys, json, collections; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, multiprocessing as mp, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
import ring_scan as RS, feet_combo as F
OUT = "WORK/wristacc/lab/fingerink"; os.makedirs(OUT, exist_ok=True)
W = np.array([[1/3, 1/3, 1/3], [.6, .2, .2], [.2, .6, .2], [.2, .2, .6], [.5, .5, 0], [0, .5, .5], [.5, 0, .5]])
def classes(px):
    x = np.clip(px, 0, 1); mx = x.max(-1); mn = x.min(-1); d = mx - mn + 1e-9; s = np.where(mx > 0, d / (mx + 1e-9), 0); v = mx
    r, g, b = x[..., 0], x[..., 1], x[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    skin = ((h < 50) | (h > 340)) & (s > 0.12) & (s < 0.7) & (v > 0.22)
    blood = (r > 1.6 * g) & (r > 1.6 * b) & (v < 0.55) & (s >= 0.55)
    return skin, blood & ~skin, ~skin & ~blood
def one(hid):
    rec = RS.SCAN[hid]; out = []
    try:
        env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
        for o in env.objects:
            if o.type.name != "MonoBehaviour": continue
            t = o.read_typetree()
            if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
            r = RS.deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
            mo = RS.deref(r, rt["m_Mesh"], objs) if r else None
            if mo is None: continue
            nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
            if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt)
        if best is None: return hid, out, "no skin"
        _, paths, mo, r, rt = best; mesh = mo.read(); h = MeshHandler(mesh); h.process()
        names = [p.split("/")[-1] for p in paths]
        bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float64); top = bi[np.arange(len(bi)), bw.argmax(1)]
        UV = np.array(h.m_UV0, float)[:, :2]
        mats = [RS.deref(r, m_, objs) for m_ in rt["m_Materials"]]
        for si, s in enumerate(h.get_triangles()):
            T = np.array(s).reshape(-1, 3)
            if not len(T) or si >= len(mats) or mats[si] is None: continue
            img = F.tex_of(mats[si])
            if img is None: continue
            hh, ww = img.shape[:2]
            uv = np.einsum("kj,tjc->tkc", W, UV[T]) % 1.0
            px = img[((1 - uv[..., 1]) * (hh - 1)).astype(int), (uv[..., 0] * (ww - 1)).astype(int)]
            sk, bl, ik = classes(px)
            for S in "LR":
                for k in range(1, 6):
                    ids = [i for i, n in enumerate(names) if n.startswith(f"Base Human{S}Digit{k}")]
                    if not ids: continue
                    on = np.isin(top[T], ids).all(1)
                    if on.sum() < 20: continue
                    fs, fb, fi = sk[on].mean(), bl[on].mean(), ik[on].mean()
                    if fs >= 0.6 and (0.015 <= fi <= 0.45 or fb >= 0.01):
                        out.append(dict(hid=hid, outfit=rec["name"], finger=f"{S}{k}", sub=si, mat=mats[si].peek_name(), tris=int(on.sum()),
                                        skin=round(float(fs), 3), ink=round(float(fi), 3), blood=round(float(fb), 3),
                                        key=f"{mesh.m_Name}|{mats[si].peek_name()}"))
        return hid, out, None
    except Exception as e: return hid, out, repr(e)[:100]
if __name__ == "__main__":
    with mp.Pool(4) as pool: res = pool.map(one, list(RS.SCAN), chunksize=2)
    found = [x for _, o, _ in res for x in o]; errs = [(RS.SCAN[h]["name"], e) for h, _, e in res if e]
    seen = {}; uniq = []
    for x in found:
        k = (x["key"], x["finger"])
        if k in seen: seen[k]["outfits"].append(x["outfit"]); continue
        x["outfits"] = [x["outfit"]]; seen[k] = x; uniq.append(x)
    json.dump(dict(found=uniq, errors=errs), open(OUT + "/report.json", "w"), indent=1)
    print(len(RS.SCAN), "outfits,", len(uniq), "finger candidates,", len(errs), "errors")
    for x in sorted(uniq, key=lambda x: (x["outfit"], x["finger"])): print(f"{x['outfit'][:45]:45} {x['finger']} ink {x['ink']:.3f} blood {x['blood']:.3f} skin {x['skin']:.2f} ({len(x['outfits'])})")
