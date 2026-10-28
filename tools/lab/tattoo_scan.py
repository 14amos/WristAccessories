# tattoo_scan.py [min %]: BARE hands with tattoos / painted textures (user 2026-10-27: "some hands like bear lonsdale hands have cool
# tattoos or textures on them. can we add them similar to how we did gloves?"). From lab/handscan/report.json (scan_hands.py: per
# outfit and side the main hand piece, "bare" = skin-coloured): the hand's triangles (Palm / Digit weight > 0.5, that side), their
# texels (triangle corners + middle, from the submesh's _MainTex), the share that is NOT skin-coloured (scan_hands.skin_like) =
# ink. Hands over min % (default 6) and with a texture not seen before (md5 of the texels) are written as picks for
# export_handwear.py (kind "hand"): lab/tattoo_picks_<main|ISB|Artem>.json, and listed.
import os, sys, json, hashlib, colorsys, collections; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
MIN = float(sys.argv[1]) if len(sys.argv) > 1 else 6.0
rep = json.load(open("WORK/wristacc/lab/handscan/report.json"))["geos"]
SCAN = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
def skin_like(rgb):
    h, s, v = colorsys.rgb_to_hsv(*rgb); h *= 360
    return (h < 50 or h > 340) and 0.12 < s < 0.7 and v > 0.22
def owner(path):
    p = path.replace("\\", "/")
    return "ISB" if "/ISB-Aishi/" in p else "Artem" if "/WTT-Artem/" in p else "main"
found = []; seen = set()
for g in rep.values():
    for S, sd in g["sides"].items():
        if sd["hand"] != "bare": continue
        for hid in [g["hid"]]:
            rec = SCAN[hid]
            try:
                env = UnityPy.load(rec["path"]); objs = {o.path_id: o for o in env.objects}; best = None
                for o in env.objects:
                    if o.type.name != "MonoBehaviour": continue
                    t = o.read_typetree()
                    if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
                    r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
                    mo = deref(r, rt["m_Mesh"], objs) if r else None
                    if mo is None: continue
                    nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
                    if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo, r, rt)
                _, paths, mo, r, rt = best; h = MeshHandler(mo.read()); h.process(); names = [p.split("/")[-1] for p in paths]
                UV = np.array(h.m_UV0, float)[:, :2]; bi = np.array(h.m_BoneIndices); bw = np.array(h.m_BoneWeights, float)
                hb = [i for i, n in enumerate(names) if ("Palm" in n or "Digit" in n) and f"Human{S}" in n]; hw = np.where(np.isin(bi, hb), bw, 0).sum(1)
                subs = [np.array(s_).reshape(-1, 3) for s_ in h.get_triangles()]
                tot = ink = 0; texels = []
                for si, T in enumerate(subs):
                    T = T[hw[T].mean(1) > 0.5]
                    if not len(T) or si >= len(rt["m_Materials"]): continue
                    m = deref(r, rt["m_Materials"][si], objs)
                    te = dict((k, v) for k, v in m.read_typetree()["m_SavedProperties"]["m_TexEnvs"]).get("_MainTex") if m else None
                    to = deref(m, te["m_Texture"], objs) if te else None
                    if to is None: continue
                    im = to.read().image.convert("RGB"); im.thumbnail((512, 512)); img = np.asarray(im).astype(float) / 255
                    pts = np.concatenate([UV[T].mean(1), UV[T[:, 0]], UV[T[:, 1]], UV[T[:, 2]]]) % 1.0
                    px = img[((1 - pts[:, 1]) * (img.shape[0] - 1)).astype(int), (pts[:, 0] * (img.shape[1] - 1)).astype(int)]
                    sk = np.array([skin_like(p) for p in px]); tot += len(px); ink += int((~sk).sum()); texels.append(px)
                if not tot: continue
                share = 100.0 * ink / tot; key = hashlib.md5(np.round(np.concatenate(texels) * 32).astype(np.uint8).tobytes()).hexdigest()[:12]
                found.append(dict(hid=hid, side=S, outfit=g["outfits"][0], outfits=len(g["outfits"]), share=round(share, 1), key=key, owner=owner(rec["path"]), mesh=g["mesh"]))
            except Exception as e:
                print("ERR", rec["name"], repr(e)[:80])
found.sort(key=lambda x: -x["share"]); picks = collections.defaultdict(list); n = 0
for x in found:
    if x["share"] < MIN or (x["key"], x["side"]) in seen: continue
    seen.add((x["key"], x["side"])); n += 1
    picks[x["owner"]].append(dict(name=f"tattoo_{x['key']}_{x['side']}", note=f"{x['outfit'].split(' (')[0] if '(' in x['outfit'] else x['outfit']}: hand", hid=x["hid"], side=x["side"], kind="hand"))
    print(f"  {x['share']:5.1f}% ink  {x['side']}  {x['owner']:5}  {x['outfit'][:46]:46} {x['mesh'][:30]}")
for k, v in picks.items(): json.dump(v, open(f"WORK/wristacc/lab/tattoo_picks_{k}.json", "w"), indent=1)
print(len(found), "bare hand sides scanned,", n, "with ink >=", MIN, "% (distinct textures):", {k: len(v) for k, v in picks.items()})
