# beads.py: our own stretch bead bracelets (user design, 2026-10-25): 24 polished 8 mm beads on a thin black elastic.
# Colourways: angelite (soft sky blue, marbled) and mixed gemstones (amethyst, green aventurine, hematite, lapis, smoky quartz,
# fluorite). Geometry and textures are generated here; the material is the game's Sacred Amulet one (neutral grey shine) with
# our colour / gloss / flat normal maps swapped in at runtime (acc "texture" -> tex_<name>_d/_s/_n.png).
import sys, numpy as np
from PIL import Image
sys.path.insert(0, "WORK/tattoo/acc")
from acclib import Curve, build, radial_from, OUT

BUNDLE = "assets/content/items/spec/item_spec_lightkeeper_amulet/item_spec_lightkeeper_amulet.bundle"
MESH = "item_spec_lightkeeper_amulet_LOD0"           # its material is looked up by this mesh name
RB, RS = 0.006, 0.0006                                # bead radius, elastic radius (m): 12 mm beads = ~8 mm real (FP arms ~1.5x real scale)
SIZES = list(range(16, 50, 2))                # bead counts: the runtime picks the size that fits the sleeve
CELL, GRID = 64, 4                                    # texture atlas: 4 x 4 cells of 64 px
rng = np.random.default_rng(7)


def fbm(n, octaves=5, seed=0):
    """smooth fractal noise in 0..1, n x n"""
    r = np.random.default_rng(seed); out = np.zeros((n, n)); amp = 1.0; tot = 0
    for o in range(octaves):
        k = 2 ** (o + 2); g = r.random((k + 1, k + 1))
        im = np.asarray(Image.fromarray((g * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC)).astype(float) / 255
        out += amp * im; tot += amp; amp *= 0.5
    return out / tot


def stone(base, vein, seed, veins=1.0, flecks=None):
    """one cell: marbled stone - base colour with soft veins along fractal noise bands"""
    f = fbm(CELL, seed=seed); band = np.abs(np.sin(f * 9 + fbm(CELL, seed=seed + 50) * 6)) ** 3 * veins
    c = np.array(base, float)[None, None] * (1 - band[..., None]) + np.array(vein, float)[None, None] * band[..., None]
    c *= (0.92 + 0.16 * fbm(CELL, 3, seed + 99))[..., None]
    if flecks is not None:                            # tiny bright inclusions (pyrite in lapis, rust in angelite)
        m = rng.random((CELL, CELL)) > 0.985; c[m] = flecks
    return np.clip(c, 0, 255)


PALETTES = {
    "angelite": [stone((122, 168, 212), (190, 214, 236), s, 0.9) for s in range(7)] + [stone((118, 162, 206), (200, 222, 240), 8, 0.9, (176, 124, 104))],
    "gems": [stone((106, 58, 148), (168, 128, 205), 11, 1.0),          # amethyst
             stone((104, 168, 138), (160, 210, 180), 12, 0.7),         # green aventurine
             stone((58, 58, 64), (96, 96, 104), 13, 0.4),              # hematite
             stone((38, 54, 138), (70, 90, 180), 14, 0.6, (210, 180, 90)),   # lapis with pyrite flecks
             stone((92, 78, 72), (140, 122, 112), 15, 0.6),            # smoky quartz
             stone((168, 192, 214), (150, 205, 190), 16, 1.0)],        # fluorite
}
# shader = Unity legacy Reflective/Bumped Specular family: _MainTex alpha = gloss (highlight tightness). Low gloss (0.6.1: ~42)
# spread a white highlight over each bead's whole top half (chalky); high gloss + weak specular = a small tight highlight on
# polished stone, colour intact (reflections are turned down at runtime)
GLOSS = {"angelite": [215] * 8, "gems": [220, 215, 240, 215, 225, 220]}
SPEC = {"angelite": [30] * 8, "gems": [32, 30, 75, 30, 34, 32]}


def atlas(name):
    cells = PALETTES[name]; d = np.zeros((CELL * GRID, CELL * GRID, 4), np.uint8); s = np.zeros_like(d)
    for i in range(GRID * GRID):
        y, x = divmod(i, GRID); sl = (slice(y * CELL, (y + 1) * CELL), slice(x * CELL, (x + 1) * CELL))
        if i < len(cells):
            d[sl][..., :3] = cells[i]; d[sl][..., 3] = GLOSS[name][i]; s[sl] = (SPEC[name][i],) * 3 + (255,)
        else:                                          # last cell: the black elastic
            d[sl] = (18, 18, 20, 60); s[sl] = (25, 25, 25, 255)
    Image.fromarray(d).save(OUT + f"tex_beads_{name}_d.png"); Image.fromarray(s).save(OUT + f"tex_beads_{name}_s.png")
    Image.new("RGBA", (4, 4), (128, 128, 255, 255)).save(OUT + f"tex_beads_{name}_n.png")   # flat (also right for DXT5nm unpacking)
    # metallic (R) + smoothness (A) map for Unity's Standard shader (what the plugin uses for our own textures: the game's
    # Reflective shaders mirrored the scene over the beads in game - white tops, dark band): stones dielectric, hematite metallic
    mg = np.zeros_like(d)
    for i in range(GRID * GRID):
        y, x = divmod(i, GRID); sl = (slice(y * CELL, (y + 1) * CELL), slice(x * CELL, (x + 1) * CELL))
        metal = name == "gems" and i == 2
        mg[sl] = (190 if metal else 0, 0, 0, 170 if metal else (140 if i < len(cells) else 60))   # 0.76 smoothness mirrored a sharp horizon line across every bead
    Image.fromarray(mg).save(OUT + f"tex_beads_{name}_m.png")
    return len(cells)


def cell_uv(i, u, v):
    """sphere / tube u, v (0..1) into atlas cell i, 2 px inside its border"""
    y, x = divmod(i, GRID); pad = 5 / (CELL * GRID); w = 1 / GRID
    return np.c_[x * w + pad + u * (w - 2 * pad), 1 - ((y + 1) * w - pad - v * (w - 2 * pad))]


def bracelet(name, N):
    R0 = N * 2 * RB * 1.02 / (2 * np.pi)              # bead centre circle: beads just apart
    ncell = atlas(name); P, Nn, T, UV, tris, pid, kind = [], [], [], [], [], [], []; base = 0
    ax = np.array([0, 0, 1.0])
    for b in range(N):                                 # beads: UV spheres, each its own rigid piece
        th = 2 * np.pi * b / N; c = np.array([np.cos(th), np.sin(th), 0]) * R0
        cell = b % ncell if name == "gems" else rng.integers(ncell)
        nu, nv = 16, 10; uu, vv = np.meshgrid(np.linspace(0, 1, nu + 1), np.linspace(0, 1, nv + 1))
        lon = uu * 2 * np.pi + th + np.pi;   # texture seam on the bead's inner side, against the arm (it showed as a dark line)
        lat = (vv - 0.5) * np.pi
        n = np.stack([np.cos(lat) * np.cos(lon), np.cos(lat) * np.sin(lon), np.sin(lat)], -1).reshape(-1, 3)
        t = np.stack([-np.sin(lon), np.cos(lon), np.zeros_like(lon)], -1).reshape(-1, 3)
        P.append(c + n * RB); Nn.append(n); T.append(np.c_[t, np.ones(len(t))]); UV.append(cell_uv(cell, uu.ravel(), vv.ravel()))
        for j in range(nv):
            for i in range(nu):
                a0 = base + j * (nu + 1) + i; a1 = a0 + 1; b0 = a0 + nu + 1; b1 = b0 + 1
                tris += [(a0, b0, a1), (a1, b0, b1)]
        cnt = (nu + 1) * (nv + 1); pid += [b] * cnt; kind += [1] * cnt; base += cnt
    # elastic: a thin tube through the bead centres (follows the sleeve)
    ns, nseg = 6, 240; ss, aa = np.meshgrid(np.linspace(0, 1, nseg + 1), np.linspace(0, 2 * np.pi, ns + 1))
    th = ss * 2 * np.pi; cdir = np.stack([np.cos(th), np.sin(th), np.zeros_like(th)], -1)
    n = cdir * np.cos(aa)[..., None] + ax * np.sin(aa)[..., None]
    P.append((cdir * (R0 - RB * 0.75) + n * RS).reshape(-1, 3)); Nn.append(n.reshape(-1, 3))    # elastic deep in the beads (hidden)
    tg = np.stack([-np.sin(th), np.cos(th), np.zeros_like(th)], -1).reshape(-1, 3); T.append(np.c_[tg, np.ones(len(tg))])
    UV.append(cell_uv(GRID * GRID - 1, ss.ravel(), aa.ravel() / (2 * np.pi)))
    for j in range(ns):
        for i in range(nseg):
            a0 = base + j * (nseg + 1) + i; a1 = a0 + 1; b0 = a0 + nseg + 1; b1 = b0 + 1
            tris += [(a0, a1, b0), (a1, b1, b0)]
    cnt = (ns + 1) * (nseg + 1); pid += [N] * cnt; kind += [0] * cnt
    # every triangle wound to face along its vertex normals, like the game's meshes (0.6.x beads were inside out: Unity culled
    # their outer surface, so you saw into the bead - the elastic showed through as a dark line, colours looked flat)
    PP, NN, TT_ = np.vstack(P), np.vstack(Nn), np.array(tris)
    cr = np.cross(PP[TT_[:, 1]] - PP[TT_[:, 0]], PP[TT_[:, 2]] - PP[TT_[:, 0]]); bad = (cr * NN[TT_].mean(1)).sum(1) < 0
    TT_[bad] = TT_[bad][:, [0, 2, 1]]
    if name == "angelite" and N == 24: print(f"  winding: {bad.mean() * 100:.0f}% of triangles flipped to face outward")
    m = dict(name=MESH, bundle=BUNDLE, mat=0, P=PP, N=NN, T=np.vstack(T), UV=np.vstack(UV), tris=[TT_])
    pts = np.c_[np.cos(np.linspace(0, 2 * np.pi, 360, endpoint=False)), np.sin(np.linspace(0, 2 * np.pi, 360, endpoint=False)), np.zeros(360)] * R0
    curve = Curve(pts, radial_from(np.zeros(3)), True)
    gap = lambda Q: np.linalg.norm(Q[:, :2], axis=1) - (R0 - RB)    # height above the bracelet's inner circle (beads touch it)
    note = {"angelite": "Angelite bead bracelet", "gems": "Gemstone bead bracelet"}[name]
    build(f"beads_{name}" + ("" if N == 24 else f"~{N}"), [m], curve, [(np.array(pid), np.array(kind))], bundle=BUNDLE, note=note, extra=lambda c, a, r: {"texture": f"tex_beads_{name}"}, gap=gap)


for nm in ("angelite", "gems"):
    for n_ in SIZES: bracelet(nm, n_)          # beads_<name> = 24 beads (the picker's entry), beads_<name>~N = the other sizes


# loose versions (picker entries): no geometry of their own - the runtime takes a size a few steps bigger than the sleeve
# needs ("Loose bracelet size" setting), lays it out as a round loop and lets it hang / slide (BraceletPhysics)
import json
for nm, note in (("angelite", "Angelite bead bracelet (loose)"), ("gems", "Gemstone bead bracelet (loose)")):
    b = json.load(open(OUT + f"acc_beads_{nm}.json"))
    json.dump(dict(name=f"beads_{nm}_loose", note=note, bundle=b["bundle"], closed=True, L=b["L"], width=b["width"], hmid=b["hmid"],
                   texture=b["texture"], meshes=[], loose=True), open(OUT + f"acc_beads_{nm}_loose.json", "w"))
    print("loose entry", nm)
