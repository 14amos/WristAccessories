# ring_icons.py <icons dir>: a 256 px picture per fring item (ring_items.py), three-quarter view, transparent background
import os, sys, json, base64, glob; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy, feet_combo as F, ring_scan as RS
from PIL import Image
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
for f in sorted(glob.glob(RS.OUT + "/fring/hw_fring_*.json")):
    h = json.load(open(f)); fg = h["fingers"]["L4"]; P = arr(fg["pos"], "<f4", 3).astype(float); N = arr(fg["nrm"], "<f4", 3).astype(float)
    UV = arr(h["uv"], "<f4", 2).astype(float); T = [arr(s["tris"], "<i4", 3) for s in h["subs"]]
    env = UnityPy.load(h["src"]["path"] if "path" in h["src"] else RS.SCAN[h["src"]["hid"]]["path"]); m = {o.peek_name(): o for o in env.objects if o.type.name == "Material"}
    tex = [F.tex_of(m[s["mat"]]) if s["mat"] in m else None for s in h["subs"]]
    Q = P - P.mean(0); ev, V = np.linalg.eigh(Q.T @ Q); ax = V[:, 0]; view = ax * 0.75 + V[:, 1] * 0.66; r = np.abs(Q).max() * 1.1
    im, idb, _ = F.render([(Q, N, UV, T, tex, None)], 256, tuple(view), box=(-r, r, -r, r), ids=True)
    rgba = np.dstack([np.clip(im * 1.15, 0, 1), (idb >= 0).astype(float)])
    Image.fromarray((rgba * 255).astype(np.uint8), "RGBA").save(f"{OUT}/{h['name']}.png"); print("icon", h["name"], h["note"])
