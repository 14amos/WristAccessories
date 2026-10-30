# marks.py: per exported hand (fingerink/hw/hw_fm_*.json) and finger: the triangles fully on that finger's Digit bones whose texels are
# not skin (ink, cuts, blood, tape) + one ring of neighbours on the same finger, lifted 0.6 mm -> hw_fmark_<key>_<finger>.json,
# kind "tattoo", keep_skin (drawn whole with its own skin patch), press, "finger" (FINGERS tab row). >= 8 triangles to count.
import os, sys, json, base64, glob; sys.path.insert(0, "WORK/wristacc/lab")
import numpy as np, hand_icons_lib as HI
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
W = np.array([[1/3, 1/3, 1/3], [.6, .2, .2], [.2, .6, .2], [.2, .2, .6], [.5, .5, 0], [0, .5, .5], [.5, 0, .5]])
picks = {p["name"]: p for p in json.load(open("fingerink/picks.json"))}
def nonskin(px):
    x = np.clip(px, 0, 1); mx = x.max(-1); mn = x.min(-1); d = mx - mn + 1e-9; s = np.where(mx > 0, d / (mx + 1e-9), 0); v = mx
    r, g, b = x[..., 0], x[..., 1], x[..., 2]
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return ~(((h < 50) | (h > 340)) & (s > 0.12) & (s < 0.7) & (v > 0.22))
OUT = "fingerink/marks"; os.makedirs(OUT, exist_ok=True); n = 0
for f in sorted(glob.glob("fingerink/hw/hw_fm_*.json")):
    j = json.load(open(f)); p = picks[j["name"]]; S = p["side"]
    P = arr(j["pos"], "<f4", 3).astype(float); N = arr(j["nrm"], "<f4", 3).astype(float); UV = arr(j["uv"], "<f4", 2).astype(float)
    BI = arr(j["bi"], "<i4", 4); BW = arr(j["bw"], "<f4", 4); top = BI[np.arange(len(BI)), BW.argmax(1)]; bones = j["bones"]
    for k in range(1, 6):
        ids = [i for i, b in enumerate(bones) if b.startswith(f"Base Human{S}Digit{k}")]
        subs = []; cnt = 0
        for s in j["subs"]:
            T = arr(s["tris"], "<i4", 3); img = HI.tex(j["bundle"], s["mat"])
            if img is None or not len(T): continue
            on = np.isin(top[T], ids).all(1)
            hh, ww = img.shape[:2]; uv = np.einsum("kj,tjc->tkc", W, UV[T]) % 1.0
            mark = nonskin(img[((1 - uv[..., 1]) * (hh - 1)).astype(int), (uv[..., 0] * (ww - 1)).astype(int)]).sum(1) >= 2
            hit = on & mark
            hv = np.zeros(len(P), bool); hv[T[hit].ravel()] = True
            keep = on & hv[T].any(1)
            if keep.sum(): subs.append(dict(mat=s["mat"], sub=s["sub"], tris=b64(T[keep].ravel(), "<i4"))); cnt += int(keep.sum())
            hits = int(hit.sum())
        if cnt < 8: continue
        fn = ["thumb", "index", "middle", "ring", "little"][k - 1]
        key = f"{j['name'][3:13]}_{S}{k}"; name = f"fmark_{key}"
        o = dict(j); o.update(name=name, note=f"{p['outfit'].split(' (')[0]}: {p['label']} ({'left' if S == 'L' else 'right'} {fn})", kind="tattoo", press=True, keep_skin=True,
                             finger=f"{S}{k}", subs=subs, pos=b64(P + N * 0.0006, "<f4"))
        json.dump(o, open(f"{OUT}/hw_{name}.json", "w")); n += 1
        print(name, o["note"], cnt, "tris")
print(n, "finger marks")
