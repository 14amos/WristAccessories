# normalize_binds.py: handwear items made in another mesh space (scaled 1/40..1/100, rotated or moved: 12 Artem gloves, ISB
# Roze's Virago, BEAR Black Lynx...) re-expressed in the game's own: per bone the game gloves' bind pose (refBind), every
# vertex v' = sum_i w_i * inverse(refBind_i) * srcBind_i * v (the same skinned result - lab BindTest), normals / tangents by
# the rotation part, bind := refBind. Their raw mesh is then hand-sized like the game's (in game one was drawn as a huge
# cone in first person, user 2026-10-27). Rewrites source + installed copies; backups in BACKUPS/wristacc-binds-<date>.
import json, glob, os, base64, shutil, numpy as np
BK = "BACKUPS/wristacc-binds-2026-10-27"; os.makedirs(BK, exist_ok=True)
files = glob.glob("WORK/wristacc/acc/hw_*.json") + glob.glob("WORK/wristacc/addons/*/acc/hw_*.json")
ref = {}
for f in sorted(files):
    d = json.load(open(f))
    if "WTT" in d["note"] or "ISB" in d["note"]: continue
    for n, b in zip(d["bones"], d["bind"]): ref.setdefault(n, []).append(np.array(b).reshape(4, 4))
ref = {n: np.median(np.stack(v), 0) for n, v in ref.items()}
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k).copy()
enc = lambda a: base64.b64encode(np.ascontiguousarray(a).tobytes()).decode()
fixed = []
for f in sorted(files):
    d = json.load(open(f)); B = [np.array(b).reshape(4, 4) for b in d["bind"]]
    if any(n not in ref for n in d["bones"]): print("skip (bone not in the game set)", os.path.basename(f)); continue
    off = max(np.abs(B[i] - ref[n]).max() for i, n in enumerate(d["bones"]))
    if off < 0.02: continue                                         # already the game's space
    pos = arr(d["pos"], np.float32, 3); nrm = arr(d["nrm"], np.float32, 3); tan = arr(d["tan"], np.float32, 4)
    bi = arr(d["bi"], np.int32, 4); bw = arr(d["bw"], np.float32, 4)
    P = np.c_[pos, np.ones(len(pos))]; np_ = np.zeros((len(pos), 3)); nn = np.zeros((len(pos), 3)); tt = np.zeros((len(pos), 3))
    for k in range(4):
        for b in np.unique(bi[:, k]):
            m = (bi[:, k] == b) & (bw[:, k] > 0)
            if not m.any(): continue
            M = np.linalg.inv(ref[d["bones"][b]]) @ B[b]; R = M[:3, :3]
            np_[m] += bw[m, k, None] * (M @ P[m].T).T[:, :3]
            nn[m] += bw[m, k, None] * (R @ nrm[m].T).T; tt[m] += bw[m, k, None] * (R @ tan[m, :3].T).T
    nn /= np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-9); tt /= np.maximum(np.linalg.norm(tt, axis=1, keepdims=True), 1e-9)
    d["pos"] = enc(np_.astype(np.float32)); d["nrm"] = enc(nn.astype(np.float32)); d["tan"] = enc(np.c_[tt, tan[:, 3]].astype(np.float32))
    d["bind"] = [ref[n].reshape(-1).tolist() for n in d["bones"]]
    for p in [f] + glob.glob("SPT/BepInEx/plugins/WristAccessories*/acc/" + os.path.basename(f)):
        shutil.copy(p, os.path.join(BK, os.path.basename(os.path.dirname(os.path.dirname(p))) + "__" + os.path.basename(p)))
        json.dump(d, open(p, "w"))
    fixed.append(os.path.basename(f)); print(f"{os.path.basename(f)}: off {off:.2f} -> game space, raw size now {np.ptp(np_, 0).max() * 1000:.0f} mm")
print(len(fixed), "items re-expressed")
