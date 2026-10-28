# predator.py: the USEC Predator's right-forearm kill sleeve (patch board + dogtag chains + hanging dogtags) as a fitted
# accessory, plus Black Division and ISB patch variants (the patches repainted with those mods' own images).
# Geometry: the Predator's first-person hands (hands_boss_blackknight_quest): the patch board = the sleeve triangles whose
# texture coordinates lie on the board (texture box below); chains and tags = the separate pieces on the right forearm.
# Laid out around the source right forearm keeping each part's angle around the arm (curve starts at the back of the hand).
import sys, os, json, numpy as np, UnityPy
from PIL import Image, ImageFilter
from UnityPy.helpers.MeshHelper import MeshHandler
sys.path.insert(0, "WORK/tattoo/acc")
from acclib import Curve, build, pieces, radial_from, OUT, surface_gap
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



G = "SPT/EscapeFromTarkov_Data/StreamingAssets/Windows/"
BUNDLE = "assets/content/hands/hands_boss_blackknight_quest/hands_boss_blackknight_quest.skin.bundle"
HID = "64ef3f3739e45b527a7c40ae"
BOARD = (1390, 880, 1720, 1440)                     # texture px (y from the top) of the forearm patch board
env = UnityPy.load(G + BUNDLE); sk = skins(env)
paths, rt, mo, mats = max(sk, key=lambda s: s[2].read_typetree()["m_VertexData"]["m_VertexCount"])
mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
names = [p.split("/")[-1] for p in paths]
M3 = bone_space(paths, mt, "Base HumanRForearm3"); R3 = M3[:3, :3]
P0 = np.array(h.m_Vertices); P = (M3 @ np.c_[P0, np.ones(len(P0))].T).T[:, :3]
N = np.array(h.m_Normals)[:, :3] @ R3.T; T4 = np.array(h.m_Tangents); T = np.c_[T4[:, :3] @ R3.T, T4[:, 3]]
UV = np.array(h.m_UV0); TR = np.array(h.get_triangles()[0]).reshape(-1, 3)
bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights); dom = bi[np.arange(len(bi)), bw.argmax(1)]
lab = pieces(P0, [TR]); sizes = np.bincount(lab)
rf = [names.index(f"Base HumanRForearm{i}") for i in (1, 2, 3)]
# patch board: triangles of the big sleeve piece with their texture centre on the board
sleeve = int(np.argmax(sizes)) if False else None
uvc = UV[TR].mean(1); px = uvc[:, 0] * 2048; py = (1 - uvc[:, 1]) * 2048
on_board = (px >= BOARD[0]) & (px <= BOARD[2]) & (py >= BOARD[1]) & (py <= BOARD[3]) & np.isin(dom[TR], rf).all(1)
board_t = TR[on_board]
# chains and tags: separate pieces (< 1000 vertices) mostly on the right forearm bones
small = [p for p in range(lab.max() + 1) if 8 <= sizes[p] < 1000 and np.isin(dom[lab == p], rf).mean() > 0.6]
kit_t = TR[np.isin(lab[TR[:, 0]], small)]
print(f"board triangles {len(board_t)}, kit pieces {len(small)} ({len(kit_t)} triangles)")


def sub(tris):
    used = np.unique(tris); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
    return used, remap[tris]


ub, tb = sub(board_t); uk, tk = sub(kit_t)
# source right-forearm frame from the outlines (same Forearm3 space)
o = json.load(open("WORK/wristacc/outlines.json"))[HID]["R"]
w, ax, e1 = (np.array(o[k]) for k in ("w", "ax", "e1")); e2 = np.cross(ax, e1)
allV = P[np.r_[ub, uk]]; rel = allV - w; al = rel @ ax; rad = np.linalg.norm(rel - np.outer(al, ax), axis=1)
a_c = float(np.median(al)); c = w + ax * a_c; r0 = float(np.median(rad))
th = np.linspace(0, 2 * np.pi, 180, endpoint=False)                 # starts at the back of the hand: angles kept
pts = c + r0 * (np.cos(th)[:, None] * e1 + np.sin(th)[:, None] * e2)
curve = Curve(pts, radial_from(c), True)
labk = pieces(P[uk], [tk]); ext = np.array([np.ptp(P[uk][labk == p], axis=0).max() for p in range(labk.max() + 1)])
kind_k = np.where(ext[labk] > 0.06, 0, 1)          # long pieces (chains around the arm) follow it, tags and rings stay rigid
mk = lambda used, tris, nm: dict(name=mesh.m_Name, bundle=BUNDLE, mat=0, P=P[used], N=N[used], T=T[used], UV=UV[used], tris=[tris])
ms = [mk(ub, tb, "board"), mk(uk, tk, "kit")]
kinds = [(np.zeros(len(ub), int), np.zeros(len(ub), int)), (1 + labk, kind_k)]


def variant(tex):
    return lambda curve, anchors, rmin: {"texture": tex} if tex else {}


rest_t = TR[~(on_board | np.isin(lab[TR[:, 0]], small))]
gap = surface_gap(P, rest_t, w, ax, e1)
# ---- dogtags: on the BD / ISB sleeves the Predator's 8 USEC/BEAR tag plates become Black Division tags (WTT-ContentBackport)
# and ISB's chess-rank tags (ISB-Aishi; same mesh as the BD red tag, own material). Each new plate (+ its small ring) takes the
# old plate's place: ring end where the old plate meets its chain, face away from the arm, real size (37.6 x 22.8 mm).
MODS = "SPT/SPT_Runtime/user/mods/"
BDK = "assets/content/items/barter/item_barter_dogtags_blackdivision/item_barter_dogtags_blackdivision_{}.bundle"
ISBK = "equipment/item_barter_dogtags_isb_{}.bundle"
tenv = UnityPy.load(MODS + "WTT-ContentBackport/bundles/" + BDK.format("red"))
tmesh = next(o.read() for o in tenv.objects if o.type.name == "Mesh" and o.peek_name() == "Item_barter_dogtags_blackdivision_red_LOD0")
th_ = MeshHandler(tmesh); th_.process()
TP = np.array(th_.m_Vertices); TN = np.array(th_.m_Normals)[:, :3]; TT = np.array(th_.m_Tangents) if th_.m_Tangents else np.zeros((len(TP), 4))
TUV = np.array(th_.m_UV0); TTR = np.array(th_.get_triangles()[0]).reshape(-1, 3); tlab = pieces(TP, [TTR])
dims_ = lambda Q: sorted(np.ptp((Q - Q.mean(0)) @ np.linalg.svd(Q - Q.mean(0))[2].T, axis=0), reverse=True)
plate = max(range(tlab.max() + 1), key=lambda q: (tlab == q).sum() if dims_(TP[tlab == q])[2] < 0.003 and dims_(TP[tlab == q])[0] < 0.045 else 0)
ring = min((q for q in range(tlab.max() + 1) if 0.004 < dims_(TP[tlab == q])[0] < 0.007), key=lambda q: np.linalg.norm(TP[tlab == q].mean(0) - TP[tlab == plate].mean(0)))
_rc = TP[tlab == plate].mean(0); _, _, _rv = np.linalg.svd(TP[tlab == plate] - _rc); _rp = (TP[tlab == plate] - _rc) @ _rv.T
def _inside(q):
    Q = (TP[tlab == q] - _rc) @ _rv.T
    return all((Q[:, i] > _rp[:, i].min() - 1e-3).all() and (Q[:, i] < _rp[:, i].max() + 1e-3).all() for i in (0, 1)) and (np.abs(Q[:, 2]) < 0.003).all()
faces = [q for q in range(tlab.max() + 1) if q not in (plate, ring) and _inside(q) and dims_(TP[tlab == q])[0] > 0.02]
tkeep = np.isin(tlab, [plate, ring] + faces); TTRk = TTR[tkeep[TTR].all(1)]      # rim = rubber silencer, faces = the plate itself
print(f"dogtag: rim + {len(faces)} plate faces + ring")
def frame(Q, hole):
    """plate frame: long axis toward the hole end, normal, third axis; origin = plate's hole end"""
    c = Q.mean(0); _, _, vt = np.linalg.svd(Q - c); a1, a3 = vt[0], vt[2]
    if (hole - c) @ a1 < 0: a1 = -a1
    a2 = np.cross(a3, a1); end = c + a1 * ((Q - c) @ a1).max()
    return np.array([a1, a2, a3]), end
Fb, Eb = frame(TP[tlab == plate], TP[tlab == ring].mean(0))
# the Predator's plates: kit pieces of 42.7 x 25.5 x 2.1 mm; hole end = the end nearest the rest of the kit (chains, rings)
KP = P[uk]; tagp = [q for q in range(labk.max() + 1) if abs(dims_(KP[labk == q])[0] - 0.0427) < 0.002 and dims_(KP[labk == q])[2] < 0.003]
others = KP[~np.isin(labk, tagp)]
# tags lie FLAT on the sleeve at the Predator tags' places (distance along the arm + angle around it), ring end toward the
# elbow; tags that would cover the patch board (-90..+33 deg around) go to the opposite side (+180 deg). 0.8 mm above the
# source sleeve, so the runtime fit keeps them on the surface instead of dangling out (user: "floating" tags)
_Bd = P[ub] - w; _ban = np.degrees(np.arctan2((_Bd - np.outer(_Bd @ ax, ax)) @ e2, (_Bd - np.outer(_Bd @ ax, ax)) @ e1))
BOARD_ANG = (float(np.percentile(_ban, 1)) - 7, float(np.percentile(_ban, 99)) + 7)
slots = []; OWN = {}
for q in tagp:
    c = KP[labk == q].mean(0); al_ = float((c - w) @ ax); r_ = (c - w) - ax * al_
    th = float(np.degrees(np.arctan2(r_ @ e2, r_ @ e1)))
    if BOARD_ANG[0] <= th <= BOARD_ANG[1]: th = th + 180 if th < 0 else th - 180
    u = np.cos(np.radians(th)) * e1 + np.sin(np.radians(th)) * e2; a1 = -ax; a2 = np.cross(u, a1)
    Fp = np.array([a1, -a2, -u]); Ep = w + ax * al_ + u * 0.05 + a1 * 0.0188         # hole end, half a tag toward the elbow; turned over (user)
    Qo = KP[labk == q]; co = Qo.mean(0); _, _, vto = np.linalg.svd(Qo - co)
    endso = [co + vto[0] * ((Qo - co) @ vto[0]).max(), co + vto[0] * ((Qo - co) @ vto[0]).min()]
    OWN[len(slots)] = (q, frame(Qo, min(endso, key=lambda e_: np.linalg.norm(others - e_, axis=1).min())))
    slots.append((Fp, Ep, al_, u, th))
slots.sort(key=lambda t: -t[2])                                           # wrist first
print('  tag places (mm along, deg around):', [(round(t[2] * 1000), round(t[4])) for t in slots])
for q in tagp:
    c_ = KP[labk == q].mean(0); r_ = (c_ - w) - ax * ((c_ - w) @ ax)
    print(f"  tag piece {q}: {((c_ - w) @ ax)*1000:6.1f} mm along, {np.degrees(np.arctan2(r_ @ e2, r_ @ e1)):6.0f} deg around, {np.linalg.norm(r_)*1000:5.1f} mm out")
import os as _os
_B = P[ub] - w; _al = _B @ ax; _r = _B - np.outer(_al, ax); _an = np.degrees(np.arctan2(_r @ e2, _r @ e1))
print(f"  board: {_al.min()*1000:.0f}..{_al.max()*1000:.0f} mm along, {_an.min():.0f}..{_an.max():.0f} deg around (median {np.median(_an):.0f})")
if _os.environ.get("SLOTS_ONLY"): raise SystemExit
print(f"dogtag slots {len(slots)} (plate {dims_(TP[tlab == plate])[0]*1000:.1f} mm + ring)")
used_t = np.unique(TTRk); rm = -np.ones(len(TP), int); rm[used_t] = np.arange(len(used_t))
def tag_mesh(Fp, Ep, key, u):
    R = Fp.T @ Fb                                                        # BD plate frame -> slot frame
    Pn = (TP[used_t] - Eb) @ R.T + Ep
    Pn = Pn + u * (0.0008 - np.maximum(gap(Pn), -0.05).min())            # flat on the source sleeve, 0.8 mm above it
    return dict(name="Item_barter_dogtags_blackdivision_red_LOD0", bundle=key, mat=0, P=Pn, N=TN[used_t] @ R.T,
                T=np.c_[TT[used_t, :3] @ R.T, TT[used_t, 3]], UV=TUV[used_t], tris=[rm[TTRk]])
# the 2 clips (44.6 x 10.4 mm) stay (user 2026-10-26: "add it back", removed 2026-10-25 19:45); only the old tags' silencers go
clips = [q for q in range(labk.max() + 1) if 0.045 < dims_(KP[labk == q])[0] < 0.052 and 0.018 < dims_(KP[labk == q])[1] < 0.022]   # the old tags' rubber silencers
clip_lab = set(lab[uk[np.isin(labk, clips)]]); print(f"silencers removed: {len(clips)}")
# the 2 clips hang straight out from the sleeve in the outfit's rest pose (3.7..41 mm off it): laid FLAT like the tags - turned
# about the end that meets the rest of the kit until their thinnest side faces away from the arm, then 0.8 mm above the sleeve
keepc = [q for q in range(labk.max() + 1) if abs(dims_(KP[labk == q])[0] - 0.0446) < 0.002 and abs(dims_(KP[labk == q])[1] - 0.0104) < 0.002]
for q in keepc:
    idx = uk[labk == q]; V = P[idx]; others = P[uk[labk != q]]
    att = V[np.argmin(np.min(np.linalg.norm(V[:, None] - others[None, ::7], axis=2), axis=1))]
    u = (att - w) - ax * ((att - w) @ ax); u /= np.linalg.norm(u)
    thin = np.linalg.svd(V - V.mean(0), full_matrices=False)[2][2]
    if thin @ u < 0: thin = -thin
    vx = np.cross(thin, u); sn = np.linalg.norm(vx); cs = thin @ u
    K = np.array([[0, -vx[2], vx[1]], [vx[2], 0, -vx[0]], [-vx[1], vx[0], 0]])
    Rm = np.eye(3) + K + K @ K * ((1 - cs) / (sn ** 2)) if sn > 1e-8 else np.eye(3)
    Vn = (V - att) @ Rm.T + att
    # then turned about the outward axis until it lies ALONG the arm, free end toward the elbow (lying round the arm, a straight
    # 44 mm clip lifts ~20 mm off the curved sleeve at its free end)
    far = Vn[np.argmax(np.linalg.norm(Vn - att, axis=1))] - att; far -= u * (far @ u); far /= np.linalg.norm(far)
    want = -ax - u * (-ax @ u); want /= np.linalg.norm(want)
    ang = np.arctan2(np.cross(far, want) @ u, far @ want); Ku = np.array([[0, -u[2], u[1]], [u[2], 0, -u[0]], [-u[1], u[0], 0]])
    R2 = np.eye(3) + np.sin(ang) * Ku + (1 - np.cos(ang)) * Ku @ Ku; Rm = R2 @ Rm
    Vn = (V - att) @ Rm.T + att; Vn = Vn + u * (0.0008 - np.maximum(gap(Vn), -0.05).min())
    P[idx] = Vn; N[idx] = N[idx] @ Rm.T; T[idx, :3] = T[idx, :3] @ Rm.T
    print(f"clip {q}: laid flat, {np.ptp((Vn - w) @ u)*1000:.1f} mm thick on the sleeve (was {np.ptp((V - w) @ u)*1000:.1f})")
kit_wo = kit_t[~np.isin(lab[kit_t[:, 0]], [s_ for s_ in small if s_ in set(lab[uk[np.isin(labk, tagp)]]) or s_ in clip_lab])]
kit_np = kit_t[~np.isin(lab[kit_t[:, 0]], list(clip_lab))]                      # the Predator's own sleeve: tags kept, clips gone
ukp, tkp = sub(kit_np); labkp = pieces(P[ukp], [tkp]); extp = np.array([np.ptp(P[ukp][labkp == p], axis=0).max() for p in range(labkp.max() + 1)])
ms = [mk(ub, tb, "board"), mk(ukp, tkp, "kit")]; kinds = [kinds[0], (1 + labkp, np.where(extp[labkp] > 0.06, 0, 1))]
uk2, tk2 = sub(kit_wo); labk2 = pieces(P[uk2], [tk2]); ext2 = np.array([np.ptp(P[uk2][labk2 == p], axis=0).max() for p in range(labk2.max() + 1)])
ms2 = [mk(ub, tb, "board"), mk(uk2, tk2, "kit")]; kinds2 = [kinds[0], (1 + labk2, np.where(ext2[labk2] > 0.06, 0, 1))]
BDT = [BDK.format(c_) for c_ in ("red", "green", "ferrum")]
ISBT = [ISBK.format(c_) for c_ in ("king", "queen", "bishop", "horse", "tower", "pawn")]
TAGS = {"bd_sleeve": [BDT[i % 3] for i in range(len(slots))], "isb_sleeve": [(BDT if i % 2 == 0 else ISBT)[(i // 2) % (3 if i % 2 == 0 else 6)] for i in range(len(slots))],
        "mixed_sleeve": [(BDT if i % 2 == 0 else ISBT)[(i // 2) % 3] for i in range(len(slots))]}
def swing_of(Pn, toward_hole):
    """the tag's pivot (its ring end) and tip (far end) vertex indices, for the runtime swing (TagSwing)"""
    d = Pn @ toward_hole; return [int(np.argmax(d)), int(np.argmin(d))]


def polish(name, swings=None):
    """board: only triangles fully inside the panel outline (convex hull of its texture area, pulled in 10 px) -> clean edge;
    edge vertices flat on the sleeve (no lip), the rest at most 2.5 mm proud. Chains (kit strands): at most 1.5 mm off the sleeve."""
    from scipy.spatial import ConvexHull
    from PIL import ImageDraw
    from scipy.ndimage import binary_erosion
    f = OUT + f"acc_{name}.json"; js = json.load(open(f)); b = js["meshes"][0]
    uv = np.array(b["uv"]) * [2048, -2048] + [0, 2048]; tr = np.array(b["tris"][0]).reshape(-1, 3); r = np.array(b["r"])
    hull = uv[ConvexHull(uv).vertices]; mk = Image.new("L", (2048, 2048), 0); ImageDraw.Draw(mk).polygon([tuple(p_) for p_ in hull], fill=255)
    inside = binary_erosion(np.asarray(mk) > 0, iterations=10); ui = np.clip(np.round(uv).astype(int), 0, 2047)
    vin = inside[ui[:, 1], ui[:, 0]]; keep = tr[vin[tr].all(1)]
    from collections import Counter
    ec = Counter(tuple(sorted(e)) for t in keep for e in ((t[0], t[1]), (t[1], t[2]), (t[2], t[0])))
    edge_v = {v for e, c in ec.items() if c == 1 for v in e}
    r = np.minimum(r, 0.0025); r[list(edge_v)] = 0.0
    b["tris"] = [keep.ravel().tolist()]; b["r"] = r.round(5).tolist()
    k = js["meshes"][1]; kr = np.array(k["r"]); kk = np.array(k["kind"]); kr[kk == 0] = np.minimum(kr[kk == 0], 0.0015); k["r"] = kr.round(5).tolist()
    if swings:                                                          # the dogtags hang and swing from their ring end
        for i_, sw in enumerate(swings): js["meshes"][2 + i_]["swing"] = sw
    json.dump(js, open(f, "w"))
    print(f"  polish {name}: board {len(tr)} -> {len(keep)} triangles, {len(edge_v)} edge vertices flat, chains <= 1.5 mm")


for name, note, tex in [("predator_sleeve", "USEC Predator kill sleeve", None),
                        ("bd_sleeve", "Black Division kill sleeve", "tex_bd_sleeve"),
                        ("isb_sleeve", "ISB kill sleeve", "tex_isb_sleeve"),
                        ("mixed_sleeve", "Kill sleeve (Black Division + ISB)", "tex_mixed_sleeve")]:
    if name not in TAGS:                                                # the Predator's own tags, laid flat at the same places
        own = []
        for i_, (Fp, Ep, _, u, _t) in enumerate(slots):
            q, (Fq, Eq) = OWN[i_]; sel = labk == q; vi = np.where(sel)[0]; rmq = -np.ones(len(labk), int); rmq[vi] = np.arange(len(vi))
            trq = tk[sel[tk].all(1)]; R = Fp.T @ Fq; Pn = (KP[vi] - Eq) @ R.T + Ep
            Pn = Pn + u * (0.0008 - np.maximum(gap(Pn), -0.05).min())
            own.append(dict(name=mesh.m_Name, bundle=BUNDLE, mat=0, P=Pn, N=N[uk][vi] @ R.T, T=np.c_[T[uk][vi, :3] @ R.T, T[uk][vi, 3]], UV=UV[uk][vi], tris=[rmq[trq]]))
        build(name, ms2 + own, curve, kinds2 + [(np.zeros(len(t["P"]), int), np.ones(len(t["P"]), int)) for t in own], bundle=BUNDLE, note=note, extra=variant(tex), gap=gap)
        polish(name, [swing_of(t["P"], sl[0][0]) for t, sl in zip(own, slots)]); continue
    tm = [tag_mesh(Fp, Ep, k_, u) for (Fp, Ep, _, u, _t), k_ in zip(slots, TAGS[name])]
    build(name, ms2 + tm, curve, kinds2 + [(np.zeros(len(t["P"]), int), np.ones(len(t["P"]), int)) for t in tm], bundle=BUNDLE, note=note, extra=variant(tex), gap=gap)
    polish(name, [swing_of(t["P"], sl[0][0]) for t, sl in zip(tm, slots)])

# ---- patch textures: the Predator's own diffuse with the four patches replaced (game / mod pixels only) ----
SRC = "WORK/wristacc/patches/"
BD = "SPT/SPT_Runtime/user/mods/BlackDivServer/db/"
ISB = "SPT/SPT_Runtime/user/mods/ISB-SpecialForces/db/CustomAchievements/Images/"
RECTS = [(1452, 962, 1668, 1082), (1432, 1072, 1582, 1238), (1456, 1245, 1702, 1330), (1470, 1305, 1660, 1398)]   # USEC, BEAR roundel, BEAR PMC strip, USEC
SETS = {"tex_bd_sleeve": [BD + "bd_common.png", "SPT/SPT_Runtime/user/mods/ISB-SpecialForces/db/ISBAndBlackDiv/Achievements/Images/BlackDivisionInvestigation.png", BD + "CustomAchievements/Images/bdlegendary.png", BD + "CustomAchievements/Images/bdrare.png"],
        "tex_isb_sleeve": [ISB + "SPECIAL.png", ISB + "ContainmentSurvivor.png", ISB + "SPECIAL2.png", ISB + "ISBTAG01.png"]}
import glob
def find(pth):
    if os.path.exists(pth): return pth
    hits = glob.glob(os.path.join(os.path.dirname(pth), os.path.basename(pth).split(".")[0][:18] + "*"))
    return hits[0] if hits else None
dif = Image.open(SRC + "Hands_Boss_1_d_rgba.png").convert("RGBA"); nrm = Image.open(SRC + "Hands_Boss_1_n.png").convert("RGBA")
os.makedirs(OUT, exist_ok=True)
def unchecker(im):
    """bd_common.png has a transparency checkerboard baked in: light grey squares connected to the border -> transparent"""
    a = np.asarray(im.convert("RGBA")).copy(); rgb = a[..., :3].astype(int)
    light = (rgb.min(2) > 150) & (rgb.max(2) - rgb.min(2) < 12)
    from scipy.ndimage import label
    lb, _ = label(light); edge = set(np.unique(np.r_[lb[0], lb[-1], lb[:, 0], lb[:, -1]])) - {0}
    a[np.isin(lb, list(edge)), 3] = 0
    return Image.fromarray(a)


# BD / ISB boards: every patch keeps its own shape and printed rim; its inside is filled with its own dark backing and
# the new art goes on it. Patches = the patch pixels (white print, black backing, greys) inside each patch's box; the
# BEAR strip and the USEC patch lying over it count as one patch. Texture px of the 2048 texture, y from the top.
from scipy.ndimage import binary_erosion, binary_opening, binary_fill_holes, binary_closing, label
PATCHES = [[(1436, 954, 1678, 1092)], [(1426, 1060, 1588, 1246)], [(1446, 1232, 1722, 1346), (1460, 1296, 1674, 1410)]]   # top USEC, BEAR shield, strip + USEC
# Black Division sleeve: Black Division's own patches (black/red shield + BD callsign tape, cut from Black Division's outfit
# textures in ISB-Aishi); ISB sleeve: ISB art
BDP = "WORK/wristacc/patches/bd/"
# ISB patch: the ISB armband's logo (ISB-Aishi item_equipment_armband_isbarmband, armband_usec_d), cut off at the texture edge;
# left wing + shield rim completed by mirroring its own right half (user pick), letters as in the texture
ISBL = "WORK/wristacc/patches/isb/isb_logo_full.png"
ISBC = "WORK/wristacc/patches/isb/isb_logo_clawgear.png"
ISBP = "WORK/wristacc/patches/isb/isb_patch_clawgear.png"          # the same logo as a whole patch on its own black backing          # complete ISB logo patch (ISB-Aishi Top_USEC_Clawgear_d)
ARTS = {"tex_bd_sleeve": [BDP + "bd1_tape.png", BDP + "bd_shield_patch.png", BDP + "bd3_tape.png"],   # BD3 = Black Division's own tape (Top_BD_Operator_03_d)     # user: BD sleeve = BD's own patches
        "tex_isb_sleeve": [None, None, ISBP],                                   # user: USEC, BEAR, ISB (None = the patch stays)
        "tex_mixed_sleeve": [BDP + "bd1_tape.png", BDP + "bd_shield_patch.png", ISBP]}   # user: both
H_, W_ = dif.height, dif.width
hsv = np.asarray(dif.convert("HSV")).astype(float); rgbf = np.asarray(dif.convert("RGB")).astype(float)
is_patch = (hsv[..., 1] < 60) | (hsv[..., 2] < 38)                    # white print, black backing, greys (camo is olive / brown)
taken = np.zeros((H_, W_), bool); masks = []
for boxes in PATCHES:
    m = np.zeros((H_, W_), bool)
    for x0, y0, x1, y1 in boxes: m[y0:y1, x0:x1] = True
    m &= is_patch & ~taken
    m = binary_fill_holes(binary_closing(binary_opening(m, iterations=2), iterations=3))
    lb, nlab = label(m); m = lb == (np.bincount(lb.ravel())[1:].argmax() + 1) if nlab else m
    masks.append(m); taken |= m
print("patch areas px:", [int(m.sum()) for m in masks])
# replaced patches: the old patch (rim included) is erased - smooth fill from its edge + the board's own fabric weave/normals from
# a clean spot of the same board - then the new patch is placed whole (its own shape and edge), centred on the old one
from scipy.ndimage import binary_dilation, uniform_filter, gaussian_filter
BLACK = np.asarray(Image.open(SRC + "Hands_Boss_1_black_d_rgba.png").convert("RGBA")).astype(float); NRM = np.asarray(nrm).astype(float)
allm_raw = binary_dilation(np.logical_or.reduce(masks), iterations=8)
allm = binary_dilation(np.logical_or.reduce(masks), iterations=12)
best = None
for yy in range(BOARD[1], BOARD[3] - 64, 8):                          # a clean 64 px fabric window on the board
    for xx in range(BOARD[0], BOARD[2] - 64, 8):
        if allm[yy:yy + 64, xx:xx + 64].any(): continue
        sd = BLACK[yy:yy + 64, xx:xx + 64, :3].mean(2).std()
        if best is None or sd < best[0]: best = (sd, yy, xx)
_, fy, fx = best; print(f"fabric sample at ({fx},{fy})")
fab = BLACK[fy:fy + 64, fx:fx + 64]; fabn = NRM[fy:fy + 64, fx:fx + 64]
fab_hp = fab - gaussian_filter(fab, (6, 6, 0))
def erase(d, n, m):
    """old patch (+8 px, + leftover bright print within 20 px) -> the board's own black: the colour/gloss of the fabric right
    around it + the fabric sample's weave and normals"""
    D = binary_dilation(m, iterations=8)
    D |= binary_dilation(D, iterations=20) & (BLACK[..., :3].mean(2) > 90) & ~allm_raw
    D = binary_dilation(D, iterations=2)
    ring = binary_dilation(D, iterations=10) & ~D & ~allm_raw
    base = np.median(d[ring], axis=0); ty, tx = np.where(D)
    d[ty, tx] = np.clip(base + fab_hp[ty % 64, tx % 64], 0, 255); n[ty, tx] = fabn[ty % 64, tx % 64]
for tex, arts in ARTS.items():
    d = BLACK.copy(); n = NRM.copy()
    for m, art in zip(masks, arts):
        if art is None: continue
        gl = float(np.median(np.asarray(dif)[..., 3][m]))
        erase(d, n, m)
        im = Image.open(art).convert("RGBA"); bb = im.getbbox(); im = im.crop(bb) if bb else im
        ys, xs = np.where(m); bw_, bh_ = xs.max() - xs.min(), ys.max() - ys.min()
        sc = min(bw_ * 0.95 / im.width, bh_ * 0.95 / im.height); im = im.resize((max(1, int(im.width * sc)), max(1, int(im.height * sc))), Image.LANCZOS).rotate(180)
        x0 = int((xs.min() + xs.max()) / 2 - im.width / 2); y0 = int((ys.min() + ys.max()) / 2 - im.height / 2)
        a_ = np.asarray(im).astype(float); al = a_[..., 3:4] / 255
        reg = d[y0:y0 + im.height, x0:x0 + im.width]
        reg[..., :3] = reg[..., :3] * (1 - al) + a_[..., :3] * al; reg[..., 3] = reg[..., 3] * (1 - al[..., 0]) + gl * al[..., 0]
        nr = n[y0:y0 + im.height, x0:x0 + im.width]; nr[al[..., 0] > 0.5] = (128, 128, 255, 255)
    Image.fromarray(np.clip(d, 0, 255).astype(np.uint8)).save(OUT + tex + "_d.png")                 # RGBA: the shader reads gloss from alpha
    print("texture", tex, [os.path.basename(a) if a else "(kept)" for a in arts])
