# screen.py <list file> <part k> <of n> <out.jsonl>: per top bundle: LOD0-ish mesh, islands fully inside each wrist box (L/R), and
# whether its meshes are already in acc/top_watch.json
import sys, os, json; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from match import *
done = set(json.load(open("WORK/wristacc/acc/top_watch.json")))
paths = [l.strip() for l in open(sys.argv[1]) if l.strip()]; k, n = int(sys.argv[2]), int(sys.argv[3]); out = open(sys.argv[4], "a")
for p in paths[k::n]:
    rec = dict(path=p)
    try:
        ms = meshes("SPT/" + p); rec["meshes"] = [f'{m["name"]}|{len(m["P"])}' for m in ms]; rec["done"] = any(x in done for x in rec["meshes"])
        m0 = max(ms, key=lambda m: (("lod1" not in m["name"].lower() and "lod2" not in m["name"].lower()), len(m["P"]))) if ms else None
        if m0 is not None:
            rec["lod0"] = m0["name"]
            for side in "LR":
                L = local(m0, f"Human{side}Forearm3")
                if L is None: rec[side] = -1; continue
                near = (L[:, 0] > -0.13) & (L[:, 0] < 0.06) & (np.hypot(L[:, 1], L[:, 2]) < 0.1); c = 0
                for T in m0["subs"]:
                    isl = islands(T, len(m0["P"]))
                    for kk in np.unique(isl): c += bool(near[T[isl == kk]].all())
                rec[side] = c
    except Exception as e: rec["err"] = str(e)[:120]
    out.write(json.dumps(rec) + "\n"); out.flush()
