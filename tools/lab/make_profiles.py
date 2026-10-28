# make_profiles.py <out json>: SHOES per-pants data for the plugin (HandWear.HideOwnBoots / FitShoe), keyed by "<mesh name>|<vertex count>" (one name, two geometries: women's variants):
#   "legs": the fit profile of the outfit's main (LOD0) mesh (feet_fit.profile, common rest space): per leg "n" (x < 0) / "p":
#           ax, zc, rb, rp (base64 float32 NZ x NA, NaN = no bin)
#   "boot": for EVERY skinned mesh of the outfit (all LODs): the boot island triangles (feet_fit.boot_tris) as global triangle
#           indices (submeshes in order), their highest rest z and leg -> removed when shoes are worn (all of a leg's, or those
#           below the cut height when the new shoe is too short for the cuff); "subs" = triangle count per submesh (sanity check)
# Outfits split over several meshes (Virago) are left out: the plugin falls back to the foot shrink there.
import os, sys, json, base64, glob, pickle; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, multiprocessing as mp, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
import feet_combo as F, feet_fit as G, feet_check as C
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
def meshes(fid):
    env = UnityPy.load(F.JOBS[fid]["path"]); objs = {o.path_id: o for o in env.objects}; out = []
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = F.deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = F.deref(r, rt["m_Mesh"], objs) if r else None
        if mo is not None: out.append((t["_bonePaths"], mo))
    return out
def one(fid):
    try:
        ms = meshes(fid)
        main = [m for m in ms if not any(f"lod{k}" in m[1].peek_name().lower() for k in range(1, 5))]
        if len(main) != 1: return fid, None, f"{len(main)} main meshes - left out"
        d = C.prep(fid); res = {}
        for paths, mo in ms:
            mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process(); names = [p.split("/")[-1] for p in paths]
            p = dict(names=names, bind=[F.bindm(mt, i) for i in range(len(names))], P=np.array(h.m_Vertices, np.float64),
                     bi=np.array(h.m_BoneIndices), bw=F.norm_w(np.array(h.m_BoneWeights, np.float64)))
            Pr = F.to_ref_pos(p); fw = G.foot_w(names, p["bi"], p["bw"]); subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
            B = np.concatenate(G.boot_tris(subs, fw, len(Pr), Pr)); T = np.concatenate(subs); idx = np.where(B)[0]
            e = dict(subs=[len(s) for s in subs], verts=len(Pr), tris=b64(idx, "<i4"), zmax=b64(Pr[T[idx]][:, :, 2].max(1), "<f4"),
                     neg=b64((Pr[T[idx]][:, :, 0].mean(1) < 0).astype(np.uint8), "u1"), dir=b64(G.tri_dir(Pr, T[idx])[1].astype(np.uint8), "u1"))
            if mesh.m_Name == d["mesh"]:
                e["legs"] = {side: dict(ax=[float(x) for x in L["ax"]], zc=float(L["zc"]), rb=b64(np.asarray(L["rb"]).ravel(), "<f4"), rp=b64(np.asarray(L["rp"]).ravel(), "<f4"), hem=b64(np.asarray(L["hem"]).ravel(), "<f4"))
                             for side, L in d["prof"].items()}
            res[f"{mesh.m_Name}|{len(Pr)}"] = e
        return fid, res, f"{len(res)} meshes"
    except Exception as ex: return fid, None, "ERR " + repr(ex)[:100]
if __name__ == "__main__":
    skip = [x for x in os.environ.get("EXCLUDE", "").split(",") if x]           # e.g. /WTT-Artem/ (user 2026-10-27: Artem removed)
    jobs = [k for k in F.JOBS if not any(x in F.JOBS[k]["path"] for x in skip)]; print(len(F.JOBS) - len(jobs), "outfits excluded", flush=True)
    with mp.Pool(8) as pool: rs = pool.map(one, jobs, chunksize=2)
    out = {}; notes = []
    for fid, res, note in rs:
        if res is None: notes.append(f"{F.JOBS[fid]['name']}: {note}"); continue
        for k, v in res.items():
            out.setdefault(k, v)
    json.dump(out, open(sys.argv[1], "w"))
    print(len(out), "meshes,", sum(1 for v in out.values() if "legs" in v), "with fit profiles,", os.path.getsize(sys.argv[1]) // 1024, "KB"); [print("  ", n) for n in notes[:20]]
