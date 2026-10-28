# outfit_acc.py: accessories baked into outfits' first-person hands (bracelets, bands, wraps) cut out as fitted wrist items.
# Input: wristacc/tools/accessory_candidates.json (cluster_pieces.py; pieces from scan_pieces.py). Per cluster: the outfit's
# hands mesh in that side's Forearm3 bone space, the cluster's pieces (same welding as scan_pieces), laid out round the source
# arm on a circle (angles kept, curve starts at the back of the hand like predator.py), heights above the source sleeve kept
# (surface_gap). Long pieces (> 30 mm) follow the curve, small ones (links, beads, tags) stay rigid.
# Writes wristacc/acc/acc_outfit_<hid8>_<side><n>.json + wristacc/acc/outfit_index.json. Env ONLY=<hid,...> to limit.
import sys, os, json, numpy as np, UnityPy
sys.path.insert(0, "WORK/tattoo/acc")
import acclib
from acclib import Curve, build, radial_from, surface_gap
from UnityPy.helpers.MeshHelper import MeshHandler
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
scan = json.load(open("WORK/tattoo/acc/all_scan_hands.json"))
cand = json.load(open("WORK/wristacc/tools/accessory_candidates.json"))
OL = json.load(open("WORK/wristacc/outlines.json"))
CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
W = ("watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin", "starline", "shock", "casio")
only = os.environ.get("ONLY", "").split(",") if os.environ.get("ONLY") else None
# PARTS=1: every cluster whose pieces sit at separate places along the arm (beads + bands) also as one item per group
# (outfit_<hid8>_<S><n>p<g>); a side with several clusters also as one bundled item (outfit_<hid8>_<S>all). User 2026-10-27.
PARTS = os.environ.get("PARTS") == "1"
if os.environ.get("OUTDIR"): acclib.OUT = os.environ["OUTDIR"].rstrip("/") + "/"; os.makedirs(acclib.OUT, exist_ok=True)

def ring_groups(Pq, w_, ax_, e1_, lab_v, pieces, touch=0.003, full=250):
    """separate bracelets inside one cluster: every piece going (nearly) all the way round is one; small pieces chained by
    touching (< touch m apart, beads on a string) that go all the way round together are one; other small bits (buckle, tab,
    shackle) join the ring they come closest to. Sorted hand end first. Returns lists of piece labels."""
    e2_ = np.cross(ax_, e1_); info = {}
    for q in pieces:
        sq = lab_v == q
        if not sq.any(): continue
        rq = Pq[sq] - w_; ang = np.degrees(np.arctan2(rq @ e2_, rq @ e1_)) % 360
        info[q] = (Pq[sq][::max(1, sq.sum() // 200)], np.histogram(ang, 36, (0, 360))[0] > 0, float((rq @ ax_).mean()))
    def dist(a, b): return float(np.min(np.linalg.norm(info[a][0][:, None] - info[b][0][None], axis=2)))
    rings = [q for q in info if info[q][1].sum() * 10 >= full]; small = [q for q in info if q not in rings]
    par = {q: q for q in small}
    def find(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for i, a in enumerate(small):
        for b in small[i + 1:]:
            if dist(a, b) < touch: par[find(a)] = find(b)
    chains = {}
    for q in small: chains.setdefault(find(q), []).append(q)
    groups = [[q] for q in rings]
    for ch in chains.values():
        cover = np.any([info[q][1] for q in ch], axis=0).sum() * 10
        if cover >= full or not rings: groups.append(ch); continue
        best = min(range(len(rings)), key=lambda i: min(dist(q, rings[i]) for q in ch)); groups[best] += ch
    # two shells of one band (outer + inner surface, BEAR Vacation's festival wristband: 115 + 115 verts 0.3 mm apart) = one item
    def shell(a, b): return float(np.mean(np.min(np.linalg.norm(info[a][0][:, None] - info[b][0][None], axis=2), axis=1))) < 0.001
    merged = []
    for g in groups:
        for m in merged:
            if any(shell(a, b) for a in g for b in m if a in rings and b in rings): m += g; break
        else: merged.append(g)
    groups = merged
    groups.sort(key=lambda g: -np.mean([info[q][2] for q in g]))     # hand end (bigger along) first
    return groups

def key_of(path):
    path = path.replace("\\", "/"); i = path.lower().find("/streamingassets/windows/")
    if i >= 0: return path[i + len("/streamingassets/windows/"):]
    i = path.lower().find("/bundles/"); return path[i + len("/bundles/"):] if i >= 0 else path

def skin(path, want):
    env = UnityPy.load(path); objs = {o.path_id: o for o in env.objects}
    def deref(owner, pp):
        if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
        cab = os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower(); f = CABS.get(cab)
        if f is None: return None
        if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
        return DEP[f].get(pp["m_PathID"])
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = deref(o, t["_skinnedMeshRenderer"]); mo = deref(r, r.read_typetree()["m_Mesh"]) if r else None
        if mo is not None and mo.peek_name() == want:
            mn = [(deref(r, m_).peek_name() if deref(r, m_) is not None else "?") for m_ in r.read_typetree()["m_Materials"]]
            return t["_bonePaths"], mo, mn
    raise RuntimeError("mesh not found " + want)

index = {}; seen = set()
for hid, v in cand.items():
    if only and hid not in only: continue
    wr = [f for f in v["found"] if f["kind"] == "wrist"]
    if not wr: continue
    try:
        groups = {}
        for f in wr: groups.setdefault(f["mesh"], []).append(f)
        for meshname, fs in groups.items():
            paths, mo, mnames = skin(scan[hid]["path"], meshname)
            mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process(); names = [p.split("/")[-1] for p in paths]
            P0 = np.array(h.m_Vertices); N0 = np.array(h.m_Normals)[:, :3]; T40 = np.array(h.m_Tangents) if h.m_Tangents else np.zeros((len(P0), 4))
            UV = np.array(h.m_UV0); subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
            T = np.vstack(subs); tsub = np.concatenate([np.full(len(s), i) for i, s in enumerate(subs)])
            k_ = np.round(P0 / 1e-5).astype(np.int64); _, inv = np.unique(k_, axis=0, return_inverse=True); inv = inv.ravel()
            e = inv[np.r_[T[:, [0, 1]], T[:, [1, 2]]]]
            lab = connected_components(coo_matrix((np.ones(len(e)), (e[:, 0], e[:, 1])), shape=(inv.max() + 1,) * 2), directed=False)[1][inv]
            if PARTS:
                bySide = {}
                for n, f in enumerate(fs): bySide.setdefault(f["side"], []).append((n, f))
                jobs = []
                for S_, lst in bySide.items():
                    bp_ = mt["m_BindPose"][names.index(f"Base Human{S_}Forearm3")]; M_ = np.array([[bp_[f"e{r}{c}"] for c in range(4)] for r in range(4)])
                    Pq = (M_ @ np.c_[P0, np.ones(len(P0))].T).T[:, :3]; o_ = OL[hid][S_]; w_ = np.array(o_["w"]); ax_ = np.array(o_["ax"]); ax_ /= np.linalg.norm(ax_)
                    al_ = (Pq - w_) @ ax_
                    for n, f in lst:
                        gs = ring_groups(Pq, w_, ax_, np.array(o_["e1"]), lab, f["pieces"])
                        if os.environ.get("STATS"):
                            e1_ = np.array(o_["e1"]); e2_ = np.cross(ax_, e1_)
                            for q in f["pieces"]:
                                sq = lab == q; rq = Pq[sq] - w_; aq = rq @ ax_; ang = np.degrees(np.arctan2(rq @ e2_, rq @ e1_)) % 360
                                h_, _ = np.histogram(ang, 36, (0, 360)); sub = np.unique(tsub[np.isin(T, np.where(sq)[0]).any(1)])
                                print(f"  piece {q}: verts {sq.sum()}, along {aq.min()*1000:.1f}..{aq.max()*1000:.1f} mm, cover {(h_ > 0).sum() * 10} deg, ang {ang.min():.0f}..{ang.max():.0f}, rad {np.linalg.norm(rq - np.outer(aq, ax_), axis=1).mean()*1000:.1f} mm, sub {sub.tolist()} {[mnames[x] for x in sub if x < len(mnames)]}", flush=True)
                        print("PARTS", v["name"], S_, n, "groups", len(gs), [len(g) for g in gs], flush=True)
                        if len(gs) > 1:
                            for gi, g in enumerate(gs): jobs.append((f"{S_}{n}p{gi}", dict(f, pieces=g)))
                    if len(lst) > 1:
                        u = dict(lst[0][1]); u["pieces"] = sorted(set(q for _, f in lst for q in f["pieces"]))
                        u["along"] = [min(f["along"][0] for _, f in lst), max(f["along"][1] for _, f in lst)]; u["verts"] = sum(f["verts"] for _, f in lst)
                        jobs.append((f"{S_}all", u))
                fs = jobs
            else:
                fs = [(str(n), f) for n, f in enumerate(fs)]
            for n, f in fs:
                S = f["side"]; sig = (meshname, S, tuple(f["along"]), f["verts"], tuple(f["pieces"]))
                if sig in seen: index.setdefault("_dupes", []).append(f"{v['name']} {S} = earlier"); continue
                seen.add(sig)
                bp = mt["m_BindPose"][names.index(f"Base Human{S}Forearm3")]; M3 = np.array([[bp[f"e{r}{c}"] for c in range(4)] for r in range(4)])
                P = (M3 @ np.c_[P0, np.ones(len(P0))].T).T[:, :3]; R3 = M3[:3, :3]
                N = N0 @ R3.T; Tg = np.c_[T40[:, :3] @ R3.T, T40[:, 3]]
                o = OL[hid][S]; w, ax, e1 = (np.array(o[k]) for k in ("w", "ax", "e1")); ax /= np.linalg.norm(ax); e2 = np.cross(ax, e1)
                selv = np.isin(lab, f["pieces"]); tri_in = selv[T].all(1)
                watch_t = np.array([any(x in (mnames[s] if s < len(mnames) else "").lower() for x in W) for s in tsub])
                gap = surface_gap(P, T[~tri_in & ~watch_t], w, ax, e1)
                V = P[selv]; rel = V - w; al = rel @ ax; rad = np.linalg.norm(rel - np.outer(al, ax), axis=1)
                c = w + ax * float(np.median(al)); r0 = float(np.median(rad))
                th = np.linspace(0, 2 * np.pi, 180, endpoint=False)
                curve = Curve(c + r0 * (np.cos(th)[:, None] * e1 + np.sin(th)[:, None] * e2), radial_from(c), True)
                ms, kinds = [], []
                for s in np.unique(tsub[tri_in]):
                    tt = T[tri_in & (tsub == s)]; used = np.unique(tt); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
                    pl = lab[used]; _, pl = np.unique(pl, return_inverse=True)
                    ext = np.array([np.ptp(P[used][pl == q], axis=0).max() for q in range(pl.max() + 1)])
                    ms.append(dict(name=meshname, bundle=key_of(scan[hid]["path"]), mat=int(s), P=P[used], N=N[used], T=Tg[used], UV=UV[used], tris=[remap[tt]]))
                    kinds.append((pl + 1000 * len(ms), np.where(ext[pl] > 0.03, 0, 1)))
                nm = f"outfit_{hid[:8]}_{n}" if PARTS else f"outfit_{hid[:8]}_{S}{n}"
                title = f"{v['name']} ({'left' if S == 'L' else 'right'} wrist)"
                build(nm, ms, curve, kinds, bundle=key_of(scan[hid]["path"]), note=title, gap=gap)
                index[nm] = dict(hid=hid, outfit=v["name"], side=S, along=f["along"], wrap=f["wrap"], pieces=len(f["pieces"]), verts=f["verts"], mats=f["mats"])
    except Exception as ex:
        print("FAIL", v["name"], str(ex)[:100], flush=True); index.setdefault("_fails", []).append(f"{v['name']}: {ex}")
json.dump(index, open(acclib.OUT + ("outfit_parts_index.json" if PARTS else "outfit_index.json"), "w"), indent=1)
print("written", len([k for k in index if not k.startswith("_")]), "dupes", len(index.get("_dupes", [])), "fails", len(index.get("_fails", [])))
