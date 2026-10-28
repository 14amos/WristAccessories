# gsheet.py <gallery dir> <out prefix>: contact sheets, one row per accessory (4 views), 8 rows per sheet
import sys, glob, os
from PIL import Image, ImageDraw
d = sys.argv[1]; items = sorted({os.path.basename(f).rsplit("_", 2)[0] for f in glob.glob(d + "/*_0_top.png")})
TW, TH = 300, 225
for s in range(0, len(items), 8):
    chunk = items[s:s + 8]; sheet = Image.new("RGB", (TW * 4 + 150, TH * len(chunk)), (20, 20, 20)); dr = ImageDraw.Draw(sheet)
    for r, it in enumerate(chunk):
        dr.text((4, r * TH + 4), it, fill=(255, 255, 0))
        for v in range(4):
            f = glob.glob(f"{d}/{it}_{v}_*.png")
            if f: sheet.paste(Image.open(f[0]).convert("RGB").resize((TW, TH)), (150 + v * TW, r * TH))
    sheet.save(f"{sys.argv[2]}_{s // 8}.jpg", quality=82); print(f"{sys.argv[2]}_{s // 8}.jpg", chunk)
