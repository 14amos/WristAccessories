# seamtest_bones.py <hid>...: the wearer hands renderer's bone names (EFT stores them as _bonePaths in a script the Unity lab can't
# read) -> seamtest/<hid8>_bones.json {mesh, bones} for SeamTest.cs (same pick as hand_onarm.py)
import os, sys, json; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import UnityPy
SCAN = json.load(open("WORK/tattoo/acc/all_scan_hands.json")); CABS = json.load(open("WORK/wristacc/tools/cab_index.json")); DEP = {}
def deref(owner, pp, objs):
    if pp["m_FileID"] == 0: return objs.get(pp["m_PathID"])
    f = CABS.get(os.path.basename(owner.assets_file.externals[pp["m_FileID"] - 1].path).lower())
    if f is None: return None
    if f not in DEP: DEP[f] = {o.path_id: o for o in UnityPy.load(f).objects}
    return DEP[f].get(pp["m_PathID"])
ids = sys.argv[1:]
if ids == ["ALL"]:                                    # one outfit per distinct hands bundle
    seen = {}
    for h, v in SCAN.items(): seen.setdefault(v["path"], h)
    ids = list(seen.values())
for WID in ids:
  try:
    if os.path.exists(f"seamtest/{WID[:8]}_bones.json"): continue
    env = UnityPy.load(SCAN[WID]["path"]); objs = {o.path_id: o for o in env.objects}; best = None
    for o in env.objects:
        if o.type.name != "MonoBehaviour": continue
        t = o.read_typetree()
        if "_bonePaths" not in t or not t.get("_skinnedMeshRenderer"): continue
        r = deref(o, t["_skinnedMeshRenderer"], objs); rt = r.read_typetree() if r else None
        mo = deref(r, rt["m_Mesh"], objs) if r else None
        if mo is None: continue
        nm = mo.peek_name().lower(); sc = ("watch" not in nm) * 8 + ("lod0" in nm) * 4 + ("hood" not in nm) * 2 + ("base" in nm)
        if best is None or sc > best[0]: best = (sc, t["_bonePaths"], mo)
    if best is None: print(WID[:8], "no skinned hands"); continue
    json.dump({"mesh": best[2].peek_name(), "bones": [p.split("/")[-1] for p in best[1]]}, open(f"seamtest/{WID[:8]}_bones.json", "w")); print(WID[:8], best[2].peek_name(), len(best[1]))
  except Exception as e: print(WID[:8], "FAIL", str(e)[:80])
