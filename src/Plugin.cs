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
[BepInPlugin("com.amos.wristaccessories", "WristAccessories", "1.3.0")]
// no BepInDependency on Manimal-WatchAnims (1.2.1, Stance Overhaul report): the soft dependency made BepInEx load his plugin
// before Fontaine's Stance Overhaul, and in that order high ready / low ready broke (user test 2026-10-30: broken with every WA
// part switched off, fine once only the order changed). The compat hooks him in Start instead (every plugin's Awake has run).
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<string> Accessory;   // "" = outfit default, "none", or a watch prefab path
    internal static ConfigEntry<string> TattooSizes, PresetLoads, LeftWrist, RightWrist, RingHand, Rings, Watch, WatchFace, WatchWrist, ItemAngles, Handwear, ItemSlides, Shoes, DogTagPicks, DogTagFlips, WatchWorn;
    internal static ConfigEntry<float> SleeveFit, PhysicsStrength, InnerAngle;
    internal static ConfigEntry<bool> SeamBlend, SoftInk, BraceletPhysicsOn, CleanWatchShadow, Straighten, OnScav, MatchSkinTone, WatchesInSlots, EveryShoe, DoubleSided, HandsAsSet, PerProfile;
    internal static ConfigEntry<string> QuickUnequip;
    internal static ConfigEntry<float> SkinShade, TattooDark;
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

        // the old "1. Wrist" options (Accessory = a mod watch prefab in the outfit watch's place, e.g. the Sacred Amulet bracelet; per-outfit
        // nudges) are gone (user 2026-10-29): the WARDROBE tab covers them - the Sacred Amulet is its "amulet" wrist item
        Watch = Config.Bind("2. Fitted accessories", "Watch", "", "Replace the outfit's watch: empty = the outfit's own, none, or one of the outfits' watches (watch_*, named after the outfit they come from in the WATCH row), fitted to your sleeve");
        Watch.SettingChanged += (_, __) => _reapplyAt = Time.unscaledTime + 0.2f;
        SleeveFit = Config.Bind("2. Fitted accessories", "Sleeve fit", 0.25f, new ConfigDescription("How much room accessories leave for the sleeve bulging when the wrist bends: 0 = tight on the arm at rest, 1 = clear of every wrist pose", new AcceptableValueRange<float>(0f, 1f)));
        Straighten = Config.Bind("2. Fitted accessories", "Straighten outfit bracelets", true, "Bracelets taken from outfits go straight round the arm and close (off: the slant and gaps they had on their source outfit)");
        DoubleSided = Config.Bind("2. Fitted accessories", "Draw both sides", true, "Straps and bands are drawn from both sides, so a strap standing off your sleeve does not look hollow from behind (off: one side, as the outfits draw them)");
        OnScav = Config.Bind("2. Fitted accessories", "On your scav", true, "Wear your accessories, handwear and watch picks on your scav too (off: the scav keeps its outfit as the game made it)");
        MatchSkinTone = Config.Bind("2. Fitted accessories", "Match skin tone", true, "Handwear from other outfits (hands, fingerless gloves): its skin is recoloured to your own hands' skin tone");
        SeamBlend = Config.Bind("2. Fitted accessories", "Blend the wrist seam", true, "A bare hand from another outfit: your own wrist and hand stay up to where it starts, its edge tucks under your skin and its skin takes your colour at the seam (WORK IN PROGRESS)");
        SkinShade = Config.Bind("2. Fitted accessories", "Skin shade", 0f, new ConfigDescription("Handwear skin made lighter (+) or darker (-) than the matched tone, percent", new AcceptableValueRange<float>(-50f, 50f)));
        SoftInk = Config.Bind("2. Fitted accessories", "Soft tattoo edges", true, "Tattoo ink fades out at its edges (off: hard cut-out edges, as before 1.3.0)");
        TattooDark = Config.Bind("2. Fitted accessories", "Tattoo darkness", 0f, new ConfigDescription("Tattoo ink made darker, percent (0 = as the outfit has it)", new AcceptableValueRange<float>(0f, 80f)));
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
        DogTagPicks = Config.Bind("2. Fitted accessories", "Dogtags", "", "The 8 hanging tags of the kill sleeves / dogtag chains: dogtag item bundles separated by |, empty = the sleeve's own tag (set in the WARDROBE's DOGTAGS tab)");
        DogTagFlips = Config.Bind("2. Fitted accessories", "Dogtag flips", "", "Per hanging tag (1..8): 1 = turned over, the back outward (set with FLIP on the WARDROBE's DOGTAGS tab)");
        PresetLoads = Config.Bind("2. Fitted accessories", "Preset loads", "all", new ConfigDescription("What loading a WARDROBE preset puts on: all (with your clothing), the wrist (items, watch, ring), the hands (other hands, gloves, tattoos) or the clothing (upper, lower)", new AcceptableValueList<string>("all", "wrist", "hands", "clothing")));
        TattooSizes = Config.Bind("2. Fitted accessories", "Tattoo sizes", "", "Each tattoo's size in percent over its automatic size (matched to your hand / arm): name=pct;name=pct (WARDROBE row TATTOO SIZE)");
        TattooSizes.SettingChanged += (_, __) => _reapplyAt = Time.unscaledTime + 0.3f;
        ItemSlides = Config.Bind("2. Fitted accessories", "Item slides", "", "Each item moved along the arm in mm, + toward the hand, - toward the elbow, name=mm;name=mm (the ACCESSORIES tab's SLIDE row)");
        ItemAngles = Config.Bind("2. Fitted accessories", "Item angles", "", "Each item's own turn round the arm in degrees, name=deg;name=deg (set in the ACCESSORIES tab: ANGLE OF / ANGLE)");
        WatchWorn = Config.Bind("2. Fitted accessories", "Watch worn", "outfit", new ConfigDescription("outfit: the watch sits exactly where its own outfit wears it (strap on the skin, the sleeve worn now covers it); fit: around the outside of your sleeve, face where Watch face says", new AcceptableValueList<string>("outfit", "fit")));
        WatchFace = Config.Bind("2. Fitted accessories", "Watch face", "back", new ConfigDescription("Where the face of every fitted watch sits: back of the wrist, or the inner wrist (the way some outfits wear theirs)", new AcceptableValueList<string>("back", "inner")));
        LeftWrist = Config.Bind("2. Fitted accessories", "Left wrist", "", "Game items worn on the left forearm, from the wrist toward the elbow, comma-separated: " +
            "rolex, chainlet, chain_gold, prokill, dogtag, paracord, amulet. Fitted to the outfit's sleeve (no clipping), first person only.");
        RightWrist = Config.Bind("2. Fitted accessories", "Right wrist", "", "Same for the right forearm.");
        // 1.2.0 (player requests 2026-10-29): gloves / hands picked as a pair, a quick unequip click, picks kept per profile
        HandsAsSet = Config.Bind("2. Fitted accessories", "Same on both hands", true, "Gloves or another outfit's hand picked for one hand go on the other hand too (off: each hand picked on its own)");
        QuickUnequip = Config.Bind("2. Fitted accessories", "Quick unequip click", "middle", new ConfigDescription("Clicking a WARDROBE tile with this mouse button takes off what it holds (back to your own / none)", new AcceptableValueList<string>("middle", "right", "off")));
        PerProfile = Config.Bind("2. Fitted accessories", "Picks per profile", true, "Every game profile keeps its own WARDROBE picks (off: one set of picks for every profile, as before 1.2.0)");
        RingHand = Config.Bind("2. Fitted accessories", "Gold skull ring", "none", new ConfigDescription("Ring finger of which hand", new AcceptableValueList<string>("none", "left", "right")));
        // FINGERS tab (user 2026-10-30, back from the 1.1.0 hands-view build): rings on any finger; the old RING setting (skull ring
        // on the ring finger) moves onto L4 / R4 once
        Rings = Config.Bind("2. Fitted accessories", "Rings", "", "Rings on any finger: finger=ring;... (L1 left thumb .. L5 left little finger, R1..R5; set on the WARDROBE FINGERS tab)");
        if (RingHand.Value == "left" || RingHand.Value == "right")
        {
            string fk = RingHand.Value == "left" ? "L4" : "R4";
            var l = (Rings.Value ?? "").Split(';').Where(x => x.Length > 0 && !x.StartsWith(fk + "=")).ToList(); l.Add(fk + "=fring_skullring");
            Rings.Value = string.Join(";", l);
            Log.LogInfo($"[WristAccessories] RING '{RingHand.Value}' moved to the finger rings: {fk} = gold skull ring"); RingHand.Value = "none";
        }

        var h = new Harmony("com.amos.wristaccessories");
        h.Patch(AccessTools.Method(typeof(CustomizationSolver), nameof(CustomizationSolver.GetWatchBundle)), postfix: new HarmonyMethod(typeof(Plugin), nameof(GetWatchBundlePostfix)));
        h.Patch(AccessTools.Method(typeof(PlayerBody), nameof(PlayerBody.SetWatches)), postfix: new HarmonyMethod(typeof(Plugin), nameof(SetWatchesPostfix)));
        _harmony = h;                                                       // WatchAnimsCompat hooks in Start
        AccessoriesTab.Patch(h);
        WristFit.Load(_dir);
        gameObject.AddComponent<WristRig>();
        foreach (var init in AccessTools.GetDeclaredMethods(typeof(PlayerBody)).Where(m => m.Name == "Init"))
            h.Patch(init, postfix: new HarmonyMethod(typeof(Plugin), nameof(BodyInitPostfix)));
        Log.LogInfo($"[WristAccessories] loaded: {Frames.Count} wrist frames (old 1. Wrist options removed)");
    }

    Harmony _harmony;
    void Start()
    {
        WatchAnimsCompat.TryPatch(_harmony);
        Log.LogInfo("[WristAccessories] plugin order: no dependency on Manimal-WatchAnims (Stance Overhaul fix), his compat hooked from Start");
    }

    void Update()
    {
        if (_reapplyAt > 0 && Time.unscaledTime >= _reapplyAt) { _reapplyAt = -1; _ = Reapply(); }
        HandWear.FlushTattooSize();
        ProfilePicks.Tick();
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
    // a watch picked in WARDROBE: the outfit's own moving-hands watch prefab is not put on (its case is hidden by WristRig)
    static void GetWatchBundlePostfix(MongoID templateId, ref WatchBundleInfo __result)
    {
        try
        {
            if (!string.IsNullOrEmpty(Watch.Value) && Templates != null && Templates.TryGetValue(templateId, out var tw) && tw.BodyPart == EBodyModelPart.Hands) __result = default;
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
