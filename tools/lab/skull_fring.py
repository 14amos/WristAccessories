# skull_fring.py <out dir>: the game's Gold skull ring as a finger ring for any finger (user 2026-10-27: "add the skull ring to the
# hands view"), from the item's own model (item_barter_valuable_skullring LOD0), in ring_items.py's format (kind "fring"):
#  - its hole axis: of the 3 principal axes, the one the band goes round (no vertex near the axis, inner surface facing it);
#  - inner radius / centre: least-squares circle of the inner surface (normals pointing at the axis);
#  - the skull's side: from the centre toward the vertices farthest from the axis -> worn on the back of the finger;
#  - per finger (reference hand, ring_items.frame at the middle of the segment): scaled evenly so the inner edge is 0.6 mm over it.
import os, sys, json, base64; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
import ring_items as RI
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
KEY = "assets/content/items/barter/item_barter_valuable_skullring/item_barter_valuable_skullring.bundle"
PATH = "SPT/EscapeFromTarkov_Data/StreamingAssets/Windows/" + KEY
env = UnityPy.load(PATH); mo = [o for o in env.objects if o.type.name == "Mesh" and o.peek_name().endswith("LOD0")][0]
mesh = mo.read(); h = MeshHandler(mesh); h.process()
P = np.array(h.m_Vertices, float); N = np.array(h.m_Normals, float)[:, :3]; UV = np.array(h.m_UV0, float)[:, :2]
T = np.array(h.get_triangles()[0]).reshape(-1, 3); mat = [o.peek_name() for o in env.objects if o.type.name == "Material"][0]
c0 = P.mean(0); ev, V = np.linalg.eigh((P - c0).T @ (P - c0)); best = None
for k in range(3):                                                               # the hole axis
    ax = V[:, k]; R = P - c0; rad = R - np.outer(R @ ax, ax); d = np.linalg.norm(rad, axis=1)
    inward = ((N * -rad).sum(1) / (np.linalg.norm(N, axis=1) * d + 1e-9)) > 0.5
    score = inward.mean() * (d.min() / np.median(d))
    if best is None or score > best[0]: best = (score, ax, inward)
_, ax, inward = best
e1 = V[:, 0] if abs(V[:, 0] @ ax) < 0.9 else V[:, 1]; e1 = e1 - ax * (e1 @ ax); e1 /= np.linalg.norm(e1); e2 = np.cross(ax, e1)
X = np.c_[(P[inward] - c0) @ e1, (P[inward] - c0) @ e2]                         # circle fit of the inner surface
A = np.c_[2 * X, np.ones(len(X))]; sol, *_ = np.linalg.lstsq(A, (X ** 2).sum(1), rcond=None)
cc = c0 + sol[0] * e1 + sol[1] * e2; cc = cc - ax * ((cc - c0) @ ax) + ax * (((P[inward] - c0) @ ax).mean()); r_in = float(np.median(np.linalg.norm((P[inward] - cc) - np.outer((P[inward] - cc) @ ax, ax), axis=1)))
R = P - cc; rv = R - np.outer(R @ ax, ax); far = np.linalg.norm(rv, axis=1); skull = rv[far > np.percentile(far, 90)].mean(0); back = skull - ax * (skull @ ax); back /= np.linalg.norm(back)
e2s = np.cross(ax, back); al = R @ ax; rx = R @ back; ry = R @ e2s; nal = N @ ax; nx = N @ back; ny = N @ e2s
print(f"skull ring: hole axis score {best[0]:.2f}, inner radius {r_in * 1000:.1f} mm, outer max {far.max() * 1000:.1f} mm, {len(P)} verts")
hid, REF = RI.ref_hand(); fingers = {}
for S2 in "LR":
    for k2 in range(1, 6):
        ft = RI.frame(REF, S2, k2, frac=0.5); e2t = np.cross(ft["ax"], ft["d"]); s = (ft["r"] + 0.0006) / r_in
        pos = ft["c"] + s * (np.outer(al, ft["ax"]) + np.outer(rx, ft["d"]) + np.outer(ry, e2t))
        nrm = np.outer(nal, ft["ax"]) + np.outer(nx, ft["d"]) + np.outer(ny, e2t)
        fingers[f"{S2}{k2}"] = dict(bone=ft["seg"], pos=b64(pos, "<f4"), nrm=b64(nrm, "<f4"), r=round(ft["r"] * 1000, 2), scale=round(s, 3))
out = dict(name="fring_skullring", note="Gold skull ring", kind="fring", side="", bundle=KEY, mesh=mesh.m_Name, n=int(len(P)), uv=b64(UV, "<f4"),
           subs=[dict(mat=mat, sub=0, tris=b64(T.ravel(), "<i4"))], fingers=fingers, src=dict(item=KEY, path=PATH, r_in_mm=round(r_in * 1000, 2)))
json.dump(out, open(f"{OUT}/hw_fring_skullring.json", "w"))
print("scale per finger:", {k: v["scale"] for k, v in fingers.items()})
