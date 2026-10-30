import sys, json, base64, glob, collections; sys.path.insert(0, "WORK/wristacc/lab")
import numpy as np, feet_combo as F, hand_icons_lib as HI
from PIL import Image, ImageDraw
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
g = collections.OrderedDict()
for f in sorted(glob.glob("fingerink/marks/hw_fmark_*.json")): g.setdefault(f.split("fmark_")[1][:12], []).append(f)
rows = []
for key, fs in g.items():
    parts = []; Pall = []
    for f in fs:
        j = json.load(open(f)); P = arr(j["pos"], "<f4", 3).astype(float); N = arr(j["nrm"], "<f4", 3).astype(float); UV = arr(j["uv"], "<f4", 2).astype(float)
        subs = [arr(s["tris"], "<i4", 3) for s in j["subs"]]; texs = [HI.tex(j["bundle"], s["mat"]) for s in j["subs"]]
        parts.append((P, N, UV, subs, texs)); Pall.append(P[np.unique(np.concatenate(subs))])
    V = np.concatenate(Pall); c = V.mean(0); ev, E = np.linalg.eigh(np.cov((V - c).T)); n = E[:, 0]; up = E[:, 2]; r = np.ptp(V @ up) * 0.6 + 0.01
    row = []
    for sgn in (1, -1):
        Rm = np.stack([np.cross(up, n * sgn), n * sgn, up])
        row.append(F.render([((P - c) @ Rm.T, N @ Rm.T, UV, subs, texs, None) for P, N, UV, subs, texs in parts], S=260, view=(0, 1.0, 0), box=(-r, r, -r, r)))
    im = Image.fromarray((np.clip(np.concatenate(row, 1), 0, 1) * 255).astype(np.uint8)); d = ImageDraw.Draw(im)
    d.text((4, 4), key + " " + json.load(open(fs[0]))["note"].split(" (")[0][:50], fill=(255, 255, 0)); rows.append(np.asarray(im))
for s in range(0, len(rows), 10):
    a = rows[s:s + 10]; Image.fromarray(np.concatenate([np.concatenate(a[i:i + 2], 1) if i + 1 < len(a) else np.concatenate([a[i], np.zeros_like(a[i])], 1) for i in range(0, len(a), 2)], 0)).save(f"fingerink/marks_{s // 10}.jpg", quality=85)
print(len(rows))
