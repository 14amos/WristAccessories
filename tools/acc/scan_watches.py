# scan_watches.py: every first-person hands outfit (vanilla + mods' CustomClothing, the same list build_outlines.py uses)
# -> which renderers / submeshes carry a baked watch (material names with watch words, or a separate *_watch mesh), the
# outfit's moving-hands WatchPrefab, and the outfit's display name (the kit that uses these hands, English locale).
# Writes watch_scan.json next to this script; watches.py picks its SPECS from it.
import UnityPy, os, sys, json, glob, hashlib
SPT = "SPT"; W = SPT + "/EscapeFromTarkov_Data/StreamingAssets/Windows"
cust = json.load(open(SPT + "/SPT_Runtime/SPT_Data/database/templates/customization.json", encoding="utf-8"))
loc = json.load(open(SPT + "/SPT_Runtime/SPT_Data/database/locales/global/en.json", encoding="utf-8"))
WORDS = ("watch", "wach", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin", "casio", "clock")
MODS = SPT + "/SPT_Runtime/user/mods/"

jobs = {k: (os.path.join(W, v["_props"]["Prefab"]["path"]), v["_props"]) for k, v in cust.items()
        if v.get("_props", {}).get("BodyPart") == "Hands" and isinstance(v["_props"].get("Prefab"), dict) and v["_props"]["Prefab"].get("path")}
names, modnames = {}, {}
for k, v in cust.items():                                    # kits: Body + Hands -> the outfit's name
    p = v.get("_props", {})
    if isinstance(p.get("Hands"), str) and loc.get(k + " Name"): names.setdefault(p["Hands"], loc[k + " Name"])
for f in glob.glob(MODS + "**/*.json", recursive=True):      # mods' CustomClothing configs (bundle + locale name if given)
    try: d = json.loads(open(f, encoding="utf-8-sig").read())
    except Exception: continue
    mod = f[len(MODS):].replace("\\", "/").split("/")[0]
    for it in (d if isinstance(d, list) else [d]):
        if not isinstance(it, dict) or not it.get("handsId") or not it.get("handsBundlePath"): continue
        nm = None
        for key in ("locales", "locale", "names"):
            lv = it.get(key)
            if isinstance(lv, dict): nm = (lv.get("en") or {}).get("name") if isinstance(lv.get("en"), dict) else lv.get("en") or lv.get("name")
            if nm: break
        nm = nm or it.get("name") or it.get("suiteName")
        if nm and isinstance(nm, str): modnames.setdefault(it["handsId"], f"{nm} ({mod})")
        if it["handsId"] in jobs: continue
        for cand in (MODS + mod + "/bundles/" + it["handsBundlePath"], W + "/" + it["handsBundlePath"], MODS + mod + "/" + it["handsBundlePath"]):
            if os.path.isfile(cand): jobs[it["handsId"]] = (cand, {"_mod": mod}); break
print("hands outfits:", len(jobs), flush=True)

CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP, TEX = {}, {}
out = {}
for i, (hid, (path, props)) in enumerate(sorted(jobs.items())):
    try:
        env = UnityPy.load(path); objs = {o.path_id: o for o in env.objects}; rends = []
        for o in env.objects:
            if o.type.name != "MonoBehaviour": continue
            t = o.read_typetree()
            if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
            r = objs.get(t["_skinnedMeshRenderer"]["m_PathID"])
            if r is None: continue
            rt = r.read_typetree(); mo = objs.get(rt["m_Mesh"]["m_PathID"])
            mats = [objs[m["m_PathID"]].peek_name() if m["m_PathID"] in objs else "?" for m in rt["m_Materials"]]
            mesh = mo.peek_name() if mo else "?"
            wsub = [j for j, m in enumerate(mats) if any(w in m.lower() for w in WORDS)]
            sep = any(w in mesh.lower() for w in ("watch", "shock"))
            subs = list(range(len(mats))) if sep else wsub
            def tex(m):                                     # a material's _MainTex -> "name|hash of its pixel data" (also in dependency bundles)
                if m["m_PathID"] not in objs: return "?"
                mo_ = objs[m["m_PathID"]]
                for k, v in mo_.read_typetree()["m_SavedProperties"]["m_TexEnvs"]:
                    if k != "_MainTex": continue
                    tp = v["m_Texture"]
                    if tp["m_PathID"] == 0: return ""
                    if tp["m_FileID"] == 0: to = objs.get(tp["m_PathID"])
                    else:
                        cab = os.path.basename(mo_.assets_file.externals[tp["m_FileID"] - 1].path).lower(); f = CABS.get(cab)
                        if f is None: return f"{cab}:{tp['m_PathID']}"
                        if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
                        to = DEP[f].get(tp["m_PathID"])
                    if to is None: return "?"
                    key = (id(to.assets_file), to.path_id)
                    if key not in TEX:
                        try: TEX[key] = f"{to.peek_name()}|{hashlib.md5(to.read().image.tobytes()).hexdigest()[:10]}"
                        except Exception as e: TEX[key] = f"{to.peek_name()}|?"
                    return TEX[key]
                return ""
            if subs: rends.append(dict(mesh=mesh, mats=mats, watch_subs=subs, tex=[tex(rt["m_Materials"][j]) for j in subs]))
        if rends:
            out[hid] = dict(path=path.replace("\\", "/"), name=names.get(hid) or modnames.get(hid) or cust.get(hid, {}).get("_name") or hid,
                            side=(props.get("Side") or ["?"])[0] if isinstance(props.get("Side"), list) else props.get("_mod", "?"),
                            prefab=(props.get("WatchPrefab") or {}).get("path", "") if isinstance(props.get("WatchPrefab"), dict) else "",
                            renderers=rends)
    except Exception as e:
        print("FAIL", hid, os.path.basename(path), str(e)[:80], flush=True)
    if i % 50 == 0: print(i, "/", len(jobs), flush=True)
json.dump(out, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "watch_scan.json"), "w"), indent=1)
by = {}
for hid, v in out.items():
    key = tuple(sorted({f"{r['mats'][j].split(' ')[0]}|{t}" for r in v["renderers"] for j, t in zip(r["watch_subs"], r["tex"]) if not r['mats'][j].startswith("Default-Material")}))
    by.setdefault(key, []).append(v["name"])
print(f"{len(out)} outfits with a watch, {len(by)} distinct watch material sets:")
for k, v in sorted(by.items(), key=lambda x: -len(x[1])): print(f"  {len(v):3}  {', '.join(k)[:110]:110}  e.g. {', '.join(v[:2])}")
