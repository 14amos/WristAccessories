# export_arm.py <hands bundle> <out json> [side=L]: the hands mesh in <S>Forearm3 bone space (the plugin's frame) for the
# Unity bead lab: positions, normals, uv, triangles per submesh + each submesh's material name
import sys, json, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
bundle, out = sys.argv[1], sys.argv[2]; S = sys.argv[3] if len(sys.argv) > 3 else "L"
env = UnityPy.load(bundle); objs = {o.path_id: o for o in env.objects}; best = None
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    t = o.read_typetree()
    if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
    r = objs.get(t["_skinnedMeshRenderer"]["m_PathID"]); rt = r.read_typetree() if r else None
    mo = objs.get(rt["m_Mesh"]["m_PathID"]) if rt else None
    if mo is None: continue
    n = mo.read_typetree()["m_VertexData"]["m_VertexCount"]; nm = mo.peek_name().lower()
    score = ("watch" not in nm) * 1e7 + n
    if best is None or score > best[0]: best = (score, t["_bonePaths"], rt, mo)
_, paths, rt, mo = best
mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
names = [p.split("/")[-1] for p in paths]
bp = mt["m_BindPose"][names.index(f"Base Human{S}Forearm3")]; M3 = np.array([[bp[f"e{r}{c}"] for c in range(4)] for r in range(4)])
P = (M3 @ np.c_[np.array(h.m_Vertices), np.ones(len(h.m_Vertices))].T).T[:, :3]
N = np.array(h.m_Normals)[:, :3] @ M3[:3, :3].T
mats = [objs[m["m_PathID"]].peek_name() if m["m_PathID"] in objs else "?" for m in rt["m_Materials"]]
subs = [np.array(s).ravel().tolist() for s in h.get_triangles()]
json.dump(dict(mesh=mesh.m_Name, pos=P.round(6).tolist(), nrm=N.round(4).tolist(), uv=np.array(h.m_UV0).round(5).tolist(), subs=subs, mats=mats), open(out, "w"))
print(mesh.m_Name, len(P), "verts,", len(subs), "submeshes", mats)
