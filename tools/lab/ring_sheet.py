# ring_sheet.py: ring_export.py's pieces, each from 3 directions (its principal axes: down the finger through the hole + 2 sides),
# textured from the source bundle -> ringscan/rings3_<k>.jpg for picking the real rings by eye.
import os, sys, json, base64, glob; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy, feet_combo as F, ring_scan as RS
from PIL import Image, ImageDraw
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
def tex(hid, mat):
    env = UnityPy.load(RS.SCAN[hid]["path"])
    for o in env.objects:
        if o.type.name == "Material" and o.peek_name() == mat: return F.tex_of(o)
    return None
fs = sorted(glob.glob(RS.OUT + "/hw/hw_ring_*.json")); S = 150; per = 24
for k in range(0, len(fs), per):
    ch = fs[k:k + per]; sheet = Image.new("RGB", (4 * 3 * S, ((len(ch) + 3) // 4) * (S + 28)), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
    for i, f in enumerate(ch):
        h = json.load(open(f)); P = arr(h["pos"], "<f4", 3).astype(float); N = arr(h["nrm"], "<f4", 3).astype(float); UV = arr(h["uv"], "<f4", 2).astype(float)
        T = [arr(s["tris"], "<i4", 3) for s in h["subs"]]; img = tex(h["src"]["hid"], h["subs"][0]["mat"])
        c = P.mean(0); Q = P - c; ev, V = np.linalg.eigh(Q.T @ Q); r = np.abs(Q).max() * 1.15
        x, y = (i % 4) * 3 * S, (i // 4) * (S + 28)
        for j, vw in enumerate([V[:, 0], V[:, 1], V[:, 2]]):
            Pp = Q.copy(); im = F.render([(Pp, N, UV, T, [img] * len(T), None)], S, tuple(vw if abs(vw[2]) < 0.95 else vw + [0.05, 0.05, 0]), box=(-r, r, -r, r))
            sheet.paste(Image.fromarray((im * 255).astype(np.uint8)), (x + j * S, y))
        dr.text((x + 3, y + S + 2), f"{k + i}: {h['note'][:52]}", fill=(255, 220, 0)); dr.text((x + 3, y + S + 14), f"{h['src']['ext_mm']} mm across, {h['src']['length_mm']} mm long, {h['n']} v", fill=(200, 200, 200))
    sheet.save(f"{RS.OUT}/rings3_{k // per}.jpg", quality=85); print("sheet", k // per, len(ch), flush=True)
