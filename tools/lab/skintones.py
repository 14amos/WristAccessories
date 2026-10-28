# skintones.py: the skin colour of every outfit's FOREARM (first-person hands meshes + third-person tops), for the handwear
# skin-tone match (user 2026-10-27: "retextures the hand based on what color the arm is"). Per Skin (EFT's _bonePaths +
# renderer): vertices whose strongest bone is a Forearm, texture of their submesh sampled at their UV, skin-coloured samples
# averaged (sRGB 0..1). No bare forearm (a sleeve): the hand's own skin (Palm / Digit) instead, marked "hand".
# Also: every handwear item's own skin colour on its piece (hw_*.json "skin").
# Out: WORK/wristacc/acc/skintones.json {mesh name: {"rgb": [r,g,b], "n": samples, "from": "forearm"|"hand"}}
import os, sys, json, glob, base64, colorsys, numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
def skin_mask(px):    # px float 0..1 (N,3): the same range as HandWear.IsSkin / scan_hands.skin_like
    mx = px.max(1); mn = px.min(1); v = mx; s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = px[:, 0], px[:, 1], px[:, 2]; d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return ((h < 50) | (h > 340)) & (s > 0.12) & (s < 0.7) & (v > 0.3) & (r - g > 0.1)   # r - g: skin, not brown cloth (BD glove cuff passed the old test)
TEX = {}
def texture(mo):
    if mo is None: return None
    k = (mo.assets_file.name, mo.path_id)
    if k in TEX: return TEX[k]
    img = None
    try:
        m = mo.read_typetree(); te = dict((kk, v["m_Texture"]) for kk, v in m["m_SavedProperties"]["m_TexEnvs"]).get("_MainTex")
        to = deref(mo, te, {o.path_id: o for o in mo.assets_file.objects.values()}) if te and te["m_PathID"] else None
        if to is not None: img = np.asarray(to.read().image.convert("RGB")).astype(np.float32) / 255
    except Exception as e: print("  tex", e)
    TEX[k] = img; return img
def sample(img, uv):
    H, W = img.shape[:2]; u = uv % 1
    return img[((1 - u[:, 1]) * (H - 1)).astype(int), (u[:, 0] * (W - 1)).astype(int)]

files = set()
for rec in json.load(open("WORK/tattoo/acc/all_scan_hands.json")).values(): files.add(rec["path"])
G = "SPT/EscapeFromTarkov_Data/StreamingAssets/Windows/assets/content/characters/character/prefabs/"
for f in os.listdir(G):
    if f.lower().startswith(("top_", "tshirt_")) and f.endswith(".bundle"): files.add(G + f)
for f in glob.glob("SPT/SPT_Runtime/user/mods/*/bundles/**/*.bundle", recursive=True):
    b = os.path.basename(f).lower()
    if b.startswith(("top_", "tshirt_")): files.add(f.replace("\\", "/"))
print(len(files), "bundles", flush=True)
out = {}
for i, path in enumerate(sorted(files)):
    try:
        env = UnityPy.load(path); objs = {o.path_id: o for o in env.objects}
        for o in env.objects:
            if o.type.name != "MonoBehaviour": continue
            t = o.read_typetree()
            if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
            r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
            mo = deref(r, rt["m_Mesh"], objs) if r else None
            if mo is None or mo.peek_name() in out: continue
            mesh = mo.read(); h = MeshHandler(mesh); h.process()
            uv = np.array(h.m_UV0, np.float32)[:, :2]; bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, np.float32)
            names = [p.split("/")[-1] for p in t["_bonePaths"]]; top = bi[np.arange(len(bi)), bw.argmax(1)]
            fore = np.array(["Forearm" in names[x] if x < len(names) else False for x in top]); hand = np.array([("Palm" in names[x] or "Digit" in names[x]) if x < len(names) else False for x in top])
            mats = [deref(r, m, objs) for m in rt["m_Materials"]]
            res = {}
            for kind, sel in (("forearm", fore), ("hand", hand)):
                acc = []
                for si, tris in enumerate(h.get_triangles()):
                    img = texture(mats[min(si, len(mats) - 1)]) if mats else None
                    if img is None: continue
                    v = np.unique(np.array(tris).reshape(-1)); v = v[sel[v]]
                    if len(v) == 0: continue
                    px = sample(img, uv[v]); acc.append(px[skin_mask(px)])
                acc = np.concatenate(acc) if acc else np.zeros((0, 3))
                res[kind] = acc
            k = "forearm" if len(res["forearm"]) >= 60 else "hand" if len(res["hand"]) >= 60 else None
            if k: out[mo.peek_name()] = {"rgb": [round(float(x), 4) for x in np.median(res[k], 0)], "n": int(len(res[k])), "from": k}
    except Exception as e: print("ERR", path, e, flush=True)
    if i % 25 == 0: print(i, len(out), flush=True)
json.dump(out, open("WORK/wristacc/acc/skintones.json", "w"), indent=0)
print("meshes with a skin tone:", len(out), "forearm", sum(v["from"] == "forearm" for v in out.values()))
