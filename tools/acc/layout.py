# layout.py: the game items as fitted wrist accessories (acc_<name>.json); helpers in acclib.py
import sys, json, pickle, numpy as np
sys.path.insert(0, "WORK/tattoo/acc")
from acclib import *
from centerline import centerline, plane_axes
import os; os.makedirs(OUT, exist_ok=True)

# Roler: the strap is a closed loop in its own plane; watch head (biggest piece) + glass rigid
ms = load("assets/content/items/barter/item_barter_valuable_rolex/item_barter_valuable_rolex.bundle", ["item_barter_valuable_rolex_LOD0", "item_barter_valuable_rolex_Glass_LOD0"])
P = ms[0]["P"]; c, a, b, n = plane_axes(P)
lab = pieces(P, ms[0]["tris"]); gl = ms[1]["P"].mean(0)
near = np.array([np.linalg.norm(P[lab == p].mean(0) - gl) < 0.025 for p in range(lab.max() + 1)])
head = int(np.where(near)[0][0]); lab = np.where(near[lab], head, lab)          # case, bezel, crown, hands: one piece
band = lab != head
# loop plane = the plane the strap pieces span; the loop's own axis is the thinnest direction of the band
cb, ab, bb, nb = plane_axes(P[band])
ang = np.arctan2((P[band] - cb) @ bb, (P[band] - cb) @ ab); rr = np.linalg.norm(np.c_[(P[band] - cb) @ ab, (P[band] - cb) @ bb], axis=1)
r0 = np.median(rr); th = np.linspace(0, 2 * np.pi, 360, endpoint=False)
# start the loop at the head so it lands on the back of the wrist
hc = P[lab == head].mean(0) - cb; th0 = np.arctan2(hc @ bb, hc @ ab); th = th + th0
pts = loop_curve(P[band], cb, ab, bb)
pa = np.arctan2((pts - cb) @ bb, (pts - cb) @ ab); pts = np.roll(pts, -int(np.argmin(np.abs(np.angle(np.exp(1j * (pa - th0)))))), 0)   # start at the head
curve = Curve(pts, radial_from(cb), True)
kinds = [(lab, np.where(lab == head, 1, 0)), (np.full(len(ms[1]["P"]), head), np.ones(len(ms[1]["P"]), int), False)]
rv = np.linalg.norm(np.c_[(P[band] - cb) @ ab, (P[band] - cb) @ bb], axis=1)
ROLEX_FIT = float(curve.L / (2 * np.pi) + 0.004)                 # the bracelet's size around (as a radius) + one link of slack
build("rolex", ms, curve, kinds, extra=lambda c, a, r: {"fitR": ROLEX_FIT}, bundle="assets/content/items/barter/item_barter_valuable_rolex/item_barter_valuable_rolex.bundle", note="Roler Submariner gold wrist watch")

# chains: traced centreline in the chain's plane; the long strand piece follows it, clasp / ring / pendant rigid
for nm, bundle, mesh, title in [("chainlet", "assets/content/items/barter/chain/item_chain.bundle", "chain_LOD0", "Chainlet"),
                                ("chain_gold", "assets/content/items/barter/chain/item_chain_gold.bundle", "gold_chain_LOD0", "Golden neck chain"),
                                ("prokill", "assets/content/items/barter/item_barter_valuable_prokill/item_barter_valuable_prokill.bundle", "item_barter_valuable_prokill_LOD0", "Chain with Prokill medallion")]:
    ms = load(bundle, [mesh]); P = ms[0]["P"]; T = np.vstack(ms[0]["tris"])
    lab = pieces(P, ms[0]["tris"]); ext = np.array([np.ptp(P[lab == p], axis=0).max() for p in range(lab.max() + 1)])
    strand = ext.argmax()
    c, a, b, n = plane_axes(P)
    line, L, _ = centerline(P, T, keep=(lab == strand) if nm == "prokill" else None)
    line = recentre(line, n, P[lab == strand])                     # trace hugs one edge and swaps sides -> strip middle
    curve = Curve(line, flat_normal(n), False)
    if nm == "prokill":
        kind = np.where(lab == strand, 0, 2)                       # medallion + its ring hang as a pendant
        kind[(lab != strand) & (ext[lab] < 0.012)] = 1             # small bits (jump ring) stay rigid on the chain
    else:
        kind = np.where(ext[lab] > 0.025, 0, 1)                    # long chain pieces follow the curve, clasp / ring rigid
    build(nm, ms, curve, [(lab, kind)], bundle=bundle, note=title, auto_pendant=nm != "prokill")

# dogtag: the USEC tag with its short ball-chain loop, hanging from the middle of the Chainlet's chain (user: "dogtag
# hanging from a chain bracelet"); the item's own loop is only ~10 cm, too short to go round a wrist
cb_bundle = "assets/content/items/barter/chain/item_chain.bundle"; dt_bundle = "assets/content/items/barter/dog_tags/item_dogtags_usec.bundle"
ch = load(cb_bundle, ["chain_LOD0"]); tg = load(dt_bundle, ["model_LOD0"])
Pc = ch[0]["P"]; labc = pieces(Pc, ch[0]["tris"]); extc = np.array([np.ptp(Pc[labc == p], axis=0).max() for p in range(labc.max() + 1)])
c, a, b, n = plane_axes(Pc); line, L, _ = centerline(Pc, np.vstack(ch[0]["tris"])); line = recentre(line, n, Pc[labc == extc.argmax()]); curve = Curve(line, flat_normal(n), False)
Pt = tg[0]["P"]; labt = pieces(Pt, tg[0]["tris"])
tag_p = int(np.argmax([np.ptp(Pt[labt == p], axis=0).min() < 0.005 and np.ptp(Pt[labt == p], axis=0).max() > 0.03 for p in range(labt.max() + 1)]))
loop_pts = Pt[labt != tag_p]; tcen = Pt[labt == tag_p].mean(0)
top = loop_pts[np.argmax(np.linalg.norm(loop_pts - tcen, axis=1))]            # far end of the loop = where it hangs
mid = curve.p[len(curve.p) // 2]
tg[0]["P"] = Pt - top + mid                                                    # hang it from the chain's middle
kc = np.where(extc[labc] > 0.025, 0, 1)
build("dogtag", ch + tg, curve, [(labc, kc), (np.full(len(Pt), 0), np.full(len(Pt), 2))], bundle=dt_bundle, note="Dogtag USEC hanging from the Chainlet's chain", auto_pendant=True)

# paracord: the hank's long axis is the curve; one turn around the wrist, cross-section scaled to a cuff
bundle = "assets/content/items/barter/item_barter_other_paracord/item_barter_other_paracord.bundle"
ms = load(bundle, ["item_barter_other_paracord_LOD0"]); P = ms[0]["P"]; lab = pieces(P, ms[0]["tris"])
c, a, b, n = plane_axes(P)
xs = (P - c) @ a; line = c + np.outer(np.linspace(xs.min(), xs.max(), 340), a)
curve = Curve(line, flat_normal(n), False)
build("paracord", ms, curve, [(np.zeros(len(P), int), np.zeros(len(P), int))], out_scale=0.12, bundle=bundle, note="Paracord (hank wound as a cuff, cross-section x0.12 = our pick)")

# Sacred Amulet: the bracelet we already wear (string loop + eye pendant, cord dropped) - classes from bracelet/classify.py
d = pickle.load(open("WORK/tattoo/bracelet/amulet.pkl", "rb")); cls = pickle.load(open("WORK/tattoo/bracelet/classes.pkl", "rb"))
bundle = "assets/content/items/spec/item_spec_lightkeeper_amulet/item_spec_lightkeeper_amulet.bundle"
ms = load(bundle, ["item_spec_lightkeeper_amulet_LOD0"]); P = ms[0]["P"]
lab = d["lab"]; kind_s = np.array([cls[l] for l in lab])
keep_t = [tr[(kind_s[tr] != "cord").all(1)] for tr in ms[0]["tris"]]; ms[0]["tris"] = keep_t
# (the item's charm is centred by design - pupil ring at the circle's centre; a 'centring' move here broke it, removed)
arc = kind_s == "arc"; xz = P[arc][:, [0, 2]]
A_ = np.c_[2 * xz, np.ones(len(xz))]; bb_ = (xz ** 2).sum(1); cx, cz, cc_ = np.linalg.lstsq(A_, bb_, rcond=None)[0]; r0 = np.sqrt(cc_ + cx ** 2 + cz ** 2)
eye_c = P[kind_s == "eye"].mean(0); t0 = np.arctan2(eye_c[2] - cz, eye_c[0] - cx)
th = t0 + np.linspace(0, 2 * np.pi, 360, endpoint=False); y0 = float(np.median(P[arc][:, 1]))
pts = np.c_[cx + r0 * np.cos(th), np.full(360, y0), cz + r0 * np.sin(th)]
curve = Curve(pts, radial_from(np.array([cx, y0, cz])), True)
kind = np.where(kind_s == "eye", 2, 1); kind[kind_s == "cord"] = 1
# the whole eye charm hangs as ONE piece (one pid): as separate pendants each part hung from its own nearest point to the string,
# which pulled the charm apart (the eye ended up off the circle's centre)
pid_ = lab.copy(); pid_[kind_s == "eye"] = int(lab[kind_s == "eye"].min())
build("amulet", ms, curve, [(pid_, kind)], bundle=bundle, note="Sacred Amulet (string + eye charm, cord dropped)")

# skull ring: wrapped like the bracelets (its inner surface follows the finger's oval, skull on the back of the finger).
# Hole axis = Unity z (seen in the 3-view render acc/shapes.jpg); centre = circle through the band's inner vertices.
bundle = "assets/content/items/barter/item_barter_valuable_skullring/item_barter_valuable_skullring.bundle"
ms = load(bundle, ["item_barter_valuable_skullring_LOD0"]); P = ms[0]["P"]
axv = np.array([0.0, 0.0, 1.0]); xy = P[:, :2]; c2 = (xy.min(0) + xy.max(0)) / 2
for _ in range(5):
    rr = np.linalg.norm(xy - c2, axis=1); inner = rr < np.percentile(rr, 35)
    A_ = np.c_[2 * xy[inner], np.ones(inner.sum())]; b_ = (xy[inner] ** 2).sum(1); cx, cy, k = np.linalg.lstsq(A_, b_, rcond=None)[0]; c2 = np.array([cx, cy])
cc = np.array([c2[0], c2[1], float(np.median(P[:, 2]))])
rel = P - cc; perp = rel - np.outer(rel @ axv, axv); rad = np.linalg.norm(perp, axis=1)
up = perp[np.argmax(rad)]; up /= np.linalg.norm(up); side = np.cross(axv, up)
r_in = float(np.percentile(rad, 2))
th = np.linspace(0, 2 * np.pi, 180, endpoint=False)                       # starts at the skull
pts = cc + r_in * (np.cos(th)[:, None] * up + np.sin(th)[:, None] * side)
curve = Curve(pts, radial_from(cc), True)
build("skullring", ms, curve, [(np.zeros(len(P), int), np.zeros(len(P), int))], bundle=bundle, note="Gold skull ring (ring finger)")
print(f"            ring hole radius {r_in*1000:.1f} mm")
