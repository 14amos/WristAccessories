# feet_ref.py: the reference rest skeleton for SHOES (one common space for every outfit's shoes + pants profiles): per bone name
# the most common bind pose over all feet outfits (rounded to 0.1 mm) -> feetscan/ref_bind.json {bone: 16 floats row-major}.
# Outfits whose leg binds differ (other scale / offset, e.g. WTT-Artem suits) are listed.
import os, sys, json, collections; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, multiprocessing as mp, feet_combo as F
def binds(fid):
    try:
        paths, mo, r, rt, objs = F.lod0(F.JOBS[fid]["path"]); mt = mo.read_typetree(); names = [p.split("/")[-1] for p in paths]
        return fid, {n: F.bindm(mt, i).ravel().tolist() for i, n in enumerate(names)}
    except Exception as e: return fid, None
if __name__ == "__main__":
    with mp.Pool(8) as pool: res = pool.map(binds, list(F.JOBS), chunksize=4)
    votes = collections.defaultdict(collections.Counter); keep = {}
    for fid, b in res:
        if not b: continue
        for n, m in b.items(): k = tuple(np.round(m, 4)); votes[n][k] += 1; keep[(n, k)] = m
    ref = {n: list(keep[(n, c.most_common(1)[0][0])]) for n, c in votes.items()}
    json.dump(ref, open("WORK/wristacc/lab/feetscan/ref_bind.json", "w"))
    odd = []
    for fid, b in res:
        if not b: continue
        foot = [n for n in b if "Foot" in n]
        d = max((float(np.abs(np.linalg.inv(np.array(b[n]).reshape(4, 4))[:3, 3] - np.linalg.inv(np.array(ref[n]).reshape(4, 4))[:3, 3]).max()) for n in foot), default=0)
        if d > 0.002: odd.append((round(d * 1000, 1), F.JOBS[fid]["name"]))
    print(len(ref), "bones;", len(odd), "outfits with feet rest > 2 mm off the reference:"); [print("  ", o) for o in sorted(odd)[-20:]]
