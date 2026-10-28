# sheet_watches.py: one thumbnail per distinct watch (material + texture) group of watch_scan.json -> watch_groups.json + sheet
import UnityPy, os, json, hashlib
from PIL import Image, ImageDraw
CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
scan = json.load(open("WORK/tattoo/acc/watch_scan.json"))
groups = {}
for hid, v in scan.items():
    key = tuple(sorted({f"{r['mats'][j].split(' ')[0]}|{t}" for r in v["renderers"] for j, t in zip(r["watch_subs"], r["tex"]) if not r['mats'][j].startswith("Default-Material")}))
    groups.setdefault(key, []).append(hid)
def main_tex(path, mesh, mats):
    env = UnityPy.load(path); objs = {o.path_id: o for o in env.objects}
    for o in env.objects:
        if o.type.name != "Material": continue
        if o.peek_name() not in mats or "glass" in o.peek_name().lower(): continue
        for k, t in o.read_typetree()["m_SavedProperties"]["m_TexEnvs"]:
            if k != "_MainTex" or t["m_Texture"]["m_PathID"] == 0: continue
            tp = t["m_Texture"]
            if tp["m_FileID"] == 0: to = objs.get(tp["m_PathID"])
            else:
                f = CABS.get(os.path.basename(o.assets_file.externals[tp["m_FileID"] - 1].path).lower())
                if not f: continue
                if f not in DEP: DEP[f] = {x.path_id: x for x in UnityPy.load(f).objects}
                to = DEP[f].get(tp["m_PathID"])
            if to is not None: return to.read().image.convert("RGB")
    return None
out = []; tiles = []
for key, hids in sorted(groups.items(), key=lambda x: -len(x[1])):
    v = scan[hids[0]]; mats = {r["mats"][j] for r in v["renderers"] for j in r["watch_subs"]}
    im = None
    try: im = main_tex(v["path"], None, mats)
    except Exception as e: print("tex fail", v["name"], e)
    mean = [round(x) for x in (im.resize((1, 1), Image.BOX).getpixel((0, 0)) if im else (0, 0, 0))]
    out.append(dict(key=list(key), outfits=hids, names=[scan[h]["name"] for h in hids], mean_rgb=mean))
    t = Image.new("RGB", (200, 230), (30, 30, 30))
    if im: t.paste(im.resize((200, 200)), (0, 0))
    ImageDraw.Draw(t).text((3, 203), f"{len(out)-1}: {v['name'][:30]}", fill=(255, 255, 255))
    tiles.append(t)
W = 7; sheet = Image.new("RGB", (200 * W, 230 * ((len(tiles) + W - 1) // W)))
for i, t in enumerate(tiles): sheet.paste(t, ((i % W) * 200, (i // W) * 230))
sheet.save("WORK/tattoo/acc/watch_groups.jpg", quality=85)
json.dump(out, open("WORK/tattoo/acc/watch_groups.json", "w"), indent=1)
for i, g in enumerate(out): print(i, len(g["outfits"]), g["mean_rgb"], g["names"][0][:40], "|", ", ".join(k.split("|")[0] for k in g["key"])[:60])
