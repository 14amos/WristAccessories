# diff_ink.py <uh hw json> <base hw json> <out json> <name> <note>: the triangles whose texture differs between our UnheardTattoos
# hands and the game's own hands (same mesh, same UVs) = exactly the scars / brand / logo (user 2026-10-30: "add the old scars and
# etchings we made ... and the unheard logo tattoo"), + one ring of neighbours, lifted 0.6 mm; kind "tattoo", keep_skin (the scar
# texels ARE skin-coloured: the ink cut-out would drop them)
import os, sys, json, base64; sys.path.insert(0, "WORK/wristacc/lab")
import numpy as np
import hand_icons_lib as HI
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
ua, ba, out, name, note = sys.argv[1:6]
U = json.load(open(ua)); Bj = json.load(open(ba))
P = arr(U["pos"], "<f4", 3).astype(float); N = arr(U["nrm"], "<f4", 3).astype(float); UV = arr(U["uv"], "<f4", 2).astype(float)
W = np.array([[1/3, 1/3, 1/3], [1, 0, 0], [0, 1, 0], [0, 0, 1], [.5, .5, 0], [0, .5, .5], [.5, 0, .5], [.6, .2, .2], [.2, .6, .2], [.2, .2, .6]])
subs = []; kept = 0
for s, sb in zip(U["subs"], Bj["subs"]):
    T = arr(s["tris"], "<i4", 3); A = HI.tex(U["bundle"], s["mat"]); B = HI.tex(Bj["bundle"], sb["mat"])
    if A is None or B is None: print("no texture", s["mat"], A is None, B is None); continue
    h, w = A.shape[:2]; Bs = B if B.shape == A.shape else np.asarray(__import__("PIL.Image", fromlist=["Image"]).fromarray((B * 255).astype(np.uint8)).resize((w, h))) / 255.0
    uv = np.einsum("kj,tjc->tkc", W, UV[T]) % 1.0
    x = (uv[..., 0] * (w - 1)).astype(int); y = ((1 - uv[..., 1]) * (h - 1)).astype(int)
    d = np.abs(A[y, x] - Bs[y, x]).max(-1).max(-1)
    hit = d > 0.08
    hv = np.zeros(len(P), bool); hv[T[hit].ravel()] = True
    keep = hv[T].any(1)                                                     # + one ring of neighbours (the mark's edge)
    print(s["mat"], "differing triangles", int(hit.sum()), "kept with the edge ring", int(keep.sum()), "of", len(T))
    subs.append(dict(mat=s["mat"], sub=s["sub"], tris=b64(T[keep].ravel(), "<i4"))); kept += int(keep.sum())
o = dict(U); o.update(name=name, note=note, kind="tattoo", press=True, keep_skin=True, subs=subs)
o["pos"] = b64(P + N * 0.0006, "<f4")
json.dump(o, open(out, "w")); print("wrote", out, kept, "triangles")
