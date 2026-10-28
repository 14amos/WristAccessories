# seamall_report.py: lab/seamall.log (SeamTest LAB_HID=ALL) -> seamall.tsv, one row per arm x hand, plus flags for a look
import re, sys, collections
rows = []; cur = None
for line in open("WORK/wristacc/lab/seamall.log", encoding="utf-8", errors="replace"):
    m = re.search(r"\[SeamTest\] case (\w{8}) (\S+) \((.*)\)", line)
    if m: cur = dict(hid=m[1], item=m[2], outfit=m[3], res="?"); rows.append(cur); continue
    if cur is None: continue
    if "[SeamTest] FAIL" in line: cur["res"] = "FAIL " + line.split(":", 2)[-1].strip()[:60]
    m = re.search(r"where it would stay is (\d+)% skin", line)
    if m: cur["res"] = "glove"; cur["vis"] = int(m[1])
    m = re.search(r"skin at the seam (\d+)% of (\d+) texels, where it stays (\d+)%, Lab ([\d.-]+)/([\d.-]+)/([\d.-]+) sd ([\d.]+)/([\d.]+)/([\d.]+)", line)
    if m: cur.update(res="seam", seam=int(m[1]), vis=int(m[3]), L=float(m[4]), a=float(m[5]), b=float(m[6]), sdL=float(m[7]))
    m = re.search(r"edge tucked under your skin: (\d+) vertices moved, max ([\d.]+) mm", line)
    if m: cur.update(tuck=int(m[1]), tmax=float(m[2]))
    m = re.search(r"matched to yours at the seam: piece Lab ([\d.-]+)/([\d.-]+)/([\d.-]+)", line)
    if m: cur.update(pL=float(m[1]))
    if "your skin tone not found" in line or "seam (lab)" in line and "tone" in line and "FAIL" in line: cur["tone_err"] = 1
with open("WORK/wristacc/lab/seamall.tsv", "w") as f:
    keys = ["hid", "item", "res", "seam", "vis", "L", "a", "b", "sdL", "tuck", "tmax", "pL", "outfit"]
    f.write("\t".join(keys) + "\n")
    for r in rows: f.write("\t".join(str(r.get(k, "")) for k in keys) + "\n")
c = collections.Counter(r["res"].split()[0] for r in rows); print(len(rows), "cases", dict(c))
flag = []
for r in rows:
    why = []
    if r["res"] == "seam":
        if r["vis"] < 60: why.append(f"visible skin {r['vis']}%")
        if r["sdL"] > 9: why.append(f"blotchy seam sd {r['sdL']}")
        if r["L"] < 25 or r["L"] > 70: why.append(f"L {r['L']}")
        if r.get("tmax", 0) >= 3.9 and r.get("tuck", 0) > 150: why.append(f"tuck {r['tuck']} verts at the 4 mm cap")
        if abs(r["a"]) < 6: why.append(f"grey skin a {r['a']}")
    elif r["res"] == "glove" and r.get("vis", 0) >= 15: why.append(f"glove call at {r['vis']}% skin")
    elif r["res"].startswith("FAIL") or r["res"] == "?": why.append(r["res"])
    if why: flag.append((r["hid"], r["item"], r["outfit"][:40], "; ".join(why)))
print(len(flag), "flagged"); open("WORK/wristacc/lab/seamall_flags.txt", "w").write("\n".join("\t".join(x) for x in flag))
for x in collections.Counter(" ".join(w.split(" ")[:2]) for _, _, _, ws in flag for w in ws.split("; ")).most_common(): print(" ", x)
