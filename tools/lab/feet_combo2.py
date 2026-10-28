# feet_combo2.py: SHOES v2 lab (feet_fit.py): pants (as the plugin leaves them) + another outfit's boot islands, fitted.
#   python feet_combo2.py <out.jpg> <pants query>[,...] <shoe source query>[,...]   (queries: fid, or part of name / bundle path)
#   env FIT=0: no fit (v2 cut only); TINT=1: pants blue, shoe orange.
import os, sys; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np, feet_combo as F, feet_fit as G
from PIL import Image, ImageDraw
CACHE = {}
def pants(fid):
    if fid not in CACHE:
        p = F.load_pants(fid); fw = G.foot_w(p["names"], p["bi"], p["bw"]); p["Pr"] = F.to_ref_pos(p); own = p["Pr"] if G.HIDDEN else F.skin_own(p, 0.7)
        CACHE[fid] = (p, fw, own, G.profile(own, p["subs"], fw))
    return CACHE[fid]
def shoe_parts(fid):
    p, fw, _, _ = pants(fid); bt = G.boot_tris(p["subs"], fw, len(p["P"]), p["P"])
    return p, [s[k] for s, k in zip(p["subs"], bt)]
if __name__ == "__main__":
    outp, pq, sq = sys.argv[1:4]; fit_on = os.environ.get("FIT", "1") != "0"; tint = os.environ.get("TINT") == "1"; S = 300
    P_ids = [F.find_pants(q) for q in pq.split(",")]; S_ids = [F.find_pants(q) for q in sq.split(",")]
    sheet = Image.new("RGB", (S * len(S_ids) * 2, (S + 34) * len(P_ids)), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
    for i, pf in enumerate(P_ids):
        p, fw, own, prof = pants(pf)
        for j, sf in enumerate(S_ids):
            sp, ssubs = shoe_parts(sf); log = []
            use = np.zeros(len(sp["P"]), bool); use[np.unique(np.concatenate([t.ravel() for t in ssubs]))] = True
            SP = G.fit(sp["Pr"], prof, log, use) if fit_on else sp["Pr"]
            parts = [(own, p["N"], p["UV"], p["subs"], p["tex"], (0.2, 0.4, 1.0) if tint else None), (SP, sp["N"], sp["UV"], ssubs, sp["tex"], (1.0, 0.5, 0.1) if tint else None)]
            for k, vw in enumerate([(0.55, 1.0, 0.15), (-1.0, 0.25, 0.1)]):
                im = (F.render(parts, S, vw) * 255).astype(np.uint8); sheet.paste(Image.fromarray(im), ((j * 2 + k) * S, i * (S + 34)))
            dr.text((j * 2 * S + 3, i * (S + 34) + S + 2), f"{p['name'][:34]} + shoes of {sp['name'][:34]}", fill=(255, 220, 0))
            dr.text((j * 2 * S + 3, i * (S + 34) + S + 16), " | ".join(log)[:100], fill=(200, 200, 200))
            print(p["name"], "+", sp["name"], log, flush=True)
    sheet.save(outp, quality=88); print("saved", outp)
