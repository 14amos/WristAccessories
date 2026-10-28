# feet_check.py [n_shoes]: SHOES v2 on EVERY pants (user 2026-10-27: "check with all pant"). Distinct pants meshes x a spread
# of shoe heights (n_shoes, by boot-island top) x 5 views (4 round + from above like first person), rasterized as ids:
#   own_boot  = pixels of the pants' own boot / sock islands still visible (the band in the user's shot) - want 0
#   poke      = shoe pixels visible more than 2 cm above the pants cuff (shoe through the pants)          - want 0
# Out: feetscan/check.csv, worst cases printed. Pants profiles cached in feetscan/prof/<fid>.pkl.
import os, sys, csv, pickle, hashlib; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, multiprocessing as mp
import feet_combo as F, feet_fit as G
LAB = "WORK/wristacc/lab/feetscan"; os.makedirs(LAB + "/prof", exist_ok=True)
VIEWS = [(0.55, 1.0, 0.15), (-1.0, 0.25, 0.1), (-0.3, -1.0, 0.15), (1.0, -0.2, 0.1), (0.0, 0.45, 1.0)]
def prep(fid):
    f = f"{LAB}/prof/{fid}.pkl"
    if os.path.exists(f): return pickle.load(open(f, "rb"))
    p = F.load_pants(fid); fw = G.foot_w(p["names"], p["bi"], p["bw"]); p["Pr"] = F.to_ref_pos(p); own = p["Pr"] if G.HIDDEN else F.skin_own(p, 0.7)
    bt = G.boot_tris(p["subs"], fw, len(p["P"]), p["Pr"]); prof = G.profile(own, p["subs"], fw)
    T = np.concatenate(p["subs"]); B = np.concatenate(bt)
    d = dict(fid=fid, name=p["name"], mesh=p["mesh"], own=own, N=p["N"], P=p["Pr"], T=T, B=B, prof=prof,
             geo=hashlib.md5(np.round(p["P"] / 0.0005).astype(np.int32).tobytes()).hexdigest()[:12])
    pickle.dump(d, open(f, "wb")); return d
def low(T, P, zmax=0.45): return T[(P[T][:, :, 2] < zmax).all(1)]
def check(args):
    pf, shoes = args
    try: d = prep(pf)
    except Exception as e: return [(pf, "", "ERR " + repr(e)[:80], 0, 0, "")]
    rows = []
    own_p = low(d["T"][~d["B"]], d["own"]); own_b = low(d["T"][d["B"]], d["own"])
    zc = min(L["zc"] for L in d["prof"].values()) if d["prof"] else 0.2
    for sf in shoes:
        s = prep(sf); ST = s["T"][s["B"]]; use = np.zeros(len(s["P"]), bool); use[np.unique(ST)] = True
        log = []; SP, cut = G.fit(s["P"], d["prof"], log, use); ST = low(ST, SP)
        parts = [(d["own"], d["N"], d["own"][:, :2], [own_p], [None], None), (d["own"], d["N"], d["own"][:, :2], [own_b], [None], None), (SP, s["N"], SP[:, :2], [ST], [None], None)]
        ob = pk = 0
        for v in VIEWS:
            _, idb, hb = F.render(parts, 160, v, ids=True)
            ob += int((idb == 1).sum()); pk += int(((idb == 2) & (hb > zc + 0.02)).sum())
        rows.append((pf, sf, d["name"], ob, pk, " | ".join(log)))
    return rows
if __name__ == "__main__":
    nsh = int(sys.argv[1]) if len(sys.argv) > 1 else 6
    # distinct pants geometries (texture variants share one)
    with mp.Pool(8) as pool: ds = pool.map(prep, list(F.JOBS), chunksize=4)
    geo = {}
    for d in ds: geo.setdefault(d["geo"], d["fid"])
    print(len(ds), "pants,", len(geo), "distinct meshes", flush=True)
    tops = sorted((float(d["own"][np.unique(d["T"][d["B"]]), 2].max()) if d["B"].any() else 0, d["fid"]) for d in ds if d["fid"] in geo.values() and d["B"].any())
    shoes = [tops[int(round(q * (len(tops) - 1)))][1] for q in np.linspace(0, 1, nsh)]
    print("shoes:", [(F.JOBS[s]["name"], round(t, 3)) for t, s in tops if s in shoes], flush=True)
    with mp.Pool(8) as pool: res = pool.map(check, [(g, shoes) for g in geo.values()], chunksize=1)
    rows = [r for rr in res for r in rr]
    with open(LAB + "/check.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f); w.writerow(["pants", "shoe", "pants name", "own_boot_px", "poke_px", "fit"]); w.writerows(rows)
    ob = np.array([r[3] for r in rows]); pk = np.array([r[4] for r in rows])
    print(f"{len(rows)} combos: own boot visible in {(ob > 20).sum()} (>20 px), poke in {(pk > 20).sum()}; median own {np.median(ob):.0f} poke {np.median(pk):.0f}")
    for r in sorted(rows, key=lambda r: -(r[3] + r[4]))[:15]: print(f"  own {r[3]:5} poke {r[4]:5}  {r[2][:36]:36} + {F.JOBS[r[1]]['name'][:30] if r[1] else ''}  {r[5][:90]}")
