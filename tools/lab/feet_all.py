# feet_all.py <shoe query>[,...] [per sheet]: SHOES v3 (own boot islands hidden, shoe fitted) on EVERY distinct pants mesh, one
# textured thumbnail each (front-side view), contact sheets feetscan/all_<shoe>_<k>.jpg + per-leg gap log (shoe top below the
# cuff = a hole under the pants) -> feetscan/all.log
import os, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, multiprocessing as mp, feet_combo as F, feet_fit as G, feet_check as C
from PIL import Image, ImageDraw
S = 220
def one(args):
    pf, sf = args
    try:
        d = C.prep(pf); s = C.prep(sf); pt = F.load_pants(pf); st = F.load_pants(sf)
        ST = s["T"][s["B"]]; use = np.zeros(len(s["P"]), bool); use[np.unique(ST)] = True
        log = []; SP, cut = G.fit(s["P"], d["prof"], log, use, ST)
        hid = G.hidden_tris(d["T"], d["B"], d["own"], cut)
        own_v = d["T"][~hid]; shoe_t = ST
        def split(T, subs):   # triangles back to their submeshes (for textures)
            out = []; k = 0
            for sub in subs: n = len(sub); out.append(sub); k += n
            return out
        osubs = [sub[~b] for sub, b in zip(pt["subs"], np.split(hid, np.cumsum([len(x) for x in pt["subs"]])[:-1]))]
        ssubs = [sub[b] for sub, b in zip(st["subs"], np.split(s["B"], np.cumsum([len(x) for x in st["subs"]])[:-1]))]
        parts = [(d["own"], d["N"], pt["UV"], osubs, pt["tex"], None), (SP, s["N"], st["UV"], ssubs, st["tex"], None)]
        im = np.concatenate([F.render(parts, S, (0.55, 1.0, 0.15)), F.render(parts, S, (-0.3, -1.0, 0.15))], 1)
        # holes: per 15 deg direction round each ankle, the pants hem (lowest pants vertex within RH) against what covers the
        # leg under it (highest vertex of the shoe or of the kept own boot within RC) -> mm of open ankle, worst direction
        gaps = []
        keptB = d["B"] & ~hid; Pk = G.surf(d["own"], d["T"][keptB])
        Ppants = G.surf(d["own"], d["T"][~d["B"]]); Pshoe = G.surf(SP, ST); Pown = G.surf(d["own"], d["T"][d["B"]])
        for side in ("n", "p"):
            if side not in G.ANKLE: continue
            ax = G.ANKLE[side]; sel = lambda X: X[(X[:, 0] < 0) == (side == "n")]
            def per_dir(X, rmax, red):
                X = sel(X); d_ = X[:, :2] - ax; r = np.linalg.norm(d_, axis=1); X = X[r < rmax]; d_ = d_[r < rmax]
                a = ((np.arctan2(d_[:, 1], d_[:, 0]) % (2 * np.pi)) / (2 * np.pi) * G.NA).astype(int) % G.NA; o = np.full(G.NA, np.nan)
                for k, z in zip(a, X[:, 2]): o[k] = z if np.isnan(o[k]) else red(o[k], z)
                return o
            hem = per_dir(Ppants[Ppants[:, 2] < 0.6], G.RH, min); cov = per_dir(np.vstack([Pshoe, Pk]), G.RC, max)
            base = per_dir(Pown, G.RC, max)                                              # the outfit as made: its own boot
            g = (hem - cov) - np.maximum(hem - base, 0); g = g[~np.isnan(g)]               # only what the swap opened
            gaps.append(round(float(g.max()) * 1000, 1) if len(g) else 0.0)
        return pf, sf, d["name"], (im * 255).astype(np.uint8), gaps, log
    except Exception as e:
        return pf, sf, F.JOBS[pf]["name"], np.zeros((S, 2 * S, 3), np.uint8), [], ["ERR " + repr(e)[:80]]
if __name__ == "__main__":
    shoes = [F.find_pants(q) for q in sys.argv[1].split(",")]; per = int(sys.argv[2]) if len(sys.argv) > 2 else 48
    with mp.Pool(8) as pool: ds = pool.map(C.prep, list(F.JOBS), chunksize=4)
    geo = {}
    for d in ds: geo.setdefault(d["geo"], d["fid"])
    pants = sorted(geo.values(), key=lambda f: F.JOBS[f]["name"])
    lines = []
    for sf in shoes:
        with mp.Pool(8) as pool: res = pool.map(one, [(pf, sf) for pf in pants], chunksize=1)
        tag = F.JOBS[sf]["name"][:20].replace(" ", "_").replace("/", "_")
        for k in range(0, len(res), per):
            chunk = res[k:k + per]; W = 6; sheet = Image.new("RGB", (W * 2 * S, ((len(chunk) + W - 1) // W) * (S + 28)), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
            for i, (pf, _, name, im, gaps, log) in enumerate(chunk):
                x, y = (i % W) * 2 * S, (i // W) * (S + 28); sheet.paste(Image.fromarray(im), (x, y))
                bad = any(g > 3 for g in gaps)
                dr.text((x + 3, y + S + 2), f"{k + i}: {name[:44]}", fill=(255, 80, 80) if bad else (255, 220, 0)); dr.text((x + 3, y + S + 14), f"open ankle {gaps} mm", fill=(200, 200, 200))
            sheet.save(f"{C.LAB}/all_{tag}_{k // per}.jpg", quality=85)
        for pf, _, name, im, gaps, log in res: lines.append(f"{F.JOBS[sf]['name'][:30]} on {name[:44]}: gap {gaps} | {' | '.join(log)}")
        print(tag, "done:", sum(1 for r in res if any(g > 3 for g in r[4])), "of", len(res), "pants with an open ankle (> 3 mm in some direction)", flush=True)
    open(C.LAB + "/all.log", "w", encoding="utf-8").write("\n".join(lines))
