# dogtag_models.py: every dogtag item model in the game + installed mods, cut down to ONE plate + its ring (the item models
# carry two tags and a ball chain in one mesh, and the game's meshes can't be read at runtime), in a canonical tag frame:
# origin = the plate's hole end, +x = along the plate toward the hole, +z = plate normal (front), y = z x x. Used by the
# WARDROBE DOGTAGS page (DogTags.cs) to hang any dogtag on the kill sleeves / dogtag chains in place of a sleeve tag.
# Geometry only: textures/materials come from the item's own bundle at runtime, so a tag only shows when its bundle is
# installed. Models that share a mesh (ISB chess tags = the BD red mesh with their own material) reuse its geometry.
# Usage: <venv python> dogtag_models.py <out json> [<preview dir>]
import sys, os, json, glob, re, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
sys.path.insert(0, "WORK/tattoo/acc")
from acclib import pieces

SPT = "SPT/"
GAME = SPT + "EscapeFromTarkov_Data/StreamingAssets/Windows/"
MODS = SPT + "SPT_Runtime/user/mods/"
out_json = sys.argv[1]; prev = sys.argv[2] if len(sys.argv) > 2 else None

# every item prefab whose path names a dogtag (game database + every mod's json), as dogtags_scan found them
items = json.load(open(SPT + "SPT_Runtime/SPT_Data/database/templates/items.json", encoding="utf-8"))
keys = {}
for v in items.values():
    pr = (v.get("_props") or {}).get("Prefab") or {}
    p = pr.get("path", "") if isinstance(pr, dict) else ""
    if re.search("dogtag|dog_tag|moreman_tag", p, re.I): keys.setdefault(p, "game")
for f in glob.glob(MODS + "**/*.json*", recursive=True):
    g = f.replace(os.sep, "/")
    if "/logs/" in g or "locale" in g.lower(): continue
    try: t = open(f, encoding="utf-8-sig").read()
    except Exception: continue
    for m in re.finditer(r'"path"\s*:\s*"([^"]*(?:dogtag|dog_tag|moreman_tag)[^"]*\.bundle)"', t, re.I):
        keys.setdefault(m.group(1), g[len(MODS):].split("/")[0])
keys = {k: v for k, v in keys.items() if "container" not in k}          # the dogtag case is a container, not a tag


def find_file(key):
    if os.path.exists(GAME + key): return GAME + key, "game"
    for d in glob.glob(MODS + "*/bundles/"):
        if os.path.exists(d + key): return d + key, d[len(MODS):].split("/")[0]
    return None, None


dims = lambda Q: sorted(np.ptp((Q - Q.mean(0)) @ np.linalg.svd(Q - Q.mean(0))[2].T, axis=0), reverse=True)


def cut(mo):
    h = MeshHandler(mo.read()); h.process()
    P = np.array(h.m_Vertices); N = np.array(h.m_Normals)[:, :3]
    T = np.array(h.m_Tangents) if h.m_Tangents else np.zeros((len(P), 4)); UV = np.array(h.m_UV0)
    TR = np.vstack([np.array(s).reshape(-1, 3) for s in h.get_triangles()]); lab = pieces(P, [TR])
    D = {q: dims(P[lab == q]) for q in range(lab.max() + 1) if (lab == q).sum() >= 3}
    # the plate: the biggest flat piece of tag size (16..60 mm long, <= 4 mm thick)
    cand = [q for q, d in D.items() if 0.016 < d[0] < 0.06 and d[2] < 0.004 and d[1] > 0.008]
    if not cand: return None, "no plate-sized flat piece (" + ", ".join(f"{d[0]*1000:.0f}x{d[1]*1000:.0f}x{d[2]*1000:.1f}" for d in sorted(D.values(), reverse=True)[:5]) + ")"
    plate = max(cand, key=lambda q: D[q][0] * D[q][1])
    Q = P[lab == plate]; c = Q.mean(0); _, _, vt = np.linalg.svd(Q - c); pr = (Q - c) @ vt.T
    def inside(q):
        X = (P[lab == q] - c) @ vt.T
        return all((X[:, i] > pr[:, i].min() - 1e-3).all() and (X[:, i] < pr[:, i].max() + 1e-3).all() for i in (0, 1)) and (np.abs(X[:, 2]) < 0.004).all()
    # inlays / rubber rim / text pieces on the plate - not chain links lying on it (long thin rods, or beads/links of a
    # chain: a run of small same-size pieces)
    faces = [q for q in D if q != plate and inside(q) and not (D[q][0] > 0.008 and D[q][1] < 0.004)]
    # the ring: a small piece (3..9 mm) near one end of the plate; that end is the hole end
    ends = [c + vt[0] * pr[:, 0].max(), c + vt[0] * pr[:, 0].min()]
    rings = [q for q, d in D.items() if q != plate and q not in faces and 0.003 < d[0] < 0.010 and min(np.linalg.norm(P[lab == q].mean(0) - e) for e in ends) < 0.02]
    ring = min(rings, key=lambda q: min(np.linalg.norm(P[lab == q].mean(0) - e) for e in ends)) if rings else None
    if ring is not None: hole = min(ends, key=lambda e: np.linalg.norm(P[lab == ring].mean(0) - e))
    else:                                                                # no ring: the end with the hole (more vertices near it)
        n0 = (pr[:, 0] > pr[:, 0].max() - 0.008).sum(); n1 = (pr[:, 0] < pr[:, 0].min() + 0.008).sum(); hole = ends[0] if n0 >= n1 else ends[1]
    a1 = vt[0] if (hole - c) @ vt[0] > 0 else -vt[0]; a3 = vt[2]
    keep = np.isin(lab, [plate] + faces + ([ring] if ring is not None else []))
    TRk = TR[keep[TR].all(1)]; used = np.unique(TRk)
    # front = the side most of the plate's detail faces (more triangles facing it: text, inlays)
    fn = np.cross(P[TRk[:, 1]] - P[TRk[:, 0]], P[TRk[:, 2]] - P[TRk[:, 0]]); area = np.linalg.norm(fn, axis=1)
    if (np.sign(fn @ a3) * area).sum() < 0: a3 = -a3
    a2 = np.cross(a3, a1); F = np.array([a1, a2, a3]); E = c + a1 * ((Q - c) @ a1).max()
    rm = -np.ones(len(P), int); rm[used] = np.arange(len(used))
    Pc = (P[used] - E) @ F.T
    flip = np.linalg.det(F) < 0                                          # never: a1, a2 = a3 x a1, a3 is right-handed
    res = dict(pos=np.round(Pc, 6).tolist(), nrm=np.round(N[used] @ F.T, 5).tolist(),
               tan=np.round(np.c_[T[used, :3] @ F.T, T[used, 3]], 5).tolist(), uv=np.round(UV[used], 6).tolist(),
               tris=rm[TRk].ravel().tolist(), pivot=int(np.argmin(np.linalg.norm(Pc, axis=1))), tip=int(np.argmin(Pc[:, 0])),
               size_mm=[round(float(x) * 1000, 1) for x in np.ptp(Pc, 0)])
    return res, f"plate {D[plate][0]*1000:.1f}x{D[plate][1]*1000:.1f}x{D[plate][2]*1000:.1f} mm + {len(faces)} inlay(s) + {'ring' if ring is not None else 'NO ring'}"


models = []; geo_by_mesh = {}
for key in sorted(keys):
    f, src = find_file(key)
    if f is None: print(f"MISSING {key}"); continue
    env = UnityPy.load(f); objs = {o.path_id: o for o in env.objects}
    # the item's LOD0 mesh filter (not its shadow / collider); a mesh in another bundle (ISB chess tags) -> by its name
    mf = None
    for o in env.objects:
        if o.type.name != "MeshFilter": continue
        t = o.read_typetree(); go = objs.get(t["m_GameObject"]["m_PathID"]); gn = go.peek_name() if go else ""
        if "LOD0" in gn and "SHADOW" not in gn.upper(): mf = (gn, objs.get(t["m_Mesh"]["m_PathID"]), t["m_Mesh"])
    if mf is None: print(f"NO LOD0 {key}"); continue
    gn, mo, ref = mf
    mesh_name = mo.peek_name() if mo is not None else gn                 # external mesh: the GameObject is named after it
    gid = mesh_name if mo is None or mesh_name.lower() not in ("model_lod0",) else mesh_name + "@" + os.path.basename(key)   # vanilla tags all call theirs model_LOD0
    if mo is not None and gid in geo_by_mesh and gid != mesh_name: gid = gid       # (same file twice cannot happen)
    if gid in geo_by_mesh: geo, how = geo_by_mesh[gid], "shares " + gid
    elif mo is None: print(f"EXTERNAL mesh {mesh_name} not cut yet for {key} (order)"); continue
    else:
        geo, how = cut(mo)
        if geo is None: print(f"SKIP {key}: {how}"); continue
        geo_by_mesh[gid] = geo
    label = os.path.basename(key).replace(".bundle", "").replace("item_barter_dogtags_", "").replace("item_", "").replace("_", " ")
    models.append(dict(key=key, mesh=mesh_name, source=src, label=label, geo=gid))
    print(f"{src:22s} {key[-60:]:60s} {how}  {geo['size_mm']}")

# meshes are stored once (ISB tags share the BD red mesh), models point at them
json.dump({"meshes": geo_by_mesh, "models": models}, open(out_json, "w"))
print(f"{len(models)} dogtag models, {len(geo_by_mesh)} meshes -> {out_json}")
if prev:
    os.makedirs(prev, exist_ok=True)
    for n, g in geo_by_mesh.items():
        json.dump({"pos": g["pos"], "tris": np.array(g["tris"]).reshape(-1, 3).tolist()}, open(f"{prev}/{n.replace('@', '_').replace('.bundle', '')}.json", "w"))
