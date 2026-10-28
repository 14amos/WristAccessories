# bindcheck.py: every handwear item's palm bind pose against the game's own hands (a game item of that side): items made
# on another skeleton space (rotated / scaled / moved) come out stretched on the player's bones (user 2026-10-27: a glove
# drawn as a long cone in first person). Prints the ones off.
import json, glob, os, numpy as np
def palm(d, side):
    i = [k for k, n in enumerate(d["bones"]) if n == f"Base Human{side}Palm"]
    return np.array(d["bind"][i[0]]).reshape(4, 4) if i else None
refs = {}
for f in sorted(glob.glob("WORK/wristacc/acc/hw_*.json")):
    d = json.load(open(f))
    if "WTT" in d["note"] or "ISB" in d["note"]: continue
    if d["side"] not in refs and palm(d, d["side"]) is not None: refs[d["side"]] = palm(d, d["side"])
bad = []
for f in sorted(glob.glob("WORK/wristacc/acc/hw_*.json") + glob.glob("WORK/wristacc/addons/*/acc/hw_*.json")):
    d = json.load(open(f)); p = palm(d, d["side"]); n = os.path.basename(f)
    if p is None: bad.append((n, d["note"][:45], "no palm bone")); continue
    r = refs[d["side"]]; rot = np.abs(p[:3, :3] - r[:3, :3]).max(); mv = np.abs(p[:3, 3] - r[:3, 3]).max(); sc = np.linalg.norm(p[:3, 0])
    if rot > 0.05 or mv > 0.01 or abs(sc - 1) > 0.02: bad.append((n, d["note"][:45], f"rot {rot:.3f}", f"move {mv * 1000:.0f} mm", f"scale {sc:.3f}"))
print(len(bad), "items off")
for b in bad: print(*b, sep=" | ")
