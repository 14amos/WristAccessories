# export_handwear.py <picks.json> <out dir>: handwear items (lab/scan_hands.py finds) -> hw_<name>.json for the plugin's HandWear:
# the piece's triangles as a skinned mesh in its source mesh space (positions, normals, tangents, uv, 4 bone weights, the bone
# NAMES it uses + their bind poses), its submesh's material by bundle + source mesh name (the plugin loads the material from
# the game's bundle, as it does for accessories). In game the piece is drawn by the player's own hand bones (same skeleton
# names in every outfit), so it bends with every finger.
#   kind "hand": the side's whole hand (every triangle mostly weighted to Palm / Digit bones, >= 0.35, so it overlaps the
#     player's hand cut at 0.5 - no gap at the wrist); the plugin hides the player's own hand on that side.
#   kind "ring" / "wrap": one piece (the scan's piece index on that side), drawn on top.
# picks.json: [{"name", "note", "hid", "side": "L"|"R", "kind", "piece": <index in the side's extras, for ring / wrap>}]
import os, sys, json, base64, collections, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
picks = json.load(open(sys.argv[1], encoding="utf-8")); OUT = sys.argv[2]; os.makedirs(OUT, exist_ok=True)
scan = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
def bundle_key(path):
    p = path.replace("\\", "/"); i = p.lower().find("assets/content/")
    if i >= 0: return p[i:]
    j = p.lower().find("/bundles/")                 # a mod's bundle: SPT's key = the path under the mod's bundles folder
    return p[j + 9:] if j >= 0 else p
for pk in picks:
    rec = scan[pk["hid"]]; env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        nm = mo.peek_name().lower(); score = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm) * 1
        if best is None or score > best[0]: best = (score, t["_bonePaths"], mo, r, rt)
    _, paths, mo, r, rt = best
    mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
    P = np.array(h.m_Vertices, np.float64); N = np.array(h.m_Normals, np.float64)[:, :3]; UV = np.array(h.m_UV0, np.float64)[:, :2]
    Tg = np.array(h.m_Tangents, np.float64) if h.m_Tangents is not None and len(h.m_Tangents) else np.tile([1, 0, 0, 1.0], (len(P), 1))
    bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float64); names = [p.split("/")[-1] for p in paths]
    handb = [i for i, n in enumerate(names) if "Palm" in n or "Digit" in n]
    hw = np.where(np.isin(bi, handb), bw, 0).sum(1); top = bi[np.arange(len(bi)), bw.argmax(1)]
    side = np.array(["L" if "HumanL" in names[x] else "R" if "HumanR" in names[x] else "?" for x in top])
    subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
    S = pk["side"]
    if pk["kind"] == "hand":
        keep = [s[(hw[s].mean(1) > 0.35) & (side[s] == S).all(1)] for s in subs]
        # holes (user 2026-10-28: "some have gaps in them"): a triangle inside the hand but weighted mostly to the forearm
        # (knuckle / back-of-hand / cuff faces) fell under 0.35 and left a hole. Fill every patch of dropped triangles that
        # the kept ones enclose: connected (by welded edges) dropped triangles whose every neighbour is kept or in the patch.
        allT = np.concatenate(subs); tsub = np.concatenate([np.full(len(s), i) for i, s in enumerate(subs)])
        kmask = np.concatenate([(hw[s].mean(1) > 0.35) & (side[s] == S).all(1) for s in subs])
        wk = np.round(P / 1e-5).astype(np.int64); _, wv = np.unique(wk, axis=0, return_inverse=True); wv = wv.ravel()
        Tw = wv[allT]; emap = collections.defaultdict(list)
        for ti, t_ in enumerate(Tw):
            for a, b in ((t_[0], t_[1]), (t_[1], t_[2]), (t_[2], t_[0])): emap[(min(a, b), max(a, b))].append(ti)
        nb = collections.defaultdict(set)
        for ts in emap.values():
            for x in ts:
                for y in ts:
                    if x != y: nb[x].add(y)
        seen = set(); filled = 0; patches = 0
        for t0 in np.where(~kmask)[0]:
            if t0 in seen: continue
            comp, st, touches = [], [int(t0)], False
            while st:
                x = st.pop()
                if x in seen: continue
                seen.add(x); comp.append(x)
                for y in nb[x]:
                    if kmask[y]: touches = True
                    elif y not in seen: st.append(y)
            # a hole is closed all round: none of its edges is an open mesh border (a cuff strip / loose flap at the wrist is)
            border = any(len(emap[(min(a, b), max(a, b))]) == 1 for x in comp for a, b in ((Tw[x][0], Tw[x][1]), (Tw[x][1], Tw[x][2]), (Tw[x][2], Tw[x][0])))
            if touches and not border and len(comp) <= 400:                  # bigger = the rest of the body, not a hole
                kmask[comp] = True; filled += len(comp); patches += 1
        keep = [allT[kmask & (tsub == i)] for i in range(len(subs))]
        print(f"  {pk['name']}: holes filled {patches} ({filled} triangles)")
    else:
        # the piece: welded pieces of the hand's triangles (as scan_hands.py), the side's extras in the same order
        T = np.concatenate([s[hw[s].mean(1) > 0.5] for s in subs]); tsub = np.concatenate([np.full((hw[s].mean(1) > 0.5).sum(), i) for i, s in enumerate(subs)])
        key = np.round(P / 0.0002).astype(np.int64); _, wid = np.unique(key, axis=0, return_inverse=True); wid = wid.ravel(); par = np.arange(wid.max() + 1)
        def f(x):
            while par[x] != x: par[x] = par[par[x]]; x = par[x]
            return x
        for t_ in T: a, b, c = f(wid[t_[0]]), f(wid[t_[1]]), f(wid[t_[2]]); par[b] = a; par[f(c)] = a
        comp = np.array([f(wid[t_[0]]) for t_ in T])
        pcs = []
        for c in np.unique(comp):
            sel = comp == c; v = np.unique(T[sel]); sd = collections.Counter(side[v]).most_common(1)[0][0]
            if sd == S: pcs.append((int(sel.sum()), c))
        pcs.sort(key=lambda x: -x[0]); extras = [c for n, c in pcs[1:] if n >= 12]
        c = extras[pk["piece"]]; keep = [T[(comp == c) & (tsub == i)] for i in range(len(subs))]
    used = np.unique(np.concatenate([k.ravel() for k in keep if len(k)])); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
    ub = np.unique(bi[used][bw[used] > 0]); bmap = {int(b): i for i, b in enumerate(ub)}
    bi2 = np.vectorize(lambda x: bmap.get(int(x), 0))(bi[used]); bw2 = bw[used] * (np.isin(bi[used], ub))
    bind = [[mt["m_BindPose"][int(b)][f"e{rr}{cc}"] for rr in range(4) for cc in range(4)] for b in ub]
    mats = [deref(r, m_, objs) for m_ in rt["m_Materials"]]; matnames = [(m.peek_name() if m is not None else "?") for m in mats]
    tris = [(i, remap[k]) for i, k in enumerate(keep) if len(k)]
    out = dict(name=pk["name"], note=pk["note"], kind=pk["kind"], side=S, bundle=bundle_key(rec["path"]), mesh=mesh.m_Name,
               bones=[names[int(b)] for b in ub], bind=bind, n=int(len(used)),
               pos=b64(P[used], "<f4"), nrm=b64(N[used], "<f4"), tan=b64(Tg[used], "<f4"), uv=b64(UV[used], "<f4"),
               bi=b64(bi2, "<i4"), bw=b64(bw2, "<f4"), subs=[dict(mat=matnames[i] if i < len(matnames) else "?", sub=int(i), tris=b64(t.ravel(), "<i4")) for i, t in tris])
    json.dump(out, open(f"{OUT}/hw_{pk['name']}.json", "w")); print(f"{pk['name']}: {pk['kind']} {S}, {len(used)} verts, {sum(len(t) for _, t in tris)} tris, bones {len(ub)}, mats {[matnames[i] for i, _ in tris]}")
