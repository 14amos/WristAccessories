# ink_export.py <out dir> <hw json>[,...]: TATTOO OVERLAYS for any bare hand (user 2026-10-27: "could we make the tattoos applicable
# to every bare hand too?"). From a (cuff-trimmed, trim_cuffs.py) tattooed hand piece: only the triangles with ink (a corner's texel
# not skin-coloured, scan_hands.skin_like), plus one ring of neighbours (the ink's edge), positions lifted 0.6 mm along the normals
# so the shell sits on the wearer's own hand. Kind "tattoo": a HAND 1 / HAND 2 piece; in game the material shows only the ink
# (HandWear.InkMaterial: skin texels transparent, Standard shader cut-out). Name ink_<key>_<side>, note "<outfit>: tattoos (left|right)".
import os, sys, json, base64, colorsys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
import hand_icons_lib as HI
OUT = sys.argv[1]; FILES = sys.argv[2].split(","); os.makedirs(OUT, exist_ok=True)
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
def skin_like(rgb):
    h, s, v = colorsys.rgb_to_hsv(*rgb); h *= 360
    return (h < 50 or h > 340) and 0.12 < s < 0.7 and v > 0.22
for f in FILES:
    h = json.load(open(f)); P = arr(h["pos"], "<f4", 3).astype(float); N = arr(h["nrm"], "<f4", 3).astype(float); UV = arr(h["uv"], "<f4", 2).astype(float)
    subs = []; kept = 0; total = 0
    for s in h["subs"]:
        T = arr(s["tris"], "<i4", 3); img = HI.tex(h["bundle"], s["mat"]); total += len(T)
        if img is None or not len(T): continue
        uv = UV % 1.0; px = img[((1 - uv[:, 1]) * (img.shape[0] - 1)).astype(int), (uv[:, 0] * (img.shape[1] - 1)).astype(int)]
        ink_v = np.array([not skin_like(p) for p in px])
        cuv = UV[T].mean(1) % 1.0; cpx = img[((1 - cuv[:, 1]) * (img.shape[0] - 1)).astype(int), (cuv[:, 0] * (img.shape[1] - 1)).astype(int)]
        ink_t = ink_v[T].any(1) | np.array([not skin_like(p) for p in cpx])
        near = np.zeros(len(P), bool); near[np.unique(T[ink_t])] = True; keep = ink_t | near[T].any(1)      # + one ring round the ink
        if os.environ.get("ALL") == "1": keep = np.ones(len(T), bool)   # 1.0.5: the whole hand shell (thin ink lines on big triangles were left out; InkMaterial shows only the ink)
        if keep.any(): subs.append(dict(s, tris=b64(T[keep].ravel(), "<i4"))); kept += int(keep.sum())
    if not subs: print("no ink:", h["name"]); continue
    P2 = P + N / (np.linalg.norm(N, axis=1, keepdims=True) + 1e-9) * 0.0006
    name = h["name"].replace("tattoo_", "ink_"); side = "left" if h["side"] == "L" else "right"
    out = dict(h, name=name, kind="tattoo", note=h["note"].split(":")[0] + ": tattoos", pos=b64(P2, "<f4"), subs=subs, ink=True)
    json.dump(out, open(f"{OUT}/hw_{name}.json", "w")); print(f"{name}: {out['note']}, {kept} of {total} triangles carry ink")
