# arm_ink.py: ARM + HAND TATTOO OVERLAYS for any top (user 2026-10-27: "add the Day Off skeleton arm tattoo ... any top, hidden under
# sleeves", "scan for other tattoos similar to this and add them too. hand and arm").
#   scan            every outfit (one per hands bundle): per side, the forearm + hand triangles (top bone Human<S> Forearm / Palm /
#                   Digit), welded islands that are mostly skin (> 50% of triangle centres skin-coloured), and in them the INK: a
#                   triangle whose centre texel is not skin. -> arm_ink_scan.json (ink triangles, share, texture) for review
#   export <hid> <S> <name> <note>   that side's ink triangles (+ one ring of neighbours) as hw_<name>.json, kind "tattoo",
#                   "press": true -> the plugin (Seam.cs PressInk) puts every vertex 0.6 mm over the WEARER's own arm and drops the
#                   triangles whose wearer surface there is not skin (sleeve, glove): hidden under sleeves. Material: InkMaterial.
import os, sys, json, base64, colorsys, collections; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
import feet_combo as F
SCAN = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
W = ("watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin", "starline", "shock", "casio")
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
def bundle_key(path):
    p = path.replace("\\", "/"); i = p.lower().find("assets/content/")
    if i >= 0: return p[i:]
    j = p.lower().find("/bundles/"); return p[j + 9:] if j >= 0 else p
def skin_like(rgb):
    h, s, v = colorsys.rgb_to_hsv(*rgb[:3]); h *= 360
    return (h < 50 or h > 340) and 0.12 < s < 0.7 and v > 0.22
def texel(img, uv):
    uv = uv % 1.0; px = img[((1 - uv[:, 1]) * (img.shape[0] - 1)).astype(int), (uv[:, 0] * (img.shape[1] - 1)).astype(int)][:, :3].astype(float)
    return px / 255.0 if px.max() > 1.5 else px
def load(hid):
    rec = SCAN[hid]; env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
        if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt)
    if best is None: return None
    _, paths, mo, r, rt = best; mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
    d = dict(rec=rec, mesh=mesh, mt=mt, names=[p.split("/")[-1] for p in paths])
    d["P"] = np.array(h.m_Vertices, float); d["N"] = np.array(h.m_Normals, float)[:, :3]; d["UV"] = np.array(h.m_UV0, float)[:, :2]
    d["Tg"] = np.array(h.m_Tangents, float) if h.m_Tangents is not None and len(h.m_Tangents) else np.tile([1, 0, 0, 1.0], (len(d["P"]), 1))
    d["bi"] = np.array(h.m_BoneIndices); d["bw"] = np.array(h.m_BoneWeights, float); d["subs"] = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
    mats = [deref(r, m_, objs) for m_ in rt["m_Materials"]]; d["matnames"] = [(m.peek_name() if m is not None else "?") for m in mats]
    d["tex"] = [F.tex_of(m) if m is not None else None for m in mats]
    return d
def ink_of(d, S):
    """per submesh: (arm triangles, ink mask, ring mask) for side S"""
    names = d["names"]; bi, bw = d["bi"], d["bw"]; top = bi[np.arange(len(bi)), bw.argmax(1)]
    arm = np.array([f"Human{S}" in names[x] and any(k in names[x] for k in ("Forearm", "Palm", "Digit")) for x in top])
    key = np.round(d["P"] / 0.0002).astype(np.int64); _, wid = np.unique(key, axis=0, return_inverse=True); wid = wid.ravel()
    out = []; allT = []; info = []
    for si, T in enumerate(d["subs"]):
        img = d["tex"][si] if si < len(d["tex"]) else None; mn = d["matnames"][si].lower() if si < len(d["matnames"]) else ""
        if img is None or not len(T) or any(w in mn for w in W): out.append(None); continue
        A = T[arm[T].all(1)]
        if not len(A): out.append(None); continue
        cs = np.array([skin_like(p) for p in texel(img, d["UV"][A].mean(1))])
        # ink as the plugin's InkMaterial sees it: CIELAB L under the texture's skin mean - 1.5 sd, or chroma > 18 and hue > 40 deg
        # away from skin; 7 samples per triangle (thin lines on big forearm triangles)
        import seamtone as ST
        fl = img.reshape(-1, img.shape[-1])[:, :3].astype(float); fl = fl / 255.0 if fl.max() > 1.5 else fl
        smp = fl[::97]; sm, ssd = ST.stats(smp) if ST.stats(smp) else (np.array([50, 15, 15.0]), np.array([10, 5, 5.0]))
        hueS = np.degrees(np.arctan2(sm[2], sm[1]))
        def inky(px):
            L = ST.to_lab(px); ch = np.hypot(L[:, 1], L[:, 2]); dh = np.abs((np.degrees(np.arctan2(L[:, 2], L[:, 1])) - hueS + 180) % 360 - 180)
            return (L[:, 0] < sm[0] - 1.5 * ssd[0]) | ((ch > 18) & (dh > 40))
        U = d["UV"][A]; hit = np.zeros(len(A), bool)
        for bw_ in ((1/3, 1/3, 1/3), (0.6, 0.2, 0.2), (0.2, 0.6, 0.2), (0.2, 0.2, 0.6), (0.45, 0.45, 0.1), (0.45, 0.1, 0.45), (0.1, 0.45, 0.45)):
            hit |= inky(texel(img, (U * np.array(bw_)[None, :, None]).sum(1)))
        out.append([A, cs, hit]); allT.append(A); info.append(si)
    if not allT: return []
    T = np.vstack(allT); par = np.arange(wid.max() + 1)
    def f(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    for t_ in T: a, b, c = f(wid[t_[0]]), f(wid[t_[1]]), f(wid[t_[2]]); par[b] = a; par[f(c)] = a
    res = []
    for si, o in enumerate(out):
        if o is None: continue
        A, cs, hit = o; comp = np.array([f(wid[t_[0]]) for t_ in A])
        # island skin share over ALL the side's arm triangles of that island (any submesh)
        res.append((si, A, cs, comp, hit))
    share = collections.defaultdict(lambda: [0, 0])
    for si, A, cs, comp, hit in res:
        for c, s in zip(comp, cs): share[c][0] += s; share[c][1] += 1
    final = []
    for si, A, cs, comp, hit in res:
        skin_isl = np.array([share[c][0] / share[c][1] > 0.5 and share[c][1] > 200 for c in comp])
        ink = skin_isl & hit
        keep = skin_isl.copy()                       # the whole bare arm + hand shell: the ink material shows only the ink
        final.append((si, A, ink, keep, skin_isl))
    # the source's own sleeve: welded to the arm skin, its cloth counts as "ink" (Three Stripes' camo cuff, lab inktest 2026-10-27).
    # Along the forearm from the palm toward the elbow, 5 mm bins: the first bin past 40 mm that is > 60% ink starts the sleeve;
    # everything from 5 mm before it on toward the elbow goes
    B = {n: np.array([[d["mt"]["m_BindPose"][i][f"e{r}{c}"] for c in range(4)] for r in range(4)]) for i, n in enumerate(names)}
    o = lambda n: np.linalg.inv(B[n])[:3, 3] if n in B else None
    pc, f3 = o(f"Base Human{S}Palm"), o(f"Base Human{S}Forearm3")
    if pc is not None and f3 is not None and final:
        up = (f3 - pc) / np.linalg.norm(f3 - pc)
        al = [((d["P"][A].mean(1) - pc) @ up) for si, A, ink, keep, isl in final]
        bins = collections.defaultdict(lambda: [0, 0])
        for (si, A, ink, keep, isl), a in zip(final, al):
            for b_, i_, k_ in zip((a / 0.005).astype(int), ink, isl):
                if k_: bins[b_][0] += i_; bins[b_][1] += 1
        cut = None
        for b_ in sorted(k for k in bins if k * 0.005 > 0.04):
            if bins[b_][1] >= 10 and bins[b_][0] / bins[b_][1] > 0.6: cut = b_ * 0.005 - 0.005; break
        if cut is not None:
            final = [(si, A, ink & (a < cut), keep & (a < cut), isl) for (si, A, ink, keep, isl), a in zip(final, al)]
            print(f"  {S}: source sleeve from {cut * 1000 + 5:.0f} mm up the forearm - cut there")
    return final

if __name__ == "__main__" and sys.argv[1] == "scan":
    seen = {}
    for h_, v in SCAN.items(): seen.setdefault(v["path"], h_)
    res = {}
    for hid in seen.values():
        try:
            d = load(hid)
            if d is None: continue
            for S in "LR":
                fin = ink_of(d, S)
                if not fin: continue
                ink = sum(int(x[2].sum()) for x in fin); skin = sum(int(x[4].sum()) for x in fin)
                res[f"{hid}_{S}"] = dict(hid=hid, side=S, outfit=d["rec"]["name"], mesh=d["mesh"].m_Name, ink=ink, skin=skin, share=round(ink / max(1, skin), 3),
                                         mats=[d["matnames"][x[0]] for x in fin])
                print(hid[:8], S, d["rec"]["name"][:40], "ink", ink, "of", skin, flush=True)
        except Exception as e: print(hid[:8], "FAIL", str(e)[:80], flush=True)
    json.dump(res, open("WORK/wristacc/lab/arm_ink_scan.json", "w"), indent=1)

if __name__ == "__main__" and sys.argv[1] == "export":
    hid, S, name, note = sys.argv[2:6]; OUT = sys.argv[6] if len(sys.argv) > 6 else "WORK/wristacc/acc"
    d = load(hid); fin = ink_of(d, S); keep = [(si, A[k]) for si, A, ink, k, _ in fin if k.any()]
    P, N, UV, Tg, bi, bw, mt, names = d["P"], d["N"], d["UV"], d["Tg"], d["bi"], d["bw"], d["mt"], d["names"]
    used = np.unique(np.concatenate([t.ravel() for _, t in keep])); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
    ub = np.unique(bi[used][bw[used] > 0]); bmap = {int(b): i for i, b in enumerate(ub)}
    bi2 = np.vectorize(lambda x: bmap.get(int(x), 0))(bi[used]); bw2 = bw[used] * (np.isin(bi[used], ub))
    bind = [[mt["m_BindPose"][int(b)][f"e{rr}{cc}"] for rr in range(4) for cc in range(4)] for b in ub]
    P2 = P[used] + N[used] / (np.linalg.norm(N[used], axis=1, keepdims=True) + 1e-9) * 0.0006
    out = dict(name=name, note=note, kind="tattoo", side=S, bundle=bundle_key(d["rec"]["path"]), mesh=d["mesh"].m_Name, bones=[names[int(b)] for b in ub], bind=bind,
               n=int(len(used)), pos=b64(P2, "<f4"), nrm=b64(N[used], "<f4"), tan=b64(Tg[used], "<f4"), uv=b64(UV[used], "<f4"), bi=b64(bi2, "<i4"), bw=b64(bw2, "<f4"),
               subs=[dict(mat=d["matnames"][si], sub=int(si), tris=b64(remap[t].ravel(), "<i4")) for si, t in keep], ink=True, press=True)
    json.dump(out, open(f"{OUT}/hw_{name}.json", "w")); print(f"{name}: {note}, {len(used)} verts, {sum(len(t) for _, t in keep)} triangles")
