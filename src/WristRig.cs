using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
using Diz.Resources;
using Diz.Skinning;
using EFT;
using EFT.Visual;
using UnityEngine;
using UnityEngine.Rendering;

namespace WristAccessories;

// The accessories on the local player's first-person hands: rebuilt when the outfit or the picks change, shown only
// while the hands are (first person), on the hands' layer. With Preview set (UI character previews, e.g. Improved
// Customization UI's tab): on that body's third-person skeleton, fitted to its upper-body (jacket) sleeves.
class WristRig : MonoBehaviour
{
    public PlayerBody Preview;
    PlayerBody _body; LoddedSkin _hands; string _key = ""; bool _busy;
    readonly List<GameObject> _objs = new List<GameObject>();
    float _next;
    static readonly Dictionary<string, (Mesh mesh, Material[] mats)> MatCache = new Dictionary<string, (Mesh, Material[])>();
    static readonly HashSet<string> Held = new HashSet<string>();
    static readonly List<Diz.DependencyManager.DependencyGraph<IEasyBundle>.Token> Tokens = new List<Diz.DependencyManager.DependencyGraph<IEasyBundle>.Token>();
    static bool Alive(Material m) => m != null && m.shader != null && m.shader.name != "Hidden/InternalErrorShader";

    // live data: the layouts / outlines / textures in the plugin folder are re-read when they change on disk (no restart)
    public static int DataVersion;
    static float _nextScan; static DateTime _stamp;
    static void ScanData()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 2f;
        try
        {
            var dir = WristFit.Dir; if (dir == null) return;
            var files = System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "acc")).Concat(System.IO.Directory.GetFiles(dir, "outlines*.json"));
            var latest = files.Select(f => System.IO.File.GetLastWriteTimeUtc(f)).DefaultIfEmpty(DateTime.MinValue).Max();
            if (_stamp == default) { _stamp = latest; return; }
            if (latest <= _stamp) return;
            _stamp = latest;
            WristFit.Load(dir); Variants.Clear(); MatCache.Clear(); DataVersion++;
            Plugin.Log.LogInfo($"[WristAccessories] fit data changed on disk: reloaded (version {DataVersion}), rebuilding");
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] data reload: " + e.Message); }
    }

    void Update()
    {
        if (Preview == null) ScanData();
        if (Time.unscaledTime >= _next && !_busy)
        {
            _next = Time.unscaledTime + 0.5f;
            bool preview = Preview != null;
            if (!preview && _wasPreview) { Clear(); Destroy(this); return; }        // its preview body is gone
            var gw = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            var body = preview ? Preview : gw?.MainPlayer?.PlayerBody;
            // a scav (raid or its preview) only when "On your scav" is on (user 2026-10-27: a toggle, not always)
            bool scav = body != null && body.PlayerSide != null && body.PlayerSide.Value == EPlayerSide.Savage;
            if (scav && !Plugin.OnScav.Value)
            {
                if (_key != "scav") Plugin.Log.LogInfo($"[WristAccessories] {(preview ? "preview: " : "")}scav - nothing worn (setting 'On your scav' is off)");
                if (_key != "scav") { _key = "scav"; _body = null; _hands = null; Clear(); }
                return;
            }
            bool fp = !preview;                                                    // first-person hands (raid) or a preview body
            var part = fp ? EBodyModelPart.Hands : EBodyModelPart.Body;
            LoddedSkin hands = null;
            if (body != null) body.BodySkins.TryGetValue(part, out hands);
            string hid = body != null && body.BodyCustomization != null && body.BodyCustomization.TryGetValue(part, out var id) ? id.ToString() : "";
            string hidNote = null;
            if (hands != null) hid = WristFit.DrawnId(hid, hands.GetRenderers().OfType<SkinnedMeshRenderer>().Select(r => r.sharedMesh != null ? r.sharedMesh.name : null), !fp, out hidNote);
            string key = body == null || hands == null ? "" : $"{hid}|{hands.GetInstanceID()}|{Plugin.Watch.Value}|{Plugin.LeftWrist.Value}|{Plugin.RightWrist.Value}|{Plugin.RingHand.Value}|{Plugin.Rings.Value}|{Plugin.WatchFace.Value}|{Plugin.WatchWrist.Value}|{Plugin.InnerAngle.Value:0}|{Plugin.ItemAngles.Value}|{Plugin.Handwear.Value}|{Plugin.ItemSlides.Value}|{Plugin.SleeveFit.Value:0.00}|{Plugin.LooseSize.Value}|{Plugin.CleanWatchShadow.Value}|{Plugin.Straighten.Value}|{Plugin.MatchSkinTone.Value}|{Plugin.SkinShade.Value:0}|{Plugin.OnScav.Value}|{Plugin.WatchesInSlots.Value}|{Plugin.Shoes.Value}|{Plugin.DogTagPicks.Value}|{Plugin.WatchWorn.Value}|{Plugin.DogTagFlips.Value}|{Plugin.DoubleSided.Value}|{Plugin.TattooSizes.Value}|{Plugin.TattooDark.Value:0}|{Plugin.SoftInk.Value}|{Plugin.SeamBlend.Value}|{DataVersion}";   // 1.1.0: tattoo sizes + seam blend rebuild too (user: "the tattoo size modifier doesnt work")
            if (key != _key)
            {
                _key = key; _body = body; _hands = hands; Clear();
                if (hidNote != null) Plugin.Log.LogInfo($"[WristAccessories] {(preview ? "preview " : "")}outline: {hidNote}");
                if (key != "") { HideOutfitWatch(body, preview); _ = Rebuild(body, hands, hid, preview, fp); }
            }
        }
        if (_objs.Count > 0 && _hands != null)
        {
            var r = _hands.GetRenderers().FirstOrDefault();
            bool on = r != null && r.enabled && r.gameObject.activeInHierarchy;
            foreach (var o in _objs) if (o != null && o.activeSelf != on) o.SetActive(on);
        }
    }

    bool _wasPreview;
    void Start() { _wasPreview = Preview != null; }
    void OnDestroy() => Clear();

    void Clear()
    {
        if (Preview == null && !_wasPreview) { MainWatchFace = null; MainWatchName = null; }
        foreach (var o in _objs) if (o != null) Destroy(o);
        _objs.Clear();
        HandWear.Restore(_swaps);                                       // the player's own hands back
        RestoreOutfitWatch();
    }
    readonly List<HandWear.Swap> _swaps = new List<HandWear.Swap>();
    readonly List<HandWear.Swap> _watchCuts = new List<HandWear.Swap>();

    void RestoreOutfitWatch()
    {
        foreach (var kv in _hidden) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;     // the outfit's own watch back
        HandWear.Restore(_watchCuts);                                                          // the top's baked watch back
        _hidden.Clear();
        if (_hiddenDial != null) _hiddenDial.SetActive(true);
        _hiddenDial = null;
    }

    // ---- the outfit's own watch: its submeshes (own materials) made invisible, its moving hands switched off ----
    static readonly string[] WatchWords = { "watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin" };
    static readonly System.Reflection.FieldInfo WatchesField = HarmonyLib.AccessTools.Field(typeof(PlayerBody), "_watches");
    readonly Dictionary<Renderer, Material[]> _hidden = new Dictionary<Renderer, Material[]>();
    GameObject _hiddenDial;
    static Material _invisible;

    static Material Invisible(Material like)
    {
        if (_invisible == null)
        {
            var sh = Shader.Find("Sprites/Default");
            if (sh != null) { _invisible = new Material(sh) { name = "WristAcc_invisible", color = new Color(0, 0, 0, 0) }; Plugin.Log.LogInfo("[WristAccessories] invisible material: Sprites/Default, alpha 0"); }
        }
        if (_invisible != null) return _invisible;
        var m = new Material(like) { name = like.name + "_hidden" };                  // fallback: cut everything away
        if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 1.01f);
        if (m.HasProperty("_Color")) { var c = m.GetColor("_Color"); c.a = 0; m.SetColor("_Color", c); }
        return m;
    }

    void HideOutfitWatch(PlayerBody body, bool preview)
    {
        if (string.IsNullOrEmpty(Plugin.Watch.Value)) return;
        // the preview body's top has its watch baked into its own material (no watch material to hide): cut its triangles out
        if (preview)
        {
            int cut = 0; var none = new List<string>();
            if (body.BodySkins.TryGetValue(EBodyModelPart.Body, out var top))
                foreach (var r in top.GetRenderers().OfType<SkinnedMeshRenderer>())
                {
                    if (HandWear.HasTopWatch(r)) { try { if (HandWear.CutTopWatch(r, _watchCuts, "preview")) cut++; } catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] top watch (preview): {r.name}: {e.Message}"); } }
                    else if (r.sharedMesh != null) none.Add(r.sharedMesh.name);
                }
            if (cut == 0) Plugin.Log.LogInfo($"[WristAccessories] top watch (preview): no baked-watch table for {(none.Count > 0 ? string.Join(", ", none) : "the top")} - its own watch (if any) stays");
        }
        int n = 0, cleaned = 0;
        foreach (var skin in body.BodySkins.Values)
            foreach (var r in skin.GetRenderers())
            {
                var mats = r.sharedMaterials; bool any = false; var nm = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    bool w = mats[i] != null && WatchWords.Any(x => mats[i].name.ToLowerInvariant().Contains(x));
                    nm[i] = w ? Invisible(mats[i]) : mats[i]; any |= w; if (w) n++;
                }
                if (!any) continue;
                for (int i = 0; i < mats.Length; i++)                         // the skin under it without the watch's painted shadow
                    if (nm[i] == mats[i]) { var f = SkinShadow.Fixed(mats[i]); if (f != null) { nm[i] = f; cleaned++; } }
                _hidden[r] = mats; r.sharedMaterials = nm;
            }
        var dial = WatchesField?.GetValue(body) as Component;
        if (dial != null && dial.gameObject.activeSelf) { dial.gameObject.SetActive(false); _hiddenDial = dial.gameObject; }
        Plugin.Log.LogInfo($"[WristAccessories] outfit watch hidden: {n} submesh(es) on {_hidden.Count} renderer(s), moving hands {(_hiddenDial != null ? "off" : "none")}, skin cleaned of its painted shadow on {cleaned} submesh(es){(n > 0 && cleaned == 0 ? (Plugin.CleanWatchShadow.Value ? " (no shadow data for this skin)" : " (turned off)") : "")}");
    }

    static List<string> Names(string csv) => (csv ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0 && WristFit.Accs.ContainsKey(s)).ToList();

    /// <summary>The main player's fitted watch on the left wrist (first person): its face direction (radians, 0 = back of
    /// the hand) and name; null = none (the outfit's own watch or no watch). Read by WatchAnimsCompat.</summary>
    public static float? MainWatchFace; public static string MainWatchName;
    static string Who(bool preview, bool fp) => !preview ? "first person" : "preview";

    async Task Rebuild(PlayerBody body, LoddedSkin hands, string hid, bool preview, bool fp)
    {
        _busy = true;
        if (!preview) { MainWatchFace = null; MainWatchName = null; LiveChecks.Clear(); }
        try
        {
            var left = Names(Plugin.LeftWrist.Value); var right = Names(Plugin.RightWrist.Value);
            if (!Plugin.WatchesInSlots.Value)                                                          // watches left in wrist slots from when it was on
            {
                int n = left.RemoveAll(x => x.StartsWith("watch_")) + right.RemoveAll(x => x.StartsWith("watch_"));
                if (n > 0) Plugin.Log.LogInfo($"[WristAccessories] watches in wrist slots: off, {n} watch(es) in the LEFT/RIGHT slots not worn");
            }
            else Plugin.Log.LogInfo($"[WristAccessories] watches in wrist slots: on, {left.Count(x => x.StartsWith("watch_"))} left / {right.Count(x => x.StartsWith("watch_"))} right in the slots");
            string ww = (Plugin.WatchWrist.Value ?? "left").ToLowerInvariant();
            if (WristFit.Accs.ContainsKey(Plugin.Watch.Value ?? ""))                                   // the picked watch: nearest the hand
            {
                if (ww != "right") left.Insert(0, Plugin.Watch.Value);
                if (ww == "right" || ww == "both") right.Insert(0, Plugin.Watch.Value);
                Plugin.Log.LogInfo($"[WristAccessories] watch {Plugin.Watch.Value} on the {(ww == "both" ? "left and right wrists" : ww + " wrist")}");
            }
            var plan = new List<(string part, string bone, List<WristFit.Acc> items)>
            {
                ("L", "Base HumanLForearm3", left.Distinct().Select(n => WristFit.Accs[n]).ToList()),
                ("R", "Base HumanRForearm3", right.Distinct().Select(n => WristFit.Accs[n]).ToList()),
            };
            // the old RING setting (skull ring on the ring finger) is gone: rings are the FINGERS tab's (HandWear.BuildRings)
            plan = plan.Where(p => p.items.Count > 0).ToList();
            var hw = (Plugin.Handwear.Value ?? "").Split(',').Select(x => x.Trim()).Where(x => HandWear.Items.ContainsKey(x)).Distinct().ToList();
            var shoes = HandWear.Items.TryGetValue(Plugin.Shoes.Value ?? "", out var sh) && sh.kind == "feet" ? new List<string> { sh.name } : new List<string>();
            var rings = HandWear.RingPicks(Plugin.Rings.Value);                // FINGERS tab
            hw = hw.Concat(rings.Values.Where(n => HandWear.Items[n].kind != "fring")).Distinct().ToList();   // finger marks (tape / cuts / finger tattoos): drawn like hand tattoos
            if (plan.Count == 0 && hw.Count == 0 && shoes.Count == 0 && rings.Count == 0) { Plugin.Log.LogInfo("[WristAccessories] wrists: nothing picked"); return; }

            // the items' own bundles (materials, textures) - loaded the way the game loads item models
            // held with our own retain token for the session, like the game's character preview holds its bundles; they used to go
            // into the Raid pools, which the game clears after every raid: the cached materials lost their shaders (magenta
            // accessories on the post-raid character preview, friend's screenshot 2026-10-26)
            var bundles = plan.SelectMany(p => p.items).SelectMany(a => a.meshes.Select(m => m.bundle).Concat(new[] { a.bundle }).Concat(a.dial != null ? new[] { a.dial.prefab } : new string[0]))   // + the item's own bundle: loose entries have no meshes of their own
                .Concat(HandWear.Bundles(hw)).Concat(HandWear.Bundles(shoes)).Concat(HandWear.Bundles(rings.Values)).Concat(DogTags.Bundles(plan.SelectMany(p => p.items)))   // + the picked dogtags' item bundles
                .Where(b => !string.IsNullOrEmpty(b)).Distinct().Where(b => !Held.Contains(b)).ToArray();
            if (bundles.Length > 0)
            {
                var tok = Singleton<IEasyAssets>.Instance.Retain(bundles);
                await EasyAssetsExtensions.LoadBundles(tok);
                Tokens.Add(tok); foreach (var b in bundles) Held.Add(b);
                Plugin.Log.LogInfo($"[WristAccessories] bundles held for the session: {bundles.Length} new, {Held.Count} in all");
            }
            if (body == null || body != _body || hands == null) return;

            // handwear: gloves / other hands / tape on the skin that has the hands. First person: the Hands part. A preview body
            // (third person): the game shows the Hands part only in first person (PlayerBody.UpdatePlayerRenders), the hands seen
            // are the top's (Body part) - the same bone names and bind poses as the first-person hands (lab/tpcheck/bindcmp.py,
            // USEC ACU: palm/fingers within 1 mm), so the pieces go on the top's skeleton and its own hands are cut.
            if (hw.Count > 0)
            {
                var hwPart = fp ? EBodyModelPart.Hands : EBodyModelPart.Body;
                if (body.BodySkins.TryGetValue(hwPart, out var handSkin) && handSkin != null)
                    HandWear.Build(handSkin, hw, _objs, _swaps, (b, m) => Materials(b, m), Who(preview, fp));
                else Plugin.Log.LogWarning($"[WristAccessories] handwear ({(preview ? "preview" : "first person")}): no {hwPart} skin on this body (has {string.Join(", ", body.BodySkins.Keys)}) - {hw.Count} item(s) not shown");
            }

            // finger rings: under the finger bones of the skin that has the hands (after the handwear: a glove swaps the bones out)
            if (rings.Count > 0)
            {
                if (body.BodySkins.TryGetValue(fp ? EBodyModelPart.Hands : EBodyModelPart.Body, out var ringSkin) && ringSkin != null)
                    HandWear.BuildRings(ringSkin, rings, _objs, (b, m) => Materials(b, m), Who(preview, fp), _swaps);
                else Plugin.Log.LogWarning($"[WristAccessories] rings ({(preview ? "preview" : "first person")}): no hands skin on this body - {rings.Count} ring(s) not shown");
            }

            // shoes: another lower outfit's shoes on the Feet part (the legs: in first person and on a preview body alike)
            if (shoes.Count > 0)
            {
                if (body.BodySkins.TryGetValue(EBodyModelPart.Feet, out var feetSkin) && feetSkin != null)
                    HandWear.Build(feetSkin, shoes, _objs, _swaps, (b, m) => Materials(b, m), preview ? "preview" : "first person");
                else Plugin.Log.LogWarning($"[WristAccessories] shoes ({(preview ? "preview" : "first person")}): no Feet skin on this body (has {string.Join(", ", body.BodySkins.Keys)}) - {shoes[0]} not shown");
            }

            var skel = fp ? body.SkeletonHands : body.SkeletonRootJoint;
            foreach (var (part, bone, items) in plan)
            {
                var boneT = skel.Bones.FirstOrDefault(kv => kv.Key.EndsWith(bone)).Value;
                if (boneT == null) { Plugin.Log.LogWarning($"[WristAccessories] no bone {bone} in the hands skeleton"); continue; }
                WristFit.ParseTurns(Plugin.ItemAngles.Value); WristFit.ParseSlides(Plugin.ItemSlides.Value); WristFit.PoseBlend = Plugin.SleeveFit.Value; WristFit.StraightenOn = Plugin.Straighten.Value; WristFit.LooseSteps = Plugin.LooseSize.Value; WristFit.NoOutfitWatch = !string.IsNullOrEmpty(Plugin.Watch.Value);
                // a long kill sleeve covers the forearm where a twist bulges most: on that arm the outline clears >= 80% of the
                // measured wrist poses (user 2026-10-27: skin through the sleeve at Sleeve fit 0.25)
                if (items.Any(i => i.width > 0.06f) && WristFit.PoseBlend < 0.8f) { Plugin.Log.LogInfo($"[WristAccessories] {part}: a kill sleeve is worn - sleeve fit {WristFit.PoseBlend:0.00} -> 0.80 on this arm"); WristFit.PoseBlend = 0.8f; }
                var o = WristFit.GetOutline(hid, part, out string how, !fp);
                if (o == null) continue;
                List<(WristFit.Acc acc, List<WristFit.Built> built, float lo, float hi, int turns, WristFit.DialPose dial)> placed;
                if (part.StartsWith("ring")) { var b = WristFit.PlaceRing(items[0], o); placed = new List<(WristFit.Acc, List<WristFit.Built>, float, float, int, WristFit.DialPose)> { (items[0], b, 0, 0, 1, default) }; }
                else
                {
                    // where the game's own outfits wear their watches (measured from their hands meshes, angle from the back of the hand):
                    // back of the wrist +25..+55 deg (GMT, Garmin, Suunto, GBD-200; mean +33), inner wrist -140..-175 (Luminox, Montana,
                    // Traser, BEAR x2, USEC, GA-2000; mean -156) - the spots Manimal's watch_usec / watch_bear clips turn to the camera
                    // WATCH WORN "as its outfit" (user 2026-10-28, Aggressor TAC watch shots): the picked watch where its own outfit
                    // wears it - that outfit's arm outline, face direction and distance from the hand (same skeleton in every outfit),
                    // so the strap sits on the skin and the sleeve worn now covers it like the game's own watch
                    var its = items; WristFit.Acc srcW = null; WristFit.Outline srcO = null;
                    var pw = its.FirstOrDefault(a => a.name == Plugin.Watch.Value);
                    if (pw != null) Plugin.Log.LogInfo($"[WristAccessories] {(preview ? "preview " : "")}{part}: watch {pw.name}, WATCH WORN '{Plugin.WatchWorn.Value}', own-outfit spot {(pw.src != null ? pw.src.hid : "NOT IN DATA")}{(part != "L" && Plugin.WatchWorn.Value == "outfit" ? " - right wrist: over the sleeve" : "")}");
                    if (pw != null && its[0] != pw) { its = new[] { pw }.Concat(its.Where(a => a != pw)).ToList(); }
                    if (part == "L" && Plugin.WatchWorn.Value == "outfit" && its.Count > 0 && its[0].name == Plugin.Watch.Value && its[0].src != null)
                    {
                        srcO = WristFit.GetOutline(its[0].src.hid, "L", out string shw, !fp);
                        if (srcO != null && shw.StartsWith("own")) { srcW = its[0]; its = its.Skip(1).ToList(); }
                        else { srcO = null; Plugin.Log.LogInfo($"[WristAccessories] {(preview ? "preview " : "")}watch {its[0].name}: no outline of its own outfit {its[0].src.hid} ({shw}) - fitted over the sleeve instead"); }
                    }
                    List<WristFit.Built> wb = null; WristFit.DialPose wd = default; var so = o;
                    if (srcW != null)
                    {
                        float phi0 = Mathf.Atan2(Vector3.Dot(srcW.src.face, srcO.e2), Vector3.Dot(srcW.src.face, srcO.e1));
                        float aC = Vector3.Dot(srcW.src.c - srcO.w, srcO.ax) + srcW.hmid;
                        wb = WristFit.Place(srcW, srcO, aC, 1, phi0); wd = WristFit.LastDial;
                        // the exact shape and spot from its outfit (user shot 2026-10-29: refitted round the Aggressor parka's sleeve the
                        // TAC strap floated as a big loose band on a bare arm) - Place only gave the layout's order and the moving hands
                        if (srcW.src.meshes != null && srcW.src.meshes.Count == wb.Count && wb.Select((b, k) => b.pos.Length == srcW.src.meshes[k].P.Length).All(x => x))
                        {
                            var head = Enumerable.Range(0, wb[0].pos.Length).Where(i => wb[0].kind[i] > 0).ToArray();
                            if (wd.ok && head.Length >= 3)
                            {
                                // the moving hands follow the head from where Place put it to where the outfit has it
                                Quaternion Frame(Vector3[] p, out Vector3 c)
                                {
                                    var cc = Vector3.zero; foreach (var i in head) cc += p[i]; cc /= head.Length; c = cc;
                                    int i1 = head.OrderByDescending(i => (p[i] - cc).sqrMagnitude).First(); var u1 = (p[i1] - cc).normalized;
                                    int i2 = head.OrderByDescending(i => Vector3.Cross(p[i] - cc, u1).sqrMagnitude).First();
                                    return Quaternion.LookRotation(u1, Vector3.Cross(u1, p[i2] - cc).normalized);
                                }
                                var qP = Frame(wb[0].pos, out var cP); var qS = Frame(srcW.src.meshes[0].P, out var cS); var R = qS * Quaternion.Inverse(qP);
                                wd = new WristFit.DialPose { ok = true, prefab = wd.prefab, pos = cS + R * (wd.pos - cP), rot = R * wd.rot };
                            }
                            for (int k = 0; k < wb.Count; k++) { var e = srcW.src.meshes[k]; wb[k].pos = (Vector3[])e.P.Clone(); wb[k].nrm = (Vector3[])e.N.Clone(); wb[k].tan = (Vector4[])e.T.Clone(); }   // copies: the widen / draw-in / ANGLE steps below change them in place
                        }
                        else Plugin.Log.LogWarning($"[WristAccessories] watch {srcW.name}: no exact outfit shape in the data - refitted shape used");
                        // ADJUST -> ANGLE / SLIDE on this watch (user 2026-10-29: "the angle change isn't doing anything" - the outfit-placed
                        // watch skipped Stack, where they are applied): turned round this arm's axis, moved along it (+ toward the hand)
                        WristFit.ParseTurns(Plugin.ItemAngles.Value); WristFit.ParseSlides(Plugin.ItemSlides.Value);
                        float tDeg = WristFit.TurnDeg.TryGetValue(srcW.name, out var td) ? td : 0f, sMm = WristFit.SlideMm.TryGetValue(srcW.name, out var sm) ? sm : 0f;
                        if (Mathf.Abs(tDeg) > 0.01f || Mathf.Abs(sMm) > 0.01f)
                        {
                            var q = Quaternion.AngleAxis(tDeg, Vector3.Cross(o.e1, o.e2).normalized); var mv = o.ax * (sMm / 1000f);
                            Vector3 Mv(Vector3 p) { var rel = p - o.w; float al = Vector3.Dot(rel, o.ax); return o.w + o.ax * al + q * (rel - o.ax * al) + mv; }
                            foreach (var b in wb)
                                for (int i = 0; i < b.pos.Length; i++)
                                {
                                    b.pos[i] = Mv(b.pos[i]); b.nrm[i] = q * b.nrm[i];
                                    var t3 = q * new Vector3(b.tan[i].x, b.tan[i].y, b.tan[i].z); b.tan[i] = new Vector4(t3.x, t3.y, t3.z, b.tan[i].w);
                                }
                            if (wd.ok) wd = new WristFit.DialPose { ok = true, prefab = wd.prefab, pos = Mv(wd.pos), rot = q * wd.rot };
                            Plugin.Log.LogInfo($"[WristAccessories] watch {srcW.name}: ANGLE {tDeg:0} deg, SLIDE {sMm:0} mm applied");
                        }
                        // the watch's head inside THIS sleeve (user 2026-10-29: "on some outfits the watch just wont show up" - a sleeve
                        // thicker than its own outfit's cuff swallows it): most of the head under this outfit's arm outline -> over the sleeve
                        WristFit.FaceAngle[srcW.name] = phi0 + tDeg * Mathf.Deg2Rad;   // the watch animation picks its clip from this (ANGLE included)
                        var headPid = wb.SelectMany(b => Enumerable.Range(0, b.pid.Length).Where(i => b.kind[i] > 0).Select(i => b.pid[i])).GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => g.Key).DefaultIfEmpty(-1).First();
                        int hv = 0, hin = 0; float need = 0f, rSum = 0f, sHead = 1f;
                        foreach (var b in wb)
                            for (int i = 0; i < b.pos.Length; i++)
                            {
                                if (b.kind[i] == 0 || b.pid[i] != headPid) continue;
                                var rel = b.pos[i] - o.w; float al = Vector3.Dot(rel, o.ax); var pp = rel - o.ax * al;
                                float ph = Mathf.Repeat(Mathf.Atan2(Vector3.Dot(pp, o.e2), Vector3.Dot(pp, o.e1)), 2 * Mathf.PI);
                                int k = ((Mathf.RoundToInt(ph / (2 * Mathf.PI / o.nd)) % o.nd) + o.nd) % o.nd, j = Mathf.Clamp(Mathf.FloorToInt((al - o.a0) / o.da), 0, o.na - 1);
                                hv++; rSum += pp.magnitude; if (pp.magnitude < o.T(j, k)) hin++;
                                need = Mathf.Max(need, o.T(j, k) + 0.0015f - pp.magnitude); if (pp.magnitude > 1e-4f) sHead = Mathf.Max(sHead, (o.T(j, k) + 0.0015f) / pp.magnitude);
                            }
                        if (hv > 0 && hin * 2 > hv)
                        {
                            // a sleeve thicker than its own outfit's cuff: the same watch, its loop widened round the arm just enough that
                            // the head sits 1.5 mm out of this sleeve (user 2026-10-29: the over-the-sleeve fallback looked buggy - ScavTac,
                            // QILO watches); the moving hands go out with it
                            float s = sHead;                                  // every head point out of the sleeve (not just the average one)
                            Vector3 Out(Vector3 p) { var rel = p - o.w; float al = Vector3.Dot(rel, o.ax); return o.w + o.ax * al + (rel - o.ax * al) * s; }
                            foreach (var b in wb) for (int i = 0; i < b.pos.Length; i++) b.pos[i] = Out(b.pos[i]);
                            if (wd.ok) wd = new WristFit.DialPose { ok = true, prefab = wd.prefab, pos = Out(wd.pos), rot = wd.rot };
                            Plugin.Log.LogInfo($"[WristAccessories] {(preview ? "preview " : "")}watch {srcW.name} as its outfit wears it, loop widened x{s:0.00} ({need * 1000:0.0} mm) to clear this sleeve ({hin} of {hv} head points were inside)");
                        }
                        else
                        {
                            // the whole watch standing off this arm (its own outfit wore it over a thicker cuff; user shot 2026-10-29: the
                            // green C-Shock "looks a bit big" on a bare arm): the loop drawn in until its closest point is 1 mm off the arm.
                            // Not when part of it is under this sleeve (that is the tucked-under-the-cuff look)
                            float sMin = 0f;
                            foreach (var b in wb)
                                for (int i = 0; i < b.pos.Length; i++)
                                {
                                    var rel = b.pos[i] - o.w; float al = Vector3.Dot(rel, o.ax); var pp = rel - o.ax * al; float r = pp.magnitude; if (r < 1e-4f) continue;
                                    float ph = Mathf.Repeat(Mathf.Atan2(Vector3.Dot(pp, o.e2), Vector3.Dot(pp, o.e1)), 2 * Mathf.PI);
                                    int k = ((Mathf.RoundToInt(ph / (2 * Mathf.PI / o.nd)) % o.nd) + o.nd) % o.nd, j = Mathf.Clamp(Mathf.FloorToInt((al - o.a0) / o.da), 0, o.na - 1);
                                    sMin = Mathf.Max(sMin, (o.T(j, k) + 0.001f) / r);
                                }
                            if (sMin > 0f && sMin < 0.98f)
                            {
                                Vector3 In(Vector3 p) { var rel = p - o.w; float al = Vector3.Dot(rel, o.ax); return o.w + o.ax * al + (rel - o.ax * al) * sMin; }
                                foreach (var b in wb) for (int i = 0; i < b.pos.Length; i++) b.pos[i] = In(b.pos[i]);
                                if (wd.ok) wd = new WristFit.DialPose { ok = true, prefab = wd.prefab, pos = In(wd.pos), rot = wd.rot };
                            }
                            Plugin.Log.LogInfo($"[WristAccessories] {(preview ? "preview " : "")}watch {srcW.name} worn as its outfit wears it: face {phi0 * Mathf.Rad2Deg:0} deg, {(aC - srcW.hmid) * 1000:0} mm along the arm (outfit {srcW.src.hid}, {hin} of {hv} head points under this sleeve){(sMin > 0f && sMin < 0.98f ? $", loop drawn in x{sMin:0.00} to sit on this arm" : "")}");
                        }
                    }
                    if (srcW != null)
                    {
                        // the other items start past the watch (user 2026-10-29: "move any accessories that could be in the way of the watch"):
                        // the stack begins 3 mm on the elbow side of the watch's own span on this arm
                        float wLo = wb.SelectMany(b => b.pos).Min(p => Vector3.Dot(p - o.w, o.ax));
                        if (wLo - 0.003f < o.start)
                        {
                            so = new WristFit.Outline { w = o.w, ax = o.ax, e1 = o.e1, e2 = o.e2, a0 = o.a0, da = o.da, na = o.na, nd = o.nd, tab = o.tab, start = wLo - 0.003f };
                            Plugin.Log.LogInfo($"[WristAccessories] {(preview ? "preview " : "")}{part}: the other items start past the watch, {so.start * 1000:0} mm from the wrist (was {o.start * 1000:0})");
                        }
                    }
                    else if (part == "L" && fp && string.IsNullOrEmpty(Plugin.Watch.Value) && its.Count > 0)
                    {
                        // no watch picked: the OUTFIT's own watch is on this wrist (user shot 2026-10-29: its green watch under the beads and
                        // chain) - its submeshes (watch materials) baked in the hands' pose now, the stack starts past them
                        float? lo = OwnWatchLo(hands, boneT, o);
                        if (lo != null && lo.Value - 0.003f < o.start)
                        {
                            so = new WristFit.Outline { w = o.w, ax = o.ax, e1 = o.e1, e2 = o.e2, a0 = o.a0, da = o.da, na = o.na, nd = o.nd, tab = o.tab, start = lo.Value - 0.003f };
                            Plugin.Log.LogInfo($"[WristAccessories] {part}: the items start past the outfit's own watch, {so.start * 1000:0} mm from the wrist (was {o.start * 1000:0})");
                        }
                    }
                    placed = its.Count > 0 ? WristFit.Stack(its, so, (Plugin.WatchFace.Value == "inner" ? Plugin.InnerAngle.Value : 33f) * Mathf.Deg2Rad)
                                           : new List<(WristFit.Acc, List<WristFit.Built>, float, float, int, WristFit.DialPose)>();
                    if (srcW != null) placed.Insert(0, (srcW, wb, float.NaN, float.NaN, 1, wd));
                    foreach (var sk in WristFit.Skipped) Plugin.Log.LogInfo($"[WristAccessories] {(preview ? "preview " : "")}{part}: not worn, sleeve too wide for {sk}");
                    if (part == "L" && WristFit.Skipped.Any(x => x.StartsWith((Plugin.Watch.Value ?? "-") + " ")))
                    { RestoreOutfitWatch(); Plugin.Log.LogInfo("[WristAccessories] picked watch does not fit this sleeve: the outfit's own watch stays"); }
                }
                int idx = -1;
                foreach (var (acc, built, lo, hi, turns, dial) in placed)
                {
                    idx++;
                    var go = new GameObject("WristAcc_" + part + "_" + acc.name);
                    go.transform.SetParent(boneT, false);
                    go.layer = hands.GetRenderers().First().gameObject.layer;
                    int parts = 0, swings = 0, tagNo = 0, doubled = 0;
                    var src = new Dictionary<Mesh, WristFit.Built>();
                    foreach (var b0 in built)
                    {
                        // a hanging tag: the dogtag picked for it on the DOGTAGS page (DogTags.cs), else the sleeve's own
                        var b = b0.swing != null && b0.swing.Length == 2 && DogTags.Sleeves.Contains(acc.name) ? DogTags.Swap(b0, tagNo++, o, acc.name) ?? b0 : b0;   // only the kill sleeves' / dogtag chains' tags (pendants like the horse hang the same way)
                        var mats = Materials(b.bundle, b.mesh);
                        var mesh = new Mesh { name = acc.name + "_" + b.mesh };
                        if (Plugin.DoubleSided.Value)
                        {
                            // both sides drawn (user shot 2026-10-28, USEC Aggressor TAC watch "not culled properly"): straps and
                            // bands cut from outfits are one-sided surfaces - where one stands off the sleeve, its inside is culled
                            // and the strap looks hollow. A back copy of every face (reversed, normals turned) fills that in.
                            int nv = b.pos.Length;
                            mesh.vertices = b.pos.Concat(b.pos).ToArray(); mesh.normals = b.nrm.Concat(b.nrm.Select(v => -v)).ToArray();
                            mesh.tangents = b.tan.Concat(b.tan.Select(t => new Vector4(-t.x, -t.y, -t.z, t.w))).ToArray(); mesh.uv = b.uv.Concat(b.uv).ToArray();
                            mesh.subMeshCount = b.tris.Length;
                            for (int s = 0; s < b.tris.Length; s++)
                            {
                                var t = b.tris[s]; var tt = new int[t.Length * 2]; Array.Copy(t, tt, t.Length);
                                for (int q = 0; q < t.Length; q += 3) { tt[t.Length + q] = t[q] + nv; tt[t.Length + q + 1] = t[q + 2] + nv; tt[t.Length + q + 2] = t[q + 1] + nv; }
                                mesh.SetTriangles(tt, s);
                            }
                            doubled++;
                        }
                        else
                        {
                            mesh.vertices = b.pos; mesh.normals = b.nrm; mesh.tangents = b.tan; mesh.uv = b.uv;
                            mesh.subMeshCount = b.tris.Length;
                            for (int s = 0; s < b.tris.Length; s++) mesh.SetTriangles(b.tris[s], s);
                        }
                        mesh.RecalculateBounds(); src[mesh] = b;
                        var child = new GameObject(b.mesh); child.transform.SetParent(go.transform, false); child.layer = go.layer;
                        child.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mr = child.AddComponent<MeshRenderer>();
                        if (acc.texture != null && mats != null && b.bundle == acc.bundle) mats = mats.Select(m => Variant(m, acc.texture, acc.accDir)).ToArray();   // BD / ISB patches (the sleeve itself, not the dogtags on it)
                        mr.sharedMaterials = Fallback.Fix(mats == null || mats.Length == 0 ? null
                            : b.mat >= 0 ? new[] { mats[Math.Min(b.mat, mats.Length - 1)] }                       // one submesh of a source outfit's mesh
                            : Enumerable.Range(0, b.tris.Length).Select(i => mats[Math.Min(i, mats.Length - 1)]).ToArray(), b.mat >= 0 ? 1 : b.tris.Length, $"{acc.name} ({b.mesh})", hands?.GetRenderers().FirstOrDefault());
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        if (b.swing != null && b.swing.Length == 2) { TagSwing.Attach(child, mesh, b, o); swings++; }   // dogtags hang from their ring
                        parts++;
                    }
                    Transform dialTf = null;
                    if (dial.ok)
                    {
                        try
                        {
                            var mh = Singleton<IEasyAssets>.Instance.InstantiateAsset<PluggableBone>(new ResourceKey { path = dial.prefab });
                            mh.transform.SetParent(go.transform, false);
                            mh.transform.localPosition = dial.pos; mh.transform.localRotation = dial.rot;
                            foreach (var t in mh.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = go.layer;
                            mh.GetComponent<Watch>()?.Init(new TimeSpan(0L)); dialTf = mh.transform;
                            Plugin.Log.LogInfo($"[WristAccessories] {acc.name}: moving hands {System.IO.Path.GetFileName(dial.prefab)} placed");
                        }
                        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] {acc.name} moving hands: {e.Message}"); }
                    }
                    if (!preview && (part == "L" || part == "R") && !float.IsNaN(lo)) { LiveChecks.Add((hands, boneT, o, acc.name, hid, part, lo, hi)); LiveArmCheck(hands, boneT, o, acc.name, hid, part, lo, hi, "built"); }
                    if (!preview && part == "L" && acc.fitR > 0 && MainWatchName == null && WristFit.FaceAngle.TryGetValue(acc.name, out float fa))
                    {
                        MainWatchFace = fa; MainWatchName = acc.name;
                        Plugin.Log.LogInfo($"[WristAccessories] {acc.name}: face {Mathf.DeltaAngle(0f, fa * Mathf.Rad2Deg):0} deg from the back of the hand (watch animation uses this)");
                    }
                    if (!part.StartsWith("ring") && idx < WristFit.LastLoose.Count && WristFit.LastLoose[idx] > 0f)
                    {
                        // loose bracelet: hangs and slides between its neighbours (the hand side ends just past the wrist joint)
                        float handLim = idx == 0 ? o.start + 0.010f : placed[idx - 1].lo - 0.003f;
                        float elbowLim = idx + 1 < placed.Count ? placed[idx + 1].hi + 0.003f : lo - 0.035f;   // lab: 60 mm slid it up onto the thick sleeve
                        var ph = go.AddComponent<BraceletPhysics>();
                        ph.O = o; ph.RingR = WristFit.LastLoose[idx]; ph.Lo = lo; ph.Hi = hi; ph.Name = acc.name; ph.Hands = hands;
                        ph.ZMax = Math.Max(0f, handLim - hi); ph.ZMin = Math.Min(0f, elbowLim - lo);
                    }
                    _objs.Add(go);
                    { var wc = go.AddComponent<HandWear.Check>(); wc.Who = (preview ? "preview " : "") + part; wc.Name = acc.name; wc.Palm = boneT; wc.Ref = "the wrist bone"; wc.MaxSize = 0.6f; wc.MaxAway = 0.35f; }
                    Plugin.Log.LogInfo($"[WristAccessories] {(preview ? "preview " : "")}{part}: {acc.name} at {lo * 1000:0}..{hi * 1000:0} mm from the wrist, {turns} turn(s), {parts} mesh(es){(swings > 0 ? $", {swings} hanging" : "")}{(doubled > 0 ? $", {doubled} drawn on both sides" : "")}, outline {how}");
                }
            }
            // everything this rig draws, by object and mesh (user 2026-10-28: claws round the wrist from an unknown piece)
            if (!preview)
                foreach (var go in _objs.Where(x => x != null))
                    Plugin.Log.LogInfo($"[WristAccessories] drawn by us: {go.name} on {(go.transform.parent != null ? go.transform.parent.name : "-")}: " +
                        string.Join(", ", go.GetComponentsInChildren<Renderer>(true).Select(r => { var m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                            return $"{r.name} [{(m != null ? m.name + " " + m.vertexCount + "v" : "no mesh")}, {string.Join("/", r.sharedMaterials.Select(x => x != null ? x.name : "null"))}{(r.enabled ? "" : ", off")}]"; })));
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] wrists: " + e); }
        finally { _busy = false; }
    }

    // a copy of the item's material with our repainted diffuse (acc/<texture>_d.png); the normal map stays the game's
    // ---- diagnostic: the arm as the game draws it right now (skinned mesh baked in its current pose) vs the offline outline ----
    /// <summary>The main player's placed wrist items (first person), re-measured on every timer key press (WatchAnimsCompat).</summary>
    public static readonly List<(LoddedSkin hands, Transform bone, WristFit.Outline o, string acc, string hid, string part, float lo, float hi)> LiveChecks = new List<(LoddedSkin, Transform, WristFit.Outline, string, string, string, float, float)>();
    /// <summary>The elbow-side end of the outfit's own watch on this arm (along o.ax from o.w), from its submeshes (watch materials)
    /// in the hands' current pose; null = the outfit has no watch there.</summary>
    static float? OwnWatchLo(LoddedSkin hands, Transform bone, WristFit.Outline o)
    {
        try
        {
            float? lo = null; int n = 0;
            foreach (var smr in hands.GetRenderers().OfType<SkinnedMeshRenderer>())
            {
                var mats = smr.sharedMaterials; if (smr.sharedMesh == null) continue;
                var subs = Enumerable.Range(0, Math.Min(mats.Length, smr.sharedMesh.subMeshCount)).Where(i => mats[i] != null && WatchWords.Any(x => mats[i].name.ToLowerInvariant().Contains(x))).ToList();
                if (subs.Count == 0) continue;
                var baked = new Mesh(); smr.BakeMesh(baked, true); var vs = baked.vertices; var M = bone.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                foreach (var s in subs)
                    foreach (var vi in baked.GetTriangles(s).Distinct())
                    {
                        float a = Vector3.Dot(M.MultiplyPoint3x4(vs[vi]) - o.w, o.ax);
                        if (Mathf.Abs(Vector3.Dot(M.MultiplyPoint3x4(vs[vi]) - o.w, o.ax)) < 0.15f) { lo = lo == null ? a : Mathf.Min(lo.Value, a); n++; }
                    }
                Destroy(baked);
            }
            if (lo != null) Plugin.Log.LogInfo($"[WristAccessories] outfit's own watch: {n} points, reaches {lo.Value * 1000:0} mm from the wrist");
            return lo;
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] outfit's own watch: " + e.Message); return null; }
    }

    public static void RunLiveChecks(string why) { foreach (var c in LiveChecks) if (c.hands != null && c.bone != null) LiveArmCheck(c.hands, c.bone, c.o, c.acc, c.hid, c.part, c.lo, c.hi, why); }

    static void LiveArmCheck(LoddedSkin hands, Transform bone, WristFit.Outline o, string acc, string hid, string part, float lo, float hi, string why)
    {
        try
        {
            var baked = new Mesh(); var V = new List<Vector3>();
            foreach (var r in hands.GetRenderers())
            {
                if (!(r is SkinnedMeshRenderer smr)) continue;
                smr.BakeMesh(baked, true); var vs = baked.vertices; var M = bone.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                var vis = BraceletPhysics.VisibleVerts(smr, baked);
                for (int i = 0; i < vs.Length; i++) if (vis == null || vis[i]) V.Add(M.MultiplyPoint3x4(vs[i]));
            }
            UnityEngine.Object.Destroy(baked);
            int nd = o.nd; var live = new float[nd]; var offl = new float[nd]; int nOff = 0; var any = new bool[nd];
            foreach (var p in V)
            {
                var rel = p - o.w; float al = Vector3.Dot(rel, o.ax); if (al < lo || al > hi) continue;
                var perp = rel - o.ax * al; float rad = perp.magnitude; if (rad > 0.12f) continue;
                int k = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(Vector3.Dot(perp, o.e2), Vector3.Dot(perp, o.e1)), 2 * Mathf.PI) / (2 * Mathf.PI / nd)) % nd;
                if (rad > live[k]) live[k] = rad; any[k] = true;
            }
            for (int j = Mathf.Clamp(Mathf.FloorToInt((lo - o.a0) / o.da), 0, o.na - 1); j <= Mathf.Clamp(Mathf.FloorToInt((hi - o.a0) / o.da), 0, o.na - 1); j++, nOff++)
                for (int k = 0; k < nd; k++) offl[k] = Math.Max(offl[k], o.T(j, k));
            float lm = 0, om = 0; int n = 0; string dirs = "";
            for (int k = 0; k < nd; k++) if (any[k]) { lm += live[k]; om += offl[k]; n++; }
            for (int k = 0; k < nd; k += nd / 8) dirs += $" {k * 360 / nd}:{(any[k] ? live[k] * 1000 : 0):0}/{offl[k] * 1000:0}";
            Plugin.Log.LogInfo($"[WristAccessories] live arm ({why}) under {acc} ({part}, hands {hid}, {V.Count} verts, renderer on {hands.GetRenderers().Any(r => r != null && r.enabled)}): outermost mean {(n > 0 ? lm / n * 1000 : 0):0.0} mm live vs {(n > 0 ? om / n * 1000 : 0):0.0} mm offline outline; per direction live/offline (deg:mm){dirs}");
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] live arm check: " + e.Message); }
    }

    static readonly Dictionary<string, Material> Variants = new Dictionary<string, Material>();
    static readonly Dictionary<string, Material> VariantSrc = new Dictionary<string, Material>();   // the game material each variant copies
    static Material Variant(Material m, string tex, string accDir = null)
    {
        if (m == null) return null;
        string k = m.name + "|" + tex;
        if (Variants.TryGetValue(k, out var v) && Alive(v) && VariantSrc.TryGetValue(k, out var vs) && Alive(vs)) return v;
        if (v != null) Plugin.Log.LogWarning($"[WristAccessories] variant {k}: its source material is gone - made again");
        try
        {
            var path = System.IO.Path.Combine(accDir ?? System.IO.Path.Combine(WristFit.Dir, "acc"), tex + "_d.png");
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!System.IO.File.Exists(path) || !t.LoadImage(System.IO.File.ReadAllBytes(path))) { Plugin.Log.LogError($"[WristAccessories] variant texture {path} not loaded"); return m; }
            t.name = tex; t.anisoLevel = 4; DontDestroyOnLoad(t);
            v = new Material(m) { name = m.name + "_" + tex }; v.SetTexture("_MainTex", t); DontDestroyOnLoad(v);
            // our own textures with a metallic/smoothness map (bead bracelets): Unity's Standard shader (shipped by the game). The
            // game's Reflective shaders mirrored the scene over round beads in game (white tops, dark band, colour lost)
            var mgPath = System.IO.Path.Combine(accDir ?? System.IO.Path.Combine(WristFit.Dir, "acc"), tex + "_m.png"); var std = Shader.Find("Standard");
            if (System.IO.File.Exists(mgPath) && std != null)
            {
                var mg = new Texture2D(2, 2, TextureFormat.RGBA32, true, true); mg.LoadImage(System.IO.File.ReadAllBytes(mgPath)); mg.name = tex + "_m"; DontDestroyOnLoad(mg);
                v = new Material(std) { name = "Standard_" + tex }; DontDestroyOnLoad(v);
                v.SetTexture("_MainTex", t); v.SetColor("_Color", Color.white);
                v.SetTexture("_MetallicGlossMap", mg); v.EnableKeyword("_METALLICGLOSSMAP"); v.SetFloat("_GlossMapScale", 1f); v.SetFloat("_Metallic", 0f);
                Variants[k] = v; VariantSrc[k] = m;
                Plugin.Log.LogInfo($"[WristAccessories] variant material {v.name} (Standard shader, metallic/smoothness map, {t.width}x{t.height})");
                return v;
            }
            // our own textures (bead bracelets) come with their gloss/spec and a flat normal map, replacing the source item's
            foreach (var (suffix, prop, linear) in new[] { ("_s.png", "_SpecMap", false), ("_n.png", "_BumpMap", true) })
            {
                var pp = System.IO.Path.Combine(accDir ?? System.IO.Path.Combine(WristFit.Dir, "acc"), tex + suffix);
                if (!System.IO.File.Exists(pp)) continue;
                Texture2D t2;
                if (linear)                                     // flat normal, built in linear space (a PNG may be read as sRGB)
                {
                    t2 = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
                    t2.SetPixels32(Enumerable.Repeat(new Color32(128, 128, 255, 255), 16).ToArray()); t2.Apply();
                }
                else { t2 = new Texture2D(2, 2, TextureFormat.RGBA32, true); if (!t2.LoadImage(System.IO.File.ReadAllBytes(pp))) continue; }
                t2.name = tex + suffix; DontDestroyOnLoad(t2); v.SetTexture(prop, t2);
                if (!linear) v.SetColor("_ReflectColor", new Color(0.06f, 0.06f, 0.06f, 0.5f));   // own textures: no mirror-like sheen
            }
            Variants[k] = v; VariantSrc[k] = m;
            Plugin.Log.LogInfo($"[WristAccessories] variant material {v.name} ({t.width}x{t.height})");
            return v;
        }
        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] variant {tex}: {e.Message}"); return m; }
    }

    static Material[] Materials(string bundle, string meshName)
    {
        string k = bundle + "|" + meshName;
        if (MatCache.TryGetValue(k, out var c))
        {
            if (c.mats.All(Alive)) return c.mats;
            MatCache.Remove(k); Plugin.Log.LogWarning($"[WristAccessories] material for {meshName}: cached one is gone (bundle unloaded) - fetched again");
        }
        Material[] mats = null;
        try
        {
            var prefab = Singleton<IEasyAssets>.Instance.GetAsset<GameObject>(new ResourceKey { path = bundle });
            var mf = prefab != null ? prefab.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.sharedMesh != null && f.sharedMesh.name == meshName) : null;
            mats = mf != null ? mf.GetComponent<MeshRenderer>()?.sharedMaterials : null;
            if (mats == null && prefab != null)                     // a watch cut from an outfit's hands: its skinned renderer, most watch materials first
                mats = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh != null && r.sharedMesh.name == meshName)
                    .OrderByDescending(r => r.sharedMaterials.Count(m => m != null && WatchWords.Any(x => m.name.ToLowerInvariant().Contains(x))))
                    .ThenBy(r => r.sharedMaterials.Count(m => m == null || m.name.StartsWith("Default-Material") || !m.HasProperty("_MainTex") || m.GetTexture("_MainTex") == null))   // USEC Special Ops: a 2nd renderer of the same mesh with only Unity's white Default-Material (user 2026-10-27: white beads)
                    .FirstOrDefault()?.sharedMaterials;
            // 1.2.1 (user log 2026-10-30: USEC Special Ops beads drawn with Default-Material/Standard): what was found has only Unity's
            // untextured placeholder (a MeshRenderer copy of the mesh is found first) -> the bundle's textured, non-watch skinned material
            if (mats != null && prefab != null && mats.All(m => m == null || m.name.StartsWith("Default-Material") || !m.HasProperty("_MainTex") || m.GetTexture("_MainTex") == null))
            {
                var alt = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .OrderByDescending(r => r.sharedMesh != null && r.sharedMesh.name == meshName)
                    .Select(r => r.sharedMaterials).Where(ms => ms.Length > 0 && ms.All(m => m != null && !m.name.StartsWith("Default-Material") && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null && !WatchWords.Any(x => m.name.ToLowerInvariant().Contains(x))))
                    .FirstOrDefault();
                Plugin.Log.LogInfo($"[WristAccessories] material for {meshName}: only untextured ({string.Join(", ", mats.Select(m => m != null ? m.name : "null"))}) - {(alt != null ? "using the bundle's textured " + string.Join(", ", alt.Select(m => m.name)) : "no textured material in the bundle")}");
                if (alt != null) mats = alt;
            }
            if (mats == null && prefab != null)                     // an item whose mesh lives in another bundle (ISB chess tags): its own renderer's material
                mats = prefab.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.sharedMaterials.Length > 0 && r.name.IndexOf("SHADOW", StringComparison.OrdinalIgnoreCase) < 0)?.sharedMaterials;
            Plugin.Log.LogInfo($"[WristAccessories] material for {meshName} ({bundle}): {(mats == null ? "NOT FOUND (prefab " + (prefab != null) + ")" : string.Join(", ", mats.Select(m => m != null ? m.name + "/" + m.shader.name + " tex " + (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null ? m.GetTexture("_MainTex").name : "NONE") : "null")))}{(prefab != null ? $" (renderers of this mesh: {prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(r => r.sharedMesh != null && r.sharedMesh.name == meshName)})" : "")}");   // 1.2.1: texture names, for the Special Ops "no textures" reports
        }
        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] material {meshName}: {e.Message}"); }
        if (mats != null) MatCache[k] = (null, mats);                 // a failed lookup (bundle not loaded yet) is retried next time
        return mats;
    }
}
