using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Configuration;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WristAccessories;

// Our own ACCESSORIES tab on the character screen (InventoryScreen: OVERALL / GEAR / HEALTH / ... tabs), user 2026-10-26 (no
// other mod's code). The tab button is a copy of the game's own last tab with our label, added to the screen's TabGroup; the
// page is our own uGUI: one dropdown per slot, the closed dropdown showing what is picked. Every pick writes the same setting
// F12 shows, which applies live.
static class AccessoriesTab
{
    static readonly FieldInfo TabsF = AccessTools.Field(typeof(InventoryScreen), "_tabs");
    static readonly FieldInfo DictF = AccessTools.Field(typeof(InventoryScreen), "_tabDictionary");

    public static void Patch(Harmony h)
    {
        var show = typeof(InventoryScreen).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == "Show" && m.GetParameters().Length == 10);
        var close = AccessTools.Method(typeof(InventoryScreen.InventoryScreenController), "CloseAction");
        if (show == null || close == null || TabsF == null || DictF == null)
        { Plugin.Log.LogWarning($"[WristAccessories] ACCESSORIES tab: game changed (Show {show != null}, CloseAction {close != null}, _tabs {TabsF != null}, _tabDictionary {DictF != null}) - picks in F12 only"); return; }
        h.Patch(show, postfix: new HarmonyMethod(typeof(AccessoriesTab), nameof(ShowPostfix)));
        // our tab INTO the screen's tab dictionary before the game builds its TabGroup from it (1.0.4, user 2026-10-28: without
        // Improved Customization UI a press on WARDROBE did nothing - the page never opened; added to the group afterwards it only
        // worked when ICUI had rebuilt the dictionary). The game's own way a tab is part of the row
        var show1 = AccessTools.Method(typeof(InventoryScreen), "Show", new[] { typeof(InventoryScreen.InventoryScreenController) });
        if (show1 != null) h.Patch(show1, prefix: new HarmonyMethod(typeof(AccessoriesTab), nameof(ShowPrefix)));
        else Plugin.Log.LogWarning("[WristAccessories] WARDROBE tab: InventoryScreen.Show(controller) not found - tab added to the row afterwards");
        h.Patch(close, prefix: new HarmonyMethod(typeof(AccessoriesTab), nameof(ClosePrefix)));
        Plugin.Log.LogInfo("[WristAccessories] ACCESSORIES tab hooked into the character screen");
    }

    internal const EInventoryTab WardrobeKey = (EInventoryTab)50;
    static void ShowPrefix(InventoryScreen __instance)
    {
        try
        {
            var dict = (IReadOnlyDictionary<EInventoryTab, Tab>)DictF.GetValue(__instance); if (dict == null) return;
            var host = __instance.GetComponent<Host>() ?? __instance.gameObject.AddComponent<Host>();
            var tab = host.Ensure(__instance, dict); if (tab == null) return;
            if (dict.TryGetValue(WardrobeKey, out var have) && have == tab) return;
            var d = dict.Where(kv => kv.Key != WardrobeKey).ToDictionary(kv => kv.Key, kv => kv.Value); d[WardrobeKey] = tab;
            DictF.SetValue(__instance, d);
            Plugin.Log.LogInfo($"[WristAccessories] WARDROBE tab registered in the screen's tabs ({d.Count}: {string.Join(", ", d.Values.Select(t => t != null ? t.name : "null"))})");
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] WARDROBE tab register: " + e); }
    }

    static void ShowPostfix(InventoryScreen __instance)
    {
        try
        {
            var host = __instance.GetComponent<Host>() ?? __instance.gameObject.AddComponent<Host>();
            host.Attach(__instance, (TabGroup)TabsF.GetValue(__instance), (IReadOnlyDictionary<EInventoryTab, Tab>)DictF.GetValue(__instance));
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] ACCESSORIES tab: " + e); }
    }

    // the game remembers the open tab by its index in its own tab list: ours is not in it, so it hands GEAR back
    static void ClosePrefix(InventoryScreen.InventoryScreenController __instance)
    {
        try
        {
            var scr = __instance.Screen; if (scr == null) return;
            var host = scr.GetComponent<Host>(); var g = (TabGroup)TabsF.GetValue(scr);
            if (host != null && g != null && host.Tab != null && g._selectedTab == host.Tab)
            {
                g._selectedTab = ((IReadOnlyDictionary<EInventoryTab, Tab>)DictF.GetValue(scr))[EInventoryTab.Gear];
                host.Hide();
            }
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] ACCESSORIES tab close: " + e.Message); }
    }

    // ---- the slots ----
    interface IBox { void UpdateLabel(); }
    class Slot { public string Caption, Group, Tip; public Func<List<(string label, string value)>> Choices; public Func<string> Get; public Action<string> Set; public IBox Box; }

    // up to 5 accessories per wrist (user 2026-10-26: BEAR Vacation's five bracelets were split apart), wrist first
    const int PerWrist = 5;
    static List<Slot> Slots()
    {
        var l = new List<Slot>();
        for (int i = 0; i < PerWrist; i++) { int k = i; l.Add(new Slot { Group = "L", Caption = $"LEFT {k + 1}", Choices = FittedChoices, Get = () => SlotOf(Plugin.LeftWrist, k), Set = v => SetSlot(Plugin.LeftWrist, k, v) }); }
        for (int i = 0; i < PerWrist; i++) { int k = i; l.Add(new Slot { Group = "R", Caption = $"RIGHT {k + 1}", Choices = FittedChoices, Get = () => SlotOf(Plugin.RightWrist, k), Set = v => SetSlot(Plugin.RightWrist, k, v) }); }
        l.Add(new Slot { Group = "W", Caption = "WATCH", Choices = WatchChoices, Get = () => Plugin.Watch.Value ?? "", Set = v => Plugin.Watch.Value = v });
        l.Add(new Slot { Group = "W", Caption = "RING", Choices = () => new List<(string, string)> { ("None", "none"), ("Gold skull ring, left hand", "left"), ("Gold skull ring, right hand", "right") }, Get = () => Plugin.RingHand.Value ?? "none", Set = v => Plugin.RingHand.Value = v });
        l.Add(new Slot { Group = "H", Caption = "LEFT HAND", Choices = () => HandChoices("L"), Get = () => HandOf("L"), Set = v => SetHand("L", v) });
        l.Add(new Slot { Group = "H", Caption = "RIGHT HAND", Choices = () => HandChoices("R"), Get = () => HandOf("R"), Set = v => SetHand("R", v) });
        for (int i = 0; i < 2; i++) { int k = i; l.Add(new Slot { Group = "H", Caption = $"HAND {k + 1}", Choices = PieceChoices, Get = () => PieceOf(k), Set = v => SetPiece(k, v) }); }
        l.Add(new Slot { Group = "T", Caption = "UPPER", Tip = "Your upper body clothing (the suits you own). Saved by the game when you close this screen, like its own clothing picker. Not in raid.", Choices = () => SuitChoices(true), Get = () => SuitOf(true), Set = v => SetSuit(v) });
        l.Add(new Slot { Group = "T", Caption = "LOWER", Tip = "Your lower body clothing (the suits you own). Saved by the game when you close this screen, like its own clothing picker. Not in raid.", Choices = () => SuitChoices(false), Get = () => SuitOf(false), Set = v => SetSuit(v) });
        l.Add(new Slot { Group = "T", Caption = "SHOES", Tip = "Another outfit's shoes on your feet: they bend with your feet, your own shoes are tucked inside. Kept whatever LOWER you wear.", Choices = ShoeChoices, Get = () => HandWear.Items.ContainsKey(Plugin.Shoes.Value ?? "") ? Plugin.Shoes.Value : "", Set = v => Plugin.Shoes.Value = v });
        // presets (Presets.cs): load (all / wrist / hands, LOAD WHAT), save the current picks under a name, delete
        l.Add(new Slot { Group = "O", Caption = "PRESET", Tip = "Load a saved preset: your clothing, wrist items, watch, ring, hands, tattoos and shoes as you saved them (LOAD WHAT picks which part).", Choices = () => new List<(string, string)> { ("Load a preset...", "") }.Concat(Presets.Names().Select(n => (n, n))).ToList(), Get = () => Presets.Last, Set = v => { if (!string.IsNullOrEmpty(v)) Presets.Load(v); } });
        l.Add(new Slot { Group = "O", Caption = "LOAD WHAT", Tip = "What loading a preset puts on.", Choices = () => new List<(string, string)> { ("Everything (with your clothing)", "all"), ("Wrist only (items, watch, ring)", "wrist"), ("Hands and tattoos only", "hands"), ("Clothing only (upper, lower)", "clothing") }, Get = () => Plugin.PresetLoads.Value, Set = v => Plugin.PresetLoads.Value = v });
        l.Add(new Slot { Group = "P", Caption = "SAVE AS", Tip = "Type a name, SAVE stores what you wear now (same name = replaced), DELETE removes the preset with that name." });
        l.Add(new Slot { Group = "S", Caption = "WATCH ON", Tip = "Which wrist the watch is on. BOTH puts the same watch on each wrist.", Choices = () => new List<(string, string)> { ("LEFT", "left"), ("RIGHT", "right"), ("BOTH", "both") }, Get = () => (Plugin.WatchWrist.Value ?? "left").ToLowerInvariant(), Set = v => Plugin.WatchWrist.Value = v });
        l.Add(new Slot { Group = "O", Caption = "WATCH FACE", Tip = "Where the watch face sits: on the back of the wrist, or on the inside like some outfits wear it.", Choices = () => new List<(string, string)> { ("Back of the wrist", "back"), ("Inner wrist", "inner") }, Get = () => Plugin.WatchFace.Value ?? "back", Set = v => Plugin.WatchFace.Value = v });
        l.Add(new Slot { Group = "O", Caption = "ADJUST", Tip = "Pick one of the things you wear, then turn it round your arm with ANGLE or move it along your arm with SLIDE.", Choices = WornChoices, Get = () => AngleItem(), Set = v => _angleItem = v });
        l.Add(new Slot { Group = "N", Caption = "ANGLE", Tip = "Turns the item picked in ANGLE OF round your arm, 5 degrees per click. 0 = where it sits by default.", Get = () => $"{TurnOf(AngleItem()):0}", Set = v => SetTurn(AngleItem(), float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)), Choices = () => new List<(string, string)>() });
        l.Add(new Slot { Group = "N", Caption = "SLIDE", Tip = "Moves the item picked in ADJUST along your arm, 2 mm per click: + toward the hand, - toward the elbow. The items after it move with it.", Get = () => $"{SlideOf(AngleItem()):0}", Set = v => SetSlide(AngleItem(), float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)), Choices = () => new List<(string, string)>() });
        { var o = OnOff("STRAIGHTEN BRACELETS", Plugin.Straighten); o.Tip = "Bracelets taken from other outfits go straight round your wrist and close properly, instead of keeping the tilt they had on their original outfit."; l.Add(o); }
        l.Add(new Slot { Group = "N", Caption = "SKIN SHADE", Tip = "Makes the skin of hands and fingerless gloves from other outfits lighter (+) or darker (-), 5% per click, if the automatic match doesn't quite fit your arm.", Get = () => $"{Plugin.SkinShade.Value:0}", Set = v => Plugin.SkinShade.Value = Mathf.Clamp(float.Parse(v, System.Globalization.CultureInfo.InvariantCulture), -50f, 50f), Choices = () => new List<(string, string)>() });
        { var o = OnOff("MATCH SKIN TONE", Plugin.MatchSkinTone); o.Tip = "Hands and fingerless gloves from other outfits get your own skin tone, so they don't look like someone else's hand at the wrist."; l.Add(o); }
        { var o = OnOff("ON YOUR SCAV", Plugin.OnScav); o.Tip = "Off: your scav wears nothing from this mod (no bracelets, watch, ring or handwear) and keeps the outfit the game gave it."; l.Add(o); }
        { var o = OnOff("WATCHES IN WRIST SLOTS", Plugin.WatchesInSlots); o.Tip = "Lets you put watches in the LEFT and RIGHT wrist slots too, as many as you like. For when you wanna be the timekeeper."; l.Add(o); }
        { var o = OnOff("EVERY SHOE", Plugin.EveryShoe); o.Tip = "Also lists the shoes only bots wear in the SHOES picker: zombies, bosses, scavs, guards and cultists."; l.Add(o); }
        { var o = OnOff("CLEAN SKIN UNDER WATCH", Plugin.CleanWatchShadow); o.Tip = "When an outfit's own watch is hidden or replaced, this removes the dark mark the watch left painted on the skin (on outfits that have the fix)."; l.Add(o); }
        return l;
    }

    // the item the ANGLE row turns: one of what is worn (watch, wrist slots), the first by default
    static string _angleItem = "";
    static List<(string, string)> WornChoices()
    {
        var names = new List<string>();
        if (WristFit.Accs.ContainsKey(Plugin.Watch.Value ?? "")) names.Add(Plugin.Watch.Value);
        names.AddRange(Split(Plugin.LeftWrist.Value)); names.AddRange(Split(Plugin.RightWrist.Value));
        var l = names.Distinct().Where(n => WristFit.Accs.ContainsKey(n)).Select(n => (Short(WristFit.Accs[n]), n)).ToList();
        if (l.Count == 0) l.Add(("Nothing worn", ""));
        return l;
    }
    static string AngleItem() { var w = WornChoices(); return w.Any(c => c.Item2 == _angleItem) ? _angleItem : w[0].Item2; }
    static float TurnOf(string n) { WristFit.ParseTurns(Plugin.ItemAngles.Value); return WristFit.TurnDeg.TryGetValue(n ?? "", out float d) ? d : 0f; }
    static float SlideOf(string n) { WristFit.ParseSlides(Plugin.ItemSlides.Value); return WristFit.SlideMm.TryGetValue(n ?? "", out float d) ? d : 0f; }
    static void SetSlide(string n, float mm)
    {
        if (string.IsNullOrEmpty(n)) return;
        WristFit.ParseSlides(Plugin.ItemSlides.Value); mm = Mathf.Clamp(mm, -60f, 40f);
        if (Math.Abs(mm) < 0.01f) WristFit.SlideMm.Remove(n); else WristFit.SlideMm[n] = mm;
        Plugin.ItemSlides.Value = string.Join(";", WristFit.SlideMm.Select(kv => $"{kv.Key}={kv.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}"));
    }
    static void SetTurn(string n, float deg)
    {
        if (string.IsNullOrEmpty(n)) return;
        WristFit.ParseTurns(Plugin.ItemAngles.Value); deg = Mathf.Repeat(deg + 180f, 360f) - 180f;
        if (Math.Abs(deg) < 0.01f) WristFit.TurnDeg.Remove(n); else WristFit.TurnDeg[n] = deg;
        Plugin.ItemAngles.Value = string.Join(";", WristFit.TurnDeg.Select(kv => $"{kv.Key}={kv.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}"));
    }

    // ---- handwear picks (setting "Handwear": a comma list; one "hand" item per side, pieces any number) ----
    static List<string> HW() => Split(Plugin.Handwear.Value).Where(HandWear.Items.ContainsKey).ToList();
    static void SaveHW(IEnumerable<string> l) => Plugin.Handwear.Value = string.Join(",", l.Where(x => !string.IsNullOrEmpty(x)).Distinct());
    static List<(string, string)> HandChoices(string side)
    {
        var l = new List<(string, string)> { ("Your own hand", "") };
        foreach (var it in HandWear.Items.Values.Where(i => i.kind == "hand" && i.side == side && Bundles.Has(i.bundle)).OrderBy(i => i.note)) l.Add((it.note, it.name));
        return l;
    }
    static string HandOf(string side) => HW().FirstOrDefault(n => HandWear.Items[n].kind == "hand" && HandWear.Items[n].side == side) ?? "";
    static void SetHand(string side, string v) { var l = HW().Where(n => !(HandWear.Items[n].kind == "hand" && HandWear.Items[n].side == side)).ToList(); if (!string.IsNullOrEmpty(v)) l.Add(v); SaveHW(l); }
    static List<(string, string)> PieceChoices()
    {
        var l = new List<(string, string)> { ("None", "") };
        foreach (var it in HandWear.Items.Values.Where(i => i.kind != "hand" && i.kind != "feet" && Bundles.Has(i.bundle)).OrderBy(i => i.note)) l.Add(($"{it.note} ({(it.side == "L" ? "left" : "right")})", it.name));
        return l;
    }
    static List<(string, string)> ShoeChoices()
    {
        var l = new List<(string, string)> { ("Your own", "") };
        foreach (var it in HandWear.Items.Values.Where(i => i.kind == "feet" && Bundles.Has(i.bundle) && (!i.rare || Plugin.EveryShoe.Value || i.name == Plugin.Shoes.Value)).OrderBy(i => i.rare).ThenBy(i => i.note)) l.Add((it.note, it.name));   // bot-only shoes last, with EVERY SHOE
        return l;
    }
    static string PieceOf(int k) { var p = HW().Where(n => HandWear.Items[n].kind != "hand").ToList(); return k < p.Count ? p[k] : ""; }
    static void SetPiece(int k, string v)
    {
        var all = HW(); var hands = all.Where(n => HandWear.Items[n].kind == "hand").ToList(); var p = all.Where(n => HandWear.Items[n].kind != "hand").ToList();
        while (p.Count <= k) p.Add(""); p[k] = v; SaveHW(hands.Concat(p));
    }

    // ---- shirt / pants: the game's own suits and its own way to change them (InventoryPlayerModelWithStatsWindow.ShowClothingPanel:
    // CustomizationSolver.GetAvailableSuites -> suite.SetClothingsToProfile -> InventoryScreen.CustomizationChanged; the screen
    // sends ApplyCustomization when it closes). Not in raid, as the game's own panel.
    static InventoryScreen _scr; static EFT.Profile _profile;
    static IEnumerable<EFT.Customization.CustomizationSuite> Suits(bool upper)
    {
        if (_profile == null || Comfort.Common.Singleton<EFT.CustomizationSolver>.Instance == null) return Enumerable.Empty<EFT.Customization.CustomizationSuite>();
        return Comfort.Common.Singleton<EFT.CustomizationSolver>.Instance.GetAvailableSuites(_profile.Info.Side).Where(x => upper ? x is EFT.Customization.UpperBodySuit : x is EFT.Customization.LowerBodySuit);
    }
    // suit pictures: the game's own clothing icons, as Ragman's clothing screen makes them (TacticalClothingView.LoadBundles:
    // IEasyAssets.Retain the clothing prefabs, ClothingIconCreator.Begin, GetIcon per CustomizationClothing; icons come later and
    // are cached by the game on disk). Begun here = ended here once the page is closed and nothing is rendering.
    static readonly Dictionary<string, EFT.Customization.ClothingIcon> SuitIcons = new Dictionary<string, EFT.Customization.ClothingIcon>();
    static readonly List<Diz.DependencyManager.DependencyGraph<Diz.Resources.IEasyBundle>.Token> SuitTokens = new List<Diz.DependencyManager.DependencyGraph<Diz.Resources.IEasyBundle>.Token>();
    static bool _iconsBegun; internal static bool IconsPending;
    static EFT.Customization.CustomizationSuite SuitById(string id) => string.IsNullOrEmpty(id) ? null : Suits(true).Concat(Suits(false)).FirstOrDefault(x => x.Id.ToString() == id);
    static EFT.Customization.CustomizationClothing ClothingOf(EFT.Customization.CustomizationSuite suit)
    {
        var solver = Comfort.Common.Singleton<EFT.CustomizationSolver>.Instance;
        return suit != null && solver != null && solver._customizationClothingTemplates.TryGetValue(suit.MainBodyPartItem, out var c) ? c : null;
    }
    internal static bool IsSuit(string id) => SuitById(id) != null;
    internal static Sprite SuitSprite(string id) => SuitIcons.TryGetValue(id ?? "", out var ic) ? ic?.Sprite : null;
    internal static async void RequestSuitIcons(IEnumerable<string> ids)
    {
        try
        {
            if (!Comfort.Common.Singleton<ClothingIconCreator>.Instantiated) { Plugin.Log.LogWarning("[WristAccessories] suit pictures: the game's clothing icon maker is not there"); return; }
            var want = ids.Where(i => !string.IsNullOrEmpty(i) && !SuitIcons.ContainsKey(i)).Select(i => (id: i, c: ClothingOf(SuitById(i)))).Where(x => x.c != null && x.c.Prefab != null).ToList();
            if (want.Count == 0) return;
            foreach (var w in want) SuitIcons[w.id] = null;                     // asked once
            var tok = EFT.EasyAssetsExtensions.Retain(Comfort.Common.Singleton<Diz.Resources.IEasyAssets>.Instance, want.Select(w => w.c.Prefab.path).Distinct());
            SuitTokens.Add(tok);
            await EFT.EasyAssetsExtensions.LoadBundles(tok);
            var mk = Comfort.Common.Singleton<ClothingIconCreator>.Instance;
            if (mk._clothingMeshContainer == null) { mk.Begin(); _iconsBegun = true; }
            foreach (var w in want) SuitIcons[w.id] = mk.GetIcon(w.c, new IntVec2(256, 256));
            IconsPending = true;
            Plugin.Log.LogInfo($"[WristAccessories] suit pictures: {want.Count} asked of the game's clothing icon maker ({SuitIcons.Count} in all)");
        }
        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] suit pictures: {e}"); }
    }
    // the page closed: hand the icon maker back and let the clothing bundles go (the pictures stay: rendered into their own textures)
    internal static void EndSuitIcons()
    {
        if (!_iconsBegun && SuitTokens.Count == 0) return;
        if (!Comfort.Common.Singleton<ClothingIconCreator>.Instantiated) return;
        var mk = Comfort.Common.Singleton<ClothingIconCreator>.Instance;
        if (mk._containerIsBusy) return;                                    // still rendering: next frame
        if (_iconsBegun) { mk.End(); _iconsBegun = false; }
        if (SuitTokens.Count > 0) { foreach (var t in SuitTokens) t.Release(); Plugin.Log.LogInfo($"[WristAccessories] suit pictures: {SuitTokens.Count} bundle hold(s) released"); SuitTokens.Clear(); }
        foreach (var k in SuitIcons.Where(kv => kv.Value == null || kv.Value.Sprite == null).Select(kv => kv.Key).ToList()) SuitIcons.Remove(k);   // unfinished: ask again next time
    }

    static string SuitName(EFT.Customization.CustomizationSuite x) { var n = EFT.LocalizationExtensions.Localized(x.NameLocalizationKey, (string)null); return string.IsNullOrEmpty(n) || n == x.NameLocalizationKey ? (x.Name ?? x.Id.ToString()) : n; }
    static List<(string, string)> SuitChoices(bool upper)
    {
        if (EFT.InGameStatus.InRaid) return new List<(string, string)> { ("Not in raid", "") };
        var l = Suits(upper).Select(x => (SuitName(x), x.Id.ToString())).OrderBy(x => x.Item1).ToList();
        return l.Count > 0 ? l : new List<(string, string)> { ("None owned", "") };
    }
    internal static string SuitOf(bool upper)
    {
        if (_profile == null) return "";
        var cur = _profile.Customization[upper ? EFT.EBodyModelPart.Body : EFT.EBodyModelPart.Feet];
        return Suits(upper).FirstOrDefault(x => x.MainBodyPartItem == cur)?.Id.ToString() ?? "";
    }
    internal static void SetSuit(string id)
    {
        if (string.IsNullOrEmpty(id) || _profile == null || _scr == null || EFT.InGameStatus.InRaid) return;
        var suit = Suits(true).Concat(Suits(false)).FirstOrDefault(x => x.Id.ToString() == id); if (suit == null) return;
        suit.SetClothingsToProfile(_profile.Customization); _scr.CustomizationChanged(suit);
        Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES tab: {(suit is EFT.Customization.UpperBodySuit ? "shirt" : "pants")} -> {SuitName(suit)} ({suit.Id}); the game saves it when the screen closes");
        _scr.GetComponent<Host>()?.ReloadViewer();
    }

    static Slot OnOff(string caption, ConfigEntry<bool> e) => new Slot
    {
        Group = "O", Caption = caption, Choices = () => new List<(string, string)> { ("On", "on"), ("Off", "off") },
        Get = () => e.Value ? "on" : "off", Set = v => e.Value = v == "on",
    };

    static List<(string, string)> WatchChoices()
    {
        var l = new List<(string, string)> { ("Outfit's own", ""), ("None", "none") };
        foreach (var a in WristFit.Accs.Values.Where(a => a.name.StartsWith("watch_") && Bundles.Has(a.bundle)).OrderBy(a => a.note)) l.Add((a.note, a.name));
        return l;
    }

    static List<(string, string)> FittedChoices()
    {
        var l = new List<(string, string)> { ("None", "") };
        foreach (var a in WristFit.Accs.Values.Where(a => a.name != "skullring" && !a.name.Contains("~") && Bundles.Has(a.bundle) && (Plugin.WatchesInSlots.Value || !a.name.StartsWith("watch_"))).OrderBy(a => Short(a))) l.Add((Short(a), a.name));   // watches: the WATCH slot's, unless WATCHES IN WRIST SLOTS
        return l;
    }

    static string Short(WristFit.Acc a) => a.name switch
    {
        "rolex" => "Roler Submariner", "chainlet" => "Chainlet", "chain_gold" => "Golden neck chain", "prokill" => "Prokill medallion chain",
        "dogtag" => "Dogtag on a chain", "paracord" => "Paracord", "amulet" => "Sacred Amulet",
        "predator_sleeve" => "Kill sleeve (USEC Predator)", "bd_sleeve" => "Kill sleeve (Black Division)", "isb_sleeve" => "Kill sleeve (ISB)", _ => a.note ?? a.name,
    };

    // slot i of a comma list setting (wrist first)
    static string SlotOf(ConfigEntry<string> e, int i) { var l = Split(e.Value); return i < l.Count ? l[i] : ""; }
    static void SetSlot(ConfigEntry<string> e, int i, string v)
    {
        var l = Split(e.Value); while (l.Count <= i) l.Add("");
        l[i] = v;
        e.Value = string.Join(",", l.Where(x => x != "").Distinct());
    }
    static List<string> Split(string s) => (s ?? "").Split(',').Select(x => x.Trim()).Where(x => x != "").ToList();

    // ---- per screen: our tab button and page ----
    class Host : MonoBehaviour, ITabController
    {
        public Tab Tab; GameObject _page; TabGroup _group; TMP_FontAsset _font; readonly List<Slot> _slots = new List<Slot>();
        GameObject _list; readonly List<(RawImage pic, string value)> _listPics = new List<(RawImage, string)>(); float _iconPoll;

        static readonly FieldInfo ProfileF = AccessTools.Field(typeof(InventoryScreen), "_profile");
        WristViewer _viewer;

        InventoryScreen _scr; bool _reactivatedLogged;
        public void Attach(InventoryScreen scr, TabGroup group, IReadOnlyDictionary<EInventoryTab, Tab> dict)
        {
            _scr = scr; AccessoriesTab._scr = scr; AccessoriesTab._profile = ProfileF?.GetValue(scr) as EFT.Profile;
            if (_viewer != null) _viewer.Profile = ProfileF?.GetValue(scr) as EFT.Profile;
            if (Tab == null) Build(scr, dict);
            if (Tab == null || group == null) return;
            Plugin.Log.LogInfo($"[WristAccessories] WARDROBE tab in the game's tab group: {group._tabs.Contains(Tab)} ({group._tabs.Length} tabs)");
            if (_group != null && _group != group) Tab.OnSelectionChanged -= _group.SelectionChangedHandler;
            if (!group._tabs.Contains(Tab)) { group._tabs = group._tabs.Concat(new[] { Tab }).ToArray(); Tab.OnSelectionChanged += group.SelectionChangedHandler; }
            _group = group;
            Tab.Init(this); Tab.UpdateVisual(false); Hide(); Tab.gameObject.SetActive(true); Tab.SetInteractable(true); Arrange();
        }

        /// <summary>The tab, built on first use (the dictionary may already hold it - it is left out of the row we copy from).</summary>
        public Tab Ensure(InventoryScreen scr, IReadOnlyDictionary<EInventoryTab, Tab> dict)
        {
            if (Tab == null) Build(scr, dict.Where(kv => kv.Key != WardrobeKey).ToDictionary(kv => kv.Key, kv => kv.Value));
            return Tab;
        }

        // the row of tabs (the game's 8, other mods' - Improved Customization UI's CUSTOMIZATION tab - and ours) must stay on
        // screen: with nine already the row reaches the right edge and ours sat past it (friend's screenshot 2026-10-26). The
        // limit is the SCREEN's right edge (the root canvas corner): the tab bar's own rect is tiny (0.7.4 took its edge, x 12,
        // and squeezed every tab off to the left). Always from the tabs' original places; never tighter than 60 % of their
        // step - then the game's tabs stay where they are.
        // 1.0.4 (user 2026-10-28, a player: WARDROBE "disappears after single press"): other mods add tabs AFTER ours is placed
        // (Interface10's Character10 went in 4th and pushed PRESTIGE onto ours; the game then redraws the row back to front, ours
        // first, so PRESTIGE covered it and took every press - diagnostic log). Now: ours goes after the RIGHT EDGE of the real last
        // tab, the row is checked again whenever it changes (tabs added / moved by anyone), and ours is drawn last while it touches
        // another tab. Squeezing (no room before BACK) is from each tab's last place set by someone else, never cumulative.
        readonly Dictionary<RectTransform, Vector3> _home = new Dictionary<RectTransform, Vector3>();
        readonly Dictionary<RectTransform, Vector3> _mine = new Dictionary<RectTransform, Vector3>();   // where we last put a tab
        string _rowSig;
        string RowSig(RectTransform bar) => string.Join(";", Enumerable.Range(0, bar.childCount).Select(i => bar.GetChild(i)).Where(t => t != Tab.transform && t.gameObject.activeSelf && t.GetComponent<Tab>() != null)
            .Select(t => t.name + "@" + Mathf.RoundToInt(((RectTransform)t).localPosition.x) + "/" + Mathf.RoundToInt(((RectTransform)t).rect.width)).OrderBy(x => x)) + "|" + bar.childCount;   // order-free: the game re-sorts the row on every pick
        static float Left(RectTransform t) => t.localPosition.x + t.rect.xMin * t.localScale.x;
        static float Right(RectTransform t) => t.localPosition.x + t.rect.xMax * t.localScale.x;
        void Arrange()
        {
            try
            {
                var bar = Tab.transform.parent as RectTransform; if (bar == null) return;
                var me = (RectTransform)Tab.transform;
                var tabs = Enumerable.Range(0, bar.childCount).Select(i => bar.GetChild(i)).Where(t => t != me && t.gameObject.activeSelf && t.GetComponent<Tab>() != null).Select(t => (RectTransform)t).ToList();
                // a tab's own place = where the game / another mod last put it (not where we squeezed it to)
                foreach (var t in tabs) if (!_mine.TryGetValue(t, out var m) || (t.localPosition - m).sqrMagnitude > 0.25f) _home[t] = t.localPosition;
                foreach (var t in tabs) t.localPosition = _home[t];
                tabs = tabs.OrderBy(t => t.localPosition.x).ToList();
                if (tabs.Count < 1) return;
                // the gap between neighbours (right edge to next left edge), as the row has it
                var gaps = new List<float>(); for (int i = 1; i < tabs.Count; i++) gaps.Add(Left(tabs[i]) - Right(tabs[i - 1]));
                float gap = gaps.Count > 0 ? Mathf.Clamp(gaps.OrderBy(g => g).ElementAt(gaps.Count / 2), 0f, 40f) : 4f;
                float lastRight = tabs.Max(Right);
                me.localPosition = new Vector3(lastRight + gap - me.rect.xMin * me.localScale.x, tabs[tabs.Count - 1].localPosition.y, tabs[tabs.Count - 1].localPosition.z);
                var canvas = bar.GetComponentInParent<Canvas>()?.rootCanvas;
                float screen = float.MaxValue;
                if (canvas != null) { var c = new Vector3[4]; ((RectTransform)canvas.transform).GetWorldCorners(c); screen = bar.InverseTransformPoint(c[2]).x; }
                float back = float.MaxValue;
                if (_scr != null && _scr._backButton != null) { var bc = new Vector3[4]; ((RectTransform)_scr._backButton.transform).GetWorldCorners(bc); back = bar.InverseTransformPoint(bc[0]).x - 8f; }
                float right = Math.Min(screen, back), x0 = Left(tabs[0]);
                Diag(me, bar);
                if (Right(me) <= right - 4f)
                    Plugin.Log.LogInfo($"[WristAccessories] WARDROBE tab after {tabs[tabs.Count - 1].name}: {tabs.Count + 1} tabs, gap {gap:0}, ends at {Right(me):0}, BACK starts {back:0}; row: {string.Join(", ", tabs.Select(t => t.name))}");
                else
                {
                    // no room: every tab narrower steps, keeping their widths (left edges spread evenly from the first to where ours ends at right)
                    float total = tabs.Sum(t => Right(t) - Left(t)) + (Right(me) - Left(me)), avail = right - 4f - x0, g2 = (avail - total) / tabs.Count;
                    if (g2 < -0.35f * tabs.Average(t => Right(t) - Left(t)))
                        Plugin.Log.LogWarning($"[WristAccessories] WARDROBE tab: no room before BACK ({tabs.Count + 1} tabs, {total:0} wide in {avail:0}) - left last, other tabs unchanged");
                    else
                    {
                        float x = x0;
                        foreach (var t in tabs) { var lp = t.localPosition; t.localPosition = new Vector3(x - t.rect.xMin * t.localScale.x, lp.y, lp.z); _mine[t] = t.localPosition; x = Right(t) + g2; }
                        me.localPosition = new Vector3(x - me.rect.xMin * me.localScale.x, me.localPosition.y, me.localPosition.z);
                        Plugin.Log.LogInfo($"[WristAccessories] WARDROBE tab: row of {tabs.Count + 1} squeezed to clear BACK, gap {gap:0} -> {g2:0}, ends at {Right(me):0} (BACK starts {back:0}); row: {string.Join(", ", tabs.Select(t => t.name))}");
                    }
                }
                _rowSig = RowSig(bar);
            }
            catch (Exception e) { Plugin.Log.LogError("[WristAccessories] WARDROBE tab arrange: " + e.Message); }
        }
        // every frame (cheap): the row changed (a mod added or moved a tab) -> place ours again; touching another tab -> drawn last
        void KeepPlace()
        {
            if (Tab == null || !Tab.gameObject.activeInHierarchy) return;
            var bar = Tab.transform.parent as RectTransform; if (bar == null) return;
            if (RowSig(bar) != _rowSig) { Plugin.Log.LogInfo("[WristAccessories] WARDROBE tab: the tab row changed - placed again"); Arrange(); }
            var me = (RectTransform)Tab.transform;
            if (me.GetSiblingIndex() == bar.childCount - 1) return;
            float l = Left(me), r = Right(me);
            for (int i = me.GetSiblingIndex() + 1; i < bar.childCount; i++)
            {
                var o = bar.GetChild(i) as RectTransform;
                if (o == null || !o.gameObject.activeSelf || o.GetComponent<Tab>() == null) continue;
                if (Right(o) > l + 1f && Left(o) < r - 1f) { me.SetAsLastSibling(); return; }
            }
        }

        // what could hide our tab: clipping masks above it, inactive / transparent pieces, where it is on the screen
        void Diag(RectTransform me, RectTransform bar)
        {
            try
            {
                var masks = me.GetComponentsInParent<RectMask2D>(true).Select(m => "RectMask2D@" + m.name).Concat(me.GetComponentsInParent<Mask>(true).Select(m => "Mask@" + m.name));
                var groups = me.GetComponentsInParent<CanvasGroup>(true).Select(g => $"{g.name}:{g.alpha:0.##}");
                var c = new Vector3[4]; me.GetWorldCorners(c);
                var cam = me.GetComponentInParent<Canvas>()?.rootCanvas?.worldCamera;
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
                Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES tab diag: active {me.gameObject.activeInHierarchy}, normal {Tab._normalVersion?.activeSelf}, selected {Tab._selectedVersion?.activeSelf}, sibling {me.GetSiblingIndex()}/{bar.childCount}, " +
                    $"screen px {a.x:0},{a.y:0} .. {b.x:0},{b.y:0} of {Screen.width}x{Screen.height}, masks [{string.Join(", ", masks)}], canvas groups [{string.Join(", ", groups)}], bar rect {bar.rect.xMin:0}..{bar.rect.xMax:0}");
            }
            catch (Exception e) { Plugin.Log.LogError("[WristAccessories] ACCESSORIES tab diag: " + e.Message); }
        }

        void Build(InventoryScreen scr, IReadOnlyDictionary<EInventoryTab, Tab> dict)
        {
            // the right-most game tab, and the step between the two right-most (the tabs sit side by side, not in a layout)
            var row = dict.Values.Where(t => t != null && t.gameObject.activeSelf).OrderBy(t => ((RectTransform)t.transform).anchoredPosition.x).ToList();
            if (row.Count == 0) { Plugin.Log.LogWarning("[WristAccessories] ACCESSORIES tab: no game tabs to copy"); return; }
            var last = row[row.Count - 1]; var lr = (RectTransform)last.transform;
            float step = row.Count > 1 ? lr.anchoredPosition.x - ((RectTransform)row[row.Count - 2].transform).anchoredPosition.x : lr.rect.width;
            var go = Instantiate(last.gameObject, last.transform.parent, false); go.name = "WristAcc_AccessoriesTab";
            var rt = (RectTransform)go.transform; rt.anchoredPosition = lr.anchoredPosition + new Vector2(step, 0);
            foreach (var lt in go.GetComponentsInChildren<LocalizedText>(true)) DestroyImmediate(lt);
            foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true)) { t.text = "WARDROBE"; _font = _font ?? t.font; }   // user 2026-10-27: the tab is called WARDROBE
            // the copied tab's own extras (Prestige's green "new" triangle, user 2026-10-27): every part a plain tab (the first one)
            // does not have is switched off, by path under the tab
            string PathIn(Transform t, Transform root) { var p = t.name; while (t.parent != null && t.parent != root) { t = t.parent; p = t.name + "/" + p; } return p; }
            var plain = new HashSet<string>(row[0].GetComponentsInChildren<Transform>(true).Where(t => t != row[0].transform).Select(t => PathIn(t, row[0].transform)));
            var tabC = go.GetComponent<Tab>(); var keep = new HashSet<Transform>();
            foreach (var k in new Component[] { tabC?._normalVersion?.transform, tabC?._selectedVersion?.transform, tabC?._hoverGraphic?.transform, tabC?._idleGraphic?.transform, tabC?._targetImage }
                .Concat(go.GetComponentsInChildren<TextMeshProUGUI>(true)).Where(c => c != null))
                for (var t = k.transform; t != null && t != go.transform; t = t.parent) keep.Add(t);            // the tab's own parts and their parents
            var all = go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform).ToList();
            var extras = all.Where(t => !plain.Contains(PathIn(t, go.transform)) && !keep.Contains(t)).ToList();
            if (extras.Count * 3 > all.Count) { Plugin.Log.LogWarning($"[WristAccessories] ACCESSORIES tab: {extras.Count} of {all.Count} parts differ from {row[0].name} - left as copied"); extras.Clear(); }
            foreach (var x in extras) if (!extras.Contains(x.parent)) x.gameObject.SetActive(false);
            if (extras.Count > 0) Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES tab: off, not on a plain tab ({row[0].name}): {string.Join(", ", extras.Where(x => !extras.Contains(x.parent)).Select(x => PathIn(x, go.transform)))}");
            Tab = go.GetComponent<Tab>();
            Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES tab made from {last.name} at x {rt.anchoredPosition.x:0} (step {step:0}), {row.Count} game tabs");

            // the page: where the game's tab pages go, but starting under the tab bar (0.7.7 covered and dimmed it)
            var like = (RectTransform)scr._skillsAndMasteringScreen.transform; var host = (RectTransform)like.parent;
            _page = new GameObject("WristAcc_AccessoriesPage", typeof(RectTransform));
            var pr = (RectTransform)_page.transform; pr.SetParent(host, false);
            var tc = new Vector3[4]; rt.GetWorldCorners(tc); float tabBottom = host.InverseTransformPoint(tc[0]).y;
            pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one; pr.pivot = new Vector2(0.5f, 1f);
            pr.offsetMin = new Vector2(0, like.offsetMin.y); pr.offsetMax = new Vector2(0, Math.Min(0f, tabBottom - host.rect.yMax - 16f));
            Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES page under the tabs: top {pr.offsetMax.y:0}, bottom {pr.offsetMin.y:0} (page parent {host.name} {host.rect.width:0}x{host.rect.height:0})");
            _slots.Clear(); _slots.AddRange(Slots());

            // left: the slots as the GEAR screen draws its slots (the game's own sprites, found on this screen), options as dropdowns
            Sprites(scr);
            const float TileStep = 140f, Block = HeadH + 10 + TileH + TileHead + 18;
            // options: one row each beside the watch / clothing tiles; clothing tiles two per row (the options start at x 320)
            const float TRow = TileHead + TileH + 40;
            int nOpt = _slots.Count(x => x.Group != "H" && x.Group != "L" && x.Group != "R" && x.Group != "W" && x.Group != "T"), nT = _slots.Count(x => x.Group == "T");
            // option rows 40 apart, closer when they would run past the page (14 rows since EVERY SHOE: 1012 of a 989 page)
            float pageH0 = host.rect.height - pr.offsetMin.y + pr.offsetMax.y;
            float OptStep = Mathf.Clamp((pageH0 - 6 - 16 - (2 * Block + HeadH + 6)) / Math.Max(1, nOpt), 37f, 40f);
            float tTop = 2 * Block + HeadH + 10 + TileHead + TileH + 24;
            float leftH = Mathf.Max(3 * Block + 304, Mathf.Max(2 * Block + HeadH + 6 + nOpt * OptStep, tTop + ((nT + 1) / 2) * TRow) + 16);
            var left = Panel(_page.transform, new Vector2(30, -6), new Vector2(940, leftH), false);
            Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES layout: {nOpt} option rows {OptStep:0.0} apart, {nT} clothing tiles, left panel {leftH:0} high (page {pageH0:0})");
            int nt = 0;
            Header(left, "LEFT WRIST", 0); Header(left, "RIGHT WRIST", Block); Header(left, "WATCH, RING AND OPTIONS", 2 * Block);
            int nl = 0, nr = 0, nw = 0, no = 0;
            foreach (var sl in _slots)
            {
                if (sl.Group == "H") continue;                                  // HANDS: in the right panel
                if (sl.Group == "L") sl.Box = new Tile(this, sl, left, new Vector2(24 + nl++ * TileStep, -(HeadH + 10)));
                else if (sl.Group == "R") sl.Box = new Tile(this, sl, left, new Vector2(24 + nr++ * TileStep, -(Block + HeadH + 10)));
                else if (sl.Group == "W") sl.Box = new Tile(this, sl, left, new Vector2(24 + nw++ * TileStep, -(2 * Block + HeadH + 10)));
                else if (sl.Group == "T") sl.Box = new Tile(this, sl, left, new Vector2(24 + (nt % 2) * TileStep, -(tTop + (nt++ / 2) * TRow)), true);
                else if (sl.Group == "N")
                {
                    float y = 2 * Block + HeadH + 6 + no++ * OptStep;
                    var cap = Text(left, sl.Caption, 15, Label, TextAlignmentOptions.MidlineLeft); cap.characterSpacing = 2; AddTip(cap, sl.Tip);
                    Place(cap.rectTransform, new Vector2(320, -y), new Vector2(250, 36));
                    sl.Box = new Nudge(this, sl, left, new Vector2(560, -y), 356f);
                }
                else if (sl.Group == "P")
                {
                    float y = 2 * Block + HeadH + 6 + no++ * OptStep;
                    var cap = Text(left, sl.Caption, 15, Label, TextAlignmentOptions.MidlineLeft); cap.characterSpacing = 2; AddTip(cap, sl.Tip);
                    Place(cap.rectTransform, new Vector2(320, -y), new Vector2(250, 36));
                    PresetRow(scr, left, new Vector2(560, -y));
                }
                else if (sl.Group == "S")
                {
                    float y = 2 * Block + HeadH + 6 + no++ * OptStep;
                    var cap = Text(left, sl.Caption, 15, Label, TextAlignmentOptions.MidlineLeft); cap.characterSpacing = 2; AddTip(cap, sl.Tip);
                    Place(cap.rectTransform, new Vector2(320, -y), new Vector2(250, 36));
                    sl.Box = new Switch(this, sl, left, new Vector2(560, -y), 356f);
                }
                else
                {
                    float y = 2 * Block + HeadH + 6 + no++ * OptStep;
                    var cap = Text(left, sl.Caption, 15, Label, TextAlignmentOptions.MidlineLeft); cap.characterSpacing = 2; AddTip(cap, sl.Tip);
                    Place(cap.rectTransform, new Vector2(320, -y), new Vector2(250, 36));
                    sl.Box = new Dropdown(this, sl, left, new Vector2(560, -y), 356f);
                }
            }

            // the preview as tall as the page allows (the hands row under it; was 540, user: "my guy is squished")
            float pageH = host.rect.height - pr.offsetMin.y + pr.offsetMax.y;
            float ViewH = Mathf.Max(540f, pageH - 6 - 12 - (HeadH + 12 + 40 + HeadH + 10 + TileHead + TileH + 18));
            Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES preview: {668}x{ViewH:0} (page {pageH:0} high)");
            var right = Panel(_page.transform, new Vector2(-30, -6), new Vector2(700, HeadH + 12 + ViewH + 40 + HeadH + 10 + TileHead + TileH + 18), true);
            Header(right, "PREVIEW", 0);
            var vgo = new GameObject("WristAcc_Viewer", typeof(RectTransform), typeof(RawImage));
            var vr = (RectTransform)vgo.transform; vr.SetParent(right, false); Place(vr, new Vector2(16, -(HeadH + 12)), new Vector2(668, ViewH));
            _viewer = vgo.AddComponent<WristViewer>(); _viewer.Profile = ProfileF?.GetValue(scr) as EFT.Profile;
            var hint = Text(right, "Drag to turn, scroll to zoom", 15, Dim, TextAlignmentOptions.MidlineRight);
            Place(hint.rectTransform, new Vector2(16, -(HeadH + 12 + ViewH + 8)), new Vector2(668, 26));
            // HANDS: gloves / other outfits' hands per side, hand pieces (tape, wraps) - the same slot tiles
            float hy = HeadH + 12 + ViewH + 40; Header(right, "HANDS", hy); int nh = 0;
            foreach (var sl in _slots.Where(x => x.Group == "H")) sl.Box = new Tile(this, sl, right, new Vector2(24 + nh++ * 166, -(hy + HeadH + 10)));
            DumpGameLook(scr);
            _page.SetActive(false);
        }

        // ---- the look: the game's dark panels, thin grey borders, beige for what is picked ----
        static readonly Color PanelBg = new Color(0.035f, 0.04f, 0.043f, 0.62f), Border = new Color(0.31f, 0.32f, 0.31f, 0.9f),
            HeadBg = new Color(0.11f, 0.115f, 0.115f, 0.78f), HeadText = new Color(0.80f, 0.79f, 0.72f), Label = new Color(0.60f, 0.60f, 0.55f),
            FieldBg = new Color(0.075f, 0.08f, 0.085f, 0.82f), FieldText = new Color(0.86f, 0.85f, 0.78f), Dim = new Color(0.50f, 0.50f, 0.46f),
            Picked = new Color(0.77f, 0.76f, 0.69f, 1f), PickedText = new Color(0.07f, 0.07f, 0.07f);
        const float RowH = 52f, HeadH = 34f, TileH = 112f, TileHead = 24f, TileW = 128f;

        // the game's own UI sprites on the character screen ("game look" log, 0.7.9): slot fill / borders / header strip of the
        // GEAR slots, the generic thin border and the tiny arrow of its dropdowns. Missing ones: our own colours instead.
        static readonly Dictionary<string, Sprite> Spr = new Dictionary<string, Sprite>();
        static void Sprites(InventoryScreen scr)
        {
            if (Spr.Count > 0) return;
            var want = new HashSet<string> { "slot_empty_fill", "slot_border_full", "slot_border_current", "header_slot", "border_generic", "arrow_down_tiny", "Allscreens_Borders_Default", "popup_shadow" };
            foreach (var img in scr.GetComponentsInChildren<Image>(true))
                if (img.sprite != null && want.Contains(img.sprite.name) && !Spr.ContainsKey(img.sprite.name)) Spr[img.sprite.name] = img.sprite;
            Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES look: game sprites {string.Join(", ", Spr.Keys)} (missing: {string.Join(", ", want.Where(w => !Spr.ContainsKey(w)))})");
        }
        public static Sprite S(string n) => Spr.TryGetValue(n, out var x) ? x : null;

        // a sprite stretched over a rect (9-sliced when the sprite has borders)
        public static Image Skin(Transform parent, string sprite, Color c, Image.Type type)
        {
            var go = new GameObject("WristAcc_" + sprite, typeof(RectTransform), typeof(Image)); var r = (RectTransform)go.transform; r.SetParent(parent, false);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>(); img.sprite = S(sprite); img.type = type; img.color = c; img.raycastTarget = false; return img;
        }
        readonly List<(string mode, Image bg, TextMeshProUGUI text)> _modeButtons = new List<(string, Image, TextMeshProUGUI)>();

        static readonly Dictionary<string, Texture2D> Icons = new Dictionary<string, Texture2D>();
        public static Texture2D Icon(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (Icons.TryGetValue(value, out var t)) return t;
            t = null;
            try
            {
                var d = WristFit.Accs.TryGetValue(value, out var acc) && acc.accDir != null ? acc.accDir : HandWear.Items.TryGetValue(value, out var hwi) ? hwi.dir : System.IO.Path.Combine(WristFit.Dir, "acc");
                var f = System.IO.Path.Combine(d, "icons", value + ".png");
                if (!System.IO.File.Exists(f)) f = System.IO.Path.Combine(WristFit.Dir, "acc", "icons", value + ".png");
                if (System.IO.File.Exists(f)) { t = new Texture2D(2, 2, TextureFormat.RGBA32, false); if (!t.LoadImage(System.IO.File.ReadAllBytes(f))) t = null; else { t.name = "WristAcc_icon_" + value; DontDestroyOnLoad(t); } }
            }
            catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] icon {value}: {e.Message}"); }
            // an item without its picture: the smiley (fallback.png), like a material that does not load (user 2026-10-27);
            // suits keep the game's own icons (checked after this)
            if (t == null && (WristFit.Accs.ContainsKey(value) || HandWear.Items.ContainsKey(value)))
            {
                t = Fallback.Picture();
                Plugin.Log.LogWarning($"[WristAccessories] icon {value}: no picture file{(t != null ? ", the fallback smiley shown" : "")}");
            }
            Icons[value] = t; return t;
        }

        // a picture by value: our icons (png) or a suit's game icon (a sprite: its texture region, kept in proportion in the box)
        static readonly Dictionary<RawImage, (Vector2 pos, float size)> PicBox = new Dictionary<RawImage, (Vector2, float)>();
        public static bool HasPic(string value) => Icon(value) != null || AccessoriesTab.IsSuit(value);
        public static bool SetPic(RawImage ri, string value)
        {
            if (ri == null) return false;
            var r = ri.rectTransform; if (!PicBox.TryGetValue(ri, out var box)) PicBox[ri] = box = (r.anchoredPosition, r.sizeDelta.x);
            var tex = Icon(value);
            if (tex != null) { ri.texture = tex; ri.uvRect = new Rect(0, 0, 1, 1); r.anchoredPosition = box.pos; r.sizeDelta = new Vector2(box.size, box.size); ri.enabled = true; return true; }
            var sp = AccessoriesTab.SuitSprite(value);
            if (sp != null && sp.texture != null)
            {
                var tr = sp.textureRect; float W = sp.texture.width, H = sp.texture.height;
                ri.texture = sp.texture; ri.uvRect = new Rect(tr.x / W, tr.y / H, tr.width / W, tr.height / H);
                float a = tr.height > 0 ? tr.width / tr.height : 1f, w = a >= 1 ? box.size : box.size * a, h = a >= 1 ? box.size / a : box.size;
                r.sizeDelta = new Vector2(w, h); r.anchoredPosition = box.pos + new Vector2((box.size - w) / 2, -(box.size - h) / 2); ri.enabled = true; return true;
            }
            ri.texture = null; ri.enabled = false; return false;
        }
        public static RawImage Pic(Transform parent, Texture2D tex, Vector2 topLeft, float size)
        {
            var go = new GameObject("WristAcc_Pic", typeof(RectTransform), typeof(RawImage)); var r = (RectTransform)go.transform; r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = topLeft; r.sizeDelta = new Vector2(size, size);
            var ri = go.GetComponent<RawImage>(); ri.texture = tex; ri.raycastTarget = false; ri.enabled = tex != null; return ri;
        }

        // once: the game's own frame / background sprites on this screen, to dress our panels like it (next step)
        static bool _dumped;
        void DumpGameLook(InventoryScreen scr)
        {
            if (_dumped) return; _dumped = true;
            try
            {
                var seen = new HashSet<string>(); var l = new List<string>();
                foreach (var img in scr.GetComponentsInChildren<Image>(true))
                {
                    if (img.sprite == null || img.transform.IsChildOf(_page.transform)) continue;
                    string k = img.sprite.name; if (!seen.Add(k)) continue;
                    var b = img.sprite.border; string path = img.name; var t = img.transform.parent; for (int i = 0; i < 2 && t != null; i++, t = t.parent) path = t.name + "/" + path;
                    l.Add($"{k} ({img.sprite.rect.width:0}x{img.sprite.rect.height:0}{(b != Vector4.zero ? $", 9-slice {b.x:0},{b.y:0},{b.z:0},{b.w:0}" : "")}, {img.type}) @ {path}");
                    if (l.Count >= 80) break;
                }
                Plugin.Log.LogInfo($"[WristAccessories] game look ({l.Count} sprites on the character screen):\n  " + string.Join("\n  ", l));
            }
            catch (Exception e) { Plugin.Log.LogError("[WristAccessories] game look: " + e.Message); }
        }

        void PaintModes()
        {
            foreach (var (mode, bg, text) in _modeButtons)
            { bool on = _viewer != null && _viewer.Mode == mode; bg.color = on ? Picked : FieldBg; text.color = on ? PickedText : FieldText; }
        }

        RectTransform Panel(Transform parent, Vector2 corner, Vector2 size, bool fromRight)
        {
            var go = new GameObject("WristAcc_Panel", typeof(RectTransform), typeof(Image), typeof(Outline)); var r = (RectTransform)go.transform; r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(fromRight ? 1 : 0, 1); r.pivot = new Vector2(fromRight ? 1 : 0, 1); r.anchoredPosition = corner; r.sizeDelta = size;
            go.GetComponent<Image>().color = PanelBg; var o = go.GetComponent<Outline>(); o.effectColor = Border; o.effectDistance = new Vector2(1, -1);
            if (S("Allscreens_Borders_Default") != null) { o.enabled = false; Skin(r, "Allscreens_Borders_Default", new Color(0.45f, 0.46f, 0.44f, 1f), Image.Type.Sliced); }
            return r;
        }

        void Header(RectTransform panel, string title, float y)
        {
            var go = new GameObject("WristAcc_Header", typeof(RectTransform), typeof(Image)); var r = (RectTransform)go.transform; r.SetParent(panel, false);
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1); r.anchoredPosition = new Vector2(0, -y); r.sizeDelta = new Vector2(0, HeadH);
            go.GetComponent<Image>().color = HeadBg;
            var line = new GameObject("Line", typeof(RectTransform), typeof(Image)); var lr = (RectTransform)line.transform; lr.SetParent(r, false);
            lr.anchorMin = Vector2.zero; lr.anchorMax = new Vector2(1, 0); lr.pivot = new Vector2(0.5f, 0); lr.sizeDelta = new Vector2(0, 1); line.GetComponent<Image>().color = Border;
            var t = Text(r, title, 17, HeadText, TextAlignmentOptions.MidlineLeft); t.characterSpacing = 4;
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.offsetMin = new Vector2(16, 0); t.rectTransform.offsetMax = Vector2.zero;
        }

        // a dark field with a thin border and text (dropdowns, buttons)
        public Button Field(Transform parent, Vector2 topLeft, Vector2 size, string text, TextAlignmentOptions align, out TextMeshProUGUI label)
        {
            var go = new GameObject("WristAcc_Field", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button)); var r = (RectTransform)go.transform; r.SetParent(parent, false);
            Place(r, topLeft, size); go.GetComponent<Image>().color = FieldBg;
            var o = go.GetComponent<Outline>(); o.effectColor = new Color(0.42f, 0.42f, 0.40f, 0.95f); o.effectDistance = new Vector2(1, -1);
            if (S("border_generic") != null) { o.enabled = false; Skin(r, "border_generic", new Color(0.62f, 0.62f, 0.58f, 1f), Image.Type.Sliced); }
            var btn = go.GetComponent<Button>(); var cb = btn.colors; cb.highlightedColor = new Color(1.9f, 1.9f, 1.8f, 1); cb.pressedColor = new Color(2.4f, 2.4f, 2.2f, 1); btn.colors = cb;
            label = Text(r, text, 18, FieldText, align);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.offsetMin = new Vector2(14, 0); label.rectTransform.offsetMax = new Vector2(-14, 0);
            return btn;
        }

        // SAVE AS: a name field (a copy of the game's own input field on this screen when there is one, so its hotkeys pause
        // while typing; else a plain TMP_InputField) + SAVE + DELETE, 356 wide like the dropdowns
        void PresetRow(InventoryScreen scr, RectTransform panel, Vector2 topLeft)
        {
            TMP_InputField inp = null; var game = scr.GetComponentsInChildren<TMP_InputField>(true).FirstOrDefault();
            if (game != null)
            {
                var go = Instantiate(game.gameObject, panel, false); go.name = "WristAcc_PresetName"; go.SetActive(true);
                inp = go.GetComponent<TMP_InputField>(); inp.onValueChanged.RemoveAllListeners(); inp.onEndEdit.RemoveAllListeners(); inp.onSubmit.RemoveAllListeners();
                inp.text = ""; Place((RectTransform)go.transform, topLeft, new Vector2(196, 36));
                Plugin.Log.LogInfo($"[WristAccessories] presets: name field = copy of the game's {game.name} ({game.GetType().Name})");
            }
            else
            {
                var bg = Field(panel, topLeft, new Vector2(196, 36), "", TextAlignmentOptions.MidlineLeft, out var txt); bg.transition = Selectable.Transition.None;
                var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)); var ar = (RectTransform)area.transform; ar.SetParent(bg.transform, false);
                ar.anchorMin = Vector2.zero; ar.anchorMax = Vector2.one; ar.offsetMin = new Vector2(10, 2); ar.offsetMax = new Vector2(-10, -2);
                txt.transform.SetParent(ar, false); txt.rectTransform.offsetMin = txt.rectTransform.offsetMax = Vector2.zero;
                inp = bg.gameObject.AddComponent<TMP_InputField>(); inp.textViewport = ar; inp.textComponent = txt; inp.fontAsset = _font; inp.characterLimit = 40;
                Plugin.Log.LogInfo("[WristAccessories] presets: no game input field on this screen - plain TMP_InputField");
            }
            var save = Field(panel, topLeft + new Vector2(204, 0), new Vector2(72, 36), "SAVE", TextAlignmentOptions.Center, out _);
            var del = Field(panel, topLeft + new Vector2(284, 0), new Vector2(72, 36), "DELETE", TextAlignmentOptions.Center, out var dl); dl.fontSize = 15;
            save.onClick.AddListener(() => { var n = string.IsNullOrWhiteSpace(inp.text) ? Presets.Last : inp.text; Presets.Save(n); Refresh(); });
            del.onClick.AddListener(() => { var n = string.IsNullOrWhiteSpace(inp.text) ? Presets.Last : inp.text; Presets.Delete(n); inp.text = ""; Refresh(); });
        }

        void MakeRow(RectTransform panel, Slot s, float y)
        {
            var cap = Text(panel, s.Caption, 17, Label, TextAlignmentOptions.MidlineLeft); cap.characterSpacing = 2;
            Place(cap.rectTransform, new Vector2(24, -y - 4), new Vector2(270, 40));
            s.Box = new Dropdown(this, s, panel, new Vector2(300, -y - 4));
        }

        // a tooltip: hover an option's name -> a small dark box with a line or two about it, next to the pointer
        GameObject _tip; TextMeshProUGUI _tipText;
        void AddTip(TextMeshProUGUI cap, string tip)
        {
            if (string.IsNullOrEmpty(tip)) return;
            cap.raycastTarget = true; cap.text += "  <color=#7d7c74><size=80%>(?)</size></color>";
            var et = cap.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter }; enter.callback.AddListener(_ => ShowTip(tip)); et.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit }; exit.callback.AddListener(_ => { if (_tip != null) _tip.SetActive(false); }); et.triggers.Add(exit);
        }
        void ShowTip(string tip)
        {
            if (_tip == null)
            {
                _tip = new GameObject("WristAcc_Tip", typeof(RectTransform), typeof(Image)); var r = (RectTransform)_tip.transform; r.SetParent(_page.transform, false);
                r.anchorMin = r.anchorMax = new Vector2(0, 0); r.pivot = new Vector2(0, 1); r.sizeDelta = new Vector2(420, 90);
                var bg = _tip.GetComponent<Image>(); bg.color = new Color(0.03f, 0.035f, 0.04f, 0.97f); bg.raycastTarget = false;
                if (S("border_generic") != null) Skin(r, "border_generic", new Color(0.62f, 0.62f, 0.58f, 1f), Image.Type.Sliced); else { var o = _tip.AddComponent<Outline>(); o.effectColor = Border; }
                _tipText = Text(r, "", 15, FieldText, TextAlignmentOptions.TopLeft); _tipText.enableWordWrapping = true; _tipText.overflowMode = TextOverflowModes.Overflow;
                _tipText.rectTransform.anchorMin = Vector2.zero; _tipText.rectTransform.anchorMax = Vector2.one; _tipText.rectTransform.offsetMin = new Vector2(12, 10); _tipText.rectTransform.offsetMax = new Vector2(-12, -10);
            }
            _tipText.text = tip; _tipText.ForceMeshUpdate();
            var tr = (RectTransform)_tip.transform; tr.sizeDelta = new Vector2(420, Mathf.Max(48f, _tipText.preferredHeight + 22f));
            _tip.transform.SetAsLastSibling(); _tip.SetActive(true); PlaceTip();
        }
        void PlaceTip()
        {
            if (_tip == null || !_tip.activeSelf) return;
            var page = (RectTransform)_page.transform; var cam = page.GetComponentInParent<Canvas>()?.rootCanvas?.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(page, Input.mousePosition, cam, out var lp))
                ((RectTransform)_tip.transform).anchoredPosition = lp - page.rect.min + new Vector2(18, -18);
        }
        void Update()
        {
            PlaceTip();
            if (AccessoriesTab.IconsPending && (_iconPoll += Time.unscaledDeltaTime) > 0.25f)
            {
                _iconPoll = 0;
                foreach (var (p, v) in _listPics) if (p != null) SetPic(p, v);
                Refresh();
                if (_page == null || !_page.activeSelf) AccessoriesTab.IconsPending = false;
            }
            if ((_page == null || !_page.activeSelf) && !AccessoriesTab.IconsPending) AccessoriesTab.EndSuitIcons();
        }

        public void Refresh() { foreach (var s in _slots) s.Box?.UpdateLabel(); }

        // a list of choices over the page, a click outside closes it
        public void OpenList(Slot s, RectTransform under)
        {
            CloseList();
            _list = new GameObject("WristAcc_List", typeof(RectTransform), typeof(Image), typeof(Button));
            var blocker = (RectTransform)_list.transform; blocker.SetParent(_page.transform, false); blocker.SetAsLastSibling();
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one; blocker.offsetMin = blocker.offsetMax = Vector2.zero;
            _list.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            _list.GetComponent<Button>().onClick.AddListener(CloseList);
            var ch = s.Choices(); string cur = s.Get();
            bool pics = ch.Any(c => HasPic(c.Item2)); _listPics.Clear();
            if (s.Group == "T") AccessoriesTab.RequestSuitIcons(ch.Select(c => c.Item2));
            float rowH = pics ? 62 : 36, h = Mathf.Min(ch.Count * rowH, pics ? 560 : 460);
            var box = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            var br = (RectTransform)box.transform; br.SetParent(blocker, false);
            box.GetComponent<Image>().color = new Color(0.05f, 0.055f, 0.058f, 0.98f);
            var bo = box.AddComponent<Outline>(); bo.effectColor = Border; bo.effectDistance = new Vector2(1, -1);
            // under the field, in the page's space (fields sit in panels now)
            var uc = new Vector3[4]; under.GetWorldCorners(uc); var tl = blocker.InverseTransformPoint(uc[1]); var bl = blocker.InverseTransformPoint(uc[0]);
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f); br.pivot = new Vector2(0, 1);
            float below = bl.y - 2 - h, top = bl.y - 2; if (below < blocker.rect.yMin + 10) top = tl.y + 2 + h;       // no room under it: above
            br.anchoredPosition = new Vector2(tl.x, top); br.sizeDelta = new Vector2(Mathf.Max(under.rect.width, 520f), h);
            if (S("border_generic") != null) { bo.enabled = false; var bd = Skin(br, "border_generic", new Color(0.62f, 0.62f, 0.58f, 1f), Image.Type.Sliced); bd.transform.SetAsLastSibling(); }
            if (S("popup_shadow") != null)                                      // the game's drop shadow under popups
            {
                var sh = new GameObject("Shadow", typeof(RectTransform), typeof(Image)); var shr = (RectTransform)sh.transform; shr.SetParent(blocker, false);
                shr.anchorMin = br.anchorMin; shr.anchorMax = br.anchorMax; shr.pivot = br.pivot; shr.anchoredPosition = br.anchoredPosition + new Vector2(-18, 18); shr.sizeDelta = br.sizeDelta + new Vector2(36, 36);
                var si = sh.GetComponent<Image>(); si.sprite = S("popup_shadow"); si.type = Image.Type.Sliced; si.color = new Color(0, 0, 0, 0.85f); si.raycastTarget = false;
                sh.transform.SetSiblingIndex(br.GetSiblingIndex());
            }
            var content = new GameObject("Content", typeof(RectTransform)); var cr = (RectTransform)content.transform; cr.SetParent(br, false);
            cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(0.5f, 1); cr.sizeDelta = new Vector2(0, ch.Count * rowH); cr.anchoredPosition = Vector2.zero;
            var sr = box.GetComponent<ScrollRect>(); sr.content = cr; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30; sr.viewport = br;
            for (int i = 0; i < ch.Count; i++)
            {
                var (label, value) = ch[i]; bool on = value == cur;
                var b = new GameObject("Choice", typeof(RectTransform), typeof(Image), typeof(Button)); var r = (RectTransform)b.transform; r.SetParent(cr, false);
                r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1); r.sizeDelta = new Vector2(0, rowH - 2); r.anchoredPosition = new Vector2(0, -i * rowH);
                var img = b.GetComponent<Image>(); img.color = on ? Picked : new Color(1, 1, 1, 0.03f);
                var btn = b.GetComponent<Button>(); var cb = btn.colors; cb.highlightedColor = on ? Color.white : new Color(4f, 4f, 3.8f, 1); btn.colors = cb;
                var t = Text(b.transform, label, 17, on ? PickedText : FieldText, TextAlignmentOptions.MidlineLeft);
                t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.offsetMin = new Vector2(pics ? 76 : 12, 0); t.rectTransform.offsetMax = new Vector2(-8, 0);
                if (pics) { var lp = Pic(r, null, new Vector2(8, -2), rowH - 6); SetPic(lp, value); _listPics.Add((lp, value)); }
                if (i > 0) { var dv = new GameObject("Divider", typeof(RectTransform), typeof(Image)); var dr = (RectTransform)dv.transform; dr.SetParent(r, false);
                    dr.anchorMin = new Vector2(0, 1); dr.anchorMax = new Vector2(1, 1); dr.pivot = new Vector2(0.5f, 1); dr.sizeDelta = new Vector2(-16, 1); dr.anchoredPosition = new Vector2(0, 1);
                    var di = dv.GetComponent<Image>(); di.color = new Color(1, 1, 1, 0.06f); di.raycastTarget = false; }
                string v = value; btn.onClick.AddListener(() => { Pick(s, v); CloseList(); });
            }
            int at = ch.FindIndex(c => c.Item2 == cur);                // the picked one in view
            if (at > 0 && ch.Count * rowH > h) sr.verticalNormalizedPosition = 1f - Mathf.Clamp01((at * rowH - h / 2) / (ch.Count * rowH - h));
        }

        public void CloseList() { if (_list != null) Destroy(_list); _list = null; }

        void Pick(Slot s, string v)
        {
            if (s.Get() == v) return;
            Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES tab: {s.Caption} picked '{v}' (was '{s.Get()}')");
            s.Set(v); Refresh();                                         // wrist slots shift when one is cleared
        }

        public TextMeshProUGUI Text(Transform parent, string text, float size, Color c, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>(); if (_font != null) t.font = _font;
            t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis; t.raycastTarget = false;
            return t;
        }

        public static void Place(RectTransform r, Vector2 topLeft, Vector2 size)
        { r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1); r.anchoredPosition = topLeft; r.sizeDelta = size; }

        public RectTransform Page => (RectTransform)_page.transform;

        // ITabController
        public void Show()
        {
            if (_page == null) return;
            _page.SetActive(true); _page.transform.SetAsLastSibling(); Refresh(); _viewer?.Open();
            Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES tab open: {string.Join(", ", _slots.Where(s => s.Get != null).Select(s => $"{s.Caption}='{s.Get()}'"))}");
        }
        public Task<bool> TryHide() { Hide(); return Task.FromResult(true); }
        public void ReloadViewer() { if (_viewer != null && _page != null && _page.activeSelf) { _viewer.Release(); _viewer.Open(); } }
        public void Hide() { CloseList(); _viewer?.Release(); if (_page != null) _page.SetActive(false); }
        void OnDestroy() { if (_page != null) Destroy(_page); }

        // the game switches our tab off after we make it (0.7.6 diag: "active False", the screen's own pass over its tabs):
        // while the game's tabs are up, ours is too
        // 1.0.4 test (user 2026-10-28: WARDROBE "disappears after single press" without ICUI): what is under the pointer when it is
        // pressed on our tab, and every change of our tab's place / state, so one run shows what takes the click
        int _sibSeen = -1; bool _actSeen, _selSeen; Transform _parentSeen;
        void Watch()
        {
            if (Tab == null) return;
            var tr = Tab.transform; int sib = tr.GetSiblingIndex(); bool act = Tab.gameObject.activeInHierarchy, sel = Tab._selectedVersion != null && Tab._selectedVersion.activeSelf;
            if (sib != _sibSeen || act != _actSeen || sel != _selSeen || tr.parent != _parentSeen)
            {
                Plugin.Log.LogInfo($"[WristAccessories] WARDROBE tab watch (frame {Time.frameCount}): sibling {sib}/{(tr.parent != null ? tr.parent.childCount : 0)} under {(tr.parent != null ? tr.parent.name : "-")}, active {act}, selected {sel}, row: {(tr.parent != null ? string.Join(", ", Enumerable.Range(0, tr.parent.childCount).Select(i => tr.parent.GetChild(i).name + (tr.parent.GetChild(i).gameObject.activeSelf ? "" : "(off)"))) : "")}");
                _sibSeen = sib; _actSeen = act; _selSeen = sel; _parentSeen = tr.parent;
            }
            if (!Input.GetMouseButtonDown(0) || UnityEngine.EventSystems.EventSystem.current == null) return;
            var rt = (RectTransform)tr; var cam = rt.GetComponentInParent<Canvas>()?.rootCanvas?.worldCamera;
            var c = new Vector3[4]; rt.GetWorldCorners(c);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, c[0]), b = RectTransformUtility.WorldToScreenPoint(cam, c[2]); var m = (Vector2)Input.mousePosition;
            if (m.x < a.x - 40 || m.x > b.x + 40 || m.y < a.y - 30 || m.y > b.y + 30) return;
            var hits = new List<UnityEngine.EventSystems.RaycastResult>();
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = m }, hits);
            string P(Transform t) { var q = t.name; for (int k = 0; k < 3 && t.parent != null; k++) { t = t.parent; q = t.name + "/" + q; } return q; }
            Plugin.Log.LogInfo($"[WristAccessories] WARDROBE press at {m.x:0},{m.y:0} (our tab {a.x:0},{a.y:0}..{b.x:0},{b.y:0}, interactable {Tab.Interactable}): top hits {string.Join(" | ", hits.Take(6).Select(h => P(h.gameObject.transform)))}");
        }

        void LateUpdate()
        {
            try { Watch(); KeepPlace(); } catch (Exception e) { Plugin.Log.LogError("[WristAccessories] WARDROBE watch: " + e.Message); }
            if (Tab == null || Tab.gameObject.activeSelf) return;
            var bar = Tab.transform.parent; if (bar == null || !bar.gameObject.activeInHierarchy) return;
            Tab.gameObject.SetActive(true);
            if (!_reactivatedLogged) { _reactivatedLogged = true; Plugin.Log.LogInfo("[WristAccessories] ACCESSORIES tab was switched off by the game - switched back on"); }
        }
    }

    // a dropdown: a box with what is picked and an arrow; a click opens the list under it
    // a number with - and + (5 deg steps), e.g. where an inner-wrist watch sits; applies live (the rig refits)
    class Nudge : IBox
    {
        readonly Slot _s; readonly TextMeshProUGUI _v;
        public Nudge(Host h, Slot s, RectTransform panel, Vector2 topLeft, float width)
        {
            _s = s;
            var minus = h.Field(panel, topLeft, new Vector2(56, 36), "\u25C4", TextAlignmentOptions.Midline, out _);
            var mid = h.Field(panel, topLeft + new Vector2(62, 0), new Vector2(width - 124, 36), "", TextAlignmentOptions.Midline, out _v); mid.interactable = false;
            var plus = h.Field(panel, topLeft + new Vector2(width - 56, 0), new Vector2(56, 36), "\u25BA", TextAlignmentOptions.Midline, out _);
            float st = s.Caption == "SLIDE" ? 2f : 5f; minus.onClick.AddListener(() => Step(-st)); plus.onClick.AddListener(() => Step(st));
            UpdateLabel();
        }
        void Step(float d)
        {
            float v = float.Parse(_s.Get(), System.Globalization.CultureInfo.InvariantCulture) + d;
            _s.Set(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES tab: {_s.Caption} -> {_s.Get()}");
            UpdateLabel();
        }
        // just the angle; dimmed while the watch face is not on the inner wrist (it only applies there)
        // the picked item's turn round the arm, signed
        public void UpdateLabel()
        {
            float v = float.Parse(_s.Get(), System.Globalization.CultureInfo.InvariantCulture);
            string u = _s.Caption == "SLIDE" ? " mm" : _s.Caption == "SKIN SHADE" ? "%" : "\u00B0";
            _v.text = v == 0 ? "0" + u : $"{v:+0;-0}{u}";
        }
    }

    // a few choices side by side (LEFT / RIGHT / BOTH), the picked one beige like a selected tab
    class Switch : IBox
    {
        readonly Slot _s; readonly List<(string v, Image bg, TextMeshProUGUI t)> _b = new List<(string, Image, TextMeshProUGUI)>();
        public Switch(Host h, Slot s, RectTransform panel, Vector2 topLeft, float width)
        {
            _s = s; var ch = s.Choices(); float w = (width - (ch.Count - 1) * 6f) / ch.Count;
            for (int i = 0; i < ch.Count; i++)
            {
                var (label, value) = ch[i];
                var btn = h.Field(panel, topLeft + new Vector2(i * (w + 6f), 0), new Vector2(w, 36), label, TextAlignmentOptions.Midline, out var t);
                t.fontSize = 15; t.characterSpacing = 2; _b.Add((value, btn.GetComponent<Image>(), t));
                string v = value; btn.onClick.AddListener(() => { if (_s.Get() == v) return; Plugin.Log.LogInfo($"[WristAccessories] ACCESSORIES tab: {_s.Caption} picked '{v}' (was '{_s.Get()}')"); _s.Set(v); UpdateLabel(); });
            }
            UpdateLabel();
        }
        public void UpdateLabel()
        {
            string cur = _s.Get();
            foreach (var (v, bg, t) in _b) { bool on = v == cur; bg.color = on ? new Color(0.77f, 0.76f, 0.69f, 1f) : new Color(0.075f, 0.08f, 0.085f, 0.96f); t.color = on ? new Color(0.07f, 0.07f, 0.07f) : new Color(0.86f, 0.85f, 0.78f); }
        }
    }

    // a slot as the GEAR screen draws one: header strip with its name, slot fill, border, the item's picture inside
    class Tile : IBox
    {
        readonly Slot _s; readonly RawImage _pic; readonly TextMeshProUGUI _name, _under; readonly Image _border;
        public Tile(Host h, Slot s, RectTransform panel, Vector2 topLeft, bool nameUnder = false)
        {
            _s = s;
            if (nameUnder)                                                      // the suit's name under the tile, like the game's clothing slots
            {
                _under = h.Text(panel, "", 12, new Color(0.86f, 0.85f, 0.78f), TextAlignmentOptions.Center); _under.enableWordWrapping = false; _under.overflowMode = TextOverflowModes.Ellipsis;
                Host.Place(_under.rectTransform, topLeft + new Vector2(0, -(HeaderH + BodyH + 2)), new Vector2(TileWidth, 20));
            }
            var hg = new GameObject("WristAcc_TileHeader", typeof(RectTransform), typeof(Image)); var hr = (RectTransform)hg.transform; hr.SetParent(panel, false);
            Host.Place(hr, topLeft, new Vector2(TileWidth, HeaderH));
            var hi = hg.GetComponent<Image>(); hi.sprite = Host.S("header_slot"); hi.color = hi.sprite != null ? Color.white : new Color(0.2f, 0.21f, 0.21f, 1f); hi.raycastTarget = false;
            var ht = h.Text(hr, s.Caption, 13, new Color(0.80f, 0.79f, 0.72f), TextAlignmentOptions.MidlineLeft); ht.characterSpacing = 1;
            ht.rectTransform.anchorMin = Vector2.zero; ht.rectTransform.anchorMax = Vector2.one; ht.rectTransform.offsetMin = new Vector2(8, 0); ht.rectTransform.offsetMax = new Vector2(-4, 0);
            var bg = new GameObject("WristAcc_Tile_" + s.Caption, typeof(RectTransform), typeof(Image), typeof(Button)); var r = (RectTransform)bg.transform; r.SetParent(panel, false);
            Host.Place(r, topLeft + new Vector2(0, -HeaderH), new Vector2(TileWidth, BodyH));
            var bi = bg.GetComponent<Image>(); bi.sprite = Host.S("slot_empty_fill"); bi.type = Image.Type.Tiled; bi.color = bi.sprite != null ? Color.white : new Color(0.06f, 0.065f, 0.07f, 0.95f);
            var btn = bg.GetComponent<Button>(); var cb = btn.colors; cb.highlightedColor = new Color(1.35f, 1.35f, 1.3f, 1); cb.pressedColor = new Color(1.6f, 1.6f, 1.5f, 1); btn.colors = cb;
            btn.onClick.AddListener(() => h.OpenList(_s, r));
            _pic = Host.Pic(r, null, new Vector2((TileWidth - 100) / 2, -(BodyH - 100) / 2), 100);
            _name = h.Text(r, "", 12, new Color(0.86f, 0.85f, 0.78f), TextAlignmentOptions.Center); _name.enableWordWrapping = true;
            _name.rectTransform.anchorMin = Vector2.zero; _name.rectTransform.anchorMax = Vector2.one; _name.rectTransform.offsetMin = new Vector2(6, 6); _name.rectTransform.offsetMax = new Vector2(-6, -6);
            _border = Host.Skin(r, Host.S("slot_border_full") != null ? "slot_border_full" : "Allscreens_Borders_Default", new Color(0.55f, 0.55f, 0.52f, 1f), Image.Type.Sliced);
            if (_border.sprite == null) { _border.enabled = false; var o = bg.AddComponent<Outline>(); o.effectColor = new Color(0.42f, 0.42f, 0.40f, 0.95f); o.effectDistance = new Vector2(1, -1); }
            if (Host.S("slot_border_current") != null)                          // hover: the GEAR screen's selected-slot border
            {
                var hv = Host.Skin(r, "slot_border_current", Color.white, Image.Type.Sliced); hv.enabled = false;
                var et = bg.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter }; enter.callback.AddListener(_ => hv.enabled = true); et.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit }; exit.callback.AddListener(_ => hv.enabled = false); et.triggers.Add(exit);
            }
            UpdateLabel();
        }
        const float TileWidth = 128f, HeaderH = 24f, BodyH = 112f;
        public void UpdateLabel()
        {
            string v = _s.Get();
            if (_s.Group == "T") AccessoriesTab.RequestSuitIcons(new[] { v });
            bool tex = Host.SetPic(_pic, v == "left" || v == "right" ? "skullring" : v);
            var ch = _s.Choices(); int i = ch.FindIndex(c => c.Item2 == v);
            bool empty = string.IsNullOrEmpty(v) || v == "none";
            if (_under != null) _under.text = i >= 0 ? ch[i].Item1 : "";
            _name.text = tex || empty || _under != null ? (string.IsNullOrEmpty(v) && _s.Caption == "WATCH" ? "OUTFIT'S OWN" : string.IsNullOrEmpty(v) && _s.Caption.EndsWith(" HAND") ? "YOUR OWN" : "") : (i >= 0 ? ch[i].Item1 : v);
            _border.color = empty ? new Color(0.40f, 0.40f, 0.38f, 1f) : new Color(0.78f, 0.77f, 0.70f, 1f);
        }
    }

    class Dropdown : IBox
    {
        readonly Slot _s; readonly TextMeshProUGUI _label; readonly RawImage _pic;
        public Dropdown(Host h, Slot s, RectTransform panel, Vector2 topLeft, float width = 600f)
        {
            _s = s;
            var btn = h.Field(panel, topLeft, new Vector2(width, 36), "", TextAlignmentOptions.MidlineLeft, out _label);
            _label.fontSize = 16;
            _label.rectTransform.offsetMax = new Vector2(-40, 0);
            var rt = (RectTransform)btn.transform; btn.onClick.AddListener(() => h.OpenList(_s, rt));
            _pic = Host.Pic(rt, null, new Vector2(6, -2), 32);
            if (Host.S("arrow_down_tiny") != null)
            {
                var ag = new GameObject("Arrow", typeof(RectTransform), typeof(Image)); var ar = (RectTransform)ag.transform; ar.SetParent(rt, false);
                ar.anchorMin = ar.anchorMax = new Vector2(1, 0.5f); ar.pivot = new Vector2(1, 0.5f); ar.anchoredPosition = new Vector2(-14, 0); ar.sizeDelta = new Vector2(12, 10);
                var ai = ag.GetComponent<Image>(); ai.sprite = Host.S("arrow_down_tiny"); ai.color = new Color(0.75f, 0.74f, 0.68f); ai.raycastTarget = false;
            }
            else
            {
                var arrow = h.Text(rt, "\u25BE", 18, new Color(0.60f, 0.60f, 0.55f), TextAlignmentOptions.Midline);
                arrow.rectTransform.anchorMin = new Vector2(1, 0); arrow.rectTransform.anchorMax = new Vector2(1, 1); arrow.rectTransform.pivot = new Vector2(1, 0.5f);
                arrow.rectTransform.sizeDelta = new Vector2(36, 0); arrow.rectTransform.anchoredPosition = Vector2.zero;
            }
            UpdateLabel();
        }
        public void UpdateLabel()
        {
            string v = _s.Get(); var ch = _s.Choices(); int i = ch.FindIndex(c => c.Item2 == v);
            _label.text = i >= 0 ? ch[i].Item1 : (string.IsNullOrEmpty(v) ? "None" : v);
            bool tex = Host.SetPic(_pic, v);
            _label.rectTransform.offsetMin = new Vector2(tex ? 50 : 14, 0);
        }
    }

    // the viewer's lights burn only between this camera's cull and its last draw, so no other camera ever sees them
    class StageLights : MonoBehaviour
    {
        public Light[] Lights;
        void OnPreCull() { Set(true); }
        void OnPostRender() { Set(false); }
        void OnDisable() { Set(false); }
        void Set(bool on) { if (Lights != null) foreach (var l in Lights) if (l != null) l.enabled = on; }
    }

    // your character on a stage of its own (far from every other camera), loaded by the game's PlayerModelLoader like the
    // character screen's model (so our preview fitting puts the accessories on it and refits on every pick), seen by our own
    // camera into a texture: aimed at the wrist, drag to turn round the forearm, scroll to zoom. Lights light only that layer.
    class WristViewer : MonoBehaviour, IDragHandler, IScrollHandler
    {
        public EFT.Profile Profile;
        PlayerModelLoader _loader; GameObject _stage; Camera _cam; RenderTexture _rt; System.Threading.CancellationTokenSource _cts;
        Transform _f3, _palm; bool _loading;
        // BODY (user 2026-10-26: "focus on the middle of the character and make it move left to right") or a wrist close-up;
        // both sway slowly left and right, a drag turns it and pauses the sway for 3 s
        public string Mode { get; private set; } = "body";
        float _yaw, _pitch = 8f, _bodyZoom = 1f, _wristDist = 0.42f, _dragAt = -10f; Vector3 _center; float _height;

        public void SetMode(string m)
        {
            if (Mode == m) return; Mode = m; _f3 = _palm = null; _yaw = 0f; _pitch = m == "body" ? 8f : 15f;
            Plugin.Log.LogInfo($"[WristAccessories] viewer: {(m == "body" ? "body" : m == "L" ? "left wrist" : "right wrist")}");
        }

        public void Open()
        {
            if (_stage == null)
            {
                int layer = LayersMaskController.WeaponPreview;
                _stage = new GameObject("WristAcc_ViewerStage"); _stage.transform.position = new Vector3(4000f, 4000f, 4000f); DontDestroyOnLoad(_stage);
                var cgo = new GameObject("Camera"); cgo.transform.SetParent(_stage.transform, false); _cam = cgo.AddComponent<Camera>();
                var box = ((RectTransform)transform).rect.size; int rw = 1024, rh = box.x > 1 ? Mathf.Clamp(Mathf.RoundToInt(1024 * box.y / box.x), 256, 2048) : 1024;
                _rt = new RenderTexture(rw, rh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "WristAcc_Viewer" };
                Plugin.Log.LogInfo($"[WristAccessories] viewer: picture {rw}x{rh} for a {box.x:0}x{box.y:0} box");
                _cam.targetTexture = _rt; _cam.cullingMask = 1 << layer; _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.045f, 0.05f, 0.053f, 1f);
                _cam.fieldOfView = 30f; _cam.nearClipPlane = 0.01f; _cam.farClipPlane = 20f;
                // EFT renders deferred, which does not keep a light to its culling mask: lit in raids (reports 2026-10-28)
                var lights = new List<Light>();
                foreach (var (rot, intensity) in new[] { (new Vector3(35, 150, 0), 1.1f), (new Vector3(10, -40, 0), 0.45f), (new Vector3(-30, 60, 0), 0.25f) })
                {
                    var l = new GameObject("Light").AddComponent<Light>(); l.transform.SetParent(_stage.transform, false); l.transform.rotation = Quaternion.Euler(rot);
                    l.type = LightType.Directional; l.intensity = intensity; l.cullingMask = 1 << layer; l.color = new Color(1f, 0.97f, 0.92f); l.shadows = LightShadows.None;
                    l.enabled = false; lights.Add(l);
                }
                cgo.AddComponent<StageLights>().Lights = lights.ToArray();
                GetComponent<RawImage>().texture = _rt;
            }
            _stage.SetActive(true); _cam.enabled = true;
            Plugin.Log.LogInfo("[WristAccessories] viewer: stage on (lights only while the viewer camera draws)");
            if (_loader == null && !_loading) _ = Load();
        }

        async Task Load()
        {
            if (Profile == null) { Plugin.Log.LogWarning("[WristAccessories] viewer: no profile on this screen"); return; }
            _loading = true; _cts = new System.Threading.CancellationTokenSource(); var t0 = Time.realtimeSinceStartup;
            try
            {
                var loader = new PlayerModelLoader();
                await loader.Load(Profile.GetVisualEquipmentState(false), null, 0f, EFT.ThirdPersonCustomizationFilter.Default, null, _cts.Token, Vector3.zero, _stage.transform, LayersMaskController.WeaponPreview, false);
                if (_cts == null || _cts.IsCancellationRequested) { loader.Clear(); return; }
                _loader = loader; _f3 = _palm = null; Frame();
                Plugin.Log.LogInfo($"[WristAccessories] viewer: character loaded in {Time.realtimeSinceStartup - t0:0.0} s (body {(loader.PlayerBody != null ? loader.PlayerBody.name : "none")})");
            }
            catch (Exception e) { Plugin.Log.LogError("[WristAccessories] viewer load: " + e); }
            finally { _loading = false; }
        }

        public void Release()
        {
            try { _cts?.Cancel(); } catch { }
            _cts = null; if (_loader != null) { _loader.Clear(); _loader = null; Plugin.Log.LogInfo("[WristAccessories] viewer: character released"); }
            _f3 = _palm = null; if (_cam != null) _cam.enabled = false;
            if (_stage != null && _stage.activeSelf) { _stage.SetActive(false); Plugin.Log.LogInfo("[WristAccessories] viewer: stage off"); }
        }

        // the body's middle and height (its renderers' bounds), kept in the body's own space so the idle animation does not drift it
        void Frame()
        {
            var root = _loader.PlayerBody.transform; Bounds b = default; bool any = false;
            foreach (var r in _loader.PlayerBody.GetComponentsInChildren<Renderer>(false))
            { if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (!any) { _center = Vector3.up; _height = 1.8f; }
            else { _center = root.InverseTransformPoint(b.center); _height = Mathf.Clamp(b.size.y, 0.8f, 2.4f); }
            Plugin.Log.LogInfo($"[WristAccessories] viewer: body middle {_center} (body space), height {_height:0.00} m, facing {root.forward}");
        }

        void LateUpdate()
        {
            if (_cam == null || !_cam.enabled || _loader?.PlayerBody == null) return;
            float sway = Time.unscaledTime - _dragAt < 3f ? 0f : Mathf.Sin(Time.unscaledTime * 0.55f) * (Mode == "body" ? 35f : 25f);
            if (Mode == "body")
            {
                var root = _loader.PlayerBody.transform; var target = root.TransformPoint(_center);
                var fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up); if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
                var dir = Quaternion.AngleAxis(_yaw + sway, Vector3.up) * fwd; dir = (dir + Vector3.up * Mathf.Sin(_pitch * Mathf.Deg2Rad)).normalized;
                float dist = _height * 0.62f / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * _bodyZoom;
                _cam.transform.position = target + dir * dist; _cam.transform.LookAt(target, Vector3.up);
                return;
            }
            if (_f3 == null || _palm == null)
            {
                foreach (var t in _loader.PlayerBody.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == $"Base Human{Mode}Forearm3") _f3 = t;
                    else if (t.name == $"Base Human{Mode}Palm") _palm = t;
                }
                if (_f3 == null || _palm == null) return;
                Plugin.Log.LogInfo($"[WristAccessories] viewer: aimed at {_f3.name} -> {_palm.name}");
            }
            var axis = (_palm.position - _f3.position).normalized; var wt = Vector3.Lerp(_f3.position, _palm.position, 0.25f);
            var side = Vector3.Cross(axis, Vector3.up); if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(axis, Vector3.forward); side.Normalize();
            var wd = Quaternion.AngleAxis(200f + _yaw + sway, axis) * side; wd = (wd + axis * Mathf.Sin(_pitch * Mathf.Deg2Rad)).normalized;
            _cam.transform.position = wt + wd * _wristDist; _cam.transform.LookAt(wt, Vector3.up);
        }

        public void OnDrag(PointerEventData e) { _dragAt = Time.unscaledTime; _yaw += e.delta.x * 0.4f; _pitch = Mathf.Clamp(_pitch - e.delta.y * 0.25f, -60f, 60f); }
        public void OnScroll(PointerEventData e)
        {
            float f = 1f - e.scrollDelta.y * 0.08f;
            if (Mode == "body") _bodyZoom = Mathf.Clamp(_bodyZoom * f, 0.35f, 1.6f); else _wristDist = Mathf.Clamp(_wristDist * f, 0.15f, 1.2f);
        }

        void OnDestroy()
        {
            Release();
            if (_stage != null) Destroy(_stage);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
