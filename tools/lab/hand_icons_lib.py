# hand_icons_lib.py: hand_icons.py's bundle / texture lookup, importable (generated from hand_icons.py)
# hand_icons.py <acc dir> [names...]: pictures for hand items (hw_*.json kind "hand": gloves, other outfits' hands) all from the SAME
# angle (user 2026-10-27: "no same gloves but one picture is left side and one picture is right side"): the back of the hand facing
# the camera, fingers up, so a left glove always reads as a left hand and a right one as a right hand. The back of the hand from the
# item's own bind poses: the side of the knuckle plane away from the thumb tip (no per-side formula: mesh rigs differ).
import os, sys, json, glob, base64; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy, feet_combo as F
from PIL import Image

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
