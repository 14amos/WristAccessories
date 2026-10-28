# hand_icons.py <acc dir> [names...]: pictures for hand items (hw_*.json kind "hand": gloves, other outfits' hands) all from the SAME
# angle (user 2026-10-27: "no same gloves but one picture is left side and one picture is right side"): the back of the hand facing
# the camera, fingers up, so a left glove always reads as a left hand and a right one as a right hand. The back of the hand from the
# item's own bind poses: the side of the knuckle plane away from the thumb tip (no per-side formula: mesh rigs differ).
import os, sys, json, glob, base64; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy, feet_combo as F
from PIL import Image
D = sys.argv[1]; only = set(sys.argv[2:])
SCAN = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); PATH = {}
for k, v in SCAN.items():
    p = v["path"].replace("\\", "/"); i = p.lower().find("assets/content/"); j = p.lower().find("/bundles/")
    PATH[p[i:] if i >= 0 else (p[j + 9:] if j >= 0 else p)] = v["path"]
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
TEX = {}
def tex(bundle, mat):
    if (bundle, mat) in TEX: return TEX[(bundle, mat)]
    img = None
    try:
        env = UnityPy.load(PATH[bundle])
        for o in env.objects:
            if o.type.name == "Material" and o.peek_name() == mat: img = F.tex_of(o); break
    except Exception: pass
    TEX[(bundle, mat)] = img; return img
n = 0
for f in sorted(glob.glob(D + "/hw_*.json")):
    h = json.load(open(f))
    if h["kind"] != "hand" or (only and h["name"] not in only): continue
    S = h["side"]; B = {b: np.array(h["bind"][i]).reshape(4, 4) for i, b in enumerate(h["bones"])}
    o = lambda n_: np.linalg.inv(B[n_])[:3, 3] if n_ in B else None
    pc, m3, t1 = o(f"Base Human{S}Palm"), o(f"Base Human{S}Digit31"), o(f"Base Human{S}Digit11")
    P = arr(h["pos"], "<f4", 3).astype(float); N = arr(h["nrm"], "<f4", 3).astype(float); UV = arr(h["uv"], "<f4", 2).astype(float)
    T = [arr(s["tris"], "<i4", 3) for s in h["subs"]]; texs = [tex(h["bundle"], s["mat"]) for s in h["subs"]]
    if pc is None or m3 is None or t1 is None: print("skip (bones)", h["name"]); continue
    fd = m3 - pc; fd /= np.linalg.norm(fd)
    # the back of the hand: the side of the knuckle plane (palm, index, little finger) away from the thumb tip, which curls to the palm
    d21, d51, tt = o(f"Base Human{S}Digit21"), o(f"Base Human{S}Digit51"), o(f"Base Human{S}Digit13") if o(f"Base Human{S}Digit13") is not None else t1
    nrm = np.cross(d21 - pc, d51 - pc); nrm /= np.linalg.norm(nrm); back = -nrm if (tt - pc) @ nrm > 0 else nrm
    c = P.mean(0); Q = P - c; r = np.abs(Q).max() * 0.95
    # feet_combo.render: up = projection of +z; give it a frame where z = the fingers (rotate the points so fd -> z, back -> view)
    e3 = fd; e2 = back - e3 * (back @ e3); e2 /= np.linalg.norm(e2); e1 = np.cross(e2, e3)
    R = np.stack([e1, e2, e3]); Qr = Q @ R.T; Nr = N @ R.T
    Qr[:, 0] *= -1; Nr[:, 0] *= -1                      # Unity's mesh space is left-handed, feet_combo.render right-handed: unmirror
    im, idb, _ = F.render([(Qr, Nr, UV, T, texs, None)], 256, (0.0, 1.0, 0.0), box=(-r, r, -r, r), ids=True)
    rgba = np.dstack([np.clip(im * 1.15, 0, 1), (idb >= 0).astype(float)])
    os.makedirs(D + "/icons", exist_ok=True); Image.fromarray((rgba * 255).astype(np.uint8), "RGBA").save(f"{D}/icons/{h['name']}.png"); n += 1
print(n, "hand pictures from the back of the hand,", D)
