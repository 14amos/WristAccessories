# Sleeve / finger outlines of every hands model for the runtime accessory fit (WristAccessories outlines.json).
# Per hands id and side (L, R):
#   wrist  - in <S>Forearm3 bone space: w = <S>Palm bone origin (the wrist joint), ax = Forearm3 -> Palm origin,
#            e1 = back of the hand (perpendicular to ax), e2 = ax x e1; tab[a][k] = outermost radius of the hands mesh
#            (sleeve, cuff, baked watch...) in axial bin a (2 mm, from A0 toward the hand) and direction k (5 deg),
#            the max over 27 poses: hand flex +-35 / deviation +-20 deg about the wrist, forearm twist +-20 deg on
#            Forearm2 (Forearm1 twice) - the same pose set the cultist bracelet fit was checked with.
#            start = first bin from the wrist toward the elbow where the outer sleeve has < 30% LPalm weight.
#   ring   - in <S>Digit41 (ring finger, first segment) bone space, along the segment, poses: rest, next segment
#            curled 30/60/90 deg, finger bent +-30 deg against the palm.
# Radii are stored as uint16 0.1 mm, base64.
import UnityPy, os, json, base64, itertools, numpy as np
from UnityPy.helpers.MeshHelper import MeshHandler
SPT = "SPT"; W = SPT + "/EscapeFromTarkov_Data/StreamingAssets/Windows"
cust = json.load(open(SPT + "/SPT_Runtime/SPT_Data/database/templates/customization.json", encoding="utf-8"))
import sys
MODE = sys.argv[1] if len(sys.argv) > 1 else "hands"      # hands = first-person hands; body = third-person upper body (tops)
PART = "Hands" if MODE == "hands" else "Body"
jobs = [(k, os.path.join(W, v["_props"]["Prefab"]["path"])) for k, v in cust.items()
        if v.get("_props", {}).get("BodyPart") == PART and isinstance(v["_props"].get("Prefab"), dict) and v["_props"]["Prefab"].get("path")]
U = SPT + "/SPT_Runtime/user/mods/UnheardTattoos/bundles/"
jobs += [("e02e6e5732bc3261f5d373bb", U + "Hands/hands_bt_3_unheard.bundle"), ("bde3ddec04fc4f9cf79010ae", U + "Hands/hands_pmc_cultist_unheard.bundle")] if MODE == "hands" else         [("ed7d31369899154fe47993fb", U + "Tops/top_bt_3_unheard.bundle"), ("40d130eabaac433e3a13a390", U + "Tops/top_pmc_cultist_unheard.bundle")]
ND = 72; DA = 0.002
WA0, WNA = -0.22, 116         # wrist table: 22 cm toward the elbow .. 1.2 cm past the wrist


def rot(axis, deg):
    a = np.radians(deg); axis = axis / np.linalg.norm(axis)
    K = np.array([[0, -axis[2], axis[1]], [axis[2], 0, -axis[0]], [-axis[1], axis[0], 0]])
    return np.eye(3) + np.sin(a) * K + (1 - np.cos(a)) * K @ K


def lbs(Pb, bi, bw, T):
    """Linear blend skinning in one bone's space; T = {bone index: (R, pivot)}, other bones stay."""
    V = np.zeros_like(Pb)
    for k in range(bi.shape[1]):
        Vk = Pb.copy(); b = bi[:, k]
        for bone, (R, p) in T.items():
            m = b == bone; Vk[m] = (Pb[m] - p) @ R.T + p
        V += bw[:, k][:, None] * Vk
    return V


def surface_points(V, tris):
    """corners, edge midpoints and centre of every triangle: a triangle can span more than one table cell"""
    A, B, C = V[tris[:, 0]], V[tris[:, 1]], V[tris[:, 2]]
    n = 4; pts = []                                         # barycentric 4 x 4 grid (third-person meshes have big triangles)
    for i in range(n + 1):
        for j in range(n + 1 - i):
            u, v = i / n, j / n; pts.append(A * (1 - u - v) + B * u + C * v)
    return np.vstack(pts)


def table(poses, c, ax, e1, e2, a0, na, rmax, tris=None):
    tab = np.zeros((na, ND))
    for V in poses:
        V = surface_points(V, TRIS if tris is None else tris)
        rel = V - c; al = rel @ ax; perp = rel - np.outer(al, ax); rad = np.linalg.norm(perp, axis=1)
        ang = np.mod(np.arctan2(perp @ e2, perp @ e1), 2 * np.pi)
        ai = np.floor((al - a0) / DA).astype(int); m = (ai >= 0) & (ai < na) & (rad < rmax)
        ki = np.round(ang[m] / (2 * np.pi / ND)).astype(int) % ND
        for dk in (-1, 0, 1):                                   # +-5 deg window, peaks kept
            np.maximum.at(tab, (ai[m], (ki + dk) % ND), rad[m])
    # empty cells: nearest filled axial bin of the same direction, else the direction's max, else the table max
    for k in range(ND):
        col = tab[:, k]; f = np.where(col > 0)[0]
        if len(f) == 0: continue
        for a in np.where(col == 0)[0]: col[a] = col[f[np.argmin(np.abs(f - a))]]
    tab[tab == 0] = tab.max()
    return tab


def enc(t): return base64.b64encode(np.clip(np.round(t * 10000), 0, 65535).astype("<u2").tobytes()).decode()


# outfits other mods add (WTT CustomClothing configs: topId / handsId + bundle paths inside that mod's bundles folder)
import glob
MODS = SPT + "/SPT_Runtime/user/mods/"
seen = {j[0] for j in jobs}
for f in glob.glob(MODS + "**/*.json", recursive=True):
    try: t = open(f, encoding="utf-8-sig").read()
    except Exception: continue
    if "BundlePath" not in t: continue
    try: d = json.loads(t)
    except Exception: continue
    mod = f[len(MODS):].replace("\\", "/").split("/")[0]
    for it in (d if isinstance(d, list) else [d]):
        if not isinstance(it, dict): continue
        cid, rel = (it.get("handsId"), it.get("handsBundlePath")) if MODE == "hands" else (it.get("topId"), it.get("topBundlePath"))
        if not cid or not rel or cid in seen: continue
        for cand in (MODS + mod + "/bundles/" + rel, W + "/" + rel, MODS + mod + "/" + rel):
            if os.path.isfile(cand): jobs.append((cid, cand)); seen.add(cid); break
print("jobs incl. mod outfits:", len(jobs), flush=True)
if len(sys.argv) > 2 and sys.argv[2] == "modsonly":                 # keep the outlines already built, add the mods' outfits
    prev = json.load(open("WORK/wristacc/outlines.json" if MODE == "hands" else "WORK/wristacc/outlines_body.json"))
    jobs = [j for j in jobs if j[0] not in prev]
    print("new jobs:", len(jobs), flush=True)
if len(sys.argv) > 2 and sys.argv[2] == "merge":                    # every job, results added to the outlines already built (ALTMESH)
    prev = json.load(open("WORK/wristacc/outlines.json" if MODE == "hands" else "WORK/wristacc/outlines_body.json"))
if os.environ.get("ONLY"): jobs = [j for j in jobs if j[0] in os.environ["ONLY"].split(",")]
CABS = json.load(open("WORK/wristacc/tools/cab_index.json")) if os.path.exists("WORK/wristacc/tools/cab_index.json") else {}
DEP = {}
print("CAB index:", len(CABS), flush=True)
out = {}; fails = []
for hid, path in jobs:
    try:
        env = UnityPy.load(path); paths = mesh = mt = None
        objs = {o.path_id: o for o in env.objects}; cands = []
        def deref(owner, pp):                                   # a PPtr, also into a dependency bundle (via cab_index.json)
            if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
            cab = os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower()
            f = CABS.get(cab)
            if f is None: return None
            if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
            return DEP[f].get(pp["m_PathID"])
        for o in env.objects:                                   # every Skin -> its renderer -> its mesh
            if o.type.name != "MonoBehaviour": continue
            t = o.read_typetree()
            if "_bonePaths" not in t: continue
            r = deref(o, t["_skinnedMeshRenderer"]) if t.get("_skinnedMeshRenderer") else None
            mo = deref(r, r.read_typetree()["m_Mesh"]) if r else None
            if mo is None: continue
            nm = mo.peek_name().lower()
            score = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm) * 1 + min(mo.read_typetree()["m_VertexData"]["m_VertexCount"], 99999) / 1e6
            rmats = r.read_typetree()["m_Materials"]
            mnames = [(deref(r, m_).peek_name() if deref(r, m_) is not None else "?") for m_ in rmats]
            cands.append((score, t["_bonePaths"], mo, mnames))
        if not cands: raise RuntimeError("no skin with a mesh in this bundle")
        cands.sort(key=lambda c: -c[0]); paths, mo, mnames = cands[0][1], cands[0][2], cands[0][3]; key = hid
        ALT = os.environ.get("ALTMESH", "").lower()          # e.g. "_cr_": the other mesh a top draws instead of its _base_ one (Arena
        if ALT:                                               # Hawaii / Deadlift CR, preview log 2026-10-27) -> extra entry <id>~<mesh>
            # not on a renderer of their own: the game swaps these meshes onto the top's renderer (same bones, same materials)
            alt = [o for o in env.objects if o.type.name == "Mesh" and ALT in o.peek_name().lower() and not any(f"lod{k}" in o.peek_name().lower() for k in range(1, 5))]
            if not alt: continue
            mo = alt[0]; key = hid + "~" + mo.peek_name()
        mesh = mo.read(); mt = mo.read_typetree()
        names = [p.split("/")[-1] for p in paths]
        h = MeshHandler(mesh); h.process()
        # the outfit's baked watch (submeshes with watch materials): the plugin hides it when another watch / none is picked,
        # so those outfits also get outlines without it (tabnw / tab0nw)
        WW = ("watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin", "starline", "shock")
        subtris = [np.array(s_).reshape(-1, 3) for s_ in h.get_triangles()]
        wsub = [i for i, n_ in enumerate(mnames[:len(subtris)]) if any(w_ in n_.lower() for w_ in WW)]
        nwkeep = None
        if wsub:
            nwkeep = np.zeros(len(h.m_Vertices), bool)
            for i, tr_ in enumerate(subtris):
                if i not in wsub: nwkeep[np.unique(tr_)] = True
            remap_ = -np.ones(len(nwkeep), int); remap_[nwkeep] = np.arange(nwkeep.sum())
            nwtris = remap_[np.vstack([subtris[i] for i in range(len(subtris)) if i not in wsub])]
        P = np.array(h.m_Vertices); bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights)
        TRIS = np.vstack([np.array(t).reshape(-1, 3) for t in h.get_triangles()])
        dom = bi[np.arange(len(bi)), bw.argmax(1)]
        rec = {"name": os.path.basename(path), "mesh": mesh.m_Name}
        for S in "LR":
            idx = lambda n: names.index(f"Base Human{S}{n}")
            def bind(n):
                bp = mt["m_BindPose"][idx(n)]; return np.array([[bp[f"e{r}{c}"] for c in range(4)] for r in range(4)])
            M3 = bind("Forearm3")
            Pb = (M3 @ np.c_[P, np.ones(len(P))].T).T[:, :3]
            org = lambda n, M=M3: (M @ np.linalg.inv(bind(n)))[:3, 3]
            w, f3o = org("Palm"), org("Forearm3")
            ax = w - f3o; ax /= np.linalg.norm(ax)
            pc = Pb[dom == idx("Palm")].mean(0)
            fing = np.isin(dom, [idx(f"Digit{a}{b}") for a in (2, 3, 4) for b in (1, 2, 3)])
            thumb = np.isin(dom, [idx(f"Digit1{b}") for b in (1, 2, 3)])
            fd = Pb[fing].mean(0) - pc; fd /= np.linalg.norm(fd)
            td = Pb[thumb].mean(0) - pc; td -= fd * (td @ fd); td /= np.linalg.norm(td)
            back = np.cross(fd, td) if S == "L" else np.cross(td, fd)     # mirrored hand: mirrored cross
            e1 = back - ax * (back @ ax); e1 /= np.linalg.norm(e1); e2 = np.cross(ax, e1)
            poses = []
            for flex, dev, tw in itertools.product([-35, 0, 35], [-20, 0, 20], [-20, 0, 20]):
                T = {idx("Palm"): (rot(e2, flex) @ rot(e1, dev), w),
                     idx("Forearm2"): (rot(ax, tw), f3o), idx("Forearm1"): (rot(ax, 2 * tw), f3o)}
                poses.append(lbs(Pb, bi, bw, T))
            tab = table(poses, w, ax, e1, e2, WA0, WNA, 0.12)
            tab0 = table([Pb], w, ax, e1, e2, WA0, WNA, 0.12)                    # rest pose only (runtime blends rest -> posed)
            if nwkeep is not None:
                tabnw = table([q[nwkeep] for q in poses], w, ax, e1, e2, WA0, WNA, 0.12, tris=nwtris); tab0nw = table([Pb[nwkeep]], w, ax, e1, e2, WA0, WNA, 0.12, tris=nwtris)
            # rigid start: outer sleeve verts' LPalm weight per bin, walking from the wrist toward the elbow
            palm_w = np.array([bw[i][bi[i] == idx("Palm")].sum() for i in range(len(bi))])
            rel = Pb - w; al = rel @ ax; rad = np.linalg.norm(rel - np.outer(al, ax), axis=1)
            start = None
            for a in np.arange(0.0, WA0, -DA):
                m = (al < a) & (al >= a - 0.006) & (rad < 0.12)
                if m.sum() < 6: continue
                outer = m & (rad >= np.percentile(rad[m], 60))
                if palm_w[outer].mean() < 0.3: start = float(a); break
            if start is None: start = -0.06
            rec[S] = dict(w=w.round(5).tolist(), ax=ax.round(5).tolist(), e1=e1.round(5).tolist(), a0=WA0, da=DA, na=WNA, nd=ND,
                          start=round(start, 4), tab=enc(tab), tab0=enc(tab0))
            if nwkeep is not None: rec[S]["tabnw"] = enc(tabnw); rec[S]["tab0nw"] = enc(tab0nw)
            # ring finger
            Mr = bind("Digit41"); Pr = (Mr @ np.c_[P, np.ones(len(P))].T).T[:, :3]
            orr = lambda n: (Mr @ np.linalg.inv(bind(n)))[:3, 3]
            r0, r1 = orr("Digit41"), orr("Digit42"); seg = np.linalg.norm(r1 - r0); rax = (r1 - r0) / seg
            rb = (Mr[:3, :3] @ (np.linalg.inv(M3)[:3, :3] @ back)); rb -= rax * (rb @ rax); rb /= np.linalg.norm(rb); re2 = np.cross(rax, rb)
            rposes = [Pr]
            for curl in (30, 60, 90): rposes.append(lbs(Pr, bi, bw, {idx("Digit42"): (rot(re2, curl), r1), idx("Digit43"): (rot(re2, curl), r1)}))
            for bend in (-30, 30): rposes.append(lbs(Pr, bi, bw, {idx("Palm"): (rot(re2, bend), r0)}))
            rna = int(np.ceil(seg / DA)) + 2
            finger = np.isin(dom, [idx("Digit41"), idx("Digit42")])                    # the ring finger's own skin only
            # the finger is not centred on its bone: the ring's axis runs through the finger's cross-section centre at
            # the ring spot (65% along the segment, past the webbing)
            rel = Pr[finger] - r0; al = rel @ rax; sl = np.abs(al - seg * 0.65) < 0.004
            off = rel[sl].mean(0) if sl.sum() >= 3 else np.zeros(3); off -= rax * (off @ rax); r0 = r0 + off
            ftris = TRIS[finger[TRIS].all(1)]
            rtab = table([Pr], r0, rax, rb, re2, -DA, rna, 0.02, tris=ftris)      # rest pose: mid-segment hardly deforms
            rec["ring" + S] = dict(w=r0.round(5).tolist(), ax=rax.round(5).tolist(), e1=rb.round(5).tolist(), a0=-DA, da=DA, na=rna, nd=ND,
                                   start=round(float(seg * 0.65), 4), tab=enc(rtab))
        out[key] = rec
        if wsub: rec["watchsubs"] = [mnames[i] for i in wsub]
        print(f"{hid} {rec['name'][:40]:40} watch {len(wsub)} sub(s), start L {rec['L']['start']*1000:.0f} R {rec['R']['start']*1000:.0f} mm, ring seg {rec['ringL']['na']*2} mm", flush=True)
    except Exception as e:
        fails.append((hid, os.path.basename(path), repr(e)[:100]))
if len(sys.argv) > 2 and sys.argv[2] in ("modsonly", "merge"): prev.update(out); out = prev
json.dump(out, open(os.environ.get("OUTF") or ("WORK/wristacc/outlines.json" if MODE == "hands" else "WORK/wristacc/outlines_body.json"), "w"))
print("outlines", len(out), "fails", len(fails)); [print(" FAIL", *f) for f in fails]
