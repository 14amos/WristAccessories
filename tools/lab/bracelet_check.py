# bracelet_check.py: the all-outfit bracelet runs (straightened, 0.7.3) merged - main pass (fpb_all_s1), its second half run on a
# project copy (fpb_all_s1b), the bead-only items rerun with the rigid-fit fix (fpb_all_rigid) - and per item over every outfit
# arm and pose: still open (> 30 deg), wandering (> 20 mm round the loop), clipping (>= 2% of verts > 1 mm in), floating at rest
# (>= 4 mm over that item / side's median gap, fp_sheets.py's rule). Out: OUT/bracelet_check.txt
import csv, os, statistics as st, collections
L = "WORK/wristacc/lab"
f = lambda x: float(x) if x not in ("", None) else 0.0
load = lambda d: [r for r in csv.DictReader(open(f"{L}/{d}/report.tsv", encoding="utf-8"), delimiter="\t") if r["pose"] not in ("-", "")] if os.path.exists(f"{L}/{d}/report.tsv") else []
rigid = set(open(f"{L}/rigid_items.txt").read().split(","))
rows = {}
for d in ("fpb_all_s1", "fpb_all_s1b"):
    for r in load(d):
        if r["item"] not in rigid: rows[(r["geo"], r["side"], r["item"], r["pose"])] = r
for r in load("fpb_all_rigid"): rows[(r["geo"], r["side"], r["item"], r["pose"])] = r
rows = list(rows.values())
geos = {r["geo"] for r in rows}
base = collections.defaultdict(list)
for r in rows:
    if r["pose"] == "rest": base[(r["item"], r["side"])].append(f(r["gap_med_mm"]))
base = {k: st.median(v) for k, v in base.items()}
by = collections.defaultdict(list)
for r in rows: by[r["item"]].append(r)
out = [f"Bracelet check, WristAccessories 0.7.3 (straightened): {len(geos)} of 176 outfit arm shapes, {len(rows)} item/arm/pose rows", ""]
out.append(f"{'item':22} {'open>30':>7} {'max open':>8} {'wander>20':>9} {'wander med':>10} {'clips':>5} {'floats':>6}")
problems = []
for it in sorted(by):
    l = by[it]; rest = [r for r in l if r["pose"] == "rest"]
    op = [r for r in rest if f(r["open_deg"]) > 30]; wd = [r for r in rest if f(r["wander_mm"]) > 20]
    cl = [r for r in l if f(r["clip_pct"]) >= 2]; fl = [r for r in rest if f(r["gap_med_mm"]) - base[(it, r["side"])] >= 4]
    out.append(f"{it:22} {len(op):7} {max(f(r['open_deg']) for r in rest):8.0f} {len(wd):9} {st.median(f(r['wander_mm']) for r in rest):10.1f} {len(cl):5} {len(fl):6}")
    for kind, lst in (("OPEN", op), ("WANDER", wd), ("CLIP", cl), ("FLOAT", fl)):
        for r in lst[:3]: problems.append(f"  {kind:6} {it:22} {r['side']} {r['pose']:8} open {f(r['open_deg']):.0f} deg, wander {f(r['wander_mm']):.1f} mm, clip {f(r['clip_pct']):.1f}% ({f(r['clip_max_mm']):.1f} mm), gap {f(r['gap_med_mm']):.1f} mm  {r['mesh']}")
out += ["", "examples (up to 3 per item and kind):"] + problems
open("OUT/bracelet_check.txt", "w", encoding="utf-8").write("\n".join(out)); print("\n".join(out[:60]))
