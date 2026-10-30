# fingerink_sheet.py: the fingers of every finger_ink_scan candidate outfit (not WTT-Artem), textured; per outfit one row:
# left hand from both sides of its flat plane, right hand the same. Sheets of 8 rows -> fingerink/sheet_<n>.jpg + index.json
import os, sys, json; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy, collections
from UnityPy.helpers.MeshHelper import MeshHandler
from PIL import Image, ImageDraw
import ring_scan as RS, feet_combo as F
R = json.load(open("fingerink/report.json"))["found"]
by = collections.OrderedDict()
for x in R:
    if "Artem" in x["outfit"] or "Unheard" in x["outfit"]: continue
    by.setdefault(x["key"], x)
items = list(by.values()); json.dump([dict(i=k, hid=x["hid"], outfit=x["outfit"], key=x["key"]) for k, x in enumerate(items)], open("fingerink/index.json", "w"), indent=0)
def load(hid):
    rec = RS.SCAN[hid]; env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = RS.deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = RS.deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
        if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt, objs)
    _, paths, mo, r, rt, objs = best; mesh = mo.read(); h = MeshHandler(mesh); h.process()
    names = [p.split("/")[-1] for p in paths]; bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, float); top = bi[np.arange(len(bi)), bw.argmax(1)]
    return (np.array(h.m_Vertices, float), np.array(h.m_Normals, float)[:, :3], np.array(h.m_UV0, float)[:, :2],
            [np.array(s).reshape(-1, 3) for s in h.get_triangles()], [F.tex_of(RS.deref(r, m_, objs)) for m_ in rt["m_Materials"]], names, top)
rows = []
for k, x in enumerate(items):
    try:
        P, N, UV, subs, texs, names, top = load(x["hid"]); row = []
        for S in "LR":
            ids = [i for i, n in enumerate(names) if f"Human{S}Digit" in n or f"Human{S}Palm" in n]
            fids = [i for i, n in enumerate(names) if f"Human{S}Digit" in n]
            ss = [T[np.isin(top[T], ids).all(1)] for T in subs]
            V = P[np.isin(top, fids)]; c = V.mean(0); ev, E = np.linalg.eigh(np.cov((V - c).T)); n = E[:, 0]
            r = np.ptp(V @ E[:, 2]) * 0.6 + 0.01
            up = E[:, 2]
            for sgn in (1, -1):
                # render() looks along view with +z up: rotate so that the hand plane normal is the view and its long axis is up
                Rm = np.stack([np.cross(up, n * sgn), n * sgn, up])          # rows: new x, new y (view), new z (up)
                Q = (P - c) @ Rm.T; NQ = N @ Rm.T
                row.append(F.render([(Q, NQ, UV, ss, [texs[min(i, len(texs) - 1)] for i in range(len(ss))], None)], S=200, view=(0, 1.0, 0), box=(-r, r, -r, r)))
        img = Image.fromarray((np.clip(np.concatenate(row, 1), 0, 1) * 255).astype(np.uint8)); d = ImageDraw.Draw(img); d.text((4, 4), f"{k}: {x['outfit'][:60]}", fill=(255, 255, 0))
        rows.append(np.asarray(img)); print(k, x["outfit"], flush=True)
    except Exception as e: print(k, "ERR", e)
for s in range(0, len(rows), 8):
    Image.fromarray(np.concatenate(rows[s:s + 8], 0)).save(f"fingerink/sheet_{s // 8}.jpg", quality=85)
