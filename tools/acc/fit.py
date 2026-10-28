# fit.py: reference of WristAccessories' runtime fit (the C# port must match this).
# outline o (one side's wrist or ring entry of outlines.json), layout A (acc_<name>.json), axial centre aC (m from
# the wrist joint along ax, negative = toward the elbow), phase phi0 (0 = back of the hand). Returns per mesh
# (positions, normals) in the outline's bone space, plus the axial range it occupies.
import json, base64, numpy as np
CLR = 0.002


def table(o):
    t = np.frombuffer(base64.b64decode(o["tab"]), dtype="<u2").astype(float).reshape(o["na"], o["nd"]) / 10000
    return t


def frame(o):
    w, ax, e1 = (np.array(o[k]) for k in ("w", "ax", "e1"))
    return w, ax, e1, np.cross(ax, e1)


def R_cells(tab, o, phis, a_lo, a_hi):
    nd = o["nd"]; ks = np.round(np.mod(phis, 2 * np.pi) / (2 * np.pi / nd)).astype(int)
    kk = np.unique(np.r_[ks, ks - 1, ks + 1] % nd)
    j0 = int(np.clip(np.floor((a_lo - o["a0"]) / o["da"]), 0, o["na"] - 1)); j1 = int(np.clip(np.floor((a_hi - o["a0"]) / o["da"]), 0, o["na"] - 1))
    return tab[j0:j1 + 1][:, kk].max()


def rigid_height(tab, o, phi0, a, dt, r):
    """lowest anchor height H at which every vertex of a rigid piece clears the sleeve cell under it: a vertex sits at
    radius sqrt((H + r)^2 + dt^2), angle phi0 - atan(dt / (H + r)), so H >= sqrt(S^2 - dt^2) - r (fixed point)"""
    H = R_cells(tab, o, np.array([phi0]), a.min(), a.max())
    for _ in range(4):
        need = 0.0
        for i in range(len(a)):
            S = R_cells(tab, o, np.array([phi0 - np.arctan2(dt[i], max(H + r[i], 0.005))]), a[i] - 0.002, a[i] + 0.002)
            if S > abs(dt[i]): need = max(need, np.sqrt(S * S - dt[i] * dt[i]) - r[i])
        done = abs(need - H) < 0.0002; H = need
        if done: break
    return H


def turns_of(A, o, tab, aC):
    if A["closed"]: return 1
    j = int(np.clip(np.floor((aC - o["a0"]) / o["da"]), 0, o["na"] - 1))
    Rbar = tab[max(0, j - 3):j + 4].mean() + CLR + 0.003
    return max(1, int(round(A["L"] / (2 * np.pi * Rbar))))


def extent(A, turns):
    """axial range (lo, hi) relative to aC, incl. the helix of open chains"""
    strand_h = [np.array(m["h"])[np.array(m["kind"]) == 0] for m in A["meshes"]]
    sw = max((np.ptp(h) for h in strand_h if len(h)), default=0.0)
    pitch = sw + 0.0015
    lo = min(np.min(m["h"]) for m in A["meshes"]) - A["hmid"]; hi = max(np.max(m["h"]) for m in A["meshes"]) - A["hmid"]
    if not A["closed"] and turns > 1: lo -= pitch * turns / 2; hi += pitch * turns / 2
    return lo, hi, pitch


def place(A, o, aC, phi0=0.0, turns=None):
    tab = table(o); w, ax, e1, e2 = frame(o)
    if turns is None: turns = turns_of(A, o, tab, aC)
    lo, hi, pitch = extent(A, turns)
    pre = []
    for m in A["meshes"]:
        s, dt, r, h = (np.array(m[k]) for k in ("s", "dt", "r", "h")); pid = np.array(m["pid"]); kind = np.array(m["kind"])
        u = s / A["L"]
        phi = phi0 - 2 * np.pi * u * (1 if A["closed"] else turns)          # band runs -tn so (T, N, B) -> (-tn, rn, ax)
        helix = 0.0 if A["closed"] or turns == 1 else pitch * (turns * u - turns / 2)
        pre.append((m, dt, r, pid, kind, phi, aC - A["hmid"] + helix + h))
    # rigid / pendant pieces: ONE height per piece across meshes (a watch case and its glass are two meshes, one piece)
    Hp = {}
    for p in sorted({int(q) for _, _, _, pid, kind, _, _ in pre for q in pid[kind > 0]}):
        sl = [pid == p for _, _, _, pid, _, _, _ in pre]
        phi_p = next(phi[q][0] for (_, _, _, _, _, phi, _), q in zip(pre, sl) if q.any())
        cat = lambda k: np.concatenate([x[k][q] for x, q in zip(pre, sl)])
        Hp[p] = rigid_height(tab, o, phi_p, cat(6), cat(1), cat(2))
    out = []
    for m, dt, r, pid, kind, phi, a in pre:
        n = np.array(m["n"]); R = np.zeros(len(a))
        for i in range(len(a)):
            R[i] = Hp[int(pid[i])] if kind[i] > 0 else R_cells(tab, o, np.array([phi[i]]), a[i] - 0.004, a[i] + 0.004)
        R += CLR
        rn = np.cos(phi)[:, None] * e1 + np.sin(phi)[:, None] * e2; tn = -np.sin(phi)[:, None] * e1 + np.cos(phi)[:, None] * e2
        rigid = kind > 0
        pos = w + ax * a[:, None] + rn * (R + r)[:, None]
        pos[rigid] += -tn[rigid] * dt[rigid][:, None]
        nrm = -tn * n[:, 0:1] + rn * n[:, 1:2] + ax * n[:, 2:3]
        out.append(dict(mesh=m["mesh"], pos=pos, nrm=nrm, uv=np.array(m["uv"]), tris=m["tris"]))
    return out, (aC + lo, aC + hi), turns


def stack(items, o, gap=0.003):
    """items placed from the side's start toward the elbow, each clear of the previous one"""
    tab = table(o); edge = o["start"]; res = []
    for A in items:
        turns = turns_of(A, o, tab, edge - 0.01)
        lo, hi, _ = extent(A, turns)
        aC = edge - hi
        placed, rng, turns = place(A, o, aC, 0.0, turns)
        res.append((A["name"], placed, rng, turns)); edge = rng[0] - gap
    return res
