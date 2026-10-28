# survey_sheets.py <folder>: group identical-looking outfit renders, contact sheets of 8 -> <folder>/sheets/sNN.jpg + groups.json
import json, numpy as np, os, sys
from PIL import Image, ImageDraw
D = sys.argv[1]; scan = json.load(open("WORK/tattoo/acc/all_scan_hands.json"))
ids = sorted(f[:-4] for f in os.listdir(D) if f.endswith(".png"))
small = {i: np.asarray(Image.open(f"{D}/{i}.png").convert("L").resize((128, 64)), float) for i in ids}
groups = []
for i in ids:
    for g in groups:
        if np.abs(small[g[0]] - small[i]).mean() < 1.5: g.append(i); break
    else: groups.append([i])
json.dump(groups, open(f"{D}/groups.json", "w")); os.makedirs(f"{D}/sheets", exist_ok=True)
for s in range(0, len(groups), 8):
    sh = Image.new("RGB", (1024, 1040), (20, 20, 20)); d = ImageDraw.Draw(sh)
    for n, g in enumerate(groups[s:s + 8]):
        x, y = (n % 2) * 512, (n // 2) * 260
        sh.paste(Image.open(f"{D}/{g[0]}.png").resize((512, 256)), (x, y + 4))
        d.text((x + 3, y + 4), f"#{s + n} {scan[g[0]]['name'][:52]}" + (f" (+{len(g)-1})" if len(g) > 1 else ""), fill=(255, 255, 0))
    sh.save(f"{D}/sheets/s{s // 8:02d}.jpg", quality=82)
print("unique looks", len(groups), "sheets", (len(groups) + 7) // 8)
