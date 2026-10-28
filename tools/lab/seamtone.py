# seamtone.py: colour transfer for a swapped hand piece so its skin matches the wearer's skin AT THE SEAM (user 2026-10-27: "the
# skin tone is still a bit off"). Reinhard transfer in CIELAB: piece skin texels' mean / std per channel -> the wearer's, statistics
# from the 15 mm before (wearer) and after (piece) the seam only. Skin mask is soft (no hard edge round the ink).
import numpy as np
def _lin(c): return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
def _srgb(c): return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(np.clip(c, 0, None), 1 / 2.4) - 0.055)
M = np.array([[0.4124, 0.3576, 0.1805], [0.2126, 0.7152, 0.0722], [0.0193, 0.1192, 0.9505]]); W = np.array([0.9505, 1.0, 1.089])
def to_lab(rgb):
    x = _lin(np.clip(rgb, 0, 1)) @ M.T / W; f = np.where(x > 0.008856, np.cbrt(x), 7.787 * x + 16 / 116)
    return np.c_[116 * f[:, 1] - 16, 500 * (f[:, 0] - f[:, 1]), 200 * (f[:, 1] - f[:, 2])]
def from_lab(lab):
    fy = (lab[:, 0] + 16) / 116; fx = fy + lab[:, 1] / 500; fz = fy - lab[:, 2] / 200; f = np.c_[fx, fy, fz]
    x = np.where(f ** 3 > 0.008856, f ** 3, (f - 16 / 116) / 7.787) * W
    return np.clip(_srgb(x @ np.linalg.inv(M).T), 0, 1)
def hsv(a):
    mx = a.max(1); mn = a.min(1); df = mx - mn + 1e-9; r, g, b = a.T
    h = np.where(mx == r, ((g - b) / df) % 6, np.where(mx == g, (b - r) / df + 2, (r - g) / df + 4)) / 6
    return np.c_[np.where(mx - mn < 1e-9, 0, h), np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-9), 0), mx]
def skin_w(rgb):
    """soft skin weight 0..1 (the plugin's IsSkin test, feathered)"""
    h = hsv(rgb); hd = np.minimum(h[:, 0] * 360, 360 - h[:, 0] * 360)        # hue distance from red
    sm = lambda x, a, b: np.clip((x - a) / (b - a), 0, 1)
    return sm(55 - hd, 0, 10) * sm(h[:, 1], 0.04, 0.09) * sm(0.75 - h[:, 1], 0, 0.08) * sm(h[:, 2], 0.18, 0.26)
def stats(px):
    w = skin_w(px); keep = w > 0.5
    if keep.sum() < 20: return None
    L = to_lab(px[keep]); return L.mean(0), L.std(0) + 1e-3
def transfer(img, src, dst, strength=1.0, std_limit=(0.6, 1.6)):
    """img HxWx3 float 0..1; src/dst = (mean, std) in Lab. Returns a new image."""
    fl = img.reshape(-1, 3); L = to_lab(fl)
    # ink / shadow texels darker than the piece's skin (> 1.2 sd below its lightness) keep their colour: the tattoo stays as dark as it was
    w = skin_w(fl) * strength * np.clip((L[:, 0] - (src[0][0] - 1.2 * src[1][0])) / max(src[1][0], 1e-3), 0, 1)
    k = np.clip(dst[1] / src[1], *std_limit); L2 = (L - src[0]) * k + dst[0]
    out = fl + (from_lab(L2) - fl) * w[:, None]
    return out.reshape(img.shape)
