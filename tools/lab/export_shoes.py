# export_shoes.py <out dir>: SHOES (user 2026-10-27, "like the gloves"): every lower-body outfit's footwear -> hw_shoes_<key>.json
# for the plugin's HandWear (kind "feet"). Tarkov's lower outfits are ONE mesh, pants and boots welded (lab/feet_pieces.py), so
# the shoe = every triangle mostly weighted to the Foot / Toe bones (> 0.35, both feet), as the gloves are cut by Palm / Digit
# weight. Same shoe in several outfits (same cut geometry + material) = one item, all outfit names kept. Skinned mesh in its
# source mesh space with its bone NAMES + bind poses (the player's own leg bones have the same names), material by bundle +
# source mesh name. Writes <out>/hw_shoes_*.json + lab/feetscan/shoes.json (key -> outfits, source bundle, tris).
import os, sys, json, base64, hashlib, numpy as np, UnityPy
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__))); import feet_fit as G
from UnityPy.helpers.MeshHelper import MeshHandler
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
jobs = json.load(open("WORK/wristacc/lab/feetscan/jobs.json", encoding="utf-8")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
REF = {n: np.array(m).reshape(4, 4) for n, m in json.load(open("WORK/wristacc/lab/feetscan/ref_bind.json")).items()}
def bindm(mt, i): return np.array([[mt["m_BindPose"][i][f"e{r}{c}"] for c in range(4)] for r in range(4)])
def bundle_key(path):
    p = path.replace("\\", "/"); i = p.lower().find("assets/content/")
    if i >= 0: return p[i:]
    j = p.lower().find("/bundles/")
    return p[j + 9:] if j >= 0 else p
items = {}; fails = []
for fid, rec in sorted(jobs.items(), key=lambda kv: ("(" in kv[1]["name"], kv[1]["name"])):     # game outfits first: they name the shared shoes
    try:
        env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
        for o in env.objects:
            if o.type.name != "MonoBehaviour": continue
            t = o.read_typetree()
            if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
            r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
            mo = deref(r, rt["m_Mesh"], objs) if r else None
            if mo is None: continue
            nm = mo.peek_name().lower(); sc = (0 if any(f"lod{k}" in nm for k in range(1, 5)) else 1, mo.read_typetree()["m_VertexData"]["m_VertexCount"])   # the biggest non-LOD1+ renderer (Virago: CastMesh pieces, no LOD names)
            if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt)
        if best is None: fails.append((rec["name"], "no skin")); continue
        _, paths, mo, r, rt = best
        mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
        P = np.array(h.m_Vertices, np.float64); N = np.array(h.m_Normals, np.float64)[:, :3]; UV = np.array(h.m_UV0, np.float64)[:, :2]
        Tg = np.array(h.m_Tangents, np.float64) if h.m_Tangents is not None and len(h.m_Tangents) else np.tile([1, 0, 0, 1.0], (len(P), 1))
        bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float64); sm = bw.sum(1, keepdims=True); bw = np.where(sm > 1.5, bw / np.maximum(sm, 1e-9), bw); names = [p.split("/")[-1] for p in paths]
        fb = [i for i, n in enumerate(names) if "Foot" in n or "Toe" in n]
        fw = np.where(np.isin(bi, fb), bw, 0).sum(1)
        subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
        keep = [s[fw[s].mean(1) > 0.35] for s in subs]                      # v1 cut: only for the item's name (stable since 0.10.0)
        ntri = sum(len(k) for k in keep)
        if ntri < 100: fails.append((rec["name"], f"only {ntri} foot tris")); continue
        used = np.unique(np.concatenate([k.ravel() for k in keep if len(k)])); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
        mats = [deref(r, m_, objs) for m_ in rt["m_Materials"]]; matnames = [(m.peek_name() if m is not None else "?") for m in mats]
        key = hashlib.md5(np.round(P[used] / 0.0005).astype(np.int32).tobytes() + "|".join(matnames[i] for i, k in enumerate(keep) if len(k)).encode()).hexdigest()[:12]
        if key in items: items[key]["outfits"].append(rec["name"]); items[key]["player"] |= rec.get("player", False); continue
        # v2 (0.10.1, lab/feet_fit.py): the shoe = whole boot islands (mean Foot / Toe weight >= 0.15): no scraps of the pants'
        # cuff, the full shaft (the plugin fits it to the wearer's pants with lab shoe_profiles.json)
        # the common rest space (lab/feet_ref.py): per vertex the weight-blended inv(ref bind) * own bind (identity for most outfits;
        # WTT-Artem / Virago meshes are 100x) - positions, normals, tangents; the item's bind poses become the reference ones
        Mb = np.array([np.linalg.inv(REF[n]) @ bindm(mt, i) if n in REF else np.eye(4) for i, n in enumerate(names)])
        Mv = np.einsum("nk,nkij->nij", bw, Mb[bi]); P = np.einsum("nij,nj->ni", Mv, np.c_[P, np.ones(len(P))])[:, :3]
        N = np.einsum("nij,nj->ni", Mv[:, :3, :3], N); N /= np.linalg.norm(N, axis=1, keepdims=True) + 1e-12
        t3 = np.einsum("nij,nj->ni", Mv[:, :3, :3], Tg[:, :3]); Tg = np.c_[t3 / (np.linalg.norm(t3, axis=1, keepdims=True) + 1e-12), Tg[:, 3]]
        keep = [s[k] for s, k in zip(subs, G.boot_tris(subs, fw, len(P), P))]; ntri = sum(len(k) for k in keep)
        used = np.unique(np.concatenate([k.ravel() for k in keep if len(k)])); remap = -np.ones(len(P), int); remap[used] = np.arange(len(used))
        ub = np.unique(bi[used][bw[used] > 0]); bmap = {int(b): i for i, b in enumerate(ub)}
        bi2 = np.vectorize(lambda x: bmap.get(int(x), 0))(bi[used]); bw2 = bw[used] * (np.isin(bi[used], ub))
        bind = [(REF[names[int(b)]] if names[int(b)] in REF else bindm(mt, int(b))).ravel().tolist() for b in ub]
        tris = [(i, remap[k]) for i, k in enumerate(keep) if len(k)]
        name = "shoes_" + key
        out = dict(name=name, note=rec["name"], kind="feet", fit=2, side="LR", bundle=bundle_key(rec["path"]), mesh=mesh.m_Name,
                   bones=[names[int(b)] for b in ub], bind=bind, n=int(len(used)),
                   pos=b64(P[used], "<f4"), nrm=b64(N[used], "<f4"), tan=b64(Tg[used], "<f4"), uv=b64(UV[used], "<f4"),
                   bi=b64(bi2, "<i4"), bw=b64(bw2, "<f4"), subs=[dict(mat=matnames[i] if i < len(matnames) else "?", sub=int(i), tris=b64(t.ravel(), "<i4")) for i, t in tris])
        json.dump(out, open(f"{OUT}/hw_{name}.json", "w"))
        items[key] = dict(name=name, player=rec.get("player", False), outfits=[rec["name"]], src=rec["path"], mesh=mesh.m_Name, mats=[matnames[i] for i, _ in tris], tris=int(ntri), verts=int(len(used)),
                          bones=sorted(set(names[int(b)].replace("Base Human", "") for b in ub)), height=float(P[used][:, int(np.argmax(np.ptp(P, 0)))].max()))
        print(f"{name}: {rec['name'][:40]:40} {ntri:5} tris {len(used):5} verts, top {items[key]['height']:.3f}, bones {items[key]['bones']}", flush=True)
    except Exception as e:
        fails.append((rec["name"], repr(e)[:120])); print("FAIL", rec["name"], repr(e)[:100], flush=True)
# bot-only shoes (no player suit among the outfits that have them): "rare" (EVERY SHOE switch) and a readable name
import re as _re
def bot_label(n):
    w = [x for x in _re.split(r"[_\s]+", n) if x and x.lower() not in ("pants", "feet", "lower", "lod0")]
    kind = "Zombie" if "zombie" in n.lower() else "Boss" if "boss" in n.lower() else "Guard" if any(k in n.lower() for k in ("security", "follower", "guard")) else "Scav" if "wild" in n.lower() else "Bot"
    w = [x for x in w if x.lower() not in ("zombie", "boss")]
    return f"{kind}: {' '.join(x.capitalize() if x.islower() else x for x in w) or n}"
seen = {}
for v in items.values():
    f_ = f"{OUT}/hw_{v['name']}.json"; h = json.load(open(f_))
    h["rare"] = not v["player"]
    if h["rare"]:
        lb = bot_label(v["outfits"][0]); seen[lb] = seen.get(lb, 0) + 1
        h["note"] = lb if seen[lb] == 1 else f"{lb} ({seen[lb]})"           # texture variants of one bot outfit
    v["note"] = h["note"]; json.dump(h, open(f_, "w"))
print(sum(1 for v in items.values() if not v["player"]), "bot-only shoes:", sorted(v["note"] for v in items.values() if not v["player"])[:12])
json.dump(dict(items=items, fails=fails), open("WORK/wristacc/lab/feetscan/shoes.json", "w", encoding="utf-8"), indent=1)
print(f"{len(items)} shoes from {sum(len(v['outfits']) for v in items.values())} outfits, {len(fails)} skipped")
