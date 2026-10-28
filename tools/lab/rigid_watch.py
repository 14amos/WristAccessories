# rigid_watch.py <acc json> [out]: an outfit watch "no moving hands" cut as strap (its case kind 0: bent / stretched round the
# arm, user 2026-10-26) -> the case made ONE rigid piece in its own flat shape, placed on the back of the wrist, turned 180 deg
# like the plugin's TurnWatch (the game's way to wear it).
#  case = the contiguous stretch of the loop (2 mm bins) round the highest point where the item stands >= 45 % of its max off
#  the arm, +2 mm; each vertex there: its place in the source cross-section from (s, r, dt) at source radius R = L / 2 pi
#  (u along the head's tangent, v along its radial), stored as rigid: s = head centre, dt = -u, r = v - R; normals/tangents
#  rotated into the head frame. Then the loop shifted so the head sits at +33 deg (back of the wrist, as WatchFace "back")
#  and the whole item turned 180 deg about the head's radial axis.
import sys, json, math, numpy as np
# options: --range LO HI (mm, the case's stretch of the loop when the highest point is not the case: Black Division's buckle
# stands taller than its case), --noturn (a source cut the other way round already reads right: Bomber Jacket's gold watch)
args = [a for a in sys.argv[1:]]; rng = None; turn = True
if "--range" in args: i = args.index("--range"); rng = (float(args[i + 1]) / 1000, float(args[i + 2]) / 1000); del args[i:i + 3]
if "--noturn" in args: args.remove("--noturn"); turn = False
src = args[0]; out = args[1] if len(args) > 1 else src
A = json.load(open(src, encoding="utf-8")); L = A["L"]; R = L / (2 * math.pi)
S = np.concatenate([np.array(m["s"]) for m in A["meshes"]]); Rr = np.concatenate([np.array(m["r"]) for m in A["meshes"]])
nb = int(L / 0.002); b = (np.mod(S, L) / L * nb).astype(int) % nb
mx = np.array([Rr[b == i].max() if (b == i).any() else 0 for i in range(nb)])
top = int(mx.argmax()); thr = 0.45 * mx[top]
lo = top; hi = top
while mx[(lo - 1) % nb] >= thr and (top - lo) < nb // 3: lo -= 1
while mx[(hi + 1) % nb] >= thr and (hi - top) < nb // 3: hi += 1
s_lo, s_hi = ((lo - 1) * L / nb, (hi + 2) * L / nb) if rng is None else rng   # +-1 bin (2 mm)
sc = (s_lo + s_hi) / 2
def inside(s):
    d = (s - s_lo) % L
    return d <= (s_hi - s_lo)
newpid = max([max(m["pid"]) for m in A["meshes"] if m["pid"]] + [0]) + 1
nconv = 0; hs = []
for m in A["meshes"]:
    for i in range(len(m["s"])):
        s, r, dt = m["s"][i], m["r"][i], m["dt"][i]
        if not inside(s): continue
        dl = ((s - sc + L / 2) % L) - L / 2                                # arc from the head centre
        d = -dl / R                                                        # phi falls as s grows
        rho = R + r
        u = rho * math.sin(d) - dt * math.cos(d); v = rho * math.cos(d) + dt * math.sin(d)
        m["s"][i] = sc; m["dt"][i] = -u; m["r"][i] = v - R; m["kind"][i] = 1; m["pid"][i] = newpid
        n = m["n"][i]; m["n"][i] = [n[0] * math.cos(d) - n[1] * math.sin(d), n[0] * math.sin(d) + n[1] * math.cos(d), n[2]]
        t = m["t"][i]; m["t"][i] = [t[0] * math.cos(d) - t[1] * math.sin(d), t[0] * math.sin(d) + t[1] * math.cos(d), t[2], t[3]]
        hs.append(m["h"][i]); nconv += 1
# put the head on the back of the wrist: Place maps phi = -2 pi s / L for items that are not watch-slot watches
target = math.radians(33); sc_new = (-target * L / (2 * math.pi)) % L; shift = sc_new - sc
hc = float(np.mean(hs))
for m in A["meshes"]:
    for i in range(len(m["s"])):
        s = (m["s"][i] + shift) % L
        # turn 180 deg about the head's radial axis (as WristFit.TurnWatch): mirror round the loop and along the arm about the head
        if not turn: m["s"][i] = s; continue
        m["s"][i] = (2 * sc_new - s) % L; m["h"][i] = 2 * hc - m["h"][i]; m["dt"][i] = -m["dt"][i]
        n = m["n"][i]; m["n"][i] = [-n[0], n[1], -n[2]]; t = m["t"][i]; m["t"][i] = [-t[0], t[1], -t[2], t[3]]
if turn: A["hmid"] = 2 * hc - A["hmid"]
A["note"] = A["note"].replace("(no moving hands)", "(no moving hands, rigid case)") if "rigid case" not in A["note"] else A["note"]
json.dump(A, open(out, "w", encoding="utf-8"))
print(f"{A['name']}: case {s_lo * 1000:.0f}..{s_hi * 1000:.0f} mm of {L * 1000:.0f} (max height {mx[top] * 1000:.1f} mm), {nconv} verts made rigid (piece {newpid}), head moved to +33 deg, {'turned 180' if turn else 'not turned'}")
