# merge_nw.py <new outlines json>: copy the no-watch tables (tabnw / tab0nw / watchsubs) into WORK/wristacc/outlines.json
import json, sys, shutil
src = json.load(open(sys.argv[1])); dst_p = "WORK/wristacc/outlines.json"
shutil.copy(dst_p, dst_p + ".bak-before-nw"); dst = json.load(open(dst_p)); n = 0
for hid, rec in src.items():
    if hid not in dst or "watchsubs" not in rec: continue
    dst[hid]["watchsubs"] = rec["watchsubs"]
    for S in ("L", "R"):
        for k in ("tabnw", "tab0nw"):
            if k in rec.get(S, {}): dst[hid][S][k] = rec[S][k]
    n += 1
json.dump(dst, open(dst_p, "w")); print("merged no-watch outlines for", n, "outfits")
