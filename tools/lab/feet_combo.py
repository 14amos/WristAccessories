# feet_combo.py: what the SHOES look like on other pants, offline (user 2026-10-27: gap / cuff scrap in game). The player's lower
# outfit (LOD0) skinned as the plugin leaves it - Foot / Toe bones swapped for a point at the Foot bone scaled SHRINK - plus the
# shoe piece (hw_shoes_*.json) in its own mesh space, both in the bind (rest) pose: every outfit's skeleton has the same rest
# (checked here: Foot bind poses compared, log "rest differs" when not). Rendered textured from the front-side, feet framed.
#   python feet_combo.py <out.jpg> <pants fid or mesh-name part>[,...] <shoe name>[,...]      (env SHRINK, default 0.7)
# Library use: load_pants(fid), load_shoe(name), skin_own(p, shrink), render(parts, S) -> RGB float image.
import os, sys, json, base64, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
from PIL import Image, ImageDraw
LAB = "WORK/wristacc/lab"; JOBS = json.load(open(LAB + "/feetscan/jobs.json", encoding="utf-8"))
SHOES = json.load(open(LAB + "/feetscan/shoes.json", encoding="utf-8"))["items"]; BYNAME = {v["name"]: v for v in SHOES.values()}
CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
TEX = {}
def tex_of(mat_obj):
    if mat_obj is None: return None
    k = (mat_obj.assets_file.name, mat_obj.path_id)
    if k in TEX: return TEX[k]
    img = None
    try:
        m = mat_obj.read(); te = dict((kk, v.m_Texture) for kk, v in m.m_SavedProperties.m_TexEnvs).get("_MainTex")
        if te is not None and te.m_PathID:
            to = deref(mat_obj, {"m_FileID": te.m_FileID, "m_PathID": te.m_PathID}, {o.path_id: o for o in mat_obj.assets_file.objects.values()})
            if to is not None: im = to.read().image.convert("RGB"); im.thumbnail((1024, 1024)); img = np.asarray(im).astype(np.float32) / 255
    except Exception as e: print("tex", e)
    TEX[k] = img; return img
def lod0(path):
    env = UnityPy.load(path); objs = {o.path_id: o for o in env.objects}; best = None
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        nm = mo.peek_name().lower(); sc = (0 if any(f"lod{k}" in nm for k in range(1, 5)) else 1, mo.read_typetree()["m_VertexData"]["m_VertexCount"])   # the biggest non-LOD1+ renderer (Virago: CastMesh pieces, no LOD names)
        if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt, objs)
    return best[1:]
def norm_w(bw):
    # some meshes decode with unnormalized weights (Killa Xmass: ~60000 per vertex) -> rows to sum 1
    sm = bw.sum(1, keepdims=True); return np.where(sm > 1.5, bw / np.maximum(sm, 1e-9), bw)
def bindm(mt, i): return np.array([[mt["m_BindPose"][i][f"e{r}{c}"] for c in range(4)] for r in range(4)])
def load_pants(fid):
    paths, mo, r, rt, objs = lod0(JOBS[fid]["path"]); mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
    names = [p.split("/")[-1] for p in paths]
    p = dict(name=JOBS[fid]["name"], mesh=mesh.m_Name, names=names, bind=[bindm(mt, i) for i in range(len(names))],
             P=np.array(h.m_Vertices, np.float64), N=np.array(h.m_Normals, np.float64)[:, :3], UV=np.array(h.m_UV0, np.float64)[:, :2],
             bi=np.array(h.m_BoneIndices), bw=norm_w(np.array(h.m_BoneWeights, np.float64)),
             subs=[np.array(s).reshape(-1, 3) for s in h.get_triangles()], tex=[tex_of(deref(r, m_, objs)) for m_ in rt["m_Materials"]])
    return p
def load_shoe(name):
    h = json.load(open(f"{LAB}/feetscan/hw/hw_{name}.json")); v = BYNAME[name]
    paths, mo, r, rt, objs = lod0(v["src"]); mats = {deref(r, m_, objs).peek_name(): deref(r, m_, objs) for m_ in rt["m_Materials"] if deref(r, m_, objs) is not None}
    bind = {b: np.array(h["bind"][i]).reshape(4, 4) for i, b in enumerate(h["bones"])}
    return dict(name=name, note=h["note"], P=arr(h["pos"], "<f4", 3).astype(float), N=arr(h["nrm"], "<f4", 3).astype(float), UV=arr(h["uv"], "<f4", 2).astype(float),
                subs=[arr(s["tris"], "<i4", 3) for s in h["subs"]], tex=[tex_of(mats.get(s["mat"])) for s in h["subs"]], bind=bind)
REF = {n: np.array(m).reshape(4, 4) for n, m in json.load(open(LAB + "/feetscan/ref_bind.json")).items()} if os.path.exists(LAB + "/feetscan/ref_bind.json") else {}
def to_ref(names, bind):
    """per bone: its own mesh space -> the reference rest space (feet_ref.py): inv(ref bind) * own bind (identity without a ref)"""
    return np.array([np.linalg.inv(REF[n]) @ bind[i] if n in REF else np.eye(4) for i, n in enumerate(names)])
def skin(P, bi, bw, M):
    P4 = np.c_[P, np.ones(len(P))]; out = np.zeros_like(P)
    for k in range(4): out += bw[:, k][:, None] * np.einsum("nij,nj->ni", M[bi[:, k]], P4)[:, :3]
    return out
def skin_own(p, shrink=0.7):
    """the pants as the plugin leaves them, in the reference rest space: Foot / Toe bones -> a point at the Foot bone, scaled"""
    M = to_ref(p["names"], p["bind"])
    for i, n in enumerate(p["names"]):
        side = "L" if "HumanL" in n else "R" if "HumanR" in n else None
        if side and ("Foot" in n or "Toe" in n):
            fn = f"Base Human{side}Foot"; fb = REF[fn] if fn in REF else p["bind"][p["names"].index(fn)]
            M[i] = np.linalg.inv(fb) @ np.diag([shrink, shrink, shrink, 1]) @ p["bind"][i]
    return skin(p["P"], p["bi"], p["bw"], M)
def to_ref_pos(p):
    return skin(p["P"], p["bi"], p["bw"], to_ref(p["names"], p["bind"]))
def skin_own_OLD(p, shrink=0.7):
    # the plugin's CutPart: every Foot / Toe bone of a side -> a point at that side's Foot bone, scale shrink. Rest pose:
    # bone world = inv(bind); the point's world = inv(bind_foot) * S; vertex = sum w * world * bind * v
    M = []
    for i, n in enumerate(p["names"]):
        side = "L" if "HumanL" in n else "R" if "HumanR" in n else None
        if side and ("Foot" in n or "Toe" in n):
            fi = p["names"].index(f"Base Human{side}Foot"); M.append(np.linalg.inv(p["bind"][fi]) @ np.diag([shrink, shrink, shrink, 1]) @ p["bind"][i])
        else: M.append(np.eye(4))
    M = np.array(M); P4 = np.c_[p["P"], np.ones(len(p["P"]))]; out = np.zeros_like(p["P"])
    for k in range(4):
        w = p["bw"][:, k]; Mk = M[p["bi"][:, k]]; out += w[:, None] * np.einsum("nij,nj->ni", Mk, P4)[:, :3]
    return out
def rest_diff(p, s):
    d = 0.0
    for b, B in s["bind"].items():
        if b in p["names"]: d = max(d, float(np.abs(np.linalg.inv(B)[:3, 3] - np.linalg.inv(p["bind"][p["names"].index(b)])[:3, 3]).max()))
    return d
def render(parts, S=300, view=(0.55, 1.0, 0.15), box=None, ids=False):
    view = np.array(view, float); view /= np.linalg.norm(view); U = np.array([0, 0, 1.0])
    up = U - view * (U @ view); up /= np.linalg.norm(up); right = np.cross(up, view)
    if box is None: box = (-0.32, 0.32, -0.02, 0.42)                          # feet + shins, mesh space x / z (m)
    out = np.full((S, S, 3), 0.12, np.float32); zb = np.full((S, S), -np.inf); idb = np.full((S, S), -1, np.int16); hb = np.zeros((S, S), np.float32)
    sc = S / (box[1] - box[0])
    for pi, (P, N, UV, subs, texs, tint) in enumerate(parts):
        x = P @ right; y = P @ up; z = P @ view; sx = (x - box[0]) * sc; sy = S - (y - box[2]) * sc
        for T, img in zip(subs, texs):
            for t in T:
                xs, ys = sx[t], sy[t]; x0, x1 = int(max(0, np.floor(xs.min()))), int(min(S - 1, np.ceil(xs.max()))); y0, y1 = int(max(0, np.floor(ys.min()))), int(min(S - 1, np.ceil(ys.max())))
                if x0 > x1 or y0 > y1: continue
                gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
                m = np.array([[xs[1] - xs[0], xs[2] - xs[0]], [ys[1] - ys[0], ys[2] - ys[0]]])
                if abs(np.linalg.det(m)) < 1e-12: continue
                inv = np.linalg.inv(m); dx, dy = gx - xs[0], gy - ys[0]
                l1 = inv[0, 0] * dx + inv[0, 1] * dy; l2 = inv[1, 0] * dx + inv[1, 1] * dy; l0 = 1 - l1 - l2
                ins = (l0 >= 0) & (l1 >= 0) & (l2 >= 0)
                if not ins.any(): continue
                zz = l0 * z[t[0]] + l1 * z[t[1]] + l2 * z[t[2]]; X, Y = gx.astype(int), gy.astype(int); sel = ins & (zz > zb[Y, X])
                if not sel.any(): continue
                b = np.stack([l0[sel], l1[sel], l2[sel]], 1); n = b @ N[t]; n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-9
                shade = 0.35 + 0.65 * np.abs(n @ view)
                if img is not None:
                    uv = (b @ UV[t]) % 1.0; col = img[((1 - uv[:, 1]) * (img.shape[0] - 1)).astype(int), (uv[:, 0] * (img.shape[1] - 1)).astype(int)]
                else: col = np.full((sel.sum(), 3), 0.7)
                if tint is not None: col = col * 0.6 + np.array(tint) * 0.4
                zb[Y[sel], X[sel]] = zz[sel]; out[Y[sel], X[sel]] = col * shade[:, None]
                if ids: idb[Y[sel], X[sel]] = pi; hb[Y[sel], X[sel]] = b @ P[t][:, 2]
    return (out, idb, hb) if ids else out
def find_pants(q):
    if q in JOBS: return q
    hits = [k for k, v in JOBS.items() if q.lower() in v["name"].lower() or q.lower() in v["path"].lower()]
    return hits[0] if hits else None
if __name__ == "__main__":
    outp, pq, sq = sys.argv[1:4]; shrink = float(os.environ.get("SHRINK", "0.7")); tint = os.environ.get("TINT") == "1"
    pants = [find_pants(q) for q in pq.split(",")]; shoes = sq.split(","); S = 300
    sheet = Image.new("RGB", (S * len(shoes) * 2, (S + 20) * len(pants)), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
    for i, fid in enumerate(pants):
        p = load_pants(fid); own = skin_own(p, shrink)
        for j, sn in enumerate(shoes):
            s = load_shoe(sn); rd = rest_diff(p, s)
            parts = [(own, p["N"], p["UV"], p["subs"], p["tex"], (0.2, 0.4, 1.0) if tint else None), (s["P"], s["N"], s["UV"], s["subs"], s["tex"], (1.0, 0.5, 0.1) if tint else None)]
            for k, vw in enumerate([(0.55, 1.0, 0.15), (-1.0, 0.25, 0.1)]):
                im = (render(parts, S, vw) * 255).astype(np.uint8); sheet.paste(Image.fromarray(im), ((j * 2 + k) * S, i * (S + 20)))
            dr.text((j * 2 * S + 3, i * (S + 20) + S + 3), f"{p['name'][:30]} + {s['note'][:30]}  rest diff {rd * 1000:.1f} mm", fill=(255, 220, 0))
    sheet.save(outp, quality=88); print("saved", outp)
