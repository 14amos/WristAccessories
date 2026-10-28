using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace WristAccessories;

// The outfit watch's shadow is painted into the hands' skin texture, so it stays as a dark band when the plugin hides that
// watch (user 2026-10-26: "a dark spot"). lab/watchspot/fix3.py rebuilt the band under the strap per texture (the colour
// just past both strap edges, bridged across, with the texel's own grain) and the review renders picked the textures that
// came out right: a patch over a rect (rgb = new colour, alpha = blend). Here: a copy of the skin material with a copy of
// its texture, the patch blended in.
static class SkinShadow
{
    // keyed "<texture>#<content hash>": ISB recolours reuse the game's texture names, so one name can have several entries;
    // the probes (x, y top-down, r, g, b of the game's texture outside the patch) pick the one whose texture this is
    class Entry { public string tex, name, file, dir; public int w, h, x, y, rw, rh; public float dark; public int[][] probe; }
    static Dictionary<string, List<Entry>> _byTex;
    static readonly Dictionary<Material, Material> Cache = new Dictionary<Material, Material>();
    static readonly List<Material> Made = new List<Material>();       // oldest first: ~5 MB each, the 6 newest are kept (outfit changes)

    static Dictionary<string, List<Entry>> ByTex()
    {
        if (_byTex != null) return _byTex;
        _byTex = new Dictionary<string, List<Entry>>();
        // ours (game + ContentBackport outfits) and each loaded addon's (its mod's outfits): <folder>\watchshadow\gains.json
        foreach (var root in new[] { WristFit.Dir }.Concat(WristFit.AddonDirs))
        {
            var d = Path.Combine(root, "watchshadow"); var p = Path.Combine(d, "gains.json");
            if (!File.Exists(p)) continue;
            try
            {
                var all = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(p));
                foreach (var kv in all)
                {
                    kv.Value.file = kv.Key; kv.Value.dir = d; var t = kv.Value.tex ?? kv.Key.Split('#')[0];
                    if (!_byTex.TryGetValue(t, out var l)) _byTex[t] = l = new List<Entry>();
                    l.Add(kv.Value);
                }
                Plugin.Log.LogInfo($"[WristAccessories] watch shadow data: {all.Count} fixes from {d} ({_byTex.Count} skin texture name(s) so far)");
            }
            catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] watch shadow data {p}: {e.Message}"); }
        }
        return _byTex;
    }

    // mean |difference| of an entry's probes against the copy, upright and upside down (a render-texture copy may come flipped)
    static (float up, float flip) ProbeError(Entry e, Color32[] px)
    {
        float up = 0, fl = 0; int n = e.probe?.Length ?? 0; if (n == 0) return (0, 0);
        foreach (var pr in e.probe)
        {
            var a1 = px[(e.h - 1 - pr[1]) * e.w + pr[0]]; var a2 = px[pr[1] * e.w + pr[0]];
            up += Math.Abs(a1.r - pr[2]) + Math.Abs(a1.g - pr[3]) + Math.Abs(a1.b - pr[4]); fl += Math.Abs(a2.r - pr[2]) + Math.Abs(a2.g - pr[3]) + Math.Abs(a2.b - pr[4]);
        }
        return (up / (3 * n), fl / (3 * n));
    }

    /// <summary>The skin material without the hidden watch's baked shadow, or null (no data for its texture / turned off).</summary>
    public static Material Fixed(Material m)
    {
        if (m == null || !Plugin.CleanWatchShadow.Value || !m.HasProperty("_MainTex")) return null;
        if (Cache.TryGetValue(m, out var c)) return c;
        var src = m.GetTexture("_MainTex") as Texture2D;
        if (src == null || !ByTex().TryGetValue(src.name, out var cands)) return null;
        cands = cands.Where(x => x.w == src.width && x.h == src.height).ToList();
        if (cands.Count == 0) { Plugin.Log.LogWarning($"[WristAccessories] watch shadow: {src.name} is {src.width}x{src.height}, no fix of that size - left as is"); Cache[m] = null; return null; }
        Texture2D t = null;
        try
        {
            int W = src.width, H = src.height;
            // the game's texture is not readable: copy it through a render texture (sRGB in, sRGB out = the same bytes)
            var rt = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active; Graphics.Blit(src, rt); RenderTexture.active = rt;
            t = new Texture2D(W, H, TextureFormat.RGBA32, true, false);
            t.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            Entry e = null; bool flip = false; float best = float.MaxValue;
            foreach (var x in cands)
            {
                var (up, fl) = ProbeError(x, px);
                Plugin.Log.LogInfo($"[WristAccessories] watch shadow: {src.name} vs fix {x.file} ({x.name}): off by {up:0.0} upright, {fl:0.0} flipped");
                if (Math.Min(up, fl) < best) { best = Math.Min(up, fl); e = x; flip = fl < up; }
            }
            if (best > 24) throw new Exception($"no fix matches this texture (best off by {best:0.0}) - left as is");
            if (flip)
                for (int r = 0; r < H / 2; r++)
                    for (int x = 0; x < W; x++) { int a = r * W + x, b = (H - 1 - r) * W + x; var tmp = px[a]; px[a] = px[b]; px[b] = tmp; }
            var gt = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!gt.LoadImage(File.ReadAllBytes(Path.Combine(e.dir, e.file + ".png"))) || gt.width != e.rw || gt.height != e.rh)
            { UnityEngine.Object.Destroy(gt); throw new Exception($"patch {e.file} not loaded ({gt.width}x{gt.height}, expected {e.rw}x{e.rh})"); }
            var pp = gt.GetPixels32(); int changed = 0, y0 = H - e.y - e.rh;    // the lab's rect is top-down; both bottom-up here
            for (int ry = 0; ry < e.rh; ry++)
                for (int rx = 0; rx < e.rw; rx++)
                {
                    var q = pp[ry * e.rw + rx]; if (q.a == 0) continue;
                    int i = (y0 + ry) * W + e.x + rx; var p = px[i]; float a = q.a / 255f;
                    px[i] = new Color32((byte)(p.r + (q.r - p.r) * a + 0.5f), (byte)(p.g + (q.g - p.g) * a + 0.5f), (byte)(p.b + (q.b - p.b) * a + 0.5f), p.a); changed++;
                }
            UnityEngine.Object.Destroy(gt);
            t.SetPixels32(px); t.Apply(true); t.Compress(true); t.Apply(false, true);
            t.name = src.name + "_noWatchShadow"; t.wrapMode = src.wrapMode; t.filterMode = src.filterMode; t.anisoLevel = src.anisoLevel;
            UnityEngine.Object.DontDestroyOnLoad(t);
            c = new Material(m) { name = m.name + "_noWatchShadow" }; c.SetTexture("_MainTex", t); UnityEngine.Object.DontDestroyOnLoad(c);
            Cache[m] = c; Made.Add(m);
            while (Made.Count > 6)
            {
                var old = Made[0]; Made.RemoveAt(0);
                if (Cache.TryGetValue(old, out var oc) && oc != null) { UnityEngine.Object.Destroy(oc.GetTexture("_MainTex")); UnityEngine.Object.Destroy(oc); }
                Cache.Remove(old);
            }
            Plugin.Log.LogInfo($"[WristAccessories] watch shadow removed from {src.name} with fix {e.file} ({e.name}, {(flip ? "flipped" : "upright")} copy): {changed} texels in {e.rw}x{e.rh} at {e.x},{e.y} (band was {e.dark * 100:0}% darker)");
            return c;
        }
        catch (Exception ex) { if (t != null) UnityEngine.Object.Destroy(t); Plugin.Log.LogError($"[WristAccessories] watch shadow {src.name}: {ex.Message}"); Cache[m] = null; return null; }
    }
}
