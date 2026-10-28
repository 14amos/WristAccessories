# ring_check.py: every fring item on all 10 fingers of the reference hand (ring_items.py), textured, back + palm side per hand
import os, sys, json, base64, glob; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy, feet_combo as F, ring_scan as RS, ring_items as RI
from UnityPy.helpers.MeshHelper import MeshHandler
from PIL import Image, ImageDraw
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
hid, REF = RI.ref_hand(); rec = RS.SCAN[hid]; env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}
mats = {o.peek_name(): o for o in env.objects if o.type.name == "Material"}
ref_tex = [F.tex_of(mats[m]) if m in mats else None for m in [None]][0]
# the reference hand's own materials in submesh order
best = None
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    t = o.read_typetree()
    if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
    r = RS.deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree(); mo = RS.deref(r, rt["m_Mesh"], objs)
    if mo.peek_name() == REF["mesh"]: best = (r, rt, mo)
r, rt, mo = best; mh = MeshHandler(mo.read()); mh.process(); UVh = np.array(mh.m_UV0, float)[:, :2]
htex = [F.tex_of(RS.deref(r, m_, objs)) for m_ in rt["m_Materials"]]
S = 300; items = sorted(glob.glob(RS.OUT + "/fring/hw_fring_*.json"))
sheet = Image.new("RGB", (4 * S, len(items) * (S + 20)), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
for ii, f in enumerate(items):
    h = json.load(open(f)); UV = arr(h["uv"], "<f4", 2).astype(float); T = [arr(s["tris"], "<i4", 3) for s in h["subs"]]
    env2 = UnityPy.load(h["src"]["path"] if "path" in h["src"] else RS.SCAN[h["src"]["hid"]]["path"]); m2 = {o.peek_name(): o for o in env2.objects if o.type.name == "Material"}
    rtex = [F.tex_of(m2[s["mat"]]) if s["mat"] in m2 else None for s in h["subs"]]
    parts = [(REF["P"], REF["N"], UVh, REF["subs"], htex, None)]
    for key, fi in h["fingers"].items():
        B = np.linalg.inv(REF["bind"][fi["bone"]]); p = arr(fi["pos"], "<f4", 3).astype(float); n = arr(fi["nrm"], "<f4", 3).astype(float)
        P = (B @ np.c_[p, np.ones(len(p))].T).T[:, :3]; N = (B[:3, :3] @ n.T).T
        parts.append((P, N, UV, T, rtex, None))
    for j, (S2, vw) in enumerate([("L", None), ("L", "palm"), ("R", None), ("R", "palm")]):
        pc = np.linalg.inv(REF["bind"][f"Base Human{S2}Palm"])[:3, 3]; d3 = np.linalg.inv(REF["bind"][f"Base Human{S2}Digit31"])[:3, 3] - pc
        fr = RI.frame(REF, S2, 3, frac=0.5); back = np.linalg.inv(REF["bind"][fr["seg"]])[:3, :3] @ fr["d"]
        view = back if vw is None else -back; c = pc + d3 * 1.2
        im = F.render([(P_ - c, N_, U_, T_, X_, t_) for P_, N_, U_, T_, X_, t_ in parts], S, tuple(view + np.array([0, 0, 0.01])), box=(-0.09, 0.09, -0.09, 0.09))
        sheet.paste(Image.fromarray((im * 255).astype(np.uint8)), (j * S, ii * (S + 20)))
    dr.text((4, ii * (S + 20) + S + 3), f"{h['note']}: on all 10 fingers (left back, left palm, right back, right palm)", fill=(255, 220, 0))
sheet.save(RS.OUT + "/fring_check.jpg", quality=88); print("saved")
