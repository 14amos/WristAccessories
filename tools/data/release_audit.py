# release_audit.py <stage dir>: before a release (user 2026-10-27): (1) no personal data / user config: text files scanned for user
# names, home paths, drive paths, e-mail, profile ids, config/preset/nudge files; (2) no missing textures: every item's icon, every
# texture file a json names, every bundle key resolvable in the vanilla game or in a mod the README names as needed.
import os, sys, json, re, glob
ST = sys.argv[1]; GAME = "SPT"; bad = []; warn = []
PAT = re.compile("|".join([r"C:[\/]+Users"] + [re.escape(w) for w in os.environ.get("AUDIT_WORDS", "").split(",") if w]), re.I)   # AUDIT_WORDS: your own name, e-mail, ... (comma list)
for root, _, fs in os.walk(ST):
    for f in fs:
        p = os.path.join(root, f); low = f.lower()
        if low.endswith((".cfg", ".bak")) or "presets" in low or "nudges" in low or low.startswith("com.amos"): bad.append(f"user/config file: {p}")
        if low.endswith((".json", ".md", ".txt", ".cfg", ".xml")):
            t = open(p, encoding="utf-8", errors="replace").read()
            for m in PAT.finditer(t): bad.append(f"{os.path.relpath(p, ST)}: '{t[max(0, m.start() - 30):m.end() + 30]}'"); break
        if low.endswith(".dll"):
            b = open(p, "rb").read()
            for s in [w.encode() for w in os.environ.get("AUDIT_WORDS", "").split(",") if w]:
                if s in b or s.decode().encode("utf-16-le") in b: warn.append(f"{os.path.relpath(p, ST)} contains {s.decode()}")
# vanilla bundle keys
van = set()
for p in glob.glob(GAME + "/EscapeFromTarkov_Data/StreamingAssets/Windows/**/*.bundle", recursive=True):
    van.add(p.replace("\\", "/").split("StreamingAssets/Windows/")[1].lower())
mods = {}
for mf in glob.glob(GAME + "/SPT_Runtime/user/mods/*/bundles.json") + glob.glob(GAME + "/SPT_Runtime/user/mods/*/*/bundles.json"):
    for e in json.load(open(mf, encoding="utf-8"))["manifest"]: mods[e["key"].lower()] = mf.replace("\\", "/").split("/mods/")[1].split("/")[0]
need = {}
for acc in glob.glob(ST + "/**/acc", recursive=True):
    icons = os.path.join(acc, "icons")
    for f in glob.glob(acc + "/*.json"):
        name = os.path.basename(f)
        if not name.startswith(("acc_", "hw_")): continue
        j = json.load(open(f, encoding="utf-8")); nm = j.get("name", name)
        if j.get("kind") != "feet" and not os.path.exists(os.path.join(icons, nm + ".png")) and not os.path.exists(os.path.join(os.path.dirname(acc), "acc", "icons", nm + ".png")):
            warn.append(f"no icon: {os.path.relpath(f, ST)}")
        if j.get("texture") and not glob.glob(os.path.join(acc, j["texture"] + "*")): bad.append(f"missing texture file {j['texture']} for {nm}")
        keys = {j.get("bundle", "")} | {m.get("bundle", "") for m in j.get("meshes", [])}
        for k in filter(None, keys):
            kl = k.lower().replace("\\", "/")
            if kl in van: continue
            if kl in mods: need.setdefault(mods[kl], set()).add(os.path.relpath(f, ST).split(os.sep)[0] + ":" + nm); continue
            bad.append(f"bundle not found anywhere: {k} ({nm})")
print("PERSONAL / CONFIG:", len(bad) and "\n  " + "\n  ".join(bad[:40]) or "none")
print("WARNINGS:", len(warn) and "\n  " + "\n  ".join(warn[:40]) + (f"\n  ... {len(warn)} total" if len(warn) > 40 else "") or "none")
for m, s in need.items(): print(f"NEEDS MOD {m}: {len(s)} items, e.g. {sorted(s)[:4]}")
