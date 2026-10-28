# cab_index.py: which bundle file holds which CAB (serialized file) - game hands / characters folders + every mod's
# bundles folder. Outfit bundles often keep their mesh in a dependency bundle (PPtr file id > 0 -> externals CAB name).
import UnityPy, os, json, sys
SPT = "SPT"; W = SPT + "/EscapeFromTarkov_Data/StreamingAssets/Windows/assets/content/"
roots = [W + "hands", W + "characters"] + [os.path.join(SPT + "/SPT_Runtime/user/mods", m, "bundles") for m in os.listdir(SPT + "/SPT_Runtime/user/mods")]
idx = {}
for r in roots:
    if not os.path.isdir(r): continue
    for d, _, fs in os.walk(r):
        for f in fs:
            if not f.endswith(".bundle"): continue
            p = os.path.join(d, f).replace("\\", "/")
            try:
                env = UnityPy.load(p)
                for bf in env.files.values():                       # the bundle -> its serialized files (CAB-...)
                    for name in getattr(bf, "files", {}):
                        if name.upper().startswith("CAB-"): idx[name.split(".")[0].lower()] = p
            except Exception: pass
json.dump(idx, open("WORK/wristacc/tools/cab_index.json", "w"))
print("CABs indexed:", len(idx))
