# fp_export.py [ONLY=<hid,...>]: every first-person hands outfit's forearms for the lab FpSurvey (Assets/BeadLab/Runtime/FpSurvey.cs).
# Per distinct geometry (389 outfits share ~150 meshes: texture variants) and side: the hands mesh in <S>Forearm3 bone space
# (the plugin's frame) cropped to the forearm + hand (-25..+8 cm along the arm), normals, uv, triangles per submesh, the
# submeshes' material names, and the weights of the bones the outline builder poses (build_outlines.py: Palm with its
# fingers about the wrist joint, Forearm2 / Forearm1 twist about the Forearm3 origin) -> lab/fpdata/<geo>.json + index.json
import os, sys, json, base64, hashlib, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
OUT = "WORK/wristacc/lab/fpdata"; os.makedirs(OUT, exist_ok=True)
outl = json.load(open("WORK/wristacc/outlines.json")); scan = json.load(open("WORK/tattoo/acc/all_scan_hands.json"))
CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
only = set(filter(None, os.environ.get("ONLY", "").split(",")))
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
index = {}; geos = {}; fails = []
for hid, rec in outl.items():
    if only and hid not in only: continue
    path = scan[hid]["path"]
    try:
        env = UnityPy.load(path); objs = {o.path_id: o for o in env.objects}; cands = []
        def deref(owner, pp):
            if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
            f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
            if f is None: return None
            if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
            return DEP[f].get(pp["m_PathID"])
        for o in env.objects:                                   # the same skin pick as build_outlines.py
            if o.type.name != "MonoBehaviour": continue
            t = o.read_typetree()
            if "_bonePaths" not in t: continue
            r = deref(o, t["_skinnedMeshRenderer"]) if t.get("_skinnedMeshRenderer") else None
            mo = deref(r, r.read_typetree()["m_Mesh"]) if r else None
            if mo is None: continue
            nm = mo.peek_name().lower()
            score = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm) * 1 + min(mo.read_typetree()["m_VertexData"]["m_VertexCount"], 99999) / 1e6
            mats = [(deref(r, m_).peek_name() if deref(r, m_) is not None else "?") for m_ in r.read_typetree()["m_Materials"]]
            cands.append((score, t["_bonePaths"], mo, mats))
        cands.sort(key=lambda c: -c[0]); paths, mo, mats = cands[0][1], cands[0][2], cands[0][3]
        mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
        P = np.array(h.m_Vertices, np.float64)
        geo = hashlib.md5(P.astype(np.float32).tobytes()).hexdigest()[:12]
        index[hid] = dict(geo=geo, mesh=mesh.m_Name, path=path, name=scan[hid]["name"])
        if geo in geos: continue
        names = [p.split("/")[-1] for p in paths]
        bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float64)
        N = np.array(h.m_Normals, np.float64)[:, :3]; UV = np.array(h.m_UV0, np.float64)[:, :2]
        subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
        g = dict(mesh=mesh.m_Name, mats=mats[:len(subs)])
        for S in "LR":
            idx = lambda n: names.index(f"Base Human{S}{n}")
            bind = lambda n: np.array([[mt["m_BindPose"][idx(n)][f"e{r}{c}"] for c in range(4)] for r in range(4)])
            M3 = bind("Forearm3"); Pb = (M3 @ np.c_[P, np.ones(len(P))].T).T[:, :3]; Nb = N @ M3[:3, :3].T
            f3o = (M3 @ np.linalg.inv(bind("Forearm3")))[:3, 3]
            o = rec[S]; w = np.array(o["w"]); ax = np.array(o["ax"])
            # Palm with everything under it (fingers) follows the wrist; the twist bones as the builder poses them
            palmset = [i for i, p in enumerate(paths) if p.split("/")[-1] == f"Base Human{S}Palm" or f"Base Human{S}Palm/" in p]
            wsum = lambda ids: np.where(np.isin(bi, ids), bw, 0).sum(1)
            wp, w2, w1 = wsum(palmset), wsum([idx("Forearm2")]), wsum([idx("Forearm1")])
            rel = Pb - w; al = rel @ ax; rad = np.linalg.norm(rel - np.outer(al, ax), axis=1)
            keep = (al > -0.25) & (al < 0.08) & (rad < 0.15)
            remap = -np.ones(len(P), int); remap[keep] = np.arange(keep.sum())
            tris = [remap[s[keep[s].all(1)]] for s in subs]
            g[S] = dict(n=int(keep.sum()), f3o=f3o.tolist(), pos=b64(Pb[keep], "<f4"), nrm=b64(Nb[keep], "<f4"), uv=b64(UV[keep], "<f4"),
                        wp=b64(wp[keep], "<f4"), w2=b64(w2[keep], "<f4"), w1=b64(w1[keep], "<f4"), tris=[b64(t.ravel(), "<i4") for t in tris])
        json.dump(g, open(f"{OUT}/{geo}.json", "w")); geos[geo] = hid
        print(f"{hid} {geo} {mesh.m_Name[:40]:40} L {g['L']['n']} R {g['R']['n']} verts", flush=True)
    except Exception as e:
        fails.append((hid, os.path.basename(path), repr(e)[:120])); print("FAIL", hid, repr(e)[:120], flush=True)
if not only: json.dump(index, open(f"{OUT}/index.json", "w"), indent=0)
print("outfits", len(index), "geometries", len(geos), "fails", len(fails))
