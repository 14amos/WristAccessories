# feet_fit.py: SHOES v2 fit (user 2026-10-27 shot: own sock band between the cuff and the new shoe, a scrap of the source's cuff).
# Lower outfits keep boots / socks / soles as their OWN texture islands (index-connected triangles) apart from the pants legs
# (lab/feet_islands.py: boot islands foot weight 0.25-1.0, pants islands <= 0.04), so:
#   shoe = every island with mean Foot / Toe weight >= ISLAND (whole: no cuff scraps, the full shaft);
#   per pants a profile (both legs, rest pose, mesh space = every outfit's, up = +z): leg axis, cuff bottom z_c (lowest pants
#   vertex on that leg), own boot radius Rb(z, a) (own boot islands after the plugin's foot shrink) and pants radius Rp(z, a);
#   the shoe's shaft (z > ZA) stretched up to z_c + TUCK when shorter, then per (z, a) bin scaled round the leg axis to lie
#   over the own boot (>= Rb + GAP, below the cuff) and inside the pants (<= Rp - GAP, under the cuff), bins smoothed.
# Runtime (HandWear.FitShoe) does the same with the profile from shoe_profiles.json (by pants mesh name).
import numpy as np
import json as _json, os as _os
def _ankles():
    f = "WORK/wristacc/lab/feetscan/ref_bind.json"
    if not _os.path.exists(f): return {}
    R = _json.load(open(f)); out = {}
    for S in "LR":
        o = np.linalg.inv(np.array(R[f"Base Human{S}Foot"]).reshape(4, 4))[:3, 3]; out["n" if o[0] < 0 else "p"] = o[:2]
    return out
ANKLE = _ankles()
SK = 4
_BARY = np.array([(i / SK, j / SK, 1 - (i + j) / SK) for i in range(SK + 1) for j in range(SK + 1 - i)])
def surf(P, T):
    """points over the triangles' surfaces (barycentric grid, (SK+1)(SK+2)/2 per triangle): coarse meshes span bins"""
    return np.einsum("bk,tkd->tbd", _BARY, P[T]).reshape(-1, 3) if len(T) else np.zeros((0, 3))                         # per leg ("n": x < 0) the Foot bone origin in the common rest space (xy)
ISLAND = 0.15; ZA = 0.09; TUCK = 0.015; GAP = 0.003; ZB = 0.01; NA = 24; ZMAX = 0.45
NZ = int(round(ZMAX / ZB))
def islands(T, nv):
    par = np.arange(nv)
    def f(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for a, b, c in T: ra, rb, rc = f(a), f(b), f(c); par[rb] = ra; par[f(rc)] = ra
    return np.array([f(a) for a in T[:, 0]])
def foot_w(names, bi, bw):
    fb = [i for i, x in enumerate(names) if "Foot" in x or "Toe" in x]
    return np.where(np.isin(bi, fb), bw, 0).sum(1)
ZISL = 0.55; SHARE = 0.45; MAXST = 2.5; R0 = 0.08; R1 = 0.13; RC = 0.10; RH = 0.14; SOFTST = 1.6; OVER = 0.01
def boot_tris(subs, fw, nv, P=None):
    """per submesh: mask of triangles in boot islands (mean foot weight >= ISLAND, below ZISL); an island reaching higher (pants
    leg and boot in one island: Wedge, some RT pants) is cut per triangle (mean foot weight > 0.35) like the 0.10.0 cut"""
    T = np.concatenate(subs); comp = islands(T, nv); keep = np.zeros(len(T), bool)
    for c in np.unique(comp):
        sel = comp == c; v = np.unique(T[sel])
        if fw[v].mean() < ISLAND: continue
        if (fw[v] > 0.35).mean() < SHARE: continue                                     # mostly not on the foot: a cuff band (ScavTac joggers) or a tall shaft - pants
        if P is None or P[v, 2].max() < ZISL: keep |= sel
        else: keep |= sel & (fw[T].mean(1) > 0.35)
    out = []; k = 0
    for s in subs: out.append(keep[k:k + len(s)]); k += len(s)
    return out
def _bins(P, ax):
    d = P[:, :2] - ax; r = np.linalg.norm(d, axis=1); a = (np.arctan2(d[:, 1], d[:, 0]) % (2 * np.pi)) / (2 * np.pi) * NA
    return r, a, P[:, 2] / ZB
def _grid(P, ax, sel, red):
    g = np.full((NZ, NA), np.nan)
    if sel.sum() == 0: return g
    r, a, z = _bins(P[sel], ax); zi = np.clip(z.astype(int), 0, NZ - 1); ai = a.astype(int) % NA
    for i, j, v in zip(zi, ai, r): g[i, j] = v if np.isnan(g[i, j]) else red(g[i, j], v)
    return g
def profile(P_own, subs, fw):
    """P_own: own vertices as the plugin leaves them (feet shrunk). Per leg (x < 0 / x > 0): axis, z_c, Rb, Rp grids (nan = none)"""
    T = np.concatenate(subs); bt = np.concatenate(boot_tris(subs, fw, len(P_own), P_own))
    bootv = np.zeros(len(P_own), bool); bootv[np.unique(T[bt])] = True
    pantv = np.zeros(len(P_own), bool); pantv[np.unique(T[~bt])] = True; pantv &= ~bootv
    legs = {}
    for side, m in (("n", P_own[:, 0] < 0), ("p", P_own[:, 0] >= 0)):
        pv = pantv & m & (P_own[:, 2] < 0.6)
        if pv.sum() == 0: continue
        zc = float(P_own[pv, 2].min())
        ax = ANKLE[side] if ANKLE else P_own[pv & (P_own[:, 2] < zc + 0.05), :2].mean(0)   # the ankle joint (v4: the hem ring's centre sat 8.5 cm behind it on slanted hems)
        rb = _grid(P_own, ax, bootv & m, max); rp = _grid(P_own, ax, pv, max)
        # own boot showing: bins where it is outside the pants (or no pants there) - its top is where the new shoe must reach
        exp = ~np.isnan(rb) & (np.isnan(rp) | (rb >= rp - 0.001)); zs = np.where(exp.any(1))[0]
        # the hem per direction round the leg (lowest pants vertex in each 15 deg bin): a slanted hem is higher in front
        Tp = T[~bt]; Tp = Tp[(m[Tp].all(1)) & (P_own[Tp][:, :, 2] < 0.6).all(1)]; S_ = surf(P_own, Tp)
        d_ = S_[:, :2] - ax; a_ = ((np.arctan2(d_[:, 1], d_[:, 0]) % (2 * np.pi)) / (2 * np.pi) * NA).astype(int) % NA
        hem = np.full(NA, np.nan); near = np.linalg.norm(d_, axis=1) < RH
        for j, zz in zip(a_[near], S_[near, 2]): hem[j] = zz if np.isnan(hem[j]) else min(hem[j], zz)
        legs[side] = dict(ax=ax.tolist(), zc=zc, rb=rb, rp=rp, hem=hem, exp_top=float((zs.max() + 1) * ZB) if len(zs) else 0.0)
    return legs
def _smooth(g):
    out = g.copy()
    for i in range(g.shape[0]):
        for j in range(g.shape[1]):
            nb = [g[ii, jj % NA] for ii in range(max(0, i - 1), min(g.shape[0], i + 2)) for jj in (j - 1, j, j + 1)]
            nb = [x for x in nb if not np.isnan(x)]
            out[i, j] = np.mean(nb) if nb else np.nan
    return out
HIDDEN = True   # 0.10.1: the plugin removes the own boot islands (HandWear.HideOwnBoots); False = old foot shrink only
def fit(P_shoe, legs, log=None, use=None, tris=None):
    """the shoe's vertices fitted to these pants (copy); use: mask of the shoe's own vertices (default all)"""
    P = P_shoe.copy(); use = np.ones(len(P), bool) if use is None else use; cut = {}
    for side, m in (("n", (P[:, 0] < 0) & use), ("p", (P[:, 0] >= 0) & use)):
        L = legs.get(side)
        if L is None or m.sum() == 0: continue
        ax = np.array(L["ax"]); zc = L["zc"]; Q = P[m]
        top = Q[:, 2].max(); st = 1.0
        # v4 (user 16:56, Crye AC pants: invisible ankles): the collar must reach the hem ALL ROUND the leg, not just at its
        # highest point. Per 15 deg direction: collar = the shoe's highest vertex, need = hem + TUCK; the ankle column stretched
        # per direction (smoothed); all own boot out only if every direction then reaches its hem, else the own boot stays
        # above the collar's lowest point - OVER.
        hem = np.asarray(L.get("hem", np.full(NA, zc)), float)
        Tl = None
        if tris is not None:                                                            # this leg's triangles, in Q's indices
            gi = -np.ones(len(P), int); gi[np.where(m)[0]] = np.arange(m.sum()); Tl = gi[tris]; Tl = Tl[(Tl >= 0).all(1)]
        def collar(Q):
            X = surf(Q, Tl) if Tl is not None else Q
            d_ = X[:, :2] - ax; a_ = ((np.arctan2(d_[:, 1], d_[:, 0]) % (2 * np.pi)) / (2 * np.pi) * NA).astype(int) % NA
            c = np.full(NA, np.nan); near = np.linalg.norm(d_, axis=1) < RC              # the collar: round the ankle, not the toe
            for j, zz in zip(a_[near], X[near, 2]): c[j] = zz if np.isnan(c[j]) else max(c[j], zz)
            d2 = Q[:, :2] - ax; aa = ((np.arctan2(d2[:, 1], d2[:, 0]) % (2 * np.pi)) / (2 * np.pi) * NA).astype(int) % NA
            return c, aa
        c0, aa = collar(Q); zs = min(ZA, 0.5 * top)
        need = hem + TUCK
        stj = np.where(np.isnan(need) | np.isnan(c0), 1.0, (need - zs) / np.maximum(c0 - zs, 1e-3))
        stj = np.clip(stj, 1.0, None); stj = np.maximum(stj, np.maximum(np.roll(stj, 1), np.roll(stj, -1)))   # neighbours: no seams
        full = not HIDDEN or stj.max() <= SOFTST
        if full and stj.max() > 1.0:
            rr = np.linalg.norm(Q[:, :2] - ax, axis=1); wr = np.clip((R1 - rr) / (R1 - R0), 0, 1); up = Q[:, 2] > zs
            fac = 1 + (np.minimum(stj[aa], MAXST) - 1) * wr; Q[up, 2] = zs + (Q[up, 2] - zs) * fac[up]; st = float(stj.max())
        c1, _ = collar(Q); short = (hem - c1)[~np.isnan(hem) & ~np.isnan(c1)]
        if HIDDEN and full and len(short) and short.max() > -TUCK + 0.003: full = False         # still under the hem somewhere
        # per direction: own boot triangles below the collar there - OVER are removed (all of them when full); where the shoe is
        # open (a sneaker's throat over the instep) the own boot stays and shows as the instep / tongue
        cc = np.where(np.isnan(c1), -np.inf, c1); cmin = np.minimum(cc, np.minimum(np.roll(cc, 1), np.roll(cc, -1)))
        Hd = np.full(NA, np.inf) if full else cmin - OVER
        H = float(np.nanmin(Hd)) if not full else np.inf
        cut[side] = Hd
        r, a, z = _bins(Q, ax); zi = np.clip(z.astype(int), 0, NZ - 1); ai = a.astype(int) % NA
        rs = _grid(Q, ax, np.ones(len(Q), bool), max)
        rb, rp = np.array(L["rb"], float), np.array(L["rp"], float)
        s = np.full((NZ, NA), np.nan)
        for i in range(NZ):
            zz = (i + 0.5) * ZB
            if zz < ZA: continue
            for j in range(NA):
                if np.isnan(rs[i, j]): continue
                b_, p_ = rb[i, j], rp[i, j]
                out_b = not np.isnan(b_) and (np.isnan(p_) or b_ >= p_ - 0.001)          # own boot outside: cover it
                if HIDDEN and not full and out_b and zz > Hd[j] - 0.015:                 # own boot shaft kept above the shoe: shoe over it where they meet
                    lo, hi = b_ + GAP, np.inf
                elif HIDDEN:                                                              # own boot gone: only the pants matter
                    tucked = out_b and not np.isnan(p_)                                   # pants were inside the own boot: shoe over them
                    lo = p_ + GAP if tucked else 0.0
                    hi = p_ - GAP if not np.isnan(p_) and not tucked else np.inf
                else:
                    lo = b_ + GAP if out_b else 0.0
                    hi = p_ - GAP if not np.isnan(p_) and not out_b else np.inf          # pants outside: stay under them
                t = min(max(rs[i, j], lo), hi) if lo <= hi else hi
                s[i, j] = t / rs[i, j]
        s = _smooth(s); fac = s[zi, ai]; fac = np.where(np.isnan(fac) | (Q[:, 2] < ZA), 1.0, fac)
        d = Q[:, :2] - ax; Q[:, :2] = ax + d * fac[:, None]; P[m] = Q
        if log is not None: log.append(f"{side}: {'all' if full else 'below ' + format(float(np.min(Hd)), '.3f') + '..' + format(float(np.max(Hd)), '.3f')} top {top:.3f} cuff {zc:.3f} hem {np.nanmin(hem):.3f}..{np.nanmax(hem):.3f} collar {np.nanmin(c1):.3f}..{np.nanmax(c1):.3f} stretch {st:.2f} scale {np.nanmin(s) if np.isfinite(np.nanmin(s)) else 1:.2f}..{np.nanmax(s) if np.isfinite(np.nanmax(s)) else 1:.2f}")
    return (P, cut) if HIDDEN else P

def tri_dir(P_rest, T):
    """per triangle: its leg ('n' x < 0) and the 15 deg direction of its centroid round that leg's ankle"""
    c = P_rest[T].mean(1); neg = c[:, 0] < 0; ax = np.where(neg[:, None], ANKLE["n"], ANKLE["p"]); d = c[:, :2] - ax
    return neg, (((np.arctan2(d[:, 1], d[:, 0]) % (2 * np.pi)) / (2 * np.pi) * NA).astype(int) % NA)
def hidden_tris(T, B, P_rest, cut):
    """own boot triangles removed for these per-direction cut heights (per leg: x < 0 'n' / x >= 0 'p'; inf = all)"""
    zmax = P_rest[T][:, :, 2].max(1); neg, dj = tri_dir(P_rest, T); out = np.zeros(len(T), bool)
    for side, m in (("n", neg), ("p", ~neg)):
        if side in cut: out |= B & m & (zmax < np.asarray(cut[side])[dj])
    return out
