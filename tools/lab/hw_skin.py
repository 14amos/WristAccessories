# hw_skin.py: each handwear item's own skin colour, measured on its piece (the texels its vertices use, skin-coloured ones,
# median, sRGB 0..1) -> "skin" in its hw_*.json (source + installed copies). For the skin-tone match (HandWear.Toned):
# gain = your forearm colour (acc/skintones.json) / this. No skin on the piece (full glove): no "skin", nothing recoloured.
import os, json, glob, base64, numpy as np, UnityPy
CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp.m_FileID == 0: return objs.get(pp.m_PathID)
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp.m_FileID - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp.m_PathID)
def skin_mask(px):
    mx = px.max(1); mn = px.min(1); v = mx; s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = px[:, 0], px[:, 1], px[:, 2]; d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return ((h < 50) | (h > 340)) & (s > 0.12) & (s < 0.7) & (v > 0.3) & (r - g > 0.1)   # r - g: skin, not brown cloth (BD glove cuff passed the old test)
ROOTS = ["SPT/EscapeFromTarkov_Data/StreamingAssets/Windows/"] + [p.replace("\\", "/") + "/" for p in glob.glob("SPT/SPT_Runtime/user/mods/*/bundles")]
def bundle_file(key):
    for r in ROOTS:
        if os.path.exists(r + key): return r + key
    return None
items = {}
for f in glob.glob("WORK/wristacc/acc/hw_*.json") + glob.glob("WORK/wristacc/addons/*/acc/hw_*.json"):
    items.setdefault(os.path.basename(f), []).append(f)
    inst = glob.glob("SPT/BepInEx/plugins/WristAccessories*/acc/" + os.path.basename(f))
    items[os.path.basename(f)] += inst
done = 0
for name, paths in sorted(items.items()):
    d = json.load(open(paths[0])); bf = bundle_file(d["bundle"])
    if bf is None: print("no bundle file", name, d["bundle"]); continue
    env = UnityPy.load(bf); objs = {o.path_id: o for o in env.objects}
    uv = np.frombuffer(base64.b64decode(d["uv"]), np.float32).reshape(-1, 2); acc = []
    for s in d["subs"]:
        img = None
        for o in env.objects:
            if o.type.name == "Material" and o.peek_name() == s["mat"]:
                m = o.read(); te = dict((kk, v.m_Texture) for kk, v in m.m_SavedProperties.m_TexEnvs).get("_MainTex")
                to = deref(o, te, objs) if te is not None and te.m_PathID else None
                if to is not None: img = np.asarray(to.read().image.convert("RGB")).astype(np.float32) / 255
                break
        if img is None: continue
        H, W = img.shape[:2]; v = np.unique(np.frombuffer(base64.b64decode(s["tris"]), np.int32)); u = uv[v] % 1
        px = img[((1 - u[:, 1]) * (H - 1)).astype(int), (u[:, 0] * (W - 1)).astype(int)]; acc.append(px[skin_mask(px)])
    acc = np.concatenate(acc) if acc else np.zeros((0, 3))
    skin = [round(float(x), 4) for x in np.median(acc, 0)] if len(acc) >= 60 else None
    for p in paths:
        dd = json.load(open(p)); dd.pop("skin", None)
        if skin: dd["skin"] = skin
        json.dump(dd, open(p, "w"))
    done += 1; print(name, "skin", skin, len(acc), flush=True)
print("items", done)
