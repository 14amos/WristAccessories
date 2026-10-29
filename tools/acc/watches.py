# watches.py: the watches baked into outfits' first-person hands, cut out as fitted wrist accessories
# (acc_watch_<name>.json). Each watch = its submeshes of the source outfit's hands mesh (strap, case, dial, glass, each
# with its own material), in that outfit's LForearm3 bone space. Laid out as a loop around the source forearm starting at
# the watch face: case + dial + glass = one rigid head, strap pieces follow the curve. The moving hands (the outfit's
# watch prefab, game's own placement WatchPosition / WatchRotation) are stored relative to the head.
import sys, os, json, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
sys.path.insert(0, "WORK/tattoo/acc")
from acclib import Curve, build, pieces, radial_from, surface_gap

G = "SPT/EscapeFromTarkov_Data/StreamingAssets/Windows/"
cust = json.load(open("SPT/SPT_Runtime/SPT_Data/database/templates/customization.json", encoding="utf-8"))
HANDS = "assets/content/hands/"
# name, source outfit (hands customization id), mesh name, submesh indices (None = all). Title = the outfit's name + "watch"
# (watch_scan.json from scan_watches.py: outfit names, bundle files incl. mods' outfits). One per watch model and colour
# (watch_groups.jpg: the other outfits carry the same watches, only recompressed).
SPECS = [
    ("watch_luminox", "6390103dfdb42a2ccc0e8cca", "Hands_Bear_SpNa", [1, 2]),
    ("watch_montana", "685d08a741b02568210760ca", "Hands_BEAR_Hawaii_03_LP", [1]),
    ("watch_traser", "62961424869a02221651df53", "Hands_bear_tactical_long1", [1]),
    ("watch_bear", "5fd760001189a17bcc172b85", "Hands_Bear_Borey", [1]),
    ("watch_bear2", "5e4bb1d286f77406a313ec59", "Hands_bear_Telnik", [1]),
    ("watch_garmin", "675ff2a7e3ee8ffb7e042465", "Hands_Usec_WinterSet_LP", [1, 2, 3]),
    ("watch_gmt", "685d08ebc18489365003668e", "Hands_USEC_Hawaii_03_LP", [1, 2]),
    ("watch_suunto", "64368d69ac11c9eaf10d890e", "Hands_BT_2", [1]),
    ("watch_usec", "5d1f5b0686f7743f8362bcde", "Hands_Usec_AGRESSOR_PARKA", [1, 2]),
    ("watch_gshock_ga2000", "675696574737376d69020746", "Hands_Bear_Instructor_watch", None),
    ("watch_gshock_gbd200", "675698339fe31e6df7031b28", "Hands_USEC_SPECIAL_OPS_watch", None),
    # mods' outfits (WTT-ContentBackport): no moving-hands prefab
    ("watch_starline", "6a7207dfb5fc03f68109f9b5", "Hands_Arena_Hawaii_NoFract_Green", [1, 2]),
    ("watch_smartultra", "69a96f08c08d716baa0385cd", "Hands_USEC_Goptac_2", [1]),
    ("watch_cshock", "69cf7ed57e099a3f800edb5c", "Hands_USEC_Arena_Turtle", [1, 2]),
    ("watch_cshock_green", "6a72099ea326354ca90b1ec9", "Hands_Arena_BEAR_Hawaii_Qilo", [1, 2]),
    ("watch_cshock_camo", "6a7209bfab6fee1bb40eb5c5", "Hands_Arena_USEC_Hawaii_Qilo", [1, 2]),
]
SCAN = json.load(open("WORK/tattoo/acc/watch_scan.json"))
ONLY = [x for x in os.environ.get("ONLY", "").split(",") if x]


def find_bundle(cid):
    """(bundle key as the game loads it, file on disk, the outfit's customization props, outfit name)"""
    sc = SCAN[cid]; f = sc["path"]
    key = f.split("/StreamingAssets/Windows/")[-1] if "/StreamingAssets/Windows/" in f else f.split("/bundles/", 1)[-1]
    name = sc["name"].split(" (")[0] if sc["name"].endswith(")") and " (" in sc["name"] and any(m in sc["name"] for m in ("WTT", "ISB", "Aishi")) else sc["name"]
    return key, f, cust.get(cid, {}).get("_props", {}), name


def rotm(axis, deg):
    a = np.radians(deg); c, s = np.cos(a), np.sin(a)
    return {"x": np.array([[1, 0, 0], [0, c, -s], [0, s, c]]), "y": np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]]), "z": np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])}[axis]


def skins(env):
    """(bone paths, SkinnedMeshRenderer typetree, mesh object, material names) for every Skin in the bundle"""
    objs = {o.path_id: o for o in env.objects}; out = []
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = objs.get(t["_skinnedMeshRenderer"]["m_PathID"])
        if r is None: continue
        rt = r.read_typetree(); mo = objs.get(rt["m_Mesh"]["m_PathID"])
        if mo is None: continue
        mats = [objs[m["m_PathID"]].peek_name() if m["m_PathID"] in objs else "?" for m in rt["m_Materials"]]
        out.append((t["_bonePaths"], rt, mo, mats))
    return out


def bone_space(paths, mt, bone):
    names = [p.split("/")[-1] for p in paths]; bp = mt["m_BindPose"][names.index(bone)]
    return np.array([[bp[f"e{r}{c}"] for c in range(4)] for r in range(4)])


for name, cid, mesh_name, subs in SPECS:
    if ONLY and name not in ONLY: continue
    path, fpath, props, outfit = find_bundle(cid); title = outfit + " watch"
    env = UnityPy.load(fpath); sk = skins(env)
    cand = [s for s in sk if s[2].peek_name() == mesh_name]
    cand.sort(key=lambda s: -sum(("watch" in m.lower() or "shock" in m.lower()) for m in s[3]))   # the renderer with the watch materials
    paths, rt, mo, mats = cand[0]
    mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
    M3 = bone_space(paths, mt, "Base HumanLForearm3"); R3 = M3[:3, :3]
    P = (M3 @ np.c_[np.array(h.m_Vertices), np.ones(len(h.m_Vertices))].T).T[:, :3]
    N = np.array(h.m_Normals)[:, :3] @ R3.T
    T4 = np.array(h.m_Tangents) if h.m_Tangents else np.zeros((len(P), 4)); T = np.c_[T4[:, :3] @ R3.T, T4[:, 3]]
    UV = np.array(h.m_UV0); tri_sets = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
    if subs is None: subs = list(range(len(tri_sets)))
    # source wrist frame, from the outfit's main hands mesh (same skeleton)
    main = max(sk, key=lambda s: s[2].read_typetree()["m_VertexData"]["m_VertexCount"] * (1 - ("watch" in s[2].peek_name().lower())))
    mpaths, _, mmo, _ = main; mm = mmo.read(); mmt = mmo.read_typetree(); hm = MeshHandler(mm); hm.process()
    Mm3 = bone_space(mpaths, mmt, "Base HumanLForearm3"); Pm = (Mm3 @ np.c_[np.array(hm.m_Vertices), np.ones(len(hm.m_Vertices))].T).T[:, :3]
    nm_ = [p.split("/")[-1] for p in mpaths]; bi = np.array(hm.m_BoneIndices); bw = np.array(hm.m_BoneWeights); dom = bi[np.arange(len(bi)), bw.argmax(1)]
    idx = lambda n: nm_.index("Base Human" + n)
    org = lambda n: (Mm3 @ np.linalg.inv(bone_space(mpaths, mmt, "Base Human" + n)))[:3, 3]
    w = org("LPalm"); ax = w - org("LForearm3"); ax /= np.linalg.norm(ax)
    pc = Pm[dom == idx("LPalm")].mean(0)
    fd = Pm[np.isin(dom, [idx(f"LDigit{a}{b}") for a in (2, 3, 4) for b in (1, 2, 3)])].mean(0) - pc; fd /= np.linalg.norm(fd)
    td = Pm[np.isin(dom, [idx(f"LDigit1{b}") for b in (1, 2, 3)])].mean(0) - pc; td -= fd * (td @ fd); td /= np.linalg.norm(td)
    back = np.cross(fd, td); e1 = back - ax * (back @ ax); e1 /= np.linalg.norm(e1); e2 = np.cross(ax, e1)
    # the watch's submeshes, each its own layout mesh with its material index
    ms = []
    for j in subs:
        tr = tri_sets[j]; used = np.unique(tr); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
        ms.append(dict(name=mesh_name, bundle=path, mat=j, P=P[used], N=N[used], T=T[used], UV=UV[used], tris=[remap[tr]]))
    allV = np.vstack([m["P"] for m in ms]); rel_ = allV - w; al = rel_ @ ax; perp = rel_ - np.outer(al, ax); rad = np.linalg.norm(perp, axis=1)
    ang = np.arctan2(perp @ e2, perp @ e1)
    rest_t = np.vstack([np.array(t).reshape(-1, 3) for k_, t in enumerate(hm.get_triangles()) if not (mmo.peek_name() == mesh_name and k_ in subs)])
    gap = surface_gap(Pm, rest_t, w, ax, e1)
    # face + head width from the watch's own thickness around the arm (outermost - innermost vertex per 10 deg): straps are a
    # few mm, the case 10+ mm. Head = the run of bins thicker than half the peak (>= 6.5 mm) around the thickest one, + 3 mm each side
    nb = 36; bi_ = np.floor((ang + np.pi) / (2 * np.pi / nb)).astype(int) % nb
    thick = np.array([np.ptp(rad[bi_ == b]) if (bi_ == b).sum() > 2 else 0.0 for b in range(nb)])
    b0 = int(thick.argmax()); lo_, hi_ = 0, 0; tmin = max(0.0065, 0.5 * thick[b0])
    while lo_ < nb // 2 and thick[(b0 - lo_ - 1) % nb] > tmin: lo_ += 1
    while hi_ < nb // 2 and thick[(b0 + hi_ + 1) % nb] > tmin: hi_ += 1
    bw_ = 2 * np.pi / nb; face = -np.pi + bw_ * (b0 + 0.5 + (hi_ - lo_) / 2)
    dang = lambda x: np.abs(np.angle(np.exp(1j * (x - face))))
    hw = bw_ * (lo_ + hi_ + 1) / 2 + 0.003 / float(np.median(rad))
    print(f"{name}: face {np.degrees(face):.0f} deg, half-width {np.degrees(hw):.0f} deg, thickness mm {np.round(np.roll(thick, nb // 2 - b0) * 1000).astype(int).tolist()}", flush=True)
    if os.environ.get("DIAG"): continue
    # "worn like its outfit" (user 2026-10-28, Aggressor TAC watch shots): where the outfit itself wears the watch, in the
    # LForearm3 bone space every outfit shares - the source outfit (its outline), the strap loop's centre on the arm axis and
    # the face direction. SRC_ONLY=<json>: only these, merged into that file (no acc_*.json rewritten)
    if os.environ.get("SRC_ONLY"):
        sf = os.environ["SRC_ONLY"]; src = json.load(open(sf)) if os.path.exists(sf) else {}
        a_c0 = float(np.median(al)); fdir = np.cos(face) * e1 + np.sin(face) * e2
        src[name] = dict(hid=cid, c=[float(x) for x in (w + ax * a_c0)], face=[float(x) for x in fdir],
                         # the watch exactly as its outfit wears it (LForearm3 bone space), per acc mesh in the acc json's vertex order
                         meshes=[dict(P=np.round(m["P"], 6).tolist(), N=np.round(m["N"], 5).tolist(), T=np.round(m["T"], 5).tolist()) for m in ms])
        json.dump(src, open(sf, "w"), indent=1); continue
    a_c = float(np.median(al)); c = w + ax * a_c
    r0 = float(np.median(rad[dang(ang) > 0.6])) if (dang(ang) > 0.6).any() else float(np.median(rad))
    th = face + np.linspace(0, 2 * np.pi, 180, endpoint=False)
    pts = c + r0 * (np.cos(th)[:, None] * e1 + np.sin(th)[:, None] * e2)
    curve = Curve(pts, radial_from(c), True)
    # pieces: head = everything within the case's angular half-width of the face (case + dial + glass share pid 0)
    kinds = []; first = True
    for m in ms:
        lab = pieces(m["P"], m["tris"]); r_ = m["P"] - w; r_ -= np.outer(r_ @ ax, ax); a_ = np.arctan2(r_ @ e2, r_ @ e1)
        head_v = dang(a_) < hw
        for p in range(lab.max() + 1):                     # a piece almost all head (or strap) goes whole; one that spans
            sel = lab == p; f = head_v[sel].mean()          # case and strap is split at the head's edge
            if f >= 0.9: head_v[sel] = True
            elif f <= 0.1: head_v[sel] = False
        pid = np.where(head_v, 0, 1 + lab); kind = np.where(head_v, 1, 0)
        kinds.append((pid, kind) if first else (pid, kind, False)); first = False
    wp = props.get("WatchPrefab") or {}
    def dial(curve, anchors, rmin):
        if not (wp.get("path")) or 0 not in anchors: return {}
        p = np.array([props["WatchPosition"][k] for k in "xyz"]); e = props["WatchRotation"]
        Rm = rotm("y", e["y"]) @ rotm("x", e["x"]) @ rotm("z", e["z"])          # Unity Quaternion.Euler: z, then x, then y
        ia, shift = anchors[0]; rel = p + shift - curve.p[ia]; Tn, Nn, Bn = curve.T[ia], curve.N[ia], curve.B[ia]
        return {"dial": dict(prefab=wp["path"], pid=0, dt=float(rel @ Tn), r=float(rel @ Nn - rmin[0]), h=float(rel @ Bn),
                             axes=[[float(Rm[:, k] @ Tn), float(Rm[:, k] @ Nn), float(Rm[:, k] @ Bn)] for k in range(3)])}
    # the biggest sleeve it fits: the source arm's radius under the strap (median) + 4 mm (one strap hole looser)
    # measured like the runtime does: the mean over directions of the source outfit's (posed) outline where the watch sat, + 3 mm;
    # outfits without an outline (the G-Shocks' outfits): the rest-pose arm under the strap + 6 mm
    sv = np.vstack([m["P"][k[1] == 0] for m, k in zip(ms, kinds)]); rv = np.linalg.norm((sv - w) - np.outer((sv - w) @ ax, ax), axis=1)
    OLS = json.load(open("WORK/wristacc/outlines.json"))
    if cid in OLS:
        import base64
        o_ = OLS[cid]["L"]; tab_ = np.frombuffer(base64.b64decode(o_["tab"]), dtype="<u2").astype(float).reshape(o_["na"], o_["nd"]) / 10000
        j_ = int(np.clip((np.median((sv - w) @ ax) - o_["a0"]) / o_["da"], 0, o_["na"] - 1))
        fitR = float(tab_[max(0, j_ - 2):j_ + 3].mean() + 0.003)
    else:
        fitR = float(np.median(rv - np.maximum(gap(sv), 0)) + 0.006)
    def extra2(curve, anchors, rmin, _d=dial, _f=fitR): e = _d(curve, anchors, rmin); e["fitR"] = _f; return e
    js = build(name, ms, curve, kinds, bundle=path, note=title, extra=extra2, gap=gap)
    print(f"            fits sleeves up to {fitR*1000:.0f} mm radius")
    print(f"            {title}: source {os.path.basename(path)}, mats {[mats[m['mat']] for m in ms]}, strap radius {r0*1000:.0f} mm, head half-width {np.degrees(hw):.0f} deg ({int(sum(k[1].sum() for k in kinds))} rigid verts), moving hands {'yes' if 'dial' in js else 'no'}")
