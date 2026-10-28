# scan_all.py: (from scan_watches.py) EVERY renderer / mesh / material of every hands outfit, to find baked accessories.
# Was scan_watches.py: every first-person hands outfit (vanilla + mods' CustomClothing, the same list build_outlines.py uses)
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


MODE = sys.argv[1] if len(sys.argv) > 1 else "Hands"
if MODE == "Body":
    jobs = {k: (os.path.join(W, v["_props"]["Prefab"]["path"]), v["_props"]) for k, v in cust.items()
            if v.get("_props", {}).get("BodyPart") == "Body" and isinstance(v["_props"].get("Prefab"), dict) and v["_props"]["Prefab"].get("path")}
    for f in glob.glob(MODS + "**/*.json", recursive=True):
        try: d = json.loads(open(f, encoding="utf-8-sig").read())
        except Exception: continue
        mod = f[len(MODS):].replace("\\", "/").split("/")[0]
        for it in (d if isinstance(d, list) else [d]):
            if not isinstance(it, dict) or not it.get("topId") or not it.get("topBundlePath") or it["topId"] in jobs: continue
            for cand in (MODS + mod + "/bundles/" + it["topBundlePath"], W + "/" + it["topBundlePath"], MODS + mod + "/" + it["topBundlePath"]):
                if os.path.isfile(cand): jobs[it["topId"]] = (cand, {"_mod": mod}); break
    print("body outfits:", len(jobs), flush=True)
out = {}
for i, (hid, (path, props)) in enumerate(sorted(jobs.items())):
    try:
        env = UnityPy.load(path); objs = {o.path_id: o for o in env.objects}; rends = []
        for o in env.objects:
            if o.type.name != "SkinnedMeshRenderer" and o.type.name != "MeshRenderer": continue
            rt = o.read_typetree()
            mats = [objs[m["m_PathID"]].peek_name() if m["m_PathID"] in objs else "?" for m in rt["m_Materials"]]
            mesh = "?"
            if "m_Mesh" in rt and rt["m_Mesh"]["m_PathID"] in objs: mesh = objs[rt["m_Mesh"]["m_PathID"]].peek_name()
            go = objs.get(rt["m_GameObject"]["m_PathID"]); gname = go.peek_name() if go else "?"
            rends.append(dict(type=o.type.name, go=gname, mesh=mesh, mats=mats))
        out[hid] = dict(path=path.replace("\\", "/"), name=names.get(hid) or modnames.get(hid) or cust.get(hid, {}).get("_name") or hid,
                        side=(props.get("Side") or ["?"])[0] if isinstance(props.get("Side"), list) else props.get("_mod", "?"), renderers=rends)
    except Exception as e:
        print("FAIL", hid, os.path.basename(path), str(e)[:80], flush=True)
    if i % 50 == 0: print(i, "/", len(jobs), flush=True)
json.dump(out, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), f"all_scan_{MODE.lower()}.json"), "w"), indent=1)
print("done", len(out))
