using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WristAccessories;

// When a piece's material does not load (the game's red ERROR placeholder, a missing or unsupported shader, no material at
// all - the magenta / invisible cases), it is drawn with the user's smiley instead (user 2026-10-27: "the default fallback
// texture in case of anything going wrong"). The picture: fallback.png in the plugin folder.
static class Fallback
{
    static Material _mat; static bool _tried;

    public static bool Bad(Material m) =>
        m == null || m.shader == null || !m.shader.isSupported || m.shader.name == "Hidden/InternalErrorShader" || string.Equals(m.name, "ERROR", StringComparison.OrdinalIgnoreCase);

    static Texture2D _tex; static bool _texTried;
    /// <summary>The smiley itself (also the picture of an item whose icon file is missing, AccessoriesTab).</summary>
    public static Texture2D Picture()
    {
        if (_texTried) return _tex;
        _texTried = true;
        var f = Path.Combine(WristFit.Dir, "fallback.png");
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
        if (!File.Exists(f) || !tex.LoadImage(File.ReadAllBytes(f))) { Plugin.Log.LogError($"[WristAccessories] fallback picture {f} not loaded"); return null; }
        tex.name = "WristAcc_fallback"; tex.wrapMode = TextureWrapMode.Repeat; UnityEngine.Object.DontDestroyOnLoad(tex);
        return _tex = tex;
    }

    static Material Mat()
    {
        if (_tried) return _mat;
        _tried = true;
        try
        {
            var tex = Picture(); if (tex == null) return null;
            // the game's own lit shader for hands and outfits first, then always-built-in ones
            var shader = new[] { "p0/Reflective/Bumped Specular SMap", "Standard", "Unlit/Transparent", "Sprites/Default" }.Select(Shader.Find).FirstOrDefault(s => s != null && s.isSupported);
            if (shader == null) { Plugin.Log.LogError("[WristAccessories] fallback: no usable shader"); return null; }
            _mat = new Material(shader) { name = "WristAcc_fallback", mainTexture = tex, color = Color.white };
            UnityEngine.Object.DontDestroyOnLoad(_mat);
            Plugin.Log.LogInfo($"[WristAccessories] fallback material ready: {tex.width}x{tex.height} picture, shader {shader.name}");
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] fallback: " + e.Message); }
        return _mat;
    }

    /// <summary>The materials with every bad one replaced by the fallback; count = how many slots the renderer needs.</summary>
    public static Material[] Fix(Material[] mats, int count, string what)
    {
        var res = new Material[Math.Max(count, 1)]; int bad = 0;
        for (int i = 0; i < res.Length; i++)
        {
            var m = mats != null && mats.Length > 0 ? mats[Math.Min(i, mats.Length - 1)] : null;
            if (Bad(m)) { bad++; m = Mat() ?? m; }
            res[i] = m;
        }
        if (bad > 0) Plugin.Log.LogWarning($"[WristAccessories] {what}: {bad} of {res.Length} material(s) did not load ({(mats == null || mats.Length == 0 ? "none found" : string.Join(", ", mats.Where(Bad).Select(m => m == null ? "null" : $"{m.name}/{m.shader?.name}")))}) - drawn with the fallback picture");
        return res;
    }
}
