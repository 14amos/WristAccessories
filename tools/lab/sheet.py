# sheet.py <frames dir> <out jpg> [every]: contact sheet of the lab frames (10 fps), labelled with the time
import sys, glob
from PIL import Image, ImageDraw
fs = sorted(glob.glob(sys.argv[1] + "/f_*.png")); ev = int(sys.argv[3]) if len(sys.argv) > 3 else 4; fs = fs[::ev]
W, H, C = 320, 240, 6; sheet = Image.new("RGB", (W * C, H * ((len(fs) + C - 1) // C)), (20, 20, 20)); d = ImageDraw.Draw(sheet)
for i, f in enumerate(fs):
    im = Image.open(f).convert("RGB").resize((W, H)); x, y = (i % C) * W, (i // C) * H; sheet.paste(im, (x, y))
    d.text((x + 4, y + 4), f"t={int(f[-7:-4]) / 10:.1f}s", fill=(255, 255, 0))
sheet.save(sys.argv[2], quality=85); print(len(fs), "tiles")
