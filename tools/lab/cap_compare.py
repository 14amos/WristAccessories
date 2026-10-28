# cap_compare.py: FpSurvey runs with the bulge-room cap (LAB_POSECAP mm) side by side. Floats are measured against the UNCAPPED
# run's per item / side / pose median gap (fp_sheets.py's rule: >= 4 mm over it), clips as in fp_sheets.py (>= 2% of the
# vertices > 1 mm under the arm).
import csv, statistics as st, collections
f = lambda x: float(x) if x not in ("", None) else 0.0
runs = [("none", "fpsurvey_cap1000"), ("4 mm", "fpsurvey_cap4"), ("3 mm", "fpsurvey_cap3"), ("2 mm", "fpsurvey_cap2")]
data = {n: [r for r in csv.DictReader(open(f"WORK/wristacc/lab/{d}/report.tsv", encoding="utf-8"), delimiter="\t") if r["pose"] not in ("-", "")] for n, d in runs}
base = collections.defaultdict(list)
for r in data["none"]: base[(r["item"], r["side"], r["pose"])].append(f(r["gap_med_mm"]))
base = {k: st.median(v) for k, v in base.items()}
print(f"{'cap':6} {'rest floats':>11} {'pose floats':>11} {'clip rows':>9} {'rest clips':>10} {'mean rest gap mm':>16} {'worst clip mm':>13}")
for n, _ in runs:
    rs = data[n]; rest = [r for r in rs if r["pose"] == "rest"]
    fl = lambda r: f(r["gap_med_mm"]) - base[(r["item"], r["side"], r["pose"])] >= 4
    cl = lambda r: f(r["clip_pct"]) >= 2
    print(f"{n:6} {sum(map(fl, rest)):11} {sum(fl(r) for r in rs if r['pose'] != 'rest'):11} {sum(map(cl, rs)):9} {sum(map(cl, rest)):10} {st.mean(f(r['gap_med_mm']) for r in rest):16.2f} {max(f(r['clip_max_mm']) for r in rs if cl(r)) if any(map(cl, rs)) else 0:13.1f}")
for n, _ in runs[1:]:
    c = [r for r in data[n] if f(r["clip_pct"]) >= 2]
    if c: print(f"\nclips with cap {n}:"); [print(f"   {r['item']:20} {r['side']} {r['pose']:8} {f(r['clip_pct']):4.1f}% max {f(r['clip_max_mm']):.1f} mm  {r['mesh']}") for r in c[:15]]
