# shoe_parity.py <hw json> <pants query> <C# output>: lab feet_fit.fit vs the plugin's FitShoe (ShoeTest) on the same shoe + pants
import os, sys, json, base64; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, feet_combo as F, feet_check as C, feet_fit as G
h = json.load(open(sys.argv[1])); P = np.frombuffer(base64.b64decode(h["pos"]), "<f4").reshape(-1, 3).astype(float)
d = C.prep(F.find_pants(sys.argv[2])); log = []; T = np.concatenate([np.frombuffer(base64.b64decode(x["tris"]), "<i4").reshape(-1, 3) for x in h["subs"]]); Q, cut = G.fit(P, d["prof"], log, None, T)
L = open(sys.argv[3]).read().splitlines(); cs = np.array([[float(x) for x in l.split()] for l in L[2:]])
print("python:", " | ".join(log)); print("C#:    ", L[1]); print("cut python", {k: np.round(v, 4).tolist() for k, v in cut.items()}); print("cut C#    ", L[0][:400])
print(f"max vertex difference {np.abs(cs - Q).max() * 1000:.3f} mm over {len(Q)} verts")
