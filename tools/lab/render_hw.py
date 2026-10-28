# render_hw.py <hw dir> <picks.json> <sheet prefix>: a thumbnail of every handwear item (its piece in source mesh space, textured
# from its bundle, lit), contact sheets of 40 with names -> review. View: along the piece's least-spread direction (a ring seen
# through its hole would be useless -> for rings the middle direction), slight tilt.
import sys, os, json, base64, numpy as np, UnityPy
from PIL import Image, ImageDraw
D, PK, PRE = sys.argv[1:4]; picks = {p["name"]: p for p in json.load(open(PK))}
scan = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp.m_FileID == 0: return objs.get(pp.m_PathID)
    import os as _o
    f = CABS.get(_o.path.basename(owner.assets_file.externals[pp.m_FileID - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp.m_PathID)
arr = lambda s, t, k: np.frombuffer(base64.b64decode(s), t).reshape(-1, k)
texcache = {}
def texture(hid, matname):
    k = (hid, matname)
    if k in texcache: return texcache[k]
    img = None
    try:
        env = UnityPy.load(scan[hid]["path"]); objs = {o.path_id: o for o in env.objects}
        for o in env.objects:
            if o.type.name == "Material" and o.peek_name() == matname:
                m = o.read(); te = dict((kk, v.m_Texture) for kk, v in m.m_SavedProperties.m_TexEnvs).get("_MainTex")
                to = deref(o, te, objs) if te is not None and te.m_PathID else None
                if to is not None: im = to.read().image.convert("RGB"); im.thumbnail((512, 512)); img = np.asarray(im).astype(np.float32) / 255
                break
    except Exception as e: print("tex", hid, matname, e)
    texcache[k] = img; return img
def render(h, img, S=200):
    P = arr(h["pos"], "<f4", 3).astype(float); N = arr(h["nrm"], "<f4", 3).astype(float); UV = arr(h["uv"], "<f4", 2).astype(float)
    T = np.concatenate([arr(s["tris"], "<i4", 3) for s in h["subs"]])
    c = P.mean(0); Q = P - c; ev, V = np.linalg.eigh(Q.T @ Q)
    view = V[:, 1] if h["kind"] == "ring" else V[:, 0]; view = (view + 0.35 * V[:, 2]); view /= np.linalg.norm(view)
    up = V[:, 2] - view * (V[:, 2] @ view); up /= np.linalg.norm(up); right = np.cross(up, view)
    x = Q @ right; y = Q @ up; z = Q @ view; sc = 0.9 * S / max(np.ptp(x), np.ptp(y), 1e-6)
    sx = S / 2 + (x - (x.max() + x.min()) / 2) * sc; sy = S / 2 - (y - (y.max() + y.min()) / 2) * sc
    out = np.full((S, S, 3), 0.16, np.float32); zb = np.full((S, S), -np.inf)
    for t in T:
        xs, ys = sx[t], sy[t]; x0, x1 = int(max(0, np.floor(xs.min()))), int(min(S - 1, np.ceil(xs.max()))); y0, y1 = int(max(0, np.floor(ys.min()))), int(min(S - 1, np.ceil(ys.max())))
        if x0 > x1 or y0 > y1: continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        m = np.array([[xs[1] - xs[0], xs[2] - xs[0]], [ys[1] - ys[0], ys[2] - ys[0]]])
        if abs(np.linalg.det(m)) < 1e-9: continue
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
        zb[Y[sel], X[sel]] = zz[sel]; out[Y[sel], X[sel]] = col * shade[:, None]
    if os.environ.get("ICONS"): return np.dstack([out, np.isfinite(zb).astype(np.float32)])
    return out
if os.environ.get("ICONS"):
    os.makedirs(os.environ["ICONS"], exist_ok=True)
    for f in sorted(os.listdir(D)):
        if not (f.startswith("hw_") and f.endswith(".json")): continue
        h = json.load(open(f"{D}/{f}")); p = picks.get(h["name"], {})
        img = texture(p.get("hid"), h["subs"][0]["mat"]) if p else None
        Image.fromarray((render(h, img, 256) * 255).astype(np.uint8), "RGBA").save(f"{os.environ['ICONS']}/{h['name']}.png")
        print("icon", h["name"], "textured" if img is not None else "NO TEXTURE", flush=True)
    sys.exit(0)
names = sorted(f[3:-5] for f in os.listdir(D) if f.startswith("hw_") and f.endswith(".json"))
order = {"ring": 0, "wrap": 1, "hand": 2}; names.sort(key=lambda n: (order.get(picks.get(n, {}).get("kind"), 9), n))
os.makedirs(D + "/thumbs", exist_ok=True); S = 200; per = 40
for k in range(0, len(names), per):
    chunk = names[k:k + per]; W = 8; sheet = Image.new("RGB", (W * S, ((len(chunk) + W - 1) // W) * (S + 26)), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
    for i, n in enumerate(chunk):
        h = json.load(open(f"{D}/hw_{n}.json")); p = picks.get(n, {})
        img = texture(p.get("hid"), h["subs"][0]["mat"]) if p else None
        th = (render(h, img, S) * 255).astype(np.uint8); Image.fromarray(th).save(f"{D}/thumbs/{n}.png")
        x, y = (i % W) * S, (i // W) * (S + 26); sheet.paste(Image.fromarray(th), (x, y))
        dr.text((x + 3, y + S + 1), f"{k + i}: {n[:28]}", fill=(255, 220, 0)); dr.text((x + 3, y + S + 12), p.get("note", "")[:34], fill=(200, 200, 200))
    sheet.save(f"{PRE}_{k // per}.jpg", quality=82); print(f"{PRE}_{k // per}.jpg", len(chunk), flush=True)
