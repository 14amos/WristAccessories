using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;

namespace WristAccessories;

// Presets (user 2026-10-27: "presets for wrist accessories and tattoos", picked: an own list on our tab, not the game's equipment
// builds): a named snapshot of the WARDROBE tab's picks in BepInEx/config/com.amos.wristaccessories.presets.json. Loading sets the
// same config entries the tab sets, so the arms are rebuilt the usual way (SettingChanged -> Reapply). What a load applies is the
// setting "Preset loads": all, the wrist (items, watch, ring, their slides / turns), the hands (other hands, gloves, tattoos) or the
// clothing (upper / lower suit you own, through the tab's picker = the game's own clothing change, saved when the screen closes).
static class Presets
{
    static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "com.amos.wristaccessories.presets.json");
    static ConfigEntry<string>[] Wrist => new[] { Plugin.LeftWrist, Plugin.RightWrist, Plugin.Watch, Plugin.WatchFace, Plugin.WatchWorn, Plugin.WatchWrist, Plugin.RingHand, Plugin.ItemAngles, Plugin.ItemSlides, Plugin.DogTagPicks, Plugin.DogTagFlips };
    static ConfigEntry<string>[] Hands => new[] { Plugin.Handwear, Plugin.TattooSizes };
    static ConfigEntry<string>[] All => Wrist.Concat(Hands).Concat(new[] { Plugin.Shoes }).ToArray();
    static string Key(ConfigEntry<string> e) => e.Definition.Key;
    public static string Last = "";

    static JObject Read()
    {
        try { return File.Exists(FilePath) ? JObject.Parse(File.ReadAllText(FilePath)) : new JObject(); }
        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] presets: {FilePath}: {e.Message}"); return new JObject(); }
    }
    static void Write(JObject o)
    {
        try { File.WriteAllText(FilePath, o.ToString()); }
        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] presets: writing {FilePath}: {e.Message}"); }
    }

    public static List<string> Names() => Read().Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public static void Save(string name)
    {
        name = (name ?? "").Trim(); if (name.Length == 0) return;
        var o = Read(); var p = new JObject(); foreach (var e in All) p[Key(e)] = e.Value ?? "";
        // the clothing (user 2026-10-27: "it didnt change my shirt"): the suits worn, by id - put back with the tab's own picker
        p["Upper"] = AccessoriesTab.SuitOf(true); p["Lower"] = AccessoriesTab.SuitOf(false);
        o[name] = p; Write(o); Last = name;
        Plugin.Log.LogInfo($"[WristAccessories] presets: saved \"{name}\" ({string.Join(", ", All.Select(e => $"{Key(e)}={(e.Value ?? "").Length} chars"))})");
    }

    public static void Load(string name)
    {
        var o = Read(); if (!(o[name] is JObject p)) { Plugin.Log.LogWarning($"[WristAccessories] presets: no preset \"{name}\""); return; }
        string mode = Plugin.PresetLoads.Value; var set = mode == "wrist" ? Wrist : mode == "hands" ? Hands : mode == "clothing" ? new ConfigEntry<string>[0] : All; int n = 0;
        foreach (var e in set) if (p[Key(e)] != null) { var v = (string)p[Key(e)]; if (e.Value != v) { e.Value = v; n++; } }
        if (mode == "all" || mode == "clothing")
            foreach (var (k, up) in new[] { ("Upper", true), ("Lower", false) })
            {
                var id = (string)p[k];
                if (string.IsNullOrEmpty(id) || id == AccessoriesTab.SuitOf(up)) continue;
                if (!AccessoriesTab.IsSuit(id)) { Plugin.Log.LogWarning($"[WristAccessories] presets: \"{name}\" {k.ToLower()} {id} is not one of your suits - left as it is"); continue; }
                AccessoriesTab.SetSuit(id); n++;
            }
        Last = name;
        Plugin.Log.LogInfo($"[WristAccessories] presets: loaded \"{name}\" ({mode}): {n} settings changed");
    }

    public static void Delete(string name)
    {
        name = (name ?? "").Trim(); var o = Read(); if (o[name] == null) return;
        o.Remove(name); Write(o); if (Last == name) Last = "";
        Plugin.Log.LogInfo($"[WristAccessories] presets: deleted \"{name}\"");
    }
}

// Dogtag presets (user 2026-10-29: "add dogtag presets, for the custom dogtag kill sleeve"): named sets of the 8 hanging tags
// (models + flips) in BepInEx/config/com.amos.wristaccessories.dogtag-presets.json, on the DOGTAGS tab - apart from the outfit
// presets, so a tag set can go onto any outfit preset
static class DogTagPresets
{
    static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "com.amos.wristaccessories.dogtag-presets.json");
    public static string Last = "";
    static JObject Read()
    {
        try { return File.Exists(FilePath) ? JObject.Parse(File.ReadAllText(FilePath)) : new JObject(); }
        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] dogtag presets: {FilePath}: {e.Message}"); return new JObject(); }
    }
    static void Write(JObject o)
    {
        try { File.WriteAllText(FilePath, o.ToString()); }
        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] dogtag presets: writing {FilePath}: {e.Message}"); }
    }
    public static List<string> Names() => Read().Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    public static void Save(string name)
    {
        name = (name ?? "").Trim(); if (name.Length == 0) return;
        var o = Read(); o[name] = new JObject { ["Dogtags"] = Plugin.DogTagPicks.Value ?? "", ["Flips"] = Plugin.DogTagFlips.Value ?? "" }; Write(o); Last = name;
        Plugin.Log.LogInfo($"[WristAccessories] dogtag presets: saved \"{name}\"");
    }
    public static void Load(string name)
    {
        if (!(Read()[name] is JObject p)) { Plugin.Log.LogWarning($"[WristAccessories] dogtag presets: no preset \"{name}\""); return; }
        Plugin.DogTagPicks.Value = (string)p["Dogtags"] ?? ""; Plugin.DogTagFlips.Value = (string)p["Flips"] ?? ""; Last = name;
        Plugin.Log.LogInfo($"[WristAccessories] dogtag presets: loaded \"{name}\"");
    }
    public static void Delete(string name)
    {
        name = (name ?? "").Trim(); var o = Read(); if (o[name] == null) return;
        o.Remove(name); Write(o); if (Last == name) Last = "";
        Plugin.Log.LogInfo($"[WristAccessories] dogtag presets: deleted \"{name}\"");
    }
}
