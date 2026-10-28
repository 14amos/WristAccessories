# ring_onhand.py <idx,...>: ring candidates (ring_sheet.py numbering) on their own source hand, textured, zoomed on the finger,
# 2 views -> ringscan/onhand.jpg (to tell rings from tape edges / glove cuffs)
import os, sys, json, base64, glob; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy, feet_combo as F, ring_scan as RS
from UnityPy.helpers.MeshHelper import MeshHandler
from PIL import Image, ImageDraw
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
fs = sorted(glob.glob(RS.OUT + "/hw/hw_ring_*.json")); idx = [int(x) for x in sys.argv[1].split(",")] if len(sys.argv) > 1 and sys.argv[1] != "all" else list(range(len(fs)))
S = 240; COLS = 4; OUTF = sys.argv[2] if len(sys.argv) > 2 else RS.OUT + "/onhand.jpg"
sheet = Image.new("RGB", (COLS * 2 * S, ((len(idx) + COLS - 1) // COLS) * (S + 24)), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
for n, i in enumerate(idx):
    gx, gy = (n % COLS) * 2 * S, (n // COLS) * (S + 24)
    h = json.load(open(fs[i])); hid = h["src"]["hid"]; env = UnityPy.load(RS.SCAN[hid]["path"]); objs = {o.path_id: o for o in env.objects}; best = None
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = RS.deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = RS.deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
        if best is None or sc > best[0]: best = (sc, mo, r, rt)
    _, mo, r, rt = best; mh = MeshHandler(mo.read()); mh.process()
    P = np.array(mh.m_Vertices, float); N = np.array(mh.m_Normals, float)[:, :3]; UV = np.array(mh.m_UV0, float)[:, :2]
    subs = [np.array(s).reshape(-1, 3) for s in mh.get_triangles()]; texs = [F.tex_of(RS.deref(r, m_, objs)) for m_ in rt["m_Materials"]]
    Pr = arr(h["pos"], "<f4", 3).astype(float); c = Pr.mean(0); Q = Pr - c; ev, V = np.linalg.eigh(Q.T @ Q); ax = V[:, 0]
    near = [s[(np.linalg.norm(P[s].mean(1) - c, axis=1) < 0.05)] for s in subs]
    for j, vw in enumerate([V[:, 1], V[:, 2]]):
        im = F.render([(P - c, N, UV, near, texs, None)], S, tuple(vw), box=(-0.04, 0.04, -0.04, 0.04))
        sheet.paste(Image.fromarray((im * 255).astype(np.uint8)), (gx + j * S, gy))
    dr.text((gx + 4, gy + S + 4), f"#{i}  {h['note'][:70]}", fill=(255, 220, 0))
sheet.save(OUTF, quality=88); print("saved", OUTF)
