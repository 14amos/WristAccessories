# seam_data.py <dir>...: per BARE hand piece (kind "hand", cuff-trimmed = has "cuff_trim") the seam start "seam_d0": per 30 deg sector
# round the forearm axis (sector 0 = the back of the hand, counting toward the thumb side... see frame()) the piece's lowest point,
# m from its wrist plane (Palm origin, toward Digit31). The plugin (HandWear.Seam*) keeps the wearer's own hand full size up to it
# and tucks the piece's edge under the wearer's skin (user 2026-10-27). Same frame code as hand_onarm.py TUCK.
import sys, json, glob, base64, numpy as np
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
def frame(o, S):
    """anatomical frame from bind poses: pc palm origin, fd toward the middle finger, ax forearm axis, e1 back of the hand, e2 = ax x e1"""
    pc = o(f"Base Human{S}Palm"); fd = o(f"Base Human{S}Digit31") - pc; fd /= np.linalg.norm(fd); f3 = o(f"Base Human{S}Forearm3")
    ax = (pc - f3) / np.linalg.norm(pc - f3)
    d21, d51, tt = o(f"Base Human{S}Digit21"), o(f"Base Human{S}Digit51"), o(f"Base Human{S}Digit13")
    n = np.cross(d21 - pc, d51 - pc); n /= np.linalg.norm(n); back = -n if (tt - pc) @ n > 0 else n
    e1 = back - ax * (back @ ax); e1 /= np.linalg.norm(e1); e2 = np.cross(ax, e1)
    return pc, fd, f3, ax, e1, e2
def sectors(Q, f3, ax, e1, e2): return ((np.arctan2((Q - f3) @ e2, (Q - f3) @ e1) % (2 * np.pi)) / (2 * np.pi) * 12).astype(int) % 12
if __name__ == "__main__":
    n = 0
    for d in sys.argv[1:]:
        for f in sorted(glob.glob(d + "/hw_*.json")):
            h = json.load(open(f))
            if h.get("kind") != "hand" or "cuff_trim" not in h: continue
            S = h["side"]; B = {b: np.array(h["bind"][i]).reshape(4, 4) for i, b in enumerate(h["bones"])}
            o = lambda nm: np.linalg.inv(B[nm])[:3, 3]
            try: pc, fd, f3, ax, e1, e2 = frame(o, S)
            except KeyError as e: print(h["name"], "no bone", e); continue
            P = arr(h["pos"], "<f4", 3).astype(float); used = np.unique(np.concatenate([arr(s["tris"], "<i4", 3).ravel() for s in h["subs"]]))
            dP = (P[used] - pc) @ fd; sP = sectors(P[used], f3, ax, e1, e2)
            d0 = np.array([dP[sP == k].min() if (sP == k).any() else dP.min() for k in range(12)])
            d0 = np.minimum(d0, np.minimum(np.roll(d0, 1), np.roll(d0, -1)))
            h["seam_d0"] = [round(float(x), 5) for x in d0]; json.dump(h, open(f, "w")); n += 1
            print(f"{h['name']}: seam {d0.min()*1000:.0f}..{d0.max()*1000:.0f} mm")
    print("written", n)
