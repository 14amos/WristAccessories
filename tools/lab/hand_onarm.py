# hand_onarm.py <out.jpg> <wearer hands id> <item json>[,...]: a hand piece on the WEARER's first-person hands, as the plugin draws it:
# the wearer's hand tucked in (HandWear.CutHand: that side's Palm / Digit bones -> one point at the palm, scale 0.8), the piece bound
# to the wearer's bones by name (its own bind poses: v' = sum w * inv(wearer bind) * piece bind * v). Rest pose, textured, two views
# of the wrist (back of the hand, thumb side). Checks the seam where a trimmed bare hand meets the wearer's own wrist.
import os, sys, json, base64; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
from PIL import Image, ImageDraw
import feet_combo as F, hand_icons_lib as HI
OUTF, WID, ITEMS = sys.argv[1], sys.argv[2], sys.argv[3].split(",")
SCAN = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
env = UnityPy.load(SCAN[WID]["path"]); objs = {o.path_id: o for o in env.objects}; best = None
for o in env.objects:
    if o.type.name != "MonoBehaviour": continue
    t = o.read_typetree()
    if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
    r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
    mo = deref(r, rt["m_Mesh"], objs) if r else None
    if mo is None: continue
    nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
    if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt)
_, paths, mo, r, rt = best; mt = mo.read_typetree(); h = MeshHandler(mo.read()); h.process(); names = [p.split("/")[-1] for p in paths]
WB = {n: np.array([[mt["m_BindPose"][i][f"e{a}{b}"] for b in range(4)] for a in range(4)]) for i, n in enumerate(names)}
WP = np.array(h.m_Vertices, float); WN = np.array(h.m_Normals, float)[:, :3]; WUV = np.array(h.m_UV0, float)[:, :2]
Wbi = np.array(h.m_BoneIndices); Wbw = np.array(h.m_BoneWeights, float)
Wsubs = [np.array(s).reshape(-1, 3) for s in h.get_triangles()]; Wtex = [F.tex_of(deref(r, m_, objs)) for m_ in rt["m_Materials"]]
def wearer(S):
    M = []
    for n in names:
        if os.environ.get("NOSHRINK") != "1" and f"Human{S}" in n and ("Palm" in n or "Digit" in n): M.append(np.linalg.inv(WB[f"Base Human{S}Palm"]) @ np.diag([0.8, 0.8, 0.8, 1]) @ WB[n])
        else: M.append(np.eye(4))
    return F.skin(WP, Wbi, Wbw, np.array(M))
S_ = int(os.environ.get("SZ", "330")); sheet = Image.new("RGB", (2 * S_ * len(ITEMS), S_ + 22), (25, 25, 25)); dr = ImageDraw.Draw(sheet)
for ii, f in enumerate(ITEMS):
    it = json.load(open(f)); S = it["side"]; Wpos = wearer(S)
    P = arr(it["pos"], "<f4", 3).astype(float); N = arr(it["nrm"], "<f4", 3).astype(float); UV = arr(it["uv"], "<f4", 2).astype(float)
    bi = arr(it["bi"], "<i4", 4); bw = arr(it["bw"], "<f4", 4).astype(float)
    M = np.array([np.linalg.inv(WB[b]) @ np.array(it["bind"][k]).reshape(4, 4) if b in WB else np.eye(4) for k, b in enumerate(it["bones"])])
    PP = F.skin(P, bi, bw, M); T = [arr(s["tris"], "<i4", 3)[:0 if os.environ.get("NOPIECE") == "1" else None] for s in it["subs"]]; tx = [HI.tex(it["bundle"], s["mat"]) for s in it["subs"]]
    def rgb2hsv(a):
        mx = a.max(1); mn = a.min(1); df = mx - mn + 1e-9; r, g, b = a.T
        h = np.where(mx == r, ((g - b) / df) % 6, np.where(mx == g, (b - r) / df + 2, (r - g) / df + 4)) / 6
        return np.c_[np.where(mx - mn < 1e-9, 0, h), np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-9), 0), mx]
    if os.environ.get("TONE") == "1" and it.get("kind") == "hand":       # as HandWear.Toned: skin texels x (wearer forearm tone / piece skin mean)
        import colorsys
        def rgb2hsv(a):
            mx = a.max(1); mn = a.min(1); df = mx - mn + 1e-9; r, g, b = a.T
            h = np.where(mx == r, ((g - b) / df) % 6, np.where(mx == g, (b - r) / df + 2, (r - g) / df + 4)) / 6
            return np.c_[np.where(mx - mn < 1e-9, 0, h), np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-9), 0), mx]
        TT = json.load(open("WORK/wristacc/acc/skintones.json")); own = TT.get(mo.peek_name(), {}).get("rgb")
        def isskin(p):
            hh, ss, vv = colorsys.rgb_to_hsv(*p[:3]); hh *= 360
            return (hh < 50 or hh > 340) and 0.12 < ss < 0.7 and vv > 0.22
        if own is not None:
            for k_ in range(len(tx)):
                img = tx[k_]
                if img is None: continue
                fl = img.reshape(-1, img.shape[-1])[:, :3].astype(float); fl = fl / 255.0 if fl.max() > 1.5 else fl
                hsv = np.array([colorsys.rgb_to_hsv(*p) for p in fl[::37]]); m_ = ((hsv[:, 0] * 360 < 50) | (hsv[:, 0] * 360 > 340)) & (hsv[:, 1] > 0.12) & (hsv[:, 1] < 0.7) & (hsv[:, 2] > 0.22)
                mean = fl[::37][m_].mean(0); gain = np.clip(np.array(own) / np.maximum(mean, 0.01), 0.5, 2)
                H = rgb2hsv(np.clip(fl, 0, 1)); mk = ((H[:, 0] * 360 < 50) | (H[:, 0] * 360 > 340)) & (H[:, 1] > 0.12) & (H[:, 1] < 0.7) & (H[:, 2] > 0.22)
                out = fl.copy(); out[mk] = np.clip(fl[mk] * gain, 0, 1)
                sc = 255.0 if img.max() > 1.5 else 1.0
                tx[k_] = (out.reshape(img.shape[0], img.shape[1], 3) * sc).astype(img.dtype); print(f"tone: own {np.round(own, 3)}, piece skin {mean.round(3)}, gain {gain.round(2)}")
    if os.environ.get("TUCK") == "1" and it.get("kind") == "hand":
        # SEAM TUCK (user 2026-10-27 "fade/tuck the new hand's edge under my own wrist"): per 30 deg sector round the forearm axis the
        # piece starts at d0 (its lowest point from the wrist plane). The wearer's own hand stays full size up to d0 + OV, then sinks
        # (blend to the shrunk pose over SINK); the piece's first BAND past d0 is pulled to DEPTH under the wearer's full-size skin.
        OV, SINK, BAND, DEPTH = (float(os.environ.get(k, v)) for k, v in (("TK_OV", "0.004"), ("TK_SINK", "0.008"), ("TK_BAND", "0.008"), ("TK_DEPTH", "0.0007")))
        o_ = lambda n_: np.linalg.inv(WB[n_])[:3, 3]
        pc_ = o_(f"Base Human{S}Palm"); fd_ = o_(f"Base Human{S}Digit31") - pc_; fd_ /= np.linalg.norm(fd_); f3_ = o_(f"Base Human{S}Forearm3")
        ax_ = (pc_ - f3_) / np.linalg.norm(pc_ - f3_); e1_ = np.cross(ax_, [0, 0, 1.0]); e1_ /= np.linalg.norm(e1_); e2_ = np.cross(ax_, e1_)
        sect = lambda Q: ((np.arctan2((Q - f3_) @ e2_, (Q - f3_) @ e1_) % (2 * np.pi)) / (2 * np.pi) * 12).astype(int) % 12
        used = np.unique(np.concatenate([t_.ravel() for t_ in T])); dP = (PP - pc_) @ fd_; sP = sect(PP)
        d0 = np.array([dP[used][sP[used] == k].min() if (sP[used] == k).any() else dP[used].min() for k in range(12)])
        d0 = np.minimum(d0, np.roll(d0, 1)); d0 = np.minimum(d0, np.roll(d0, -1))          # no steps between sectors
        Wfull = F.skin(WP, Wbi, Wbw, np.array([np.eye(4)] * len(names)))
        dW = (Wfull - pc_) @ fd_; tW = np.clip((dW - d0[sect(Wfull)] - OV) / SINK, 0, 1)
        Wpos = Wfull + (Wpos - Wfull) * tW[:, None]
        from scipy.spatial import cKDTree
        WNf = WN / np.linalg.norm(WN, axis=1, keepdims=True); kd = cKDTree(Wfull)
        wt = np.clip(1 - (dP - d0[sP]) / BAND, 0, 1); sel = np.where(wt > 0)[0]
        if len(sel):
            _, nn = kd.query(PP[sel], k=4); q = Wfull[nn].mean(1); n_ = WNf[nn].mean(1); n_ /= np.linalg.norm(n_, axis=1, keepdims=True)
            sd = ((PP[sel] - q) * n_).sum(1); mv = np.clip(-DEPTH - sd, -float(os.environ.get("TK_MAX", "0.004")), 0)                        # only ever inward
            PP[sel] += n_ * (mv * wt[sel])[:, None]
        if os.environ.get("SEAMTONE") == "1":
            # colour across the seam: the wearer's skin texels just before the new hand vs the piece's skin texels in its first 15 mm
            def texmean(Pos, subsT, texs, uvs, keep):
                acc = []
                for T_, img in zip(subsT, texs):
                    if img is None or not len(T_): continue
                    m_ = keep(Pos[T_].mean(1))
                    if not m_.any(): continue
                    uv = uvs[T_[m_]].mean(1) % 1.0; px = img[((1 - uv[:, 1]) * (img.shape[0] - 1)).astype(int), (uv[:, 0] * (img.shape[1] - 1)).astype(int)][:, :3].astype(float)
                    px = px / 255.0 if px.max() > 1.5 else px; hv = rgb2hsv(px)
                    acc.append(px[((hv[:, 0] * 360 < 50) | (hv[:, 0] * 360 > 340)) & (hv[:, 1] > 0.12) & (hv[:, 1] < 0.7) & (hv[:, 2] > 0.22)])
                acc = np.vstack(acc) if acc else np.zeros((0, 3)); return acc.mean(0) if len(acc) > 20 else None
            if os.environ.get("SEAMTONE2") == "1":
                import seamtone as ST
                def texpx(Pos, subsT, texs, uvs, keep):
                    acc = []
                    for T_, img in zip(subsT, texs):
                        if img is None or not len(T_): continue
                        m_ = keep(Pos[T_].mean(1))
                        if not m_.any(): continue
                        for bw_ in ((1/3, 1/3, 1/3), (0.6, 0.2, 0.2), (0.2, 0.6, 0.2), (0.2, 0.2, 0.6)):      # 4 samples per triangle
                            uv = (uvs[T_[m_]] * np.array(bw_)[None, :, None]).sum(1) % 1.0
                            px = img[((1 - uv[:, 1]) * (img.shape[0] - 1)).astype(int), (uv[:, 0] * (img.shape[1] - 1)).astype(int)][:, :3].astype(float)
                            acc.append(px / 255.0 if px.max() > 1.5 else px)
                    return np.vstack(acc) if acc else np.zeros((0, 3))
                ws = ST.stats(texpx(Wfull, Wsubs, Wtex, WUV, lambda C: (lambda dd: (dd > d0[sect(C)] - 0.015) & (dd < d0[sect(C)] + OV))((C - pc_) @ fd_)))
                ps = ST.stats(texpx(PP, T, tx, UV, lambda C: (lambda dd: dd < d0[sect(C)] + 0.015)((C - pc_) @ fd_)))
                if ws and ps:
                    for k_ in range(len(tx)):
                        img = tx[k_]
                        if img is None: continue
                        sc = 255.0 if img.max() > 1.5 else 1.0; im3 = img[..., :3].astype(float) / sc
                        tx[k_] = (ST.transfer(im3, ps, ws) * sc).astype(img.dtype)
                    print(f"seam tone (Lab): wearer L {ws[0].round(1)} sd {ws[1].round(1)}, piece L {ps[0].round(1)} sd {ps[1].round(1)}")
                wm = pm = None
            else: wm = texmean(Wfull, Wsubs, Wtex, WUV, lambda C: (lambda dd: (dd > d0[sect(C)] - 0.015) & (dd < d0[sect(C)] + OV))((C - pc_) @ fd_))
            pm = texmean(PP, T, tx, UV, lambda C: (lambda dd: dd < d0[sect(C)] + 0.015)((C - pc_) @ fd_))
            if wm is not None and pm is not None:
                g2 = np.clip(wm / np.maximum(pm, 0.01), 0.5, 2)
                for k_ in range(len(tx)):
                    img = tx[k_]
                    if img is None: continue
                    fl = img.reshape(-1, img.shape[-1])[:, :3].astype(float); sc = 255.0 if fl.max() > 1.5 else 1.0; fl /= sc
                    H = rgb2hsv(np.clip(fl, 0, 1)); mk = ((H[:, 0] * 360 < 50) | (H[:, 0] * 360 > 340)) & (H[:, 1] > 0.12) & (H[:, 1] < 0.7) & (H[:, 2] > 0.22)
                    fl[mk] = np.clip(fl[mk] * g2, 0, 1); tx[k_] = (fl.reshape(img.shape[0], img.shape[1], 3) * sc).astype(img.dtype)
                print(f"seam tone: wearer {wm.round(3)}, piece edge {pm.round(3)}, gain {g2.round(2)}")
        print(f"tuck: piece starts {d0.min()*1000:.0f}..{d0.max()*1000:.0f} mm, {len(sel)} edge verts, moved max {(-mv.min() if len(sel) else 0)*1000:.1f} mm")
    if it.get("kind") == "tattoo":                                   # as the plugin's ink material: skin texels see-through (per triangle here)
        import colorsys
        def skin(rgb):
            hh, ss, vv = colorsys.rgb_to_hsv(*rgb); hh *= 360
            return (hh < 50 or hh > 340) and 0.12 < ss < 0.7 and vv > 0.3 and rgb[0] - rgb[1] > 0.1
        for k_ in range(len(T)):
            img = tx[k_]
            if img is None or not len(T[k_]): continue
            cuv = UV[T[k_]].mean(1) % 1.0; cpx = img[((1 - cuv[:, 1]) * (img.shape[0] - 1)).astype(int), (cuv[:, 0] * (img.shape[1] - 1)).astype(int)]
            T[k_] = T[k_][np.array([not skin(p) for p in cpx])]
    o = lambda n_: np.linalg.inv(WB[n_])[:3, 3]
    pc = o(f"Base Human{S}Palm"); fd = o(f"Base Human{S}Digit31") - pc; fd /= np.linalg.norm(fd)
    WS = Wsubs
    if os.environ.get("CUTW"):                                       # the wearer's own triangles past CUTW (m from the wrist plane) out
        cw = float(os.environ["CUTW"]); WS = [T_[((WP[T_].mean(1) - pc) @ fd) < cw] for T_ in Wsubs]
    d21, d51, tt = o(f"Base Human{S}Digit21"), o(f"Base Human{S}Digit51"), o(f"Base Human{S}Digit13")
    nrm = np.cross(d21 - pc, d51 - pc); nrm /= np.linalg.norm(nrm); back = -nrm if (tt - pc) @ nrm > 0 else nrm
    side = np.cross(fd, back); c = pc - fd * 0.01
    for j, vw in enumerate([back, side * (1 if S == "L" else -1) + back * 0.3]):
        e3 = fd; e2 = vw - e3 * (vw @ e3); e2 /= np.linalg.norm(e2); e1 = np.cross(e2, e3); R = np.stack([e1, e2, e3])
        parts = [((Wpos - c) @ R.T * [-1, 1, 1], WN @ R.T * [-1, 1, 1], WUV, WS, Wtex, None), ((PP - c) @ R.T * [-1, 1, 1], N @ R.T * [-1, 1, 1], UV, T, tx, None)]
        im = F.render(parts, S_, (0.0, 1.0, 0.0), box=(-0.09, 0.09, -0.07, 0.11))
        sheet.paste(Image.fromarray((im * 255).astype(np.uint8)), ((2 * ii + j) * S_, 0))
    dr.text((2 * ii * S_ + 4, S_ + 4), f"{it['note'][:48]}  (trimmed {it.get('cuff_trim', 0)} tris)", fill=(255, 220, 0))
sheet.save(OUTF, quality=88); print("saved", OUTF)
