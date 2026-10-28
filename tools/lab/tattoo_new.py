# tattoo_new.py: which tattoo_scan.py candidates (lab/tattoo/hw_*) the mod does not have yet (same geometry + texture pixels + side as
# an existing hand item = have; hw_dedupe.py's test) -> lab/tattoo/new.json {tattoo: [...], new: [...]} (candidate numbers)
import os, sys, json, glob, base64, hashlib; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
__file__ = os.path.abspath("hw_dedupe.py"); ns = {"__file__": __file__}
exec(compile(open("hw_dedupe.py").read().split("rows = []")[0], "hw_dedupe_lib", "exec"), ns)
def key(f):
    j = json.load(open(f)); P = np.frombuffer(base64.b64decode(j["pos"]), "<f4").reshape(-1, 3)
    return (hashlib.md5(np.round(P / 0.001).astype(np.int32).tobytes()).hexdigest()[:12], "+".join(ns["tex_hash"](j["bundle"], s["mat"]) for s in j["subs"]), j["side"]), j
have = {}
for tag, d in ns["DIRS"]:
    for f in glob.glob(d + "/hw_*.json"):
        k, j = key(f)
        if j["kind"] == "hand": have[k] = (tag, j["name"])
picks = json.load(open("tattoo_picks_all.json")); tattoo = {3, 5, 9, 17, 19, 35, 46, 82, 84, 90, 102}
dup = {}; new = []
for i, p in enumerate(picks):
    k, j = key(f"tattoo/hw_{p['name']}.json")
    if k in have: dup[i] = have[k]
    elif i not in tattoo: new.append(i)
print("already have:", len(dup), sorted(dup))
print("tattooed, new:", sorted(tattoo - set(dup)), "| tattooed, already in:", sorted(tattoo & set(dup)))
print("other new:", len(new), new)
json.dump(dict(new=new, tattoo=sorted(tattoo - set(dup))), open("tattoo/new.json", "w"))
