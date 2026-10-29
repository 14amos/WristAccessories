# acclib.py (shared by layout.py / watches.py): wrist-accessory layouts for WristAccessories' runtime fit (acc_<name>.json in the plugin folder).
# Every vertex of the game model gets band coordinates relative to a curve through the accessory:
#   s  = arc length along the curve (m), dt = tangential offset, r = outward offset, h = axial offset,
#   normal / tangent as (T, N, B) components, uv; plus its piece and how the piece is placed:
#   kind 0 = strand (each vertex follows the curve), 1 = rigid (the piece moves as a whole, anchored at its mean s),
#   2 = pendant (rigid, laid flat on the sleeve, hanging toward the hand from its anchor).
# Curves: "loop" (closed: angle around the loop's centre in its plane), "open" (traced centreline of the chain),
# "line" (the model's long axis). The game material is taken at runtime from the item's own prefab (mesh name).
import sys, json, pickle, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
sys.path.insert(0, "WORK/tattoo/acc"); from centerline import centerline, plane_axes

G = "SPT/EscapeFromTarkov_Data/StreamingAssets/Windows/"
OUT = "WORK/wristacc/acc/"


def load(bundle, names):
    env = UnityPy.load(G + bundle); res = []
    for want in names:
        for o in env.objects:
            if o.type.name == "Mesh" and o.peek_name() == want:
                m = o.read(); h = MeshHandler(m); h.process()
                tris = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
                res.append(dict(name=want, bundle=bundle, P=np.array(h.m_Vertices), N=np.array(h.m_Normals)[:, :3], T=np.array(h.m_Tangents) if h.m_Tangents else np.zeros((len(h.m_Vertices), 4)),
                                UV=np.array(h.m_UV0), tris=tris))
    return res


def pieces(P, tris):
    T = np.vstack(tris); key = np.round(P / 1e-5).astype(np.int64); _, inv = np.unique(key, axis=0, return_inverse=True); inv = inv.ravel()
    Tw = inv[T]; m = inv.max() + 1
    g = coo_matrix((np.ones(Tw.size * 2), (np.r_[Tw[:, 0], Tw[:, 1], Tw[:, 2], Tw[:, 1], Tw[:, 2], Tw[:, 0]], np.r_[Tw[:, 1], Tw[:, 2], Tw[:, 0], Tw[:, 0], Tw[:, 1], Tw[:, 2]])), shape=(m, m))
    return connected_components(g, directed=False)[1][inv]


class Curve:
    """Polyline curve with a frame: T (tangent), N (outward when worn), B (axial when worn)."""
    def __init__(self, pts, Nfun, closed):
        self.p = pts; self.closed = closed
        d = np.diff(np.r_[pts, pts[:1]] if closed else pts, axis=0); seg = np.linalg.norm(d, axis=1)
        self.s = np.r_[0, np.cumsum(seg)][:len(pts)]; self.L = float(seg.sum())
        t = np.gradient(pts, axis=0) if not closed else (np.roll(pts, -1, 0) - np.roll(pts, 1, 0))
        self.T = t / np.linalg.norm(t, axis=1, keepdims=True)
        self.N = Nfun(pts, self.T); self.N -= self.T * (self.N * self.T).sum(1, keepdims=True); self.N /= np.linalg.norm(self.N, axis=1, keepdims=True)
        self.B = np.cross(self.T, self.N)

    def project(self, X):
        i = np.argmin(((X[:, None, :] - self.p[None]) ** 2).sum(2), axis=1)
        rel = X - self.p[i]
        return i, (rel * self.T[i]).sum(1), (rel * self.N[i]).sum(1), (rel * self.B[i]).sum(1)


def build(name, meshes, curve, kinds, out_scale=1.0, closed=None, bundle="", note="", auto_pendant=False, extra=None, gap=None):
    """kinds: per mesh, per vertex piece kind (0/1/2); piece ids per mesh (offset to be global)."""
    verts = []; pid_base = 0; recs = []
    # pre-pass: global piece ids; a piece spanning several meshes (watch case + glass + dial) gets ONE anchor
    gp = []; base = 0
    for m, kk in zip(meshes, kinds):
        own = len(kk) > 2 and kk[2] is False; g = kk[0] + (0 if own else base); gp.append(g); base = max(base, int(g.max()) + 1)
    allP = np.vstack([m["P"] for m in meshes]); allpid = np.concatenate(gp); anchors = {}
    def anchor_of(p, kind):
        if p not in anchors:
            Q = allP[allpid == p]
            ia = int(np.argmin(np.linalg.norm(curve.p - Q.mean(0), axis=1)))
            dd = np.linalg.norm(Q - curve.p[ia], axis=1); vi = int(np.argmin(dd)); shift = np.zeros(3)
            if kind == 1 and dd[vi] > 0.012:                     # a stray bit (clasp lying in a spiral's middle): onto the curve
                g = curve.p[ia] - Q[vi]; shift = g * (1 - 0.001 / np.linalg.norm(g))
            anchors[p] = (ia, shift)
        return anchors[p]
    for mi, (m, kk) in enumerate(zip(meshes, kinds)):
        pid, kind = kk[0], kk[1]; own = len(kk) > 2 and kk[2] is False          # False: pid shared with an earlier mesh
        i, dt, r, h = curve.project(m["P"])
        s = curve.s[i]
        Tm, Nm, Bm = curve.T[i], curve.N[i], curve.B[i]
        rec = dict(mesh=m["name"], bundle=m.get("bundle", ""), mat=m.get("mat", -1), s=s, dt=dt, r=r * out_scale, h=h * out_scale, pid=pid + (0 if own else pid_base), kind=kind,
                   n=np.c_[(m["N"] * Tm).sum(1), (m["N"] * Nm).sum(1), (m["N"] * Bm).sum(1)],
                   t=np.c_[(m["T"][:, :3] * Tm).sum(1), (m["T"][:, :3] * Nm).sum(1), (m["T"][:, :3] * Bm).sum(1), m["T"][:, 3]],
                   uv=m["UV"], tris=m["tris"], P=m["P"])
        # clean-up: small bits touching a pendant join it; rigid pieces standing > 6 mm proud hang as pendants
        Pm = m["P"]; ids = np.unique(rec["pid"])
        for p in ids:
            sel = rec["pid"] == p
            if not sel.any() or rec["kind"][sel][0] != 1: continue
            for q in ids:
                selq = rec["pid"] == q
                if q == p or not selq.any() or rec["kind"][selq][0] != 2: continue
                if np.ptp(Pm[sel], axis=0).max() < 0.015 and np.min(np.linalg.norm(Pm[sel][:, None] - Pm[selq][None, ::4], axis=2)) < 0.003:
                    rec["pid"][sel] = q; rec["kind"][sel] = 2; break
        for p in np.unique(rec["pid"]):
            sel = rec["pid"] == p
            if auto_pendant and rec["kind"][sel][0] == 1 and np.ptp(rec["r"][sel]) > 0.006: rec["kind"][sel] = 2
        # rigid / pendant pieces: anchored where they come closest to the curve, offsets in the anchor's frame;
        # a rigid piece lying away from the curve is moved up to it (its closest point 1 mm off)
        for p in np.unique(rec["pid"]):
            sel = rec["pid"] == p
            if rec["kind"][sel][0] == 0: continue
            ia, shift = anchor_of(p, rec["kind"][sel][0]) if (allpid == p).any() else (int(np.argmin(np.linalg.norm(curve.p - m["P"][sel].mean(0), axis=1))), np.zeros(3))
            rel = m["P"][sel] + shift - curve.p[ia]
            rec["s"][sel] = curve.s[ia]
            if rec["kind"][sel][0] == 1:
                rec["dt"][sel] = rel @ curve.T[ia]; rec["r"][sel] = rel @ curve.N[ia] * out_scale; rec["h"][sel] = rel @ curve.B[ia] * out_scale
                Fa = (curve.T[ia], curve.N[ia], curve.B[ia])
            else:
                # pendant frame: its own thinnest axis -> outward, anchor -> centre direction -> toward the hand
                # a pendant spanning several meshes (charm + a bail from another item) gets ONE frame from all its parts
                Q = allP[allpid == p] if (allpid == p).sum() > sel.sum() else m["P"][sel]; c0 = Q.mean(0); _, _, vt = np.linalg.svd(Q - c0, full_matrices=False); pn = vt[2]
                if pn @ curve.N[ia] < 0: pn = -pn
                pd = c0 - curve.p[ia]; pd -= pn * (pd @ pn)
                pd = pd / np.linalg.norm(pd) if np.linalg.norm(pd) > 1e-6 else vt[0]
                px = np.cross(pn, pd)                                 # (px, pn, pd) right-handed like (T, N, B)
                top = Q[np.argmin(np.linalg.norm(Q - curve.p[ia], axis=1))]   # hangs from its own nearest point
                q = m["P"][sel] - top
                rec["dt"][sel] = q @ px; rec["r"][sel] = q @ pn; rec["h"][sel] = q @ pd
                Fa = (px, pn, pd)
            rec["n"][sel] = np.c_[m["N"][sel] @ Fa[0], m["N"][sel] @ Fa[1], m["N"][sel] @ Fa[2]]
            rec["t"][sel, :3] = np.c_[m["T"][sel, :3] @ Fa[0], m["T"][sel, :3] @ Fa[1], m["T"][sel, :3] @ Fa[2]]
        pid_base = max(pid_base, int(rec["pid"].max()) + 1)
        recs.append(rec)
    # innermost point of every strand / rigid piece at r = 0 - per piece across all meshes (a watch head and its glass
    # are two meshes but one piece)
    rmin = {}
    gaps = [np.maximum(gap(rec["P"]), 0) for rec in recs] if gap else None     # cut from an outfit: height above ITS sleeve
    for p in np.unique(np.concatenate([rec["pid"] for rec in recs])):
        mn = min(rec["r"][rec["pid"] == p].min() for rec in recs if (rec["pid"] == p).any())
        lift = 0.0
        if gaps is not None:
            sel_all = [(rec["pid"] == p) for rec in recs]
            if recs[0]["kind"].size and any(rec["kind"][sl].size and rec["kind"][sl][0] == 0 for rec, sl in zip(recs, sel_all)):
                for rec, g, sl in zip(recs, gaps, sel_all): rec["r"][sl] = g[sl]       # strands: each vertex keeps its own gap
                rmin[int(p)] = 0.0; continue
            lift = min(g[sl].min() for g, sl in zip(gaps, sel_all) if sl.any())      # rigid: its closest point keeps its gap
        for rec in recs: rec["r"][rec["pid"] == p] += lift - mn
        rmin[int(p)] = float(mn - lift)
    hmin = min(rec["h"].min() for rec in recs); hmax = max(rec["h"].max() for rec in recs)
    js = dict(name=name, note=note, bundle=bundle, closed=curve.closed if closed is None else closed, L=curve.L, width=float(hmax - hmin), hmid=float((hmin + hmax) / 2),
              meshes=[dict(mesh=rec["mesh"], bundle=rec["bundle"], mat=int(rec["mat"]), s=rec["s"].round(5).tolist(), dt=rec["dt"].round(5).tolist(), r=rec["r"].round(5).tolist(), h=rec["h"].round(5).tolist(),
                           pid=rec["pid"].astype(int).tolist(), kind=rec["kind"].astype(int).tolist(), n=rec["n"].round(4).tolist(), t=rec["t"].round(4).tolist(),
                           uv=rec["uv"].round(5).tolist(), tris=[tr.astype(int).ravel().tolist() for tr in rec["tris"]]) for rec in recs])
    if extra: js.update(extra(curve, anchors, rmin))           # e.g. a watch's moving hands, posed relative to its head
    json.dump(js, open(OUT + f"acc_{name}.json", "w"))
    nk = np.concatenate([rec["kind"] for rec in recs])
    print(f"{name:11} L {curve.L*1000:5.0f} mm, width {js['width']*1000:4.1f} mm, verts {len(nk)} (strand {np.sum(nk==0)}, rigid {np.sum(nk==1)}, pendant {np.sum(nk==2)}), pieces {pid_base}")
    return js


def loop_curve(X, c, a, b, n_bins=120):
    """closed curve through a loop-shaped part: centroid of its vertices in every angle bin around c (plane a, b),
    smoothed - follows ovals, unlike a fitted circle"""
    ang = np.arctan2((X - c) @ b, (X - c) @ a); edges = np.linspace(-np.pi, np.pi, n_bins + 1); pts = []
    for i in range(n_bins):
        m = (ang >= edges[i]) & (ang < edges[i + 1])
        if m.any(): pts.append(X[m].mean(0))
    pts = np.array(pts)
    for _ in range(4): pts = (np.roll(pts, 1, 0) + 2 * pts + np.roll(pts, -1, 0)) / 4
    return pts


def flat_normal(n):
    return lambda pts, T: np.repeat(n[None], len(pts), 0)


def radial_from(c):
    return lambda pts, T: pts - c




def surface_gap(V, tris, w, ax, e1, nd=72, da=0.002):
    """height of points above a source sleeve (V, tris: the outfit's own mesh without the cut-out parts, same bone space)
    around the axis (w, ax), per 5 deg x 2 mm cell (outermost surface, triangle points sampled on a 4 x 4 grid)"""
    e2 = np.cross(ax, e1); A, B, C = V[tris[:, 0]], V[tris[:, 1]], V[tris[:, 2]]
    S = np.vstack([A * (1 - u - v) + B * u + C * v for i in range(5) for j in range(5 - i) for u, v in [(i / 4, j / 4)]])
    rel = S - w; al = rel @ ax; perp = rel - np.outer(al, ax); rad = np.linalg.norm(perp, axis=1)
    ang = np.mod(np.arctan2(perp @ e2, perp @ e1), 2 * np.pi); a0 = al.min(); na = int((al.max() - a0) / da) + 2
    tab = np.zeros((na, nd)); ai = ((al - a0) / da).astype(int); ki = np.round(ang / (2 * np.pi / nd)).astype(int) % nd
    np.maximum.at(tab, (ai, ki), rad)
    def gap(P):
        r_ = P - w; a_ = r_ @ ax; p_ = r_ - np.outer(a_, ax); rr = np.linalg.norm(p_, axis=1)
        k_ = np.round(np.mod(np.arctan2(p_ @ e2, p_ @ e1), 2 * np.pi) / (2 * np.pi / nd)).astype(int) % nd
        j_ = np.clip(((a_ - a0) / da).astype(int), 0, na - 1); out = np.zeros(len(P))
        for i in range(len(P)):
            sl = tab[max(0, j_[i] - 2):j_[i] + 3][:, [(k_[i] + d) % nd for d in (-1, 0, 1)]]
            out[i] = rr[i] - (sl.max() if sl.max() > 0 else rr[i])
        return out
    return gap


def recentre(line, n, X, iters=3, win=8):
    """The traced centreline (graph diameter) runs along one edge of a chain strip and swaps sides on the way; move it
    onto the strip's middle across (B) and through (N): per line point, the mid of the strand vertices' extent within
    +-win points (1 mm each), smoothed. A window counts only where it spans most of the strip's width / thickness (the
    strip's cross-sections are ~7 mm apart; a window holding one edge only would put the middle on that edge)."""
    for _ in range(iters):
        c = Curve(line, flat_normal(n), False)
        i, _, r, h = c.project(X)
        ext = [(np.ptp(h[np.abs(i - j) <= win]), np.ptp(r[np.abs(i - j) <= win])) for j in range(len(line)) if (np.abs(i - j) <= win).sum() >= 3]
        W, D = np.median([e[0] for e in ext]), np.median([e[1] for e in ext])
        mh = np.full(len(line), np.nan); mr = np.full(len(line), np.nan)
        for j in range(len(line)):
            q = np.abs(i - j) <= win
            if q.sum() < 3: continue
            if np.ptp(h[q]) >= 0.8 * W: mh[j] = (h[q].min() + h[q].max()) / 2
            if np.ptp(r[q]) >= 0.8 * D: mr[j] = (r[q].min() + r[q].max()) / 2
        k = np.arange(len(line)); okh = ~np.isnan(mh); okr = ~np.isnan(mr)
        mh = np.interp(k, k[okh], mh[okh]); mr = np.interp(k, k[okr], mr[okr])
        ker = np.ones(2 * win + 1) / (2 * win + 1)
        mh = np.convolve(np.pad(mh, win, mode="edge"), ker, "valid"); mr = np.convolve(np.pad(mr, win, mode="edge"), ker, "valid")
        line = line + c.B * mh[:, None] + c.N * mr[:, None]
    return line
