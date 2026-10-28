# ring_items.py <out dir> <idx,...>: finger RINGS for any finger (user 2026-10-27: rings #2, #3, #6 of ringscan/rings_on_hand_*.jpg,
# "make them applicable to any hand"). Per picked candidate (ring_export.py's hw_ring_*.json, sorted numbering) + the islands of the
# same outfit and finger segment within 3 mm of it (a ring's inner / outer surfaces can be two islands):
#  - source frame on its own hand: the finger segment's axis (bone -> child bone), the back of the finger (back of the hand, as
#    build_outlines.py: cross of finger and thumb directions), and at the ring's place along the segment the finger's centre and
#    radius (that segment's own skin verts within +-3 mm, the ring left out);
#  - the same frame for every finger on a REFERENCE hand (BEAR Senezh's bare hands): thumb on its 2nd segment, fingers on the 1st,
#    at the ring's relative place along the segment;
#  - the ring re-seated per finger: same offset along the finger, same angle round it, radial distance + (r_target - r_source).
# Out: hw_fring_<key>.json: name, note, kind "fring", bundle / mesh (material lookup), uv, subs (tris), and "fingers":
# {"L1".."R5": {bone, pos, nrm}} with pos / nrm in that finger bone's local space (bind pose of the reference hand).
import os, sys, json, base64, glob, collections; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
import ring_scan as RS
b64 = lambda a, t: base64.b64encode(np.ascontiguousarray(a, dtype=t).tobytes()).decode()
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
FS = sorted(glob.glob(RS.OUT + "/hw/hw_ring_*.json"))
def hand(hid):
    rec = RS.SCAN[hid]; env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = RS.deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = RS.deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
        if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo)
    _, paths, mo = best; mesh = mo.read(); mt = mo.read_typetree(); h = MeshHandler(mesh); h.process()
    names = [p.split("/")[-1] for p in paths]; P = np.array(h.m_Vertices, float); N = np.array(h.m_Normals, float)[:, :3]
    bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, float); sm = bw.sum(1, keepdims=True); bw = np.where(sm > 1.5, bw / np.maximum(sm, 1e-9), bw)
    bind = {n: np.array([[mt["m_BindPose"][i][f"e{a}{b}"] for b in range(4)] for a in range(4)]) for i, n in enumerate(names)}
    subs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]
    return dict(names=names, P=P, N=N, top=bi[np.arange(len(bi)), bw.argmax(1)], bind=bind, subs=subs, T=np.concatenate(subs), mesh=mesh.m_Name)
def seg_of(S, k): return f"Base Human{S}Digit{k}{2 if k == 1 else 1}"
def frame(H, S, k, frac=None, at=None, exclude=None):
    """finger k of side S on hand H: in the segment bone's local space: origin of the frame (finger centre at the spot), axis,
    back-of-finger, radius; frac = place along the segment (0 bone origin .. 1 child), or at = a local point to take the place from"""
    seg = seg_of(S, k); child = seg[:-1] + str(int(seg[-1]) + 1); B = H["bind"][seg]
    loc = lambda p: (B @ np.c_[p, np.ones(len(p))].T).T[:, :3]
    o_child = (B @ np.linalg.inv(H["bind"][child]))[:3, 3]; L = np.linalg.norm(o_child); ax = o_child / L
    palm = H["bind"][f"Base Human{S}Palm"]; inv = np.linalg.inv
    pc = lambda n: inv(H["bind"][n])[:3, 3]                                                        # bone origins, mesh space
    fd = pc(f"Base Human{S}Digit31") - pc(f"Base Human{S}Palm"); td = pc(f"Base Human{S}Digit11") - pc(f"Base Human{S}Palm")
    fd /= np.linalg.norm(fd); td -= fd * (td @ fd); td /= np.linalg.norm(td)
    back = np.cross(fd, td) if S == "L" else np.cross(td, fd)                                     # mirrored hand: mirrored cross (checked: thumb tip on the palm side, both hands)
    d = B[:3, :3] @ back; d -= ax * (d @ ax); d /= np.linalg.norm(d)
    if at is not None: frac = float(at @ ax) / L
    a0 = frac * L
    idx = H["seg_v"].get(seg) if "seg_v" in H else None
    if idx is None:
        si = H["names"].index(seg); H.setdefault("seg_v", {})[seg] = idx = np.where(H["top"] == si)[0]
    if exclude is not None: idx = np.setdiff1d(idx, exclude)
    Q = loc(H["P"][idx]); al = Q @ ax; sel = np.abs(al - a0) < 0.003
    if sel.sum() < 6: sel = np.abs(al - a0) < 0.006
    X = Q[sel]; c = X.mean(0); c = c - ax * ((c @ ax) - a0)
    rad = np.linalg.norm((X - c) - np.outer((X - c) @ ax, ax), axis=1)
    return dict(seg=seg, c=c, ax=ax, d=d, r=float(np.median(rad)), frac=frac, L=L)
def ref_hand():
    hid = [k for k, v in RS.SCAN.items() if v['name'].startswith('BEAR Senezh')][0]; return hid, hand(hid)
if __name__ == "__main__":
    OUT = sys.argv[1]; IDX = [int(x) for x in sys.argv[2].split(",")]; os.makedirs(OUT, exist_ok=True)
    REFHID = [k for k, v in RS.SCAN.items() if v["name"].startswith("BEAR Senezh")][0]; REF = hand(REFHID)
    print("reference hand:", RS.SCAN[REFHID]["name"], REF["mesh"])
    done = set()
    for i in IDX:
        h0 = json.load(open(FS[i])); src = h0["src"]; H = hand(src["hid"]); S = h0["side"]; k = int(h0["finger"][-1]); seg = seg_of(S, k)
        # merge: same outfit + finger segment islands within 3 mm (inner / outer surfaces)
        group = []
        for f in FS:
            h = json.load(open(f))
            if h["src"]["hid"] == src["hid"] and h["finger"] == h0["finger"] and h["src"]["seg"] == src["seg"]:
                if np.linalg.norm(arr(h["pos"], "<f4", 3).mean(0) - arr(h0["pos"], "<f4", 3).mean(0)) < 0.006: group.append(h)
        if any(g["name"] in done for g in group): continue
        done |= {g["name"] for g in group}
        if src["seg"].endswith(seg[-6:]) is False: print("  note: scan segment", src["seg"], "vs", seg)
        seg = "Base Human" + src["seg"]; Bs = H["bind"][seg]
        P = np.vstack([arr(g["pos"], "<f4", 3) for g in group]).astype(float); Nn = np.vstack([arr(g["nrm"], "<f4", 3) for g in group]).astype(float)
        UV = np.vstack([arr(g["uv"], "<f4", 2) for g in group]); off = np.cumsum([0] + [g["n"] for g in group])
        subs = collections.defaultdict(list)
        for gi, g in enumerate(group):
            for sb in g["subs"]: subs[(sb["mat"], sb["sub"])].append(arr(sb["tris"], "<i4", 3) + off[gi])
        Pl = (Bs @ np.c_[P, np.ones(len(P))].T).T[:, :3]; Nl = (Bs[:3, :3] @ Nn.T).T
        # the ring's own vertices in the source hand (to leave them out of the finger measurement): nearest source verts
        ring_v = np.unique(np.argmin(np.linalg.norm(H["P"][None, :, :] - P[:, None, :], axis=2), axis=1))
        k_src = int(src["seg"][-2]); fs = frame(H, S, k_src, at=Pl.mean(0), exclude=ring_v)
        # ring in the source frame: along, angle, radius
        # the ring's OWN frame: its axis = the direction it is flattest in (PCA), turned to point like the finger; centre = its middle
        # (the QILO thumb ring sits askew to its bone: the bone axis turned it edge-on); back = the finger's back, made square to it
        rc = Pl.mean(0); ev, V = np.linalg.eigh((Pl - rc).T @ (Pl - rc)); rax = V[:, 0] * np.sign(V[:, 0] @ fs["ax"])
        rd = fs["d"] - rax * (fs["d"] @ rax); rd /= np.linalg.norm(rd); fs = dict(fs, c=rc, ax=rax, d=rd)
        e2s = np.cross(fs["ax"], fs["d"]); R = Pl - fs["c"]; al = R @ fs["ax"]; rx = R @ fs["d"]; ry = R @ e2s
        ra = np.hypot(rx, ry); th = np.arctan2(ry, rx)
        nal = Nl @ fs["ax"]; nx = Nl @ fs["d"]; ny = Nl @ e2s
        fingers = {}
        for S2 in "LR":
            for k2 in range(1, 6):
                ft = frame(REF, S2, k2, frac=fs["frac"]); e2t = np.cross(ft["ax"], ft["d"])
                r2 = ra - ra.min() + ft["r"] + 0.0006                          # the ring's inner edge 0.6 mm over that finger's surface
                pos = ft["c"] + np.outer(al, ft["ax"]) + np.outer(r2 * np.cos(th), ft["d"]) + np.outer(r2 * np.sin(th), e2t)
                nrm = np.outer(nal, ft["ax"]) + np.outer(nx, ft["d"]) + np.outer(ny, e2t)
                fingers[f"{S2}{k2}"] = dict(bone=ft["seg"], pos=b64(pos, "<f4"), nrm=b64(nrm, "<f4"), r=round(ft["r"] * 1000, 2))
        outfit = src["outfits"][0].split(" (")[0] if "(" in src["outfits"][0] else src["outfits"][0]
        key = h0["name"].split("_")[1]; name = f"fring_{key}"
        out = dict(name=name, note=f"{outfit} ring", kind="fring", side="", bundle=h0["bundle"], mesh=h0["mesh"], n=int(len(P)),
                   uv=b64(UV, "<f4"), subs=[dict(mat=m, sub=s, tris=b64(np.vstack(t).ravel(), "<i4")) for (m, s), t in subs.items()],
                   fingers=fingers, src=dict(outfit=src["outfits"][0], seg=src["seg"], frac=round(fs["frac"], 3), r_mm=round(fs["r"] * 1000, 2),
                                             islands=[g["name"] for g in group], hid=src["hid"]))
        json.dump(out, open(f"{OUT}/hw_{name}.json", "w"))
        print(f"{name}: {out['note']}, {len(group)} island(s), {len(P)} verts, on {src['seg']} at {fs['frac']:.2f} of the segment, finger r {fs['r'] * 1000:.1f} mm; "
              f"per finger r {', '.join(f'{k_}:{v_['r']}' for k_, v_ in fingers.items())}")
