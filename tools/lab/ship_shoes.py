# ship_shoes.py: export_shoes.py's items (lab/feetscan/hw + icons) into the data folders by where the shoe comes from
# (user rule: other mods' items only as ADDONS): game + WTT-ContentBackport -> acc/, ISB-Aishi -> addons/WristAccessories-ISB/acc,
# (WTT-Artem removed 2026-10-27). Any other mod: left out and listed.
import json, os, shutil, collections
LAB = "WORK/wristacc/lab/feetscan"; ROOT = "WORK/wristacc"
DEST = {"game": ROOT + "/acc", "WTT-ContentBackport": ROOT + "/acc", "ISB-Aishi": ROOT + "/addons/WristAccessories-ISB/acc"}   # WTT-Artem: removed (user 2026-10-27), its shoes are left out
items = json.load(open(LAB + "/shoes.json", encoding="utf-8"))["items"]; n = collections.Counter(); left = []
for v in items.values():
    p = v["src"].replace("\\", "/"); mod = p.split("/user/mods/")[1].split("/")[0] if "/user/mods/" in p else "game"
    d = DEST.get(mod)
    if d is None: left.append((mod, v["outfits"][0])); continue
    os.makedirs(d + "/icons", exist_ok=True)
    shutil.copy2(f"{LAB}/hw/hw_{v['name']}.json", d); shutil.copy2(f"{LAB}/icons/{v['name']}.png", d + "/icons")
    n[mod] += 1
print(dict(n)); print("left out:", left)
