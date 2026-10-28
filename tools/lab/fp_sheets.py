# fp_sheets.py [survey dir] [out prefix]: FpSurvey report -> flags + contact sheets.
# CLIP  = >= 2% of the item's vertices more than 1 mm under the posed arm surface (any of the 7 poses).
# FLOAT = the item's inner side (median over directions of its smallest gap to the arm) >= 4 mm more than the same item /
#         side / pose's median over all outfits (the items stand off by design: Sleeve fit 0.25 leaves room for wrist poses;
#         the 2026-10-26 01:25 stale-outline bug measured +5..7 mm in the lab).
import sys, os, csv, json, statistics as st, collections
from PIL import Image, ImageDraw
D = sys.argv[1] if len(sys.argv) > 1 else "WORK/wristacc/lab/fpsurvey"
OUT = sys.argv[2] if len(sys.argv) > 2 else "OUT/fpsurvey"
idx = json.load(open("WORK/wristacc/lab/fpdata/index.json"))
outfits = collections.defaultdict(list)
for hid, v in idx.items(): outfits[v["geo"]].append(v["name"])
rows = [r for r in csv.DictReader(open(D + "/report.tsv", encoding="utf-8"), delimiter="\t") if r["pose"] not in ("-", "")]
other = [r for r in csv.DictReader(open(D + "/report.tsv", encoding="utf-8"), delimiter="\t") if r["pose"] in ("-", "")]
f = lambda x: float(x) if x not in ("", None) else 0.0
base = collections.defaultdict(list)
for r in rows: base[(r["item"], r["side"], r["pose"])].append(f(r["gap_med_mm"]))
base = {k: st.median(v) for k, v in base.items()}
worst = {}
for r in rows:
    clip = f(r["clip_pct"]) >= 2.0
    excess = f(r["gap_med_mm"]) - base[(r["item"], r["side"], r["pose"])]
    fl = excess >= 4.0
    r["excess"] = excess; r["flags"] = ("CLIP " if clip else "") + ("FLOAT" if fl else "")
    k = (r["geo"], r["side"], r["item"])
    score = (f(r["clip_pct"]) if clip else 0) + (excess * 5 if fl else 0)
    if r["flags"] and (k not in worst or score > worst[k][0]): worst[k] = (score, r)
lines = [f"FpSurvey: {len({r['geo'] for r in rows})} geometries ({len(idx)} outfits), {len(rows)} item/pose rows; "
         f"baseline = per item/side/pose median inner gap over all outfits", ""]
lines.append("rest-pose baseline gap (mm), L / R:")
for it in sorted({r["item"] for r in rows}):
    lines.append(f"  {it:22} {base.get((it, 'L', 'rest'), 0):4.1f} / {base.get((it, 'R', 'rest'), 0):4.1f}")
lines.append("")
per = collections.Counter(); peroutf = collections.Counter()
for (geo, side, it), (sc, r) in worst.items():
    for fl in r["flags"].split(): per[(it, fl)] += 1; peroutf[(geo, fl)] += 1
lines.append("flagged geometry/side combos per item (of %d per side x 2):" % len({r['geo'] for r in rows}))
for (it, fl), n in sorted(per.items()): lines.append(f"  {it:22} {fl:5} {n}")
lines.append("")
lines.append("worst (score = clip % + 5 x excess float mm), top 60:")
for (geo, side, it), (sc, r) in sorted(worst.items(), key=lambda kv: -kv[1][0])[:60]:
    names = outfits[geo]
    lines.append(f"  {sc:6.1f} {r['flags']:11} {it:20} {side} pose {r['pose']:8} clip {f(r['clip_pct']):4.1f}% (max {f(r['clip_max_mm']):.1f} mm) gap {f(r['gap_med_mm']):.1f} mm (+{r['excess']:.1f})  {r['mesh']} = {names[0]}{f' (+{len(names) - 1})' if len(names) > 1 else ''}")
if other:
    lines.append(""); lines.append("not placed / failed:")
    for r in other: lines.append(f"  {r['flag']}  {r['item']} {r['side']} {r['mesh']}")
open(OUT + "_summary.txt", "w", encoding="utf-8").write("\n".join(lines)); print("\n".join(lines[:40]))

# flagged sheets: one row per flagged geometry/side/item = rest (2 views) | worst pose (2 views)
fl = sorted(worst.items(), key=lambda kv: -kv[1][0])
W, H = 320, 240; per_sheet = 10; sheets = []
for s in range(0, len(fl), per_sheet):
    chunk = fl[s:s + per_sheet]; im = Image.new("RGB", (W * 4, H * len(chunk)), (18, 18, 18)); dr = ImageDraw.Draw(im)
    for i, ((geo, side, it), (sc, r)) in enumerate(chunk):
        for c, kind in enumerate(("rest", "worst")):
            p = f"{D}/{geo}_{side}_{it}_{kind}.png"
            if os.path.exists(p): im.paste(Image.open(p).convert("RGB"), (c * W * 2, i * H))
        label = f"{r['flags']} {it} {side} | {outfits[geo][0][:40]} | worst {r['pose']}: clip {f(r['clip_pct']):.1f}% gap {f(r['gap_med_mm']):.1f} (+{r['excess']:.1f}) mm"
        dr.rectangle((0, i * H, W * 4, i * H + 14), fill=(0, 0, 0)); dr.text((4, i * H + 1), label, fill=(255, 220, 0))
        wt = f"{D}/{geo}_{side}_{it}_worst.txt"; wp = open(wt).read().split("	")[6] if os.path.exists(wt) else "?"
        dr.text((W * 2 + 4, i * H + 16), f"pose {wp}", fill=(255, 120, 120)); dr.text((4, i * H + 16), "rest", fill=(160, 255, 160))
    fn = f"{OUT}_flagged_{s // per_sheet}.jpg"; im.save(fn, quality=80); sheets.append(fn)
print("sheets:", len(sheets))
