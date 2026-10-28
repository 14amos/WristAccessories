# cluster_pieces.py: pieces.json -> per outfit / side, clusters of small detached pieces round the wrist / forearm (bracelet,
# band, wrap candidates) and detached finger pieces (rings, tape). Watch materials excluded. -> accessory_candidates.json
import json, re
W = ("watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin", "starline", "shock", "casio")
d = json.load(open("WORK/wristacc/tools/pieces.json")); out = {}
pc = lambda b: bin(b).count("1")
for hid, v in d.items():
    found = []
    for S in "LR":
        ps = [p for p in v["pieces"] if not p.get("unskinned") and p["side"] == S and "along_mm" in p
              and not any(w in m.lower() for m in p["mats"] for w in W)]
        small = [p for p in ps if p["along_mm"][1] - p["along_mm"][0] <= 70 and -150 <= p["along_mm"][0] and p["along_mm"][1] <= 45
                 and not p["bone"].startswith(S + "Digit")]
        small.sort(key=lambda p: p["along_mm"][0]); cl = []
        for p in small:
            if cl and p["along_mm"][0] <= cl[-1]["hi"] + 3:
                c = cl[-1]; c["hi"] = max(c["hi"], p["along_mm"][1]); c["bins"] |= p["bins"]; c["n"] += 1; c["verts"] += p["verts"]; c["mats"] |= set(p["mats"]); c["pieces"].append(p["piece"])
            else: cl.append(dict(lo=p["along_mm"][0], hi=p["along_mm"][1], bins=p["bins"], n=1, verts=p["verts"], mats=set(p["mats"]), mesh=p["mesh"], pieces=[p["piece"]]))
        for c in cl:
            c["wrap"] = round(pc(c["bins"]) / 36, 2)
            if c["wrap"] >= 0.5: found.append(dict(kind="wrist", side=S, along=[c["lo"], c["hi"]], wrap=c["wrap"], n=c["n"], verts=c["verts"], mats=sorted(c["mats"]), mesh=c["mesh"], pieces=c["pieces"]))
        for p in ps:
            if p["bone"].startswith(S + "Digit") and p["verts"] < 4000:
                found.append(dict(kind="finger", side=S, bone=p["bone"], verts=p["verts"], mats=p["mats"], mesh=p["mesh"], pieces=[p["piece"]], along=p["along_mm"]))
    out[hid] = dict(name=v["name"], found=found)
json.dump(out, open("WORK/wristacc/tools/accessory_candidates.json", "w"), indent=1)
nw = sum(1 for v in out.values() for f in v["found"] if f["kind"] == "wrist"); nf = sum(1 for v in out.values() for f in v["found"] if f["kind"] == "finger")
print("outfits with candidates", sum(1 for v in out.values() if v["found"]), "wrist clusters", nw, "finger pieces", nf)
for hid, v in out.items():
    for f in v["found"]:
        print(f"{v['name'][:42]:42} {f['kind']:6} {f['side']} {str(f.get('along')):12} wrap {f.get('wrap','-')!s:4} n {len(f['pieces']):3} v {f['verts']:5} {f.get('bone','')[:10]:10} {','.join(f['mats'])[:40]}")
