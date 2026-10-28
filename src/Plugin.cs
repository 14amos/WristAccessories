using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using Diz.Resources;
using Diz.Skinning;
using EFT;
using EFT.Customization;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Reflection.Utils;
using UnityEngine;

namespace WristAccessories;

// Wrist accessories for any outfit (first person). Every watch in the game hangs off the hands outfit through
// CustomizationSolver.GetWatchBundle(handsId) - used both by the bundle loaders and by PlayerBody.SetWatches - so one
// postfix there swaps in the accessory picked in F12. Fit on hands it wasn't made for: the accessory's placement is
// taken relative to the wrist frame of the hands it ships with and re-applied to the worn hands' wrist frame
// (wrist_frames.json, measured offline from every hands mesh in LForearm3 bone space), scaled by the wrist radius ratio.
[BepInPlugin("com.amos.wristaccessories", "WristAccessories", "1.0.4")]
[BepInDependency("com.manimal.watchanims", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<string> Accessory;   // "" = outfit default, "none", or a watch prefab path
    internal static ConfigEntry<string> PresetLoads, LeftWrist, RightWrist, RingHand, Watch, WatchFace, WatchWrist, ItemAngles, Handwear, ItemSlides, Shoes;
    internal static ConfigEntry<float> SleeveFit, PhysicsStrength, InnerAngle;
    internal static ConfigEntry<bool> SeamBlend, BraceletPhysicsOn, CleanWatchShadow, Straighten, OnScav, MatchSkinTone, WatchesInSlots, EveryShoe;
    internal static ConfigEntry<float> SkinShade;
    internal static ConfigEntry<int> LooseSize;   // Watch: "" = the outfit's own, "none", or a watch_* layout   // fitted accessories (WristFit): names from wrist toward elbow
    static string _dir, _nudgePath;
    internal static Dictionary<string, Frame> Frames = new();
    internal static Dictionary<string, Nudge> Nudges = new();
    internal static List<Acc> Catalog;
    static readonly Dictionary<string, float> ScaleByPose = new();
    static readonly HashSet<string> Logged = new();
    static readonly FieldInfo WatchesField = AccessTools.Field(typeof(PlayerBody), "_watches");
    static Plugin _inst;
    float _reapplyAt = -1, _saveAt = -1;

    public class Frame { public float[] o, ax, up; public float r; public string name; }
    public class Nudge { public float px, py, pz, rx, ry, rz, s = 1f; }
    public class Acc { public string Path, Name, RefHands; public Vector3 RefPos, RefRot; public bool Bracelet; }

    void Awake()
    {
        _inst = this; Log = Logger;
        _dir = Path.GetDirectoryName(Info.Location);
        _nudgePath = Path.Combine(BepInEx.Paths.ConfigPath, "com.amos.wristaccessories.nudges.json");
        try { Frames = JsonConvert.DeserializeObject<Dictionary<string, Frame>>(File.ReadAllText(Path.Combine(_dir, "wrist_frames.json"))); }
        catch (Exception e) { Log.LogError("[WristAccessories] wrist_frames.json: " + e.Message); }
        try { if (File.Exists(_nudgePath)) Nudges = JsonConvert.DeserializeObject<Dictionary<string, Nudge>>(File.ReadAllText(_nudgePath)) ?? new(); }
        catch (Exception e) { Log.LogError("[WristAccessories] nudges: " + e.Message); }

        Enabled = Config.Bind("1. Wrist", "Enabled", true, new ConfigDescription("Use the accessory picked below instead of the outfit's own watch.", null, new ConfigurationManagerAttributes { Order = 3 }));
        Accessory = Config.Bind("1. Wrist", "Accessory", "", new ConfigDescription("Watch / bracelet on the left wrist, for every outfit (first person only, like the game's own watches).", null,
            new ConfigurationManagerAttributes { Order = 2, CustomDrawer = DrawAccessory, HideDefaultButton = true }));
        Config.Bind("1. Wrist", "Fit for the hands you wear", "", new ConfigDescription("Nudge the accessory on the outfit you are wearing now (saved per outfit).", null,
            new ConfigurationManagerAttributes { Order = 1, CustomDrawer = DrawNudge, HideDefaultButton = true }));
        Watch = Config.Bind("2. Fitted accessories", "Watch", "", "Replace the outfit's watch: empty = the outfit's own, none, or one of the outfits' watches (watch_*, named after the outfit they come from in the WATCH row), fitted to your sleeve");
        Watch.SettingChanged += (_, __) => _reapplyAt = Time.unscaledTime + 0.2f;
        SleeveFit = Config.Bind("2. Fitted accessories", "Sleeve fit", 0.25f, new ConfigDescription("How much room accessories leave for the sleeve bulging when the wrist bends: 0 = tight on the arm at rest, 1 = clear of every wrist pose", new AcceptableValueRange<float>(0f, 1f)));
        Straighten = Config.Bind("2. Fitted accessories", "Straighten outfit bracelets", true, "Bracelets taken from outfits go straight round the arm and close (off: the slant and gaps they had on their source outfit)");
        OnScav = Config.Bind("2. Fitted accessories", "On your scav", true, "Wear your accessories, handwear and watch picks on your scav too (off: the scav keeps its outfit as the game made it)");
        MatchSkinTone = Config.Bind("2. Fitted accessories", "Match skin tone", true, "Handwear from other outfits (hands, fingerless gloves): its skin is recoloured to your own hands' skin tone");
        SeamBlend = Config.Bind("2. Fitted accessories", "Blend the wrist seam", true, "A bare hand from another outfit: your own wrist and hand stay up to where it starts, its edge tucks under your skin and its skin takes your colour at the seam (WORK IN PROGRESS)");
        SkinShade = Config.Bind("2. Fitted accessories", "Skin shade", 0f, new ConfigDescription("Handwear skin made lighter (+) or darker (-) than the matched tone, percent", new AcceptableValueRange<float>(-50f, 50f)));
        CleanWatchShadow = Config.Bind("2. Fitted accessories", "Clean skin under a replaced watch", true, "When the outfit's own watch is hidden, take its shadow (painted into the skin texture) off the wrist");
        BraceletPhysicsOn = Config.Bind("3. Bracelet physics", "Enabled", true, "Loose bracelets (the \"(loose)\" entries) hang and slide on the arm");
        PhysicsStrength = Config.Bind("3. Bracelet physics", "Strength", 1f, new ConfigDescription("How strongly gravity and arm swings move loose bracelets", new AcceptableValueRange<float>(0f, 2f)));
        LooseSize = Config.Bind("3. Bracelet physics", "Loose bracelet size v2", 0, new ConfigDescription("Loose bracelets: 0 = just big enough to go round the arm and hang; each step = 2 beads bigger", new AcceptableValueRange<int>(0, 6)));
        WatchesInSlots = Config.Bind("2. Fitted accessories", "Watches in wrist slots", false, "Lets the LEFT / RIGHT 1-5 slots hold watches too (more than one watch per arm); off = watches only in the WATCH slot");
        EveryShoe = Config.Bind("2. Fitted accessories", "Every shoe", false, "SHOES list: also the shoes only bots wear (zombies, bosses, scavs, guards); off = shoes from suits players can get");
        WatchWrist = Config.Bind("2. Fitted accessories", "Watch wrist", "left", new ConfigDescription("Which wrist the picked watch is on: left, right or both", new AcceptableValueList<string>("left", "right", "both")));
        InnerAngle = Config.Bind("2. Fitted accessories", "Inner wrist angle", -161f, new ConfigDescription("Where an inner-wrist watch sits round the arm, degrees from the back of the hand (the game's inner watches: -140..-175)", new AcceptableValueRange<float>(-215f, -100f)));
        Handwear = Config.Bind("2. Fitted accessories", "Handwear", "", "Gloves, other outfits' hands and hand tape on your hands (comma list of handwear names, set in the ACCESSORIES tab's HANDS row)");
        Shoes = Config.Bind("2. Fitted accessories", "Shoes", "", "Another outfit's shoes on your feet (a shoes_* name, set in the ACCESSORIES tab's SHOES tile); empty = your own");
        PresetLoads = Config.Bind("2. Fitted accessories", "Preset loads", "all", new ConfigDescription("What loading a WARDROBE preset puts on: all (with your clothing), the wrist (items, watch, ring), the hands (other hands, gloves, tattoos) or the clothing (upper, lower)", new AcceptableValueList<string>("all", "wrist", "hands", "clothing")));
        ItemSlides = Config.Bind("2. Fitted accessories", "Item slides", "", "Each item moved along the arm in mm, + toward the hand, - toward the elbow, name=mm;name=mm (the ACCESSORIES tab's SLIDE row)");
        ItemAngles = Config.Bind("2. Fitted accessories", "Item angles", "", "Each item's own turn round the arm in degrees, name=deg;name=deg (set in the ACCESSORIES tab: ANGLE OF / ANGLE)");
        WatchFace = Config.Bind("2. Fitted accessories", "Watch face", "back", new ConfigDescription("Where the face of every fitted watch sits: back of the wrist, or the inner wrist (the way some outfits wear theirs)", new AcceptableValueList<string>("back", "inner")));
        LeftWrist = Config.Bind("2. Fitted accessories", "Left wrist", "", "Game items worn on the left forearm, from the wrist toward the elbow, comma-separated: " +
            "rolex, chainlet, chain_gold, prokill, dogtag, paracord, amulet. Fitted to the outfit's sleeve (no clipping), first person only.");
        RightWrist = Config.Bind("2. Fitted accessories", "Right wrist", "", "Same for the right forearm.");
        RingHand = Config.Bind("2. Fitted accessories", "Gold skull ring", "none", new ConfigDescription("Ring finger of which hand", new AcceptableValueList<string>("none", "left", "right")));
        // 1.1.0 test builds kept the skull ring as a finger ring (setting "Rings", hands view - removed, user 2026-10-27): back to RING
        var oldRings = Config.Bind("2. Fitted accessories", "Rings", "", "(1.1.0 test builds only, not used)");
        if (!string.IsNullOrEmpty(oldRings.Value))
        {
            if ((RingHand.Value ?? "none") == "none") { if (oldRings.Value.Contains("L4=fring_skullring")) RingHand.Value = "left"; else if (oldRings.Value.Contains("R4=fring_skullring")) RingHand.Value = "right"; }
            Log.LogInfo($"[WristAccessories] old finger rings '{oldRings.Value}' dropped (hands view removed); RING = {RingHand.Value}"); oldRings.Value = "";
        }
        Enabled.SettingChanged += (_, __) => _reapplyAt = Time.unscaledTime + 0.2f;
        Accessory.SettingChanged += (_, __) => _reapplyAt = Time.unscaledTime + 0.2f;

        var h = new Harmony("com.amos.wristaccessories");
        h.Patch(AccessTools.Method(typeof(CustomizationSolver), nameof(CustomizationSolver.GetWatchBundle)), postfix: new HarmonyMethod(typeof(Plugin), nameof(GetWatchBundlePostfix)));
        h.Patch(AccessTools.Method(typeof(PlayerBody), nameof(PlayerBody.SetWatches)), postfix: new HarmonyMethod(typeof(Plugin), nameof(SetWatchesPostfix)));
        WatchAnimsCompat.TryPatch(h);
        AccessoriesTab.Patch(h);
        WristFit.Load(_dir);
        gameObject.AddComponent<WristRig>();
        foreach (var init in AccessTools.GetDeclaredMethods(typeof(PlayerBody)).Where(m => m.Name == "Init"))
            h.Patch(init, postfix: new HarmonyMethod(typeof(Plugin), nameof(BodyInitPostfix)));
        Log.LogInfo($"[WristAccessories] loaded: {Frames.Count} wrist frames, {Nudges.Count} saved nudges, accessory '{Accessory.Value}'");
    }

    void Update()
    {
        if (_reapplyAt > 0 && Time.unscaledTime >= _reapplyAt) { _reapplyAt = -1; _ = Reapply(); }
        if (_saveAt > 0 && Time.unscaledTime >= _saveAt) { _saveAt = -1; try { File.WriteAllText(_nudgePath, JsonConvert.SerializeObject(Nudges, Formatting.Indented)); } catch (Exception e) { Log.LogError("[WristAccessories] save: " + e.Message); } }
    }

    // UI character previews (PlayerModelLoader: customization tab, Ragman, character screen) get the fitted accessories
    // on their third-person body; players and corpses don't (first person only, like the game's watches)
    static void BodyInitPostfix(PlayerBody __instance)
    {
        try
        {
            if (__instance == null || __instance.GetComponentInParent<Player>() != null || __instance.GetComponentInParent<EFT.Interactive.Corpse>() != null) return;
            var rig = __instance.GetComponent<WristRig>() ?? __instance.gameObject.AddComponent<WristRig>();
            rig.Preview = __instance;
        }
        catch (Exception e) { Log.LogError("[WristAccessories] preview: " + e.Message); }
    }

    // ---- catalog: every watch prefab the outfits use (game + mods), with the hands it ships on ----
    static Dictionary<MongoID, CustomizationClothing> Templates =>
        Singleton<CustomizationSolver>.Instantiated ? Singleton<CustomizationSolver>.Instance._customizationClothingTemplates : null;

    internal static void EnsureCatalogPublic() => EnsureCatalog();

    static void EnsureCatalog()
    {
        if (Catalog != null && Catalog.Count > 0) return;
        var t = Templates; if (t == null) return;
        Catalog = new List<Acc>();
        foreach (var kv in t)
        {
            var c = kv.Value; string p = c.WatchPrefab?.path;
            if (c.BodyPart != EBodyModelPart.Hands || string.IsNullOrEmpty(p)) continue;
            // the game's own watch bundles hold only the dial hands - the case/strap is part of each outfit's hands mesh,
            // so they would float on other outfits: only self-contained accessories (mod bundles) are offered
            if (p.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) continue;
            string id = kv.Key.ToString();
            var a = Catalog.FirstOrDefault(x => x.Path == p);
            if (a == null) { a = new Acc { Path = p, Name = Pretty(p), Bracelet = p.IndexOf("bracelet", StringComparison.OrdinalIgnoreCase) >= 0 }; Catalog.Add(a); }
            if (a.RefHands == null || (!Frames.ContainsKey(a.RefHands) && Frames.ContainsKey(id)))
            { a.RefHands = id; a.RefPos = c.WatchPosition; a.RefRot = c.WatchRotation; }
        }
        Catalog = Catalog.OrderBy(a => a.Name).ToList();
        foreach (var a in Catalog) Log.LogInfo($"[WristAccessories] catalog: {a.Name} ({a.Path}) ref hands {a.RefHands} frame {Frames.ContainsKey(a.RefHands)}");
    }

    static string Pretty(string path)
    {
        string f = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        if (f.Contains("amulet_bracelet")) return "Sacred Amulet bracelet";
        if (f.Contains("traserp66")) return "Traser P66";
        if (f.Contains("luminoxrecon")) return "Luminox Recon";
        if (f.Contains("garmintactix")) return "Garmin Tactix";
        if (f.Contains("rolex")) return "Rolex GMT-Master II";
        if (f == "bear_watch") return "BEAR watch";
        return f.Replace('_', ' ');
    }

    // ---- the swap ----
    static void GetWatchBundlePostfix(MongoID templateId, ref WatchBundleInfo __result)
    {
        try
        {
            if (!Enabled.Value) return;
            if (!string.IsNullOrEmpty(Watch.Value) && Templates != null && Templates.TryGetValue(templateId, out var tw) && tw.BodyPart == EBodyModelPart.Hands) { __result = default; return; }
            string sel = Accessory.Value ?? "", hid = templateId.ToString();
            var t = Templates; if (t == null || !t.TryGetValue(templateId, out var tpl) || tpl.BodyPart != EBodyModelPart.Hands) return;
            if (sel == "none") { __result = default; return; }
            Acc acc = null;
            if (sel != "") { EnsureCatalog(); acc = Catalog?.FirstOrDefault(a => a.Path == sel); if (acc == null) return; }
            if (acc == null && !__result.HasValidPath()) return;          // outfit default and the outfit has no watch
            string path = acc?.Path ?? __result.WatchPrefab.path;
            Vector3 pos; Quaternion rot; float scale = 1f; string how;
            Frames.TryGetValue(hid, out var fh);
            if (acc == null || tpl.WatchPrefab?.path == path) { pos = tpl.WatchPosition; rot = Quaternion.Euler(tpl.WatchRotation); how = "outfit's own placement"; }
            else if (fh != null && Frames.TryGetValue(acc.RefHands ?? "", out var fr))
            {
                Quaternion qr = Q(fr), qh = Q(fh); float s = fh.r / fr.r;
                Vector3 rel = Quaternion.Inverse(qr) * (acc.RefPos - V(fr.o));
                pos = V(fh.o) + qh * (rel * s); rot = qh * (Quaternion.Inverse(qr) * Quaternion.Euler(acc.RefRot));
                if (acc.Bracelet) scale = s;
                how = $"wrist fit from {fr.name} (r {fr.r:0.000}) to {fh.name} (r {fh.r:0.000})";
            }
            else if (!string.IsNullOrEmpty(tpl.WatchPrefab?.path)) { pos = tpl.WatchPosition; rot = Quaternion.Euler(tpl.WatchRotation); how = "outfit's watch spot (no wrist frame)"; }
            else { pos = acc.RefPos; rot = Quaternion.Euler(acc.RefRot); how = "accessory's own placement (no wrist frame)"; }
            if (Nudges.TryGetValue(hid, out var n))
            {
                Quaternion basis = fh != null ? Q(fh) : Quaternion.identity;
                pos += basis * new Vector3(n.px, n.py, n.pz) * 0.001f;
                rot = rot * Quaternion.Euler(n.rx, n.ry, n.rz); scale *= n.s <= 0 ? 1f : n.s;
            }
            __result = new WatchBundleInfo { WatchPrefab = new ResourceKey { path = path, rcid = "" }, WatchPosition = pos, WatchRotation = rot.eulerAngles };
            ScaleByPose[Key(pos)] = scale;
            if (Logged.Add(hid + path + Key(pos))) Log.LogInfo($"[WristAccessories] hands {hid}: {Pretty(path)}, {how}, pos {pos.ToString("F4")} rot {rot.eulerAngles.ToString("F1")} scale {scale:0.00}");
        }
        catch (Exception e) { Log.LogError("[WristAccessories] GetWatchBundle: " + e); }
    }

    static void SetWatchesPostfix(PlayerBody __instance, WatchBundleInfo watchBundleInfo)
    {
        var w = WatchesField.GetValue(__instance) as PluggableBone;
        if (w != null && ScaleByPose.TryGetValue(Key(watchBundleInfo.WatchPosition), out float s)) w.transform.localScale = Vector3.one * s;
    }

    static string Key(Vector3 v) => v.ToString("F5");
    static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);
    static Quaternion Q(Frame f) => Quaternion.LookRotation(V(f.ax), V(f.up));

    // ---- live re-apply on your own player (raid) ----
    static async Task Reapply()
    {
        try
        {
            var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            if (player == null) { Log.LogInfo("[WristAccessories] changed; in the menu it shows the next time the character is loaded (raid, hideout, character screen)"); return; }
            var body = player.PlayerBody;
            if (WatchesField.GetValue(body) is PluggableBone old && old) { Destroy(old.gameObject); WatchesField.SetValue(body, null); }
            var hands = player.Profile.Customization[EBodyModelPart.Hands];
            var info = Singleton<CustomizationSolver>.Instance.GetWatchBundle(hands);
            if (!info.HasValidPath()) { Log.LogInfo("[WristAccessories] re-applied: no accessory"); return; }
            await Singleton<IEasyAssets>.Instance.RetainSeparateTask(new[] { info.WatchPrefab.path });
            body.SetWatches(info, body.SkeletonHands);
            if (WatchesField.GetValue(body) is PluggableBone w && w) w.gameObject.SetActive(player.PointOfView == EPointOfView.FirstPerson);
            Log.LogInfo($"[WristAccessories] re-applied in raid: {Pretty(info.WatchPrefab.path)}");
        }
        catch (Exception e) { Log.LogError("[WristAccessories] re-apply: " + e); }
    }

    // ---- F12 drawers ----
    static string CurrentHands(out string name)
    {
        name = "?";
        try
        {
            var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            var prof = player != null ? player.Profile : PatchConstants.BackEndSession?.Profile;
            if (prof == null) return null;
            string id = prof.Customization[EBodyModelPart.Hands].ToString();
            name = Frames.TryGetValue(id, out var f) ? f.name.Replace(".skin.bundle", "").Replace(".bundle", "") : id;
            return id;
        }
        catch { return null; }
    }

    static void DrawAccessory(ConfigEntryBase _)
    {
        EnsureCatalog();
        GUILayout.BeginVertical();
        Choice("Outfit default", ""); Choice("None", "none");
        if (Catalog == null || Catalog.Count == 0) GUILayout.Label("(catalog appears after the profile has loaded)");
        else foreach (var a in Catalog) Choice(a.Name, a.Path);
        GUILayout.EndVertical();
    }

    static void Choice(string label, string value)
    {
        bool on = (Accessory.Value ?? "") == value;
        if (GUILayout.Toggle(on, label, GUI.skin.button) && !on) Accessory.Value = value;
    }

    static void DrawNudge(ConfigEntryBase _)
    {
        string id = CurrentHands(out string name);
        GUILayout.BeginVertical();
        if (id == null) { GUILayout.Label("(no profile loaded yet)"); GUILayout.EndVertical(); return; }
        GUILayout.Label($"Outfit hands: {name}" + (Frames.ContainsKey(id) ? "" : "  (no wrist frame: offsets are in bone space)"));
        if (!Nudges.TryGetValue(id, out var n)) n = new Nudge();
        bool ch = false;
        ch |= Slider("Along arm (mm)", ref n.pz, -60, 60); ch |= Slider("Up / down (mm)", ref n.py, -30, 30); ch |= Slider("Sideways (mm)", ref n.px, -30, 30);
        ch |= Slider("Twist around arm (deg)", ref n.rz, -180, 180); ch |= Slider("Tilt (deg)", ref n.rx, -45, 45); ch |= Slider("Turn (deg)", ref n.ry, -45, 45);
        ch |= Slider("Size", ref n.s, 0.5f, 1.6f);
        if (GUILayout.Button("Reset this outfit")) { Nudges.Remove(id); _inst._saveAt = Time.unscaledTime + 0.5f; _inst._reapplyAt = Time.unscaledTime + 0.2f; }
        else if (ch) { Nudges[id] = n; _inst._saveAt = Time.unscaledTime + 1f; _inst._reapplyAt = Time.unscaledTime + 0.3f; }
        GUILayout.EndVertical();
    }

    static bool Slider(string label, ref float v, float min, float max)
    {
        GUILayout.BeginHorizontal(); GUILayout.Label(label, GUILayout.Width(170));
        float nv = GUILayout.HorizontalSlider(v, min, max, GUILayout.ExpandWidth(true)); GUILayout.Label(nv.ToString("0.0"), GUILayout.Width(45)); GUILayout.EndHorizontal();
        nv = Mathf.Round(nv * 10f) / 10f; if (Mathf.Approximately(nv, v)) return false; v = nv; return true;
    }
}

// Read by BepInEx ConfigurationManager (matched by type name)
internal sealed class ConfigurationManagerAttributes
{
    public int? Order;
    public bool? HideDefaultButton;
    public Action<ConfigEntryBase> CustomDrawer;
}
