using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WristAccessories;

// Dogtags on the kill sleeves / dogtag chains (user 2026-10-28: "customize them in a small menu dropdown with every dogtag in
// the game", incl. Unlock All Dogtags', ISB's, Black Division's, Moreman's). Every hanging tag of a worn sleeve (a mesh with
// "swing") can be swapped for another dogtag model: one plate (+ ring) per model, cut offline from the item's LOD0 mesh
// (tattoo/acc/dogtag_models.py -> acc/dogtag_models.json; the item meshes carry two tags and a ball chain and can't be read
// at runtime), in a tag frame: origin = hole end, +x toward the hole, +z = front. Material from the item's own bundle at
// runtime, so a model is only offered when its bundle is installed; names from the live item database.
static class DogTags
{
    public const int Count = 8;                                             // the kill sleeves carry 8 tags
    /// <summary>The items whose hanging pieces are dogtags (user 2026-10-29: the horse / Firefly pendants hang the same way and got Tag 1).</summary>
    public static readonly HashSet<string> Sleeves = new HashSet<string> { "predator_sleeve", "bd_sleeve", "isb_sleeve", "mixed_sleeve", "dogtag_sleeve" };
    class Geo { public Vector3[] pos, nrm; public Vector4[] tan; public Vector2[] uv; public int[] tris; public int pivot, tip; }
    class Model { public string key, mesh, label, geo; }
    static readonly Dictionary<string, Geo> Geos = new Dictionary<string, Geo>();
    static readonly List<Model> Models = new List<Model>();

    public static void Load(IEnumerable<string> accDirs)
    {
        Geos.Clear(); Models.Clear();
        foreach (var dir in accDirs)
            foreach (var f in Directory.Exists(dir) ? Directory.GetFiles(dir, "dogtag_models*.json") : new string[0])
            {
                try
                {
                    var j = JObject.Parse(File.ReadAllText(f));
                    foreach (var p in ((JObject)j["meshes"]).Properties())
                    {
                        var g = (JObject)p.Value;
                        Vector3[] V3(string k) => g[k].Select(t => new Vector3((float)t[0], (float)t[1], (float)t[2])).ToArray();
                        Geos[p.Name] = new Geo
                        {
                            pos = V3("pos"), nrm = V3("nrm"), tan = g["tan"].Select(t => new Vector4((float)t[0], (float)t[1], (float)t[2], (float)t[3])).ToArray(),
                            uv = g["uv"].Select(t => new Vector2((float)t[0], (float)t[1])).ToArray(), tris = g["tris"].ToObject<int[]>(), pivot = (int)g["pivot"], tip = (int)g["tip"],
                        };
                    }
                    foreach (var m in (JArray)j["models"])
                        if (Models.All(x => x.key != (string)m["key"])) Models.Add(new Model { key = (string)m["key"], mesh = (string)m["mesh"], label = (string)m["label"], geo = (string)m["geo"] });
                    Plugin.Log.LogInfo($"[WristAccessories] dogtags: {f} -> {Models.Count} models, {Geos.Count} tag shapes");
                }
                catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] dogtags {f}: {e.Message}"); }
            }
        _names = null;
    }

    // ---- picks: Plugin.DogTagPicks = "key1|key2|...|key8" (empty = the sleeve's own tag) ----
    static string[] Picks()
    {
        var a = (Plugin.DogTagPicks.Value ?? "").Split('|'); var r = new string[Count];
        for (int i = 0; i < Count; i++) r[i] = i < a.Length ? a[i].Trim() : "";
        return r;
    }
    static void Save(string[] p) => Plugin.DogTagPicks.Value = p.All(x => x == "") ? "" : string.Join("|", p);

    // the DOGTAGS page: ALL TAGS + one picture tile per tag ("all" or "1".."8")
    public static string ModelOf(string which)
    {
        var p = Picks();
        if (which != "all") return int.TryParse(which, out int k) && k >= 1 && k <= Count ? p[k - 1] : "";
        return p.Distinct().Count() == 1 ? p[0] : "*";
    }
    public static void SetModel(string which, string key)
    {
        if (key == "*") return;
        var p = Picks();
        if (which == "all") for (int i = 0; i < Count; i++) p[i] = key;
        else if (int.TryParse(which, out int k) && k >= 1 && k <= Count) p[k - 1] = key;
        Save(p);
        Plugin.Log.LogInfo($"[WristAccessories] dogtags: {(which == "all" ? "all tags" : "tag " + which)} -> {(key == "" ? "the sleeve's own" : key)}");
    }
    public static List<(string, string)> ModelChoices(string which)
    {
        var l = new List<(string, string)>();
        if (which == "all" && ModelOf("all") == "*") l.Add(("Different per tag", "*"));
        l.Add(("Sleeve's own", ""));
        var ms = Models.Where(m => WristAccessories.Bundles.Has(m.key)).Select(m => (name: Name(m), m)).ToList();
        // the game gives several tags the same name (every edition / prestige / story tag is "Dogtag USEC" or "Dogtag BEAR"):
        // those get their variant after the name
        var dup = new HashSet<string>(ms.GroupBy(x => x.name).Where(g => g.Count() > 1).Select(g => g.Key));
        foreach (var x in ms.Select(x => (dup.Contains(x.name) ? $"{x.name} ({Variant(x.m)})" : x.name, x.m.key)).OrderBy(x => x.Item1)) l.Add(x);
        return l;
    }
    static string Variant(Model m)
    {
        var s = m.label;                                                    // from the bundle name: "usec eod", "story 04 for humanity"
        foreach (var (a, b) in new[] { ("eod", "Edge of Darkness"), ("tue", "Unheard"), ("bear", "BEAR"), ("usec", "USEC"), ("t2", "plain"), ("blackdivision", "Black Division") })
            s = System.Text.RegularExpressions.Regex.Replace(s, $@"\b{a}\b", b);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b0(\d)\b", "$1");
        return string.Join(" ", s.Split(' ').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }

    // FLIP (user 2026-10-28): per tag, the other face outward. Plugin.DogTagFlips = 8 chars '0' / '1'
    public static bool Flipped(int k) { var f = Plugin.DogTagFlips.Value ?? ""; return k >= 0 && k < f.Length && f[k] == '1'; }
    public static void Flip(int k)
    {
        var f = (Plugin.DogTagFlips.Value ?? "").PadRight(Count, '0').ToCharArray();
        if (k < 0 || k >= Count) return;
        f[k] = f[k] == '1' ? '0' : '1'; Plugin.DogTagFlips.Value = new string(f);
        Plugin.Log.LogInfo($"[WristAccessories] dogtags: tag {k + 1} {(f[k] == '1' ? "flipped (back outward)" : "front outward")}");
    }
    public static bool IsModel(string key) => !string.IsNullOrEmpty(key) && Models.Any(m => m.key == key);

    // pictures: the game's own item icons (ItemViewFactory.GetItemSpriteAsync on a throwaway item of that template, as the
    // stash draws it), made once per model on first use; null until it is ready (the page polls while IconsPending)
    static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
    static readonly HashSet<string> IconBusy = new HashSet<string>();
    public static Sprite Icon(string key)
    {
        if (!IsModel(key)) return null;
        if (Icons.TryGetValue(key, out var sp) && sp != null && sp.texture != null) return sp;
        if (Icons.ContainsKey(key)) { Icons.Remove(key); IconBusy.Remove(key); Plugin.Log.LogWarning($"[WristAccessories] dogtags: picture for {key} was freed - made again"); }
        AccessoriesTab.IconsPending = true;
        if (IconBusy.Add(key)) MakeIcon(key);
        return null;
    }
    static async void MakeIcon(string key)
    {
        try
        {
            Name(Models.First(m => m.key == key));                              // fills the template index
            var f = Singleton<ItemFactory>.Instantiated ? Singleton<ItemFactory>.Instance : null;
            if (f == null || _tpl == null || !_tpl.TryGetValue(key, out var tpl)) { Plugin.Log.LogWarning($"[WristAccessories] dogtags: no item template for {key} - no picture"); return; }
            var item = f.CreateItem(MongoID.Generate().ToString(), tpl, null);
            var gs = await EFT.UI.DragAndDrop.ItemViewFactory.GetItemSpriteAsync(item);
            // our own copy: the game's icon cache frees / reuses its sprites (a tile went blank, user shot 2026-10-28)
            Sprite sp = null; float alpha = -1f;
            if (gs != null && gs.texture != null)
            {
                var r = gs.textureRect; int w = Mathf.Max(1, (int)r.width), h = Mathf.Max(1, (int)r.height);
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(gs.texture, rt, new Vector2(r.width / gs.texture.width, r.height / gs.texture.height), new Vector2(r.x / gs.texture.width, r.y / gs.texture.height));
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "WristAcc_dogtag_" + Path.GetFileNameWithoutExtension(key) };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DontDestroyOnLoad(tex);
                alpha = tex.GetPixels32().Average(c => c.a / 255f);
                sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            }
            Icons[key] = sp;
            Plugin.Log.LogInfo($"[WristAccessories] dogtags: picture for {key}: {(sp != null ? $"{sp.rect.width:0}x{sp.rect.height:0}, {alpha * 100:0}% covered" : "NONE")}");
        }
        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] dogtags: picture {key}: {e.Message}"); }
    }

    // the item's own name (the live item database: game + every installed mod), else our label
    static Dictionary<string, string> _names, _tpl;
    static string Name(Model m)
    {
        if (_names == null)
        {
            try
            {
                var f = Singleton<ItemFactory>.Instantiated ? Singleton<ItemFactory>.Instance : null;
                if (f != null)
                {
                    _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); _tpl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var t in f.ItemTemplates.Values)
                    {
                        var path = t.Prefab != null ? t.Prefab.path : null;
                        if (string.IsNullOrEmpty(path) || _names.ContainsKey(path) || Models.All(x => !string.Equals(x.key, path, StringComparison.OrdinalIgnoreCase))) continue;
                        var n = EFT.LocalizationExtensions.Localized(t.NameLocalizationKey, (string)null);
                        _names[path] = string.IsNullOrEmpty(n) || n == t.NameLocalizationKey ? t.Name : n; _tpl[path] = t._id.ToString();
                    }
                    Plugin.Log.LogInfo($"[WristAccessories] dogtags: {_names.Count} of {Models.Count} models named from the item database");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[WristAccessories] dogtags: item names not readable ({e.Message}) - our labels used"); _names = new Dictionary<string, string>(); _tpl = new Dictionary<string, string>(); }
        }
        return _names != null && _names.TryGetValue(m.key, out var s) && !string.IsNullOrEmpty(s) ? s : m.label;
    }

    /// <summary>The picked models' bundles, when something worn has hanging tags (loaded with the rest of the rig's bundles).</summary>
    public static IEnumerable<string> Bundles(IEnumerable<WristFit.Acc> worn)
    {
        if (!worn.Any(a => Sleeves.Contains(a.name))) return Enumerable.Empty<string>();
        return Picks().Where(k => k != "" && Models.Any(m => m.key == k) && WristAccessories.Bundles.Has(k)).Distinct();
    }

    /// <summary>Tag k (0-based, in the sleeve's order) of a placed sleeve as the picked model, or null = keep the sleeve's own.
    /// The new plate hangs where the old one did: its hole end at the old ring end, along the old plate toward its tip, front
    /// away from the arm, its back no deeper into the sleeve than the old plate's.</summary>
    /// <summary>The sleeve's own tag k turned over (FLIP without a picked model): 180 deg about its length through its centre.</summary>
    static WristFit.Built Turned(WristFit.Built b, int k, string acc)
    {
        Vector3 p0 = b.pos[b.swing[0]], p1 = b.pos[b.swing[1]];
        var c = Vector3.zero; foreach (var v in b.pos) c += v; c /= b.pos.Length;
        var q = Quaternion.AngleAxis(180f, (p0 - p1).normalized); int nv = b.pos.Length;
        var nb = new WristFit.Built { mesh = b.mesh, bundle = b.bundle, mat = b.mat, pos = new Vector3[nv], nrm = new Vector3[nv], tan = new Vector4[nv], uv = b.uv, tris = b.tris, swing = b.swing, pid = b.pid, kind = b.kind };
        for (int i = 0; i < nv; i++)
        {
            nb.pos[i] = c + q * (b.pos[i] - c); nb.nrm[i] = q * b.nrm[i];
            var t3 = q * new Vector3(b.tan[i].x, b.tan[i].y, b.tan[i].z); nb.tan[i] = new Vector4(t3.x, t3.y, t3.z, b.tan[i].w);
        }
        Plugin.Log.LogInfo($"[WristAccessories] dogtags: {acc} tag {k + 1}: own tag flipped");
        return nb;
    }

    public static WristFit.Built Swap(WristFit.Built b, int k, WristFit.Outline o, string acc)
    {
        if (k < 0 || k >= Count) return null;
        string key = Picks()[k]; if (key == "") return Flipped(k) ? Turned(b, k, acc) : null;
        var m = Models.FirstOrDefault(x => x.key == key);
        if (m == null || !Geos.TryGetValue(m.geo, out var g)) { Plugin.Log.LogWarning($"[WristAccessories] dogtags: {acc} tag {k + 1}: no shape for {key} - own tag kept"); return null; }
        if (!WristAccessories.Bundles.Has(key)) { Plugin.Log.LogInfo($"[WristAccessories] dogtags: {acc} tag {k + 1}: {key} is not installed - own tag kept"); return null; }
        Vector3 p0 = b.pos[b.swing[0]], p1 = b.pos[b.swing[1]];
        var a1 = (p0 - p1).normalized;
        // the old plate's normal: from its ring-tip line and the vertex farthest off that line
        Vector3 side = Vector3.zero; float best = 0f;
        foreach (var v in b.pos) { var d = (v - p0) - a1 * Vector3.Dot(v - p0, a1); if (d.sqrMagnitude > best) { best = d.sqrMagnitude; side = d; } }
        var n = Vector3.Cross(a1, side).normalized;
        var rel = p0 - o.w; var outward = (rel - o.ax * Vector3.Dot(rel, o.ax)).normalized;
        if (Vector3.Dot(n, outward) < 0) n = -n;                                     // front away from the arm
        if (Flipped(k)) n = -n;                                                      // FLIP: the back outward (turned about its length)
        var a2 = Vector3.Cross(n, a1);
        var c = Vector3.zero; foreach (var v in b.pos) c += v; c /= b.pos.Length;
        float oldBelow = b.pos.Max(v => Vector3.Dot(c - v, n)), newBelow = -g.pos.Min(v => v.z);
        var origin = p0 + n * (Vector3.Dot(c - p0, n) + Mathf.Max(0f, newBelow - oldBelow));
        int nv = g.pos.Length;
        var nb = new WristFit.Built
        {
            mesh = m.mesh, bundle = key, mat = 0, pos = new Vector3[nv], nrm = new Vector3[nv], tan = new Vector4[nv], uv = g.uv,
            tris = new[] { g.tris }, swing = new[] { g.pivot, g.tip }, pid = Enumerable.Repeat(b.pid[0], nv).ToArray(), kind = Enumerable.Repeat(1, nv).ToArray(),
        };
        for (int i = 0; i < nv; i++)
        {
            var q = g.pos[i]; nb.pos[i] = origin + a1 * q.x + a2 * q.y + n * q.z;
            var w = g.nrm[i]; nb.nrm[i] = (a1 * w.x + a2 * w.y + n * w.z).normalized;
            var t = g.tan[i]; var t3 = a1 * t.x + a2 * t.y + n * t.z; nb.tan[i] = new Vector4(t3.x, t3.y, t3.z, t.w == 0 ? 1 : t.w);
        }
        Plugin.Log.LogInfo($"[WristAccessories] dogtags: {acc} tag {k + 1} -> {Name(m)} ({nv} verts, lifted {Mathf.Max(0f, newBelow - oldBelow) * 1000:0.0} mm)");
        return nb;
    }
}
