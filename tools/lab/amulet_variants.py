# amulet_variants.py: preview versions of acc_amulet.json with the fangs (long rigid pieces that stick out from the string)
# turned to lie flat: "hand" = tip toward the hand (+h), "band" = along the string (+dt). Each fang pivots on its base (the
# vertex closest to the arm) and is then lifted so nothing goes below the sleeve. Writes acc_amulet_hand.json / _band.json.
import json, numpy as np
A = "WORK/wristacc/acc/"
src = json.load(open(A + "acc_amulet.json"))
def rot_between(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b); v = np.cross(a, b); c = a @ b
    if np.linalg.norm(v) < 1e-8: return np.eye(3) if c > 0 else -np.eye(3)
    K = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]]); return np.eye(3) + K + K @ K / (1 + c)
for mode, target in (("hand", np.array([0, 0, 1.0])), ("band", np.array([1.0, 0, 0]))):   # +h = toward the hand (Place: a = aC - hmid + h, ax toward the hand)
    j = json.loads(json.dumps(src)); n_f = 0
    for m in j["meshes"]:
        X = np.c_[m["dt"], m["r"], m["h"]]; pid = np.array(m["pid"]); kind = np.array(m["kind"])
        Nn = np.array(m["n"]); Tt = np.array(m["t"])
        for p in np.unique(pid):
            sel = (pid == p) & (kind == 1)
            if sel.sum() < 6: continue
            Q = X[sel]; ext = np.ptp(Q, axis=0)
            if ext[1] < 0.010 or ext[1] < 0.6 * ext.max(): continue                    # a fang: long, pointing away from the arm
            base = Q[np.argmin(Q[:, 1])]; c = Q.mean(0); axis = c - base
            R = rot_between(axis, target)
            Q2 = (Q - base) @ R.T + base; Q2[:, 1] += max(0.0, -(Q2[:, 1].min()))   # lie on the sleeve, not in it
            X[sel] = Q2; Nn[sel] = Nn[sel] @ R.T; Tt[sel, :3] = Tt[sel, :3] @ R.T; n_f += 1
        m["dt"], m["r"], m["h"] = X[:, 0].round(5).tolist(), X[:, 1].round(5).tolist(), X[:, 2].round(5).tolist()
        m["n"] = Nn.round(4).tolist(); m["t"] = Tt.round(4).tolist()
    hs = [h for m in j["meshes"] for h in m["h"]]; j["width"] = float(max(hs) - min(hs)); j["hmid"] = float((max(hs) + min(hs)) / 2)
    j["name"] = "amulet_" + mode; j["note"] = "Sacred Amulet (fangs " + ("toward the hand" if mode == "hand" else "along the band") + ")"
    json.dump(j, open(A + f"acc_amulet_{mode}.json", "w")); print(mode, "fangs turned:", n_f)
