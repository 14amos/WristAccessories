# trim_cuffs.py <in dir> <out dir>: BARE hand pieces without the source outfit's wrist things (user 2026-10-27: "what are we gonna do
# about the wrist portion where it has the cuff of the wrist or watch or bracelet"). A hand piece (export_handwear.py, kind "hand") is
# cut by hand-bone weight, so the outfit's sleeve cuff, watch strap or bracelet at the wrist comes along. Per item:
#  - bare = at least 50% of its triangles' texels are skin-coloured (gloves are left as they are: their cuff is the glove);
#  - the wrist plane: through the Palm bone origin, normal = toward the middle finger (from the item's own bind poses);
#  - removed: every triangle whose centre lies up the arm from 5 mm past the wrist plane (user: "i dont want there to just be a gap
#    between the wrist and hand" - deleting only the cuff left a hole, nothing is modelled under a cuff): the wearer's own wrist (their
#    skin, sleeve or watch) carries on there and the new hand starts where theirs is tucked in (HandWear.CutHand, 0.8 at the palm).
# Writes the same file names to <out dir>, a log line per item.
import os, sys, json, glob, base64, colorsys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy
import feet_combo as F
IN, OUT = sys.argv[1], sys.argv[2]; os.makedirs(OUT, exist_ok=True)
CUT = float(os.environ.get("CUFF_CUT", "-0.005")); BAND = float(os.environ.get("CUFF_BAND", "0.035")); IBAND = float(os.environ.get("CUFF_IBAND", "0.05")); TAPER = float(os.environ.get("CUFF_TAPER", "0.03"))   # m from the wrist plane toward the fingers
SCAN = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); PATH = {}
for k, v in SCAN.items():
    p = v["path"].replace("\\", "/"); i = p.lower().find("assets/content/"); j = p.lower().find("/bundles/")
    PATH[p[i:] if i >= 0 else (p[j + 9:] if j >= 0 else p)] = v["path"]
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
def skin_like(rgb):
    h, s, v = colorsys.rgb_to_hsv(*rgb); h *= 360
    return (h < 50 or h > 340) and 0.12 < s < 0.7 and v > 0.22
TEX = {}
def tex(bundle, mat):
    if (bundle, mat) in TEX: return TEX[(bundle, mat)]
    img = None
    try:
        env = UnityPy.load(PATH[bundle])
        for o in env.objects:
            if o.type.name == "Material" and o.peek_name() == mat: img = F.tex_of(o); break
    except Exception: pass
    TEX[(bundle, mat)] = img; return img
for f in sorted(glob.glob(IN + "/hw_*.json")):
    h = json.load(open(f))
    if h["kind"] != "hand": json.dump(h, open(os.path.join(OUT, os.path.basename(f)), "w")); continue
    S = h["side"]; B = {b: np.array(h["bind"][i]).reshape(4, 4) for i, b in enumerate(h["bones"])}
    o = lambda n_: np.linalg.inv(B[n_])[:3, 3] if n_ in B else None
    pc, m3 = o(f"Base Human{S}Palm"), o(f"Base Human{S}Digit31")
    P = arr(h["pos"], "<f4", 3).astype(float); UV = arr(h["uv"], "<f4", 2).astype(float)
    subs = [(s, arr(s["tris"], "<i4", 3)) for s in h["subs"]]
    cols = []
    for s, T in subs:
        img = tex(h["bundle"], s["mat"])
        if img is None: cols.append(None); continue
        uv = UV[T].mean(1) % 1.0; cols.append(img[((1 - uv[:, 1]) * (img.shape[0] - 1)).astype(int), (uv[:, 0] * (img.shape[1] - 1)).astype(int)])
    sk = [None if c is None else np.array([skin_like(x) for x in c]) for c in cols]
    allsk = np.concatenate([x for x in sk if x is not None]) if any(x is not None for x in sk) else np.zeros(0, bool)
    bare = len(allsk) > 0 and allsk.mean() >= 0.5
    if not bare or pc is None or m3 is None:
        json.dump(h, open(os.path.join(OUT, os.path.basename(f)), "w")); print(f"{h['name']}: {'glove' if not bare else 'no bones'} - as it is"); continue
    fd = (m3 - pc) / np.linalg.norm(m3 - pc); gone = 0; total = 0; new_subs = []
    # islands (index-connected triangles over all submeshes): a cuff / strap / bracelet is its own island - one sitting round the
    # wrist (its middle before BAND) that is mostly not skin goes whole (per-triangle colour left torn fringes, an orange cord passed)
    allT = np.concatenate([T for _, T in subs]); par = np.arange(len(P))
    def fnd(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for a, b_, c_ in allT: ra, rb, rc = fnd(a), fnd(b_), fnd(c_); par[rb] = ra; par[fnd(rc)] = ra
    comp = np.array([fnd(a) for a in allT[:, 0]]); dall = (P[allT].mean(1) - pc) @ fd
    skall = np.concatenate([x if x is not None else np.ones(len(T), bool) for (_, T), x in zip(subs, sk)])
    # islands judged by a stricter skin test: saturated orange / tan (cords, leather) is not skin there
    def strict(rgb):
        hh, ss, vv = colorsys.rgb_to_hsv(*rgb); hh *= 360
        return (hh < 45 or hh > 345) and 0.12 < ss < 0.62 and vv > 0.25
    skstrict = np.concatenate([np.array([strict(x) for x in c]) if c is not None else np.ones(len(T), bool) for (_, T), c in zip(subs, cols)])
    island_drop = set()
    for cc in np.unique(comp):
        m_ = comp == cc
        if dall[m_].mean() < IBAND and (~skstrict[m_]).mean() > 0.35: island_drop.add(int(cc))
    # the skin's radius round the forearm axis per 5 mm x 30 deg cell near the wrist (for the raised-cuff test)
    f3_ = o(f"Base Human{S}Forearm3"); RAISE = None
    if f3_ is not None:
        RAX0 = f3_; RAXD = (pc - f3_) / np.linalg.norm(pc - f3_); RE1 = np.cross(RAXD, [0, 0, 1.0]); RE1 = RE1 / np.linalg.norm(RE1) if np.linalg.norm(RE1) > 1e-6 else np.array([1.0, 0, 0]); RE2 = np.cross(RAXD, RE1)
        cells = {}
        for (s0, T0), sk0 in zip(subs, sk):
            if sk0 is None: continue
            c0 = P[T0].mean(1); d0 = (c0 - pc) @ fd; rel0 = c0 - RAX0; rv0 = rel0 - np.outer(rel0 @ RAXD, RAXD); r0 = np.linalg.norm(rv0, axis=1)
            a0 = ((np.arctan2(rv0 @ RE2, rv0 @ RE1) % (2 * np.pi)) / (2 * np.pi) * 12).astype(int) % 12; b0 = np.clip(((d0 - CUT) / 0.005).astype(int), 0, 39)
            for ok, bb, aa, rr0 in zip(sk0, b0, a0, r0):
                if ok: cells.setdefault((int(bb), int(aa)), []).append(rr0)
        RAISE = {k: float(np.median(v)) for k, v in cells.items() if len(v) >= 3}
    off = 0
    for (s, T), skn in zip(subs, sk):
        d = (P[T].mean(1) - pc) @ fd; total += len(T); cm = comp[off:off + len(T)]; off += len(T)
        isl = np.isin(cm, list(island_drop)) if island_drop else np.zeros(len(T), bool)
        # up the arm from the wrist joint: everything (the wearer's own wrist carries on); the first BAND onto the hand: what is not
        # skin (cuffs, straps, bracelets sit -1..+2.5 cm round the joint); further on: everything stays (tattoos on the back of the hand)
        drop = (d < CUT) | isl | ((d < BAND) & (~skn if skn is not None else False))
        # a cuff stitched into the skin island further onto the hand (BEAR Lonsdale left: 3-5 cm, where the ink starts too): non-skin
        # triangles RAISED off the skin (> 0.8 mm over the skin's radius round the arm axis in that 5 mm x 30 deg cell) go; ink lies on it
        if skn is not None and RAISE is not None and os.environ.get("CUFF_RAISE", "1") == "1":
            cen = P[T].mean(1); rel = cen - RAX0; al_ = rel @ RAXD; rv = rel - np.outer(al_, RAXD); rr = np.linalg.norm(rv, axis=1)
            ang = ((np.arctan2(rv @ RE2, rv @ RE1) % (2 * np.pi)) / (2 * np.pi) * 12).astype(int) % 12; db = np.clip(((d - CUT) / 0.005).astype(int), 0, 39)
            ref = RAISE.get
            raised = np.array([(not sk_) and 0 <= di < 14 and ref((di, ai), 9) + 0.0008 < r_ for sk_, di, ai, r_ in zip(skn, db, ang, rr)])
            drop |= raised
        gone += int(drop.sum()); keep = T[~drop]
        if len(keep): new_subs.append(dict(s, tris=b64(keep.ravel(), "<i4")))
    # scraps: after the cuts, kept pieces under 40 triangles near the wrist no longer joined to the hand (cuff fringe) go
    keptT = [(i, arr(ns["tris"], "<i4", 3)) for i, ns in enumerate(new_subs)]
    if keptT:
        par2 = np.arange(len(P))
        def f2(x):
            while par2[x] != x: par2[x] = par2[par2[x]]; x = par2[x]
            return x
        for _, T2 in keptT:
            for a, b_, c_ in T2: ra, rb, rc = f2(a), f2(b_), f2(c_); par2[rb] = ra; par2[f2(rc)] = ra
        allk = np.concatenate([T2 for _, T2 in keptT]); comp2 = np.array([f2(a) for a in allk[:, 0]]); d2 = (P[allk].mean(1) - pc) @ fd
        small = {int(cc) for cc in np.unique(comp2) if (comp2 == cc).sum() < 40 and d2[comp2 == cc].mean() < 0.08}
        if small:
            for i, T2 in keptT:
                m2 = np.isin(np.array([f2(a) for a in T2[:, 0]]), list(small)); gone += int(m2.sum())
                new_subs[i] = dict(new_subs[i], tris=b64(T2[~m2].ravel(), "<i4"))
    # taper: the hand's last TAPER above the cut drawn in toward the wrist axis (15% at the cut, none at TAPER) - under the cuff the
    # source hand is wider than the wearer's wrist, a ledge showed at the cut (user 2026-10-27, BEAR Lonsdale)
    f3 = o(f"Base Human{S}Forearm3"); ax0 = pc if f3 is None else f3
    axd = fd if f3 is None else (pc - f3) / np.linalg.norm(pc - f3)
    dv = (P - pc) @ fd; t = np.clip((dv - CUT) / TAPER, 0, 1); k = 1 - 0.15 * (1 - t)
    rel = P - ax0; along = rel @ axd; foot = ax0 + np.outer(along, axd); P = foot + (P - foot) * k[:, None]
    h["pos"] = b64(P, "<f4")
    h["subs"] = new_subs; h["cuff_trim"] = gone
    json.dump(h, open(os.path.join(OUT, os.path.basename(f)), "w"))
    print(f"{h['name']}: bare ({allsk.mean() * 100:.0f}% skin), {gone} of {total} triangles up the arm from {CUT * 1000:.0f} mm past the wrist removed")
