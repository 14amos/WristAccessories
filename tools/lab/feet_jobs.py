# feet_jobs.py: every lower-body (Feet) outfit, vanilla + mods' CustomClothing (bottomId / bottomBundlePath), with its display
# name (the lower suit that uses it, English locale) -> lab/feetscan/jobs.json {fid: {path, name, side, player}}; player = a suit
# players can get uses it (else bot-only: zombies, bosses, scavs, guards - the plugin's EVERY SHOE switch)
import os, json, glob
SPT = "SPT"; W = SPT + "/EscapeFromTarkov_Data/StreamingAssets/Windows"; MODS = SPT + "/SPT_Runtime/user/mods/"
cust = json.load(open(SPT + "/SPT_Runtime/SPT_Data/database/templates/customization.json", encoding="utf-8"))
loc = json.load(open(SPT + "/SPT_Runtime/SPT_Data/database/locales/global/en.json", encoding="utf-8"))
names = {}
for k, v in cust.items():
    p = v.get("_props", {})
    if isinstance(p.get("Feet"), str) and loc.get(k + " Name"): names.setdefault(p["Feet"], loc[k + " Name"])
jobs = {k: dict(path=os.path.join(W, v["_props"]["Prefab"]["path"]).replace("\\", "/"), name=names.get(k) or v["_props"].get("Name") or k, side=",".join(v["_props"].get("Side") or []), player=k in names)
        for k, v in cust.items() if v.get("_props", {}).get("BodyPart") == "Feet" and isinstance(v["_props"].get("Prefab"), dict) and v["_props"]["Prefab"].get("path")}
for f in glob.glob(MODS + "**/*.json", recursive=True):
    try: d = json.loads(open(f, encoding="utf-8-sig").read())
    except Exception: continue
    mod = f[len(MODS):].replace("\\", "/").split("/")[0]
    for it in (d if isinstance(d, list) else [d]):
        if not isinstance(it, dict) or not it.get("bottomId") or not it.get("bottomBundlePath") or it["bottomId"] in jobs: continue
        nm = None
        for key in ("locales", "locale", "names"):
            lv = it.get(key)
            if isinstance(lv, dict): nm = (lv.get("en") or {}).get("name") if isinstance(lv.get("en"), dict) else lv.get("en") or lv.get("name")
            if nm: break
        nm = nm if isinstance(nm, str) else it.get("name") or it.get("suiteName") or it["bottomId"]
        for cand in (MODS + mod + "/bundles/" + it["bottomBundlePath"], W + "/" + it["bottomBundlePath"], MODS + mod + "/" + it["bottomBundlePath"]):
            if os.path.isfile(cand): jobs[it["bottomId"]] = dict(path=cand.replace("\\", "/"), name=f"{nm} ({mod})", side=mod, player=True); break   # mods' clothing: sold by traders
missing = [k for k, v in jobs.items() if not os.path.isfile(v["path"])]
for k in missing: del jobs[k]
json.dump(jobs, open("WORK/wristacc/lab/feetscan/jobs.json", "w", encoding="utf-8"), indent=1)
print(len(jobs), "feet outfits,", len(missing), "bundles missing,", sum(1 for v in jobs.values() if "(" in v["name"]), "from mods")
