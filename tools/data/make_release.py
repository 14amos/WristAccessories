# make_release.py <version>: WristAccessories release zips in OUT/release-wristacc-<version>/
#   WristAccessories-<v>.zip               the FULL release: main mod + the ISB-Aishi addon folder (user 2026-10-27 22:20: "build the
#                                          isb mod into it", "remove artem entirely from the full release build"); no WTT-Artem
#                                          anything. The ISB folder still skips itself when ISB-Aishi is not installed.
#   WristAccessories-<v>-friends.zip       PRIVATE (user's friends only): main mod with the Artem data (arm outlines, pants data,
#                                          skin tone) + ISB addon + Artem addon (from BACKUPS/wristacc-artem-removed-2026-10-27)
# Layout as installed (BepInEx/plugins/...), README.md / CHANGELOG.md / LICENSE at the zip root; *.bak* files never go in.
import os, sys, shutil, zipfile, hashlib
V = sys.argv[1]; W = "WORK/wristacc"; R = f"OUT/release-wristacc-{V}"; S = R + "/stage"
ARTEM = "BACKUPS/wristacc-artem-removed-2026-10-27/source/WristAccessories-Artem"
ign = shutil.ignore_patterns("*.bak*", "*.tmp", "Thumbs.db")
def main_mod(dst, friends):
    M = f"{dst}/BepInEx/plugins/WristAccessories"; os.makedirs(M)
    shutil.copy2(f"{W}/src/out/WristAccessories.dll", M); shutil.copy2(f"{W}/wrist_frames.json", M); shutil.copy2(f"{W}/fallback.png", M)
    for f in ("outlines.json", "outlines_body.json"): shutil.copy2(f"{W}/{f}.bak-before-artem" if friends else f"{W}/{f}", f"{M}/{f}")
    for d in ("acc", "watchshadow"): shutil.copytree(f"{W}/{d}", f"{M}/{d}", ignore=ign)
    if friends:
        shutil.copy2(f"{W}/lab/feetscan/shoe_profiles_all.json", f"{M}/acc/shoe_profiles.json")
        shutil.copy2(f"{W}/lab/feetscan/skintones_all.json", f"{M}/acc/skintones.json")
    for f in ("README.md", "CHANGELOG.md", "LICENSE"): shutil.copy2(f"{W}/{f}", dst)
def addon(dst, src, readme=None):
    shutil.copytree(src, f"{dst}/BepInEx/plugins/{os.path.basename(src)}", ignore=ign)
    if readme: shutil.copy2(readme, f"{dst}/README.md")
def zipdir(base, z):
    with zipfile.ZipFile(z, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for root, _, fs in os.walk(base):
            for f in sorted(fs):
                if ".bak" in f: continue
                p = os.path.join(root, f); zf.write(p, os.path.relpath(p, base).replace("\\", "/"))
    print(os.path.basename(z), os.path.getsize(z) // (1024 * 1024), "MB, sha", hashlib.sha256(open(z, "rb").read()).hexdigest()[:12], len(zipfile.ZipFile(z).namelist()), "files")
shutil.rmtree(S, ignore_errors=True)
main_mod(f"{S}/main", False); addon(f"{S}/main", f"{W}/addons/WristAccessories-ISB")
main_mod(f"{S}/friends", True); addon(f"{S}/friends", f"{W}/addons/WristAccessories-ISB"); addon(f"{S}/friends", ARTEM)
zipdir(f"{S}/main", f"{R}/WristAccessories-{V}.zip")
# no Artem in the full release: refuse to finish if anything Artem got in
import zipfile as _z
_bad = [n for n in _z.ZipFile(f"{R}/WristAccessories-{V}.zip").namelist() if "artem" in n.lower()]
if _bad: raise SystemExit(f"ARTEM IN THE FULL RELEASE: {_bad[:5]}")
zipdir(f"{S}/friends", f"{R}/WristAccessories-{V}-friends.zip")
