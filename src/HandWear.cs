using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WristAccessories;

// Handwear (user 2026-10-26: rings from other clothing, the USEC Deadlift finger wraps, gloves): pieces of other outfits' hands
// (lab/scan_hands.py -> lab/export_handwear.py -> acc/hw_<name>.json) drawn on the player's own hands. Every outfit's hands use
// the same skeleton names, so a piece is a skinned mesh in its source mesh space, bound to the player's bones by NAME with its
// own bind poses: it bends with every finger. kind "hand" (a glove, or another outfit's bare hand) replaces that side's hand:
// the player's hands mesh is swapped for a copy without that side's hand triangles (mostly Palm / Digit weighted, as cut in
// the lab at 0.5; the piece is cut at 0.35 so it overlaps - no gap at the wrist). kind "ring" / "wrap": drawn on top.
static partial class HandWear
{
    public class Item
    {
        public string name, note, kind, side, bundle, mesh, dir; public string[] bones; public Matrix4x4[] bind; public int n; public Color? skin; public int fit; public bool rare; public bool inkAll; public float[] seamD0; public bool noTone, press;
        public Vector3[] pos, nrm; public Vector4[] tan; public Vector2[] uv; public BoneWeight[] bw; public List<(string mat, int sub, int[] tris)> subs;
    }
    public static readonly Dictionary<string, Item> Items = new Dictionary<string, Item>();

    // every outfit's forearm skin colour by mesh name (lab/skintones.py: the texels the forearm vertices use, skin-coloured,
    // median), for the skin-tone match; ships in the main acc folder
    public static readonly Dictionary<string, Color> ArmTone = new Dictionary<string, Color>();
    public static void Load(string accDir)
    {
        var tf = Path.Combine(accDir, "skintones.json");
        if (File.Exists(tf))
            try
            {
                foreach (var p in JObject.Parse(File.ReadAllText(tf)).Properties()) { var a = (JArray)p.Value["rgb"]; ArmTone[p.Name] = new Color((float)a[0], (float)a[1], (float)a[2]); }
                Plugin.Log.LogInfo($"[WristAccessories] handwear: arm skin tones of {ArmTone.Count} outfit meshes");
            }
            catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] skintones.json: {e.Message}"); }
        var pf = Path.Combine(accDir, "shoe_profiles.json");
        if (File.Exists(pf))
            try
            {
                foreach (var p in JObject.Parse(File.ReadAllText(pf)).Properties())
                {
                    var o = (JObject)p.Value;
                    var d = new Pants { subs = o["subs"].ToObject<int[]>(), verts = (int)o["verts"], tris = I((string)o["tris"]), zmax = F((string)o["zmax"]), neg = Convert.FromBase64String((string)o["neg"]), dir = Convert.FromBase64String((string)o["dir"]) };
                    if (o["legs"] is JObject lg)
                        foreach (var lp in lg.Properties())
                        {
                            var lo = (JObject)lp.Value; var ax = (JArray)lo["ax"];
                            d.legs.Add(new Leg { neg = lp.Name == "n", ax = new Vector2((float)ax[0], (float)ax[1]), zc = (float)lo["zc"], rb = F((string)lo["rb"]), rp = F((string)lo["rp"]), hem = F((string)lo["hem"]) });
                        }
                    PantsData[p.Name] = d;
                }
                Plugin.Log.LogInfo($"[WristAccessories] shoes: own-boot tables of {PantsData.Count} pants meshes ({PantsData.Values.Count(x => x.legs.Count > 0)} with fit profiles) from {accDir}");
            }
            catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] shoe_profiles.json: {e.Message}"); }
        if (!Directory.Exists(accDir)) return; int n = 0;
        foreach (var f in Directory.GetFiles(accDir, "hw_*.json"))
        {
            try
            {
                var j = JObject.Parse(File.ReadAllText(f)); var it = new Item { name = (string)j["name"], note = (string)j["note"], kind = (string)j["kind"], side = (string)j["side"], bundle = BundleKey((string)j["bundle"]), mesh = (string)j["mesh"], dir = accDir, n = (int)j["n"] };
                it.fit = (int?)j["fit"] ?? 0; it.rare = (bool?)j["rare"] ?? false;
                it.noTone = (bool?)j["no_tone"] ?? false; it.inkAll = (bool?)j["ink_all"] ?? false; it.press = ((bool?)j["press"] ?? false) || it.kind == "tattoo";   // every tattoo pressed onto the hand / arm actually drawn (1.0.5)      // a fabric glove the skin test can't tell from skin: its colours stay
                if (j["seam_d0"] is JArray sd && sd.Count == 12) it.seamD0 = sd.Select(x => (float)x).ToArray();
                if (j["skin"] is JArray sk && sk.Count == 3) it.skin = new Color((float)sk[0], (float)sk[1], (float)sk[2]);
                it.bones = j["bones"].ToObject<string[]>();
                it.bind = j["bind"].ToObject<float[][]>().Select(a => { var m = new Matrix4x4(); for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) m[r, c] = a[r * 4 + c]; return m; }).ToArray();
                var P = F((string)j["pos"]); var N = F((string)j["nrm"]); var T = F((string)j["tan"]); var U = F((string)j["uv"]); var BI = I((string)j["bi"]); var BW = F((string)j["bw"]);
                it.pos = new Vector3[it.n]; it.nrm = new Vector3[it.n]; it.tan = new Vector4[it.n]; it.uv = new Vector2[it.n]; it.bw = new BoneWeight[it.n];
                for (int i = 0; i < it.n; i++)
                {
                    it.pos[i] = new Vector3(P[i * 3], P[i * 3 + 1], P[i * 3 + 2]); it.nrm[i] = new Vector3(N[i * 3], N[i * 3 + 1], N[i * 3 + 2]);
                    it.tan[i] = T.Length >= it.n * 4 ? new Vector4(T[i * 4], T[i * 4 + 1], T[i * 4 + 2], T[i * 4 + 3]) : new Vector4(1, 0, 0, 1); it.uv[i] = new Vector2(U[i * 2], U[i * 2 + 1]);
                    it.bw[i] = new BoneWeight { boneIndex0 = BI[i * 4], boneIndex1 = BI[i * 4 + 1], boneIndex2 = BI[i * 4 + 2], boneIndex3 = BI[i * 4 + 3], weight0 = BW[i * 4], weight1 = BW[i * 4 + 1], weight2 = BW[i * 4 + 2], weight3 = BW[i * 4 + 3] };
                }
                it.subs = j["subs"].Select(s => ((string)s["mat"], (int)s["sub"], I((string)s["tris"]))).ToList();
                Items[it.name] = it; n++;
            }
            catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] handwear {Path.GetFileName(f)}: {e.Message}"); }
        }
        if (n > 0) Plugin.Log.LogInfo($"[WristAccessories] handwear: {n} items from {accDir}");
    }
    // a mod's bundle is known to the game by its SPT key (the path under that mod's "bundles" folder), not by a file path
    // (0.9.0 data had file paths: the game handed back its ERROR placeholder material, red gloves - user 2026-10-27)
    static string BundleKey(string b)
    {
        if (string.IsNullOrEmpty(b)) return b; var p = b.Replace('\\', '/');
        if (p.IndexOf("assets/content/", StringComparison.OrdinalIgnoreCase) >= 0) return p.Substring(p.IndexOf("assets/content/", StringComparison.OrdinalIgnoreCase));
        int j = p.IndexOf("/bundles/", StringComparison.OrdinalIgnoreCase); return j >= 0 ? p.Substring(j + 9) : p;
    }
    static float[] F(string b64) { var b = Convert.FromBase64String(b64); var a = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }
    static int[] I(string b64) { var b = Convert.FromBase64String(b64); var a = new int[b.Length / 4]; Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }

    /// <summary>The bundles the picked items need (loaded with the accessories' bundles).</summary>
    public static IEnumerable<string> Bundles(IEnumerable<string> names) => names.Where(Items.ContainsKey).Select(n => Items[n].bundle).Where(b => !string.IsNullOrEmpty(b)).Distinct();

    // the player's hands renderers whose mesh we swapped (hand cut out) -> their own mesh back on Restore
    public class Swap { public SkinnedMeshRenderer r; public Transform[] bones; public Mesh mesh; public readonly List<GameObject> points = new List<GameObject>(); }

    /// <summary>The picked items onto these hands: new objects into objs, swapped meshes into swaps. mats: the plugin's material
    /// lookup (bundle, mesh name) -> the source renderer's materials.</summary>
    public static void Build(EFT.Visual.LoddedSkin hands, IEnumerable<string> names, List<GameObject> objs, List<Swap> swaps, Func<string, string, Material[]> mats, string who)
    {
        var smr = hands?.GetRenderers().OfType<SkinnedMeshRenderer>().OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0).FirstOrDefault();
        if (smr == null || smr.bones == null) { Plugin.Log.LogWarning($"[WristAccessories] handwear ({who}): no skinned hands"); return; }
        var byName = new Dictionary<string, Transform>();
        foreach (var b in smr.bones) if (b != null && !byName.ContainsKey(b.name)) byName[b.name] = b;
        if (smr.rootBone != null) foreach (var b in smr.rootBone.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(b.name)) byName[b.name] = b;
        var picked = names.Where(Items.ContainsKey).Select(n => Items[n]).ToList();
        if (picked.Count > 0 && picked.All(i => i.kind == "feet")) { BuildFeet(hands, smr, byName, picked, objs, swaps, mats, who); return; }
        // the player's own skin tone (then SKIN SHADE: lighter / darker by hand): the hands renderer's texture with the most skin in it (before any hand is hidden)
        Color? own = null;
        if (Plugin.MatchSkinTone.Value && smr.sharedMesh != null && ArmTone.TryGetValue(smr.sharedMesh.name, out var arm))
        {
            own = arm; Plugin.Log.LogInfo($"[WristAccessories] handwear ({who}): your skin tone {Hex(arm)} from your forearm ({smr.sharedMesh.name}, lab table)");
        }
        else if (Plugin.MatchSkinTone.Value)
        {
            var best = smr.sharedMaterials.Where(m => m != null && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null).Select(m => (m, st: SkinOf(m.GetTexture("_MainTex")))).OrderByDescending(x => x.st.frac).FirstOrDefault();
            if (best.m != null && best.st.frac > 0.03f) { own = best.st.mean; Plugin.Log.LogInfo($"[WristAccessories] handwear ({who}): your skin tone {Hex(own.Value)} from {best.m.GetTexture("_MainTex").name} ({best.st.frac * 100:0}% skin)"); }
            else Plugin.Log.LogInfo($"[WristAccessories] handwear ({who}): your skin tone not found in {smr.name}'s textures - handwear keeps its own");
        }
        // the own hand out of every LOD of that skin (a preview body is often seen at LOD1)
        var all = hands.GetRenderers().OfType<SkinnedMeshRenderer>().Where(r => r.sharedMesh != null && r.bones != null && r.bones.Any(b => b != null && b.name.Contains("Palm"))).ToList();
        // a bare hand with seam data: the own hand kept full size up to its edge (Seam.cs) in the main renderer; other LODs as before
        var seams = new Dictionary<string, SeamInfo>();
        if (Plugin.SeamBlend.Value)
            foreach (var hi in picked.Where(i => i.kind == "hand" && i.seamD0 != null))
                if (!seams.ContainsKey(hi.side))
                {
                    try { var si = SeamHand(smr, hi, swaps, who); if (si != null) seams[hi.side] = si; }
                    catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] seam ({who}) {hi.name}: {e}"); }
                }
        foreach (var side in picked.Where(i => i.kind == "hand").Select(i => i.side).Distinct()) foreach (var r in all) if (!(r == smr && seams.ContainsKey(side))) CutHand(r, side, swaps, who);
        var built = new Dictionary<string, SkinnedMeshRenderer>();          // side -> the swapped hand drawn there (tattoos press onto it)
        foreach (var it in picked.OrderBy(i => i.kind == "tattoo" ? 1 : 0))   // hands first, tattoos after
        {
            try
            {
                var mesh = new Mesh { name = "WristAcc_hw_" + it.name, indexFormat = it.n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                var pos = it.pos; Vector3[] vw = null; SeamInfo seam = null;
                if (it.kind == "hand" && seams.TryGetValue(it.side, out seam))
                {
                    pos = (Vector3[])it.pos.Clone();
                    try { vw = SeamTuck(it, pos, seam, who); } catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] seam ({who}) tuck {it.name}: {e}"); pos = it.pos; vw = null; }
                }
                int[][] subTris = it.subs.Select(x => x.tris).ToArray();
                if (it.kind == "tattoo" && it.press)
                {
                    pos = (Vector3[])it.pos.Clone();
                    // the renderer that really carries this tattoo's bones (1.1.0, user 2026-10-28: in the WARDROBE preview the biggest renderer had
                    // none of them - "16 bones not in your hands", nothing pressed, so TATTOO SIZE seemed to do nothing)
                    var pr = all.Where(x => x.gameObject.activeInHierarchy).OrderByDescending(x => it.bones.Count(n => x.bones.Any(b => b != null && b.name == n))).ThenByDescending(x => x.sharedMesh.vertexCount).FirstOrDefault() ?? smr;
                    int[][] pressed = null;
                    try { pressed = PressInk(it, pos, pr, built.TryGetValue(it.side, out var hp) ? hp : null, who, swaps.FirstOrDefault(x => x.r == pr && x.bones != null)?.bones); } catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] tattoos ({who}) press {it.name}: {e}"); pos = it.pos; }
                    // not pressed on a third-person body (user shot 2026-10-28, pre-raid screen: the arm tattoo's whole shell drawn over the
                    // parka sleeve as purple streaks): the unpressed shell is the source outfit's arm, not this body's - left out there
                    if (pressed == null && who != "first person") { Plugin.Log.LogWarning($"[WristAccessories] tattoos ({who}): {it.name} could not be pressed onto this body - not drawn here"); continue; }
                    subTris = pressed ?? subTris;
                }
                mesh.vertices = pos; mesh.normals = it.nrm; mesh.tangents = it.tan; mesh.uv = it.uv; mesh.boneWeights = it.bw; mesh.bindposes = it.bind;
                mesh.subMeshCount = it.subs.Count; for (int s = 0; s < it.subs.Count; s++) mesh.SetTriangles(subTris[s], s);
                mesh.RecalculateBounds();
                var go = new GameObject("WristAcc_hw_" + it.name); go.transform.SetParent(smr.transform.parent, false); go.layer = smr.gameObject.layer;
                go.transform.localPosition = smr.transform.localPosition; go.transform.localRotation = smr.transform.localRotation; go.transform.localScale = smr.transform.localScale;
                var r = go.AddComponent<SkinnedMeshRenderer>(); int missing = 0;
                r.bones = it.bones.Select(n => { if (byName.TryGetValue(n, out var t)) return t; missing++; return smr.rootBone; }).ToArray();
                r.rootBone = smr.rootBone; r.sharedMesh = mesh; r.updateWhenOffscreen = true; r.shadowCastingMode = smr.shadowCastingMode;
                var src = mats(it.bundle, it.mesh);
                r.sharedMaterials = it.subs.Select(s => src != null && src.Length > 0 ? src[Math.Min(s.sub, src.Length - 1)] : null).ToArray();
                if (it.kind == "tattoo") r.sharedMaterials = r.sharedMaterials.Select(m => InkMaterial(m, it.name, who, it.inkAll)).ToArray();   // only the ink shows
                else if (Plugin.MatchSkinTone.Value && !it.noTone && (own != null || vw != null))
                {
                    float sh = 1f + Plugin.SkinShade.Value / 100f; var target = own == null ? Color.clear : new Color(Mathf.Clamp01(own.Value.r * sh), Mathf.Clamp01(own.Value.g * sh), Mathf.Clamp01(own.Value.b * sh));
                    r.sharedMaterials = r.sharedMaterials.Select((m, si) =>
                    {
                        Material t = null;
                        if (vw != null) { try { t = SeamToned(m, it, si, vw, seam, who); } catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] seam ({who}) tone {it.name}: {e}"); } }
                        return t ?? (own != null ? Toned(m, target, it, who) : m);
                    }).ToArray();
                }
                r.sharedMaterials = Fallback.Fix(r.sharedMaterials, it.subs.Count, $"handwear {it.name}", smr);
                objs.Add(go); if (it.kind == "hand") built[it.side] = r;
                var chk = go.AddComponent<Check>(); chk.Who = who; chk.Name = it.name; chk.Palm = byName.TryGetValue("Base Human" + it.side + "Palm", out var pb) ? pb : null;
                Plugin.Log.LogInfo($"[WristAccessories] handwear ({who}): {it.name} ({it.kind}, {it.side}) {it.n} verts on {it.bones.Length} bones{(missing > 0 ? $", {missing} bones not found" : "")}, materials {(src == null ? "NOT FOUND" : string.Join("/", r.sharedMaterials.Select(m => m != null ? m.name : "null")))}");
            }
            catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] handwear {it.name}: {e}"); }
        }
    }

    // the player's own hand on one side out of sight: in this renderer only, that side's Palm / Digit bones are swapped for one
    // point at the palm, scaled to HandShrink, so the hand sits shrunk inside the glove. Not folded to nothing (0.9.2b): the
    // wrist vertices partly on the palm went with it and left a gap between sleeve and glove (user 2026-10-27); at 0.8 a wrist
    // vertex half on the palm, 3 cm from it, moves 3 mm (the glove has its own renderer on the real
    // bones). Works on locked (not readable) meshes: the game's outfit meshes are (0.9.2 log: "not readable").
    const float HandShrink = 0.8f;
    static void CutHand(SkinnedMeshRenderer smr, string side, List<Swap> swaps, string who)
    {
        if (smr == null || smr.bones == null) return;
        var sw = swaps.FirstOrDefault(x => x.r == smr);
        var bones = (Transform[])smr.bones.Clone();
        bool Hand(Transform b) => b != null && (b.name.Contains("Palm") || b.name.Contains("Digit")) && b.name.Contains("Human" + side);
        var palm = bones.FirstOrDefault(b => b != null && b.name.Contains("Human" + side + "Palm"));
        if (palm == null) { Plugin.Log.LogWarning($"[WristAccessories] handwear ({who}): no {side} palm bone in {smr.name} - your own {side} hand stays under the glove"); return; }
        var pt = new GameObject("WristAcc_HandHidden_" + side).transform; pt.SetParent(palm, false); pt.localPosition = Vector3.zero; pt.localRotation = Quaternion.identity; pt.localScale = Vector3.one * HandShrink;
        int k = 0; for (int i = 0; i < bones.Length; i++) if (Hand(bones[i])) { bones[i] = pt; k++; }
        if (sw == null) swaps.Add(sw = new Swap { r = smr, bones = smr.bones });
        sw.points.Add(pt.gameObject); smr.bones = bones;
        Plugin.Log.LogInfo($"[WristAccessories] handwear ({who}): your {side} hand hidden in {smr.name} ({k} hand bones shrunk to {HandShrink:0.00} at the palm)");
    }

    // ---- shoes (kind "feet", lab/export_shoes.py + lab/feet_fit.py): another lower outfit's boot ISLANDS (lower outfits are one
    // mesh, but boots / socks / soles are their own texture islands), in the common rest space (lab/feet_ref.py), as a skinned piece
    // on the Feet part. 0.10.1 (user shot: own sock band between cuff and shoe, a scrap of the source's cuff): the wearer's own boot
    // islands are REMOVED from their pants (per pants mesh table, shoe_profiles.json) and the shoe is fitted to the pants:
    //  - the shoe reaches the cuff (stretch of the ankle column <= 1.6): all of that leg's own boot goes;
    //  - else (tall own boots, low shoe): no stretch, only the own boot below the shoe's top - 1 cm goes, the rest of its shaft stays.
    // Pants without a table (Virago: 20 meshes) fall back to the 0.10.0 way: own Foot / Toe bones shrunk inside the shoe.
    // No skin-tone change: leather and suede would pass as skin.
    const float FootShrink = 0.7f;
    static string Key(Mesh m) => m == null ? "?" : $"{m.name}|{m.vertexCount}";
    static void BuildFeet(EFT.Visual.LoddedSkin feet, SkinnedMeshRenderer smr, Dictionary<string, Transform> byName, List<Item> picked, List<GameObject> objs, List<Swap> swaps, Func<string, string, Material[]> mats, string who)
    {
        var rends = feet.GetRenderers().OfType<SkinnedMeshRenderer>().Where(r => r.sharedMesh != null && r.bones != null).ToList();
        PantsData.TryGetValue(Key(smr.sharedMesh), out var own);
        float[] cutN = null, cutP = null;                                            // per direction; null = keep all of that leg's own boot
        foreach (var it in picked)
        {
            try
            {
                var mesh = new Mesh { name = "WristAcc_shoes_" + it.name, indexFormat = it.n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                var pos = it.pos;
                if (it.fit >= 2 && own != null && own.legs.Count > 0)
                {
                    var fl = new List<string>(); pos = FitShoe(it.pos, it.subs.SelectMany(x => x.tris).ToArray(), own.legs, fl, out cutN, out cutP);
                    Plugin.Log.LogInfo($"[WristAccessories] shoes ({who}): {it.name} fitted to {Key(smr.sharedMesh)}: {string.Join(" | ", fl)}");
                }
                else Plugin.Log.LogWarning($"[WristAccessories] shoes ({who}): {it.name} NOT fitted ({(it.fit < 2 ? "old shoe data" : "no fit profile for " + Key(smr.sharedMesh))})");
                mesh.vertices = pos; mesh.normals = it.nrm; mesh.tangents = it.tan; mesh.uv = it.uv; mesh.boneWeights = it.bw; mesh.bindposes = it.bind;
                mesh.subMeshCount = it.subs.Count; for (int s = 0; s < it.subs.Count; s++) mesh.SetTriangles(it.subs[s].tris, s);
                mesh.RecalculateBounds();
                var go = new GameObject("WristAcc_shoes_" + it.name); go.transform.SetParent(smr.transform.parent, false); go.layer = smr.gameObject.layer;
                go.transform.localPosition = smr.transform.localPosition; go.transform.localRotation = smr.transform.localRotation; go.transform.localScale = smr.transform.localScale;
                var r = go.AddComponent<SkinnedMeshRenderer>(); int missing = 0;
                r.bones = it.bones.Select(n => { if (byName.TryGetValue(n, out var t)) return t; missing++; return smr.rootBone; }).ToArray();
                r.rootBone = smr.rootBone; r.sharedMesh = mesh; r.updateWhenOffscreen = true; r.shadowCastingMode = smr.shadowCastingMode;
                var src = mats(it.bundle, it.mesh);
                r.sharedMaterials = Fallback.Fix(it.subs.Select(s => src != null && src.Length > 0 ? src[Math.Min(s.sub, src.Length - 1)] : null).ToArray(), it.subs.Count, $"shoes {it.name}", smr);
                objs.Add(go);
                var chk = go.AddComponent<Check>(); chk.Who = who; chk.Name = it.name; chk.Ref = "the left foot bone"; chk.Palm = byName.TryGetValue("Base HumanLFoot", out var fb) ? fb : null; chk.MaxSize = 1.0f; chk.MaxAway = 0.6f;
                Plugin.Log.LogInfo($"[WristAccessories] shoes ({who}): {it.name} ({it.note}) {it.n} verts on {it.bones.Length} bones of {smr.name}{(missing > 0 ? $", {missing} bones not found" : "")}, materials {(src == null ? "NOT FOUND" : string.Join("/", r.sharedMaterials.Select(m => m != null ? m.name : "null")))}");
            }
            catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] shoes {it.name}: {e}"); }
        }
        // the own boots: removed per table, or (no table for the main mesh) the old foot shrink
        if (own == null)
        {
            Plugin.Log.LogWarning($"[WristAccessories] shoes ({who}): no own-boot table for {Key(smr.sharedMesh)} - your own feet are shrunk inside the shoe instead");
            foreach (var r in rends) foreach (var side in new[] { "L", "R" }) CutPart(r, side, b => b.name.Contains("Human" + side + "Foot") || b.name.Contains("Human" + side + "Toe"), "Foot", FootShrink, "foot", swaps, who);
            return;
        }
        foreach (var r in rends)
        {
            if (!PantsData.TryGetValue(Key(r.sharedMesh), out var d)) { Plugin.Log.LogInfo($"[WristAccessories] shoes ({who}): {Key(r.sharedMesh)} has no own-boot table - left as it is"); continue; }
            try { HideOwnBoots(r, d, cutN, cutP, swaps, who); }
            catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] shoes ({who}): own boots in {r.name}: {e}"); }
        }
    }

    // the pants mesh of this renderer swapped for a copy without the own boot triangles (readable meshes: a copy; locked ones -
    // the game's usually are - rebuilt: BakeMesh keeps vertices / normals / tangents / uv / submeshes in order but no weights, so
    // the weights come from one extra bake per bone with that bone nudged (children held): displacement / nudge = the weight
    // (skinning is linear; lab BakeTest: exact), bind poses from the bones as they are at the bake)
    static void HideOwnBoots(SkinnedMeshRenderer r, Pants d, float[] cutN, float[] cutP, List<Swap> swaps, string who)
    {
        var m = r.sharedMesh;
        if (m.subMeshCount != d.subs.Length) { Plugin.Log.LogWarning($"[WristAccessories] shoes ({who}): {Key(m)} has {m.subMeshCount} submeshes, table {d.subs.Length} - left as it is"); return; }
        Mesh c; string how;
        if (m.isReadable) { c = UnityEngine.Object.Instantiate(m); how = "copied"; }
        else { c = BakeCopy(r, who); how = "rebuilt from bakes"; if (c == null) return; }
        if (c.vertexCount != d.verts) { Plugin.Log.LogWarning($"[WristAccessories] shoes ({who}): {Key(m)} copy has {c.vertexCount} verts - left as it is"); UnityEngine.Object.Destroy(c); return; }
        var hide = new HashSet<int>();
        for (int k = 0; k < d.tris.Length; k++) { var cut = d.neg[k] == 1 ? cutN : cutP; if (cut != null && d.zmax[k] < cut[d.dir[k]]) hide.Add(d.tris[k]); }
        int off = 0, gone = 0;
        var tris = new List<int[]>();
        for (int s = 0; s < c.subMeshCount; s++)
        {
            var t = c.GetTriangles(s);
            if (t.Length / 3 != d.subs[s]) { Plugin.Log.LogWarning($"[WristAccessories] shoes ({who}): {Key(m)} submesh {s} has {t.Length / 3} triangles, table {d.subs[s]} - left as it is"); UnityEngine.Object.Destroy(c); return; }
            var keep = new List<int>(t.Length);
            for (int i = 0; i < t.Length / 3; i++) { if (hide.Contains(off + i)) { gone++; continue; } keep.Add(t[3 * i]); keep.Add(t[3 * i + 1]); keep.Add(t[3 * i + 2]); }
            tris.Add(keep.ToArray()); off += t.Length / 3;
        }
        for (int s = 0; s < tris.Count; s++) c.SetTriangles(tris[s], s, false);
        c.name = m.name + "_WristAccNoBoots";
        swaps.Add(new Swap { r = r, mesh = m }); r.sharedMesh = c;
        Plugin.Log.LogInfo($"[WristAccessories] shoes ({who}): your own boots out of {r.name} ({how}): {gone} of {off} triangles removed (per leg: {CutText(cutN)} / {CutText(cutP)})");
    }
    static Mesh BakeCopy(SkinnedMeshRenderer r, string who)
    {
        bool en = r.enabled; r.enabled = true;
        try
        {
            if (!r.gameObject.activeInHierarchy) { Plugin.Log.LogInfo($"[WristAccessories] shoes ({who}): {r.name} is inactive - own boots left in it"); return null; }
            var c = new Mesh(); r.BakeMesh(c, true); var v0 = c.vertices; var bones = r.bones; int nv = v0.Length;
            if (nv != r.sharedMesh.vertexCount) { Plugin.Log.LogWarning($"[WristAccessories] shoes ({who}): bake of {r.name} gave {nv} verts of {r.sharedMesh.vertexCount}"); UnityEngine.Object.Destroy(c); return null; }
            var w = new float[nv, bones.Length]; var dW = new Vector3(0.1f, 0, 0); var e = r.transform.InverseTransformVector(dW); float ee = e.sqrMagnitude;
            var tmp = new Mesh(); var seen = new HashSet<Transform>();
            for (int j = 0; j < bones.Length; j++)
            {
                var bj = bones[j]; if (bj == null || !seen.Add(bj)) continue;
                var kids = new List<(Transform t, Vector3 p)>(); for (int i = 0; i < bj.childCount; i++) kids.Add((bj.GetChild(i), bj.GetChild(i).position));
                var p0 = bj.position; bj.position = p0 + dW; foreach (var (t, p) in kids) t.position = p;
                r.BakeMesh(tmp, true); var v = tmp.vertices;
                bj.position = p0; foreach (var (t, p) in kids) t.position = p;
                for (int i = 0; i < nv; i++) w[i, j] = Vector3.Dot(v[i] - v0[i], e) / ee;
            }
            UnityEngine.Object.Destroy(tmp);
            var bw = new BoneWeight[nv]; var top = new (int b, float w)[4];
            for (int i = 0; i < nv; i++)
            {
                for (int k = 0; k < 4; k++) top[k] = (0, 0f);
                for (int j = 0; j < bones.Length; j++)
                {
                    float x = w[i, j]; if (x <= 0.001f) continue;
                    int lo = 0; for (int k = 1; k < 4; k++) if (top[k].w < top[lo].w) lo = k;
                    if (x > top[lo].w) top[lo] = (j, x);
                }
                float sum = top.Sum(t => t.w); if (sum <= 0) sum = 1;
                bw[i] = new BoneWeight { boneIndex0 = top[0].b, weight0 = top[0].w / sum, boneIndex1 = top[1].b, weight1 = top[1].w / sum, boneIndex2 = top[2].b, weight2 = top[2].w / sum, boneIndex3 = top[3].b, weight3 = top[3].w / sum };
            }
            c.boneWeights = bw;
            c.bindposes = bones.Select(b => (b != null ? b.worldToLocalMatrix : Matrix4x4.identity) * r.transform.localToWorldMatrix).ToArray();
            c.bounds = r.sharedMesh.bounds;
            return c;
        }
        finally { r.enabled = en; }
    }

    // ---- shoe fit (lab/feet_fit.py fit(), same numbers; Unity-lab parity ShoeTest): per pants mesh a profile per leg (common
    // rest space, up = +z, centred on the ankle joint): cuff bottom zc, hem per 15 deg direction, own boot radius rb and pants
    // radius rp per 1 cm x 15 deg bin (NaN = none). 0.10.2 (user: invisible ankles on Crye AC pants - the 0.10.1 fit looked only at
    // the collar's highest point): per direction the collar (highest point of the shoe's SURFACE within RC of the ankle, 15
    // samples per triangle) must reach that direction's hem + TUCK; the ankle column (< R0, faded to R1) stretched per direction
    // when that takes <= SOFTST. Reaching all round: all own boot out. Else per direction the own boot below the collar - OVER
    // goes (a sneaker's open throat keeps the own boot there, as the instep). Then scaled round the leg per bin: under pants
    // worn over boots (<= rp - GAP), over pants tucked into boots (>= rp + GAP), over a kept own shaft (>= rb + GAP); 3 x 3 smoothed.
    public class Leg { public bool neg; public Vector2 ax; public float zc; public float[] rb, rp, hem; }
    public class Pants { public int[] subs; public int verts; public int[] tris; public float[] zmax; public byte[] neg, dir; public readonly List<Leg> legs = new List<Leg>(); }
    public static readonly Dictionary<string, Pants> PantsData = new Dictionary<string, Pants>();
    const float ZA = 0.09f, TUCK = 0.015f, GAP = 0.003f, ZB = 0.01f, MAXST = 2.5f, SOFTST = 1.6f, OVER = 0.01f, R0 = 0.08f, R1 = 0.13f, RC = 0.10f; const int NA = 24, NZ = 45, SK = 4;
    static string CutText(float[] c) => c == null ? "own boot kept" : c.All(float.IsPositiveInfinity) ? "all" : $"{c.Where(x => !float.IsInfinity(x)).DefaultIfEmpty(0).Min():0.000}..{c.Where(x => !float.IsInfinity(x)).DefaultIfEmpty(0).Max():0.000}";
    static int Dir(float x, float y, Vector2 ax) => ((int)(Mathf.Repeat(Mathf.Atan2(y - ax.y, x - ax.x), 2 * Mathf.PI) / (2 * Mathf.PI) * NA)) % NA;
    static (float r, int zi, int ai) Bin(Vector3 v, Vector2 ax)
    {
        var d = new Vector2(v.x, v.y) - ax; float a = Mathf.Repeat(Mathf.Atan2(d.y, d.x), 2 * Mathf.PI) / (2 * Mathf.PI) * NA;
        return (d.magnitude, Mathf.Clamp((int)(v.z / ZB), 0, NZ - 1), ((int)a) % NA);
    }
    // the collar per direction: highest point of the leg's triangle surfaces within RC of the ankle (NaN = none)
    static float[] Collar(Vector3[] P, List<int> T, Vector2 ax)
    {
        var c = new float[NA]; for (int j = 0; j < NA; j++) c[j] = float.NaN;
        for (int t = 0; t < T.Count; t += 3)
            for (int i = 0; i <= SK; i++)
                for (int k = 0; k <= SK - i; k++)
                {
                    float u = i / (float)SK, v = k / (float)SK, w = 1 - u - v;
                    var p = P[T[t]] * u + P[T[t + 1]] * v + P[T[t + 2]] * w;
                    if ((new Vector2(p.x, p.y) - ax).magnitude >= RC) continue;
                    int j = Dir(p.x, p.y, ax); if (float.IsNaN(c[j]) || p.z > c[j]) c[j] = p.z;
                }
        return c;
    }
    static Vector3[] FitShoe(Vector3[] src, int[] tris, List<Leg> legs, List<string> log, out float[] cutN, out float[] cutP)
    {
        var P = (Vector3[])src.Clone(); cutN = cutP = null;
        foreach (var L in legs)
        {
            var idx = Enumerable.Range(0, P.Length).Where(i => (P[i].x < 0) == L.neg).ToList(); if (idx.Count == 0) continue;
            var mine = new HashSet<int>(idx); var T = new List<int>();
            for (int t = 0; t + 2 < tris.Length; t += 3) if (mine.Contains(tris[t]) && mine.Contains(tris[t + 1]) && mine.Contains(tris[t + 2])) { T.Add(tris[t]); T.Add(tris[t + 1]); T.Add(tris[t + 2]); }
            float top = idx.Max(i => P[i].z), st = 1f, zs = Mathf.Min(ZA, 0.5f * top);
            var c0 = Collar(P, T, L.ax); var stj = new float[NA];
            for (int j = 0; j < NA; j++)
            {
                float need = L.hem[j] + TUCK;
                stj[j] = float.IsNaN(need) || float.IsNaN(c0[j]) ? 1f : Mathf.Max(1f, (need - zs) / Mathf.Max(c0[j] - zs, 1e-3f));
            }
            var stn = new float[NA]; for (int j = 0; j < NA; j++) stn[j] = Mathf.Max(stj[j], Mathf.Max(stj[(j + 1) % NA], stj[(j + NA - 1) % NA]));
            float smaxj = stn.Max(); bool full = smaxj <= SOFTST;
            if (full && smaxj > 1f)
            {
                foreach (var i in idx)
                {
                    if (P[i].z <= zs) continue;
                    float rr = (new Vector2(P[i].x, P[i].y) - L.ax).magnitude, wr = Mathf.Clamp01((R1 - rr) / (R1 - R0));
                    float fac = 1 + (Mathf.Min(stn[Dir(P[i].x, P[i].y, L.ax)], MAXST) - 1) * wr;
                    P[i].z = zs + (P[i].z - zs) * fac;
                }
                st = smaxj;
            }
            var c1 = Collar(P, T, L.ax);
            if (full) for (int j = 0; j < NA; j++) if (!float.IsNaN(L.hem[j]) && !float.IsNaN(c1[j]) && L.hem[j] - c1[j] > -TUCK + 0.003f) { full = false; break; }
            var Hd = new float[NA];
            for (int j = 0; j < NA; j++)
            {
                if (full) { Hd[j] = float.PositiveInfinity; continue; }
                float a = float.IsNaN(c1[j]) ? float.NegativeInfinity : c1[j], b = float.IsNaN(c1[(j + 1) % NA]) ? float.NegativeInfinity : c1[(j + 1) % NA], e = float.IsNaN(c1[(j + NA - 1) % NA]) ? float.NegativeInfinity : c1[(j + NA - 1) % NA];
                Hd[j] = Mathf.Min(a, Mathf.Min(b, e)) - OVER;
            }
            if (L.neg) cutN = Hd; else cutP = Hd;
            var rs = new float[NZ * NA]; for (int k = 0; k < rs.Length; k++) rs[k] = float.NaN;
            foreach (var i in idx) { var b = Bin(P[i], L.ax); int k = b.zi * NA + b.ai; if (float.IsNaN(rs[k]) || b.r > rs[k]) rs[k] = b.r; }
            var s = new float[NZ * NA]; float smin = 9, smax = 0;
            for (int zi = 0; zi < NZ; zi++)
                for (int ai = 0; ai < NA; ai++)
                {
                    int k = zi * NA + ai; s[k] = float.NaN; float zz = (zi + 0.5f) * ZB;
                    if (zz < ZA || float.IsNaN(rs[k])) continue;
                    float b_ = L.rb[k], p_ = L.rp[k], lo, hi;
                    bool outB = !float.IsNaN(b_) && (float.IsNaN(p_) || b_ >= p_ - 0.001f);
                    if (!full && outB && zz > Hd[ai] - 0.015f) { lo = b_ + GAP; hi = float.PositiveInfinity; }
                    else
                    {
                        bool tucked = outB && !float.IsNaN(p_);
                        lo = tucked ? p_ + GAP : 0f;
                        hi = !float.IsNaN(p_) && !tucked ? p_ - GAP : float.PositiveInfinity;
                    }
                    float t = lo <= hi ? Mathf.Min(Mathf.Max(rs[k], lo), hi) : hi; s[k] = t / rs[k];
                }
            var sm = new float[NZ * NA];
            for (int zi = 0; zi < NZ; zi++)
                for (int ai = 0; ai < NA; ai++)
                {
                    float sum = 0; int c = 0;
                    for (int z2 = Math.Max(0, zi - 1); z2 <= Math.Min(NZ - 1, zi + 1); z2++)
                        for (int a2 = ai - 1; a2 <= ai + 1; a2++) { float x = s[z2 * NA + ((a2 + NA) % NA)]; if (!float.IsNaN(x)) { sum += x; c++; } }
                    sm[zi * NA + ai] = c > 0 ? sum / c : float.NaN;
                    if (c > 0) { smin = Mathf.Min(smin, sm[zi * NA + ai]); smax = Mathf.Max(smax, sm[zi * NA + ai]); }
                }
            foreach (var i in idx)
            {
                if (P[i].z < ZA) continue;
                var b = Bin(P[i], L.ax); float f = sm[b.zi * NA + b.ai]; if (float.IsNaN(f)) continue;
                var d = new Vector2(P[i].x, P[i].y) - L.ax; var q = L.ax + d * f; P[i] = new Vector3(q.x, q.y, P[i].z);
            }
            log.Add($"{(L.neg ? "n" : "p")}: {(full ? "own boot all out" : "own boot out below the collar " + CutText(Hd))}, top {top:0.000} cuff {L.zc:0.000} hem {L.hem.Where(x => !float.IsNaN(x)).DefaultIfEmpty(0).Min():0.000}..{L.hem.Where(x => !float.IsNaN(x)).DefaultIfEmpty(0).Max():0.000} collar {c1.Where(x => !float.IsNaN(x)).DefaultIfEmpty(0).Min():0.000}..{c1.Where(x => !float.IsNaN(x)).DefaultIfEmpty(0).Max():0.000} stretch {st:0.00} scale {(smax > 0 ? smin : 1):0.00}..{(smax > 0 ? smax : 1):0.00}");
        }
        return P;
    }

    // one side's bones (match) in this renderer swapped for one point at that side's <pivot> bone, scaled to shrink
    static void CutPart(SkinnedMeshRenderer smr, string side, Func<Transform, bool> match, string pivot, float shrink, string what, List<Swap> swaps, string who)
    {
        var bones = (Transform[])smr.bones.Clone();
        var pv = bones.FirstOrDefault(b => b != null && b.name.Contains("Human" + side + pivot));
        if (pv == null) { Plugin.Log.LogInfo($"[WristAccessories] shoes ({who}): no {side} {pivot} bone in {smr.name} - nothing hidden there"); return; }
        var pt = new GameObject($"WristAcc_{what}Hidden_" + side).transform; pt.SetParent(pv, false); pt.localPosition = Vector3.zero; pt.localRotation = Quaternion.identity; pt.localScale = Vector3.one * shrink;
        int k = 0; for (int i = 0; i < bones.Length; i++) if (bones[i] != null && match(bones[i])) { bones[i] = pt; k++; }
        var sw = swaps.FirstOrDefault(x => x.r == smr);
        if (sw == null) swaps.Add(sw = new Swap { r = smr, bones = smr.bones });
        sw.points.Add(pt.gameObject); smr.bones = bones;
        Plugin.Log.LogInfo($"[WristAccessories] shoes ({who}): your {side} {what} tucked in {smr.name} ({k} bones shrunk to {shrink:0.00} at the {pivot.ToLowerInvariant()})");
    }

    // ---- tattoo overlays (kind "tattoo", lab/ink_export.py; user 2026-10-27: "make the tattoos applicable to every bare hand"): the
    // tattooed hand's shell a hair over the wearer's own hand, drawn with a copy of its texture whose skin pixels are transparent
    // (IsSkin), Unity's Standard shader in cut-out mode (the game ships it; the beads use it too) - only the ink shows. Built once
    // per source material.
    static readonly Dictionary<int, Material> InkCache = new Dictionary<int, Material>();
    static Material InkMaterial(Material m, string item, string who, bool inkAll)
    {
        if (m == null || !m.HasProperty("_MainTex") || m.GetTexture("_MainTex") == null) return m;
        int key = m.GetInstanceID() * 2 + (inkAll ? 1 : 0);
        if (InkCache.TryGetValue(key, out var c) && c != null) return c;
        var std = Shader.Find("Standard"); if (std == null) { Plugin.Log.LogWarning($"[WristAccessories] tattoos ({who}): no Standard shader - {item} drawn as it is"); return m; }
        var src = m.GetTexture("_MainTex"); Texture2D t = null;
        try
        {
            t = Copy(src, src.width, src.height); var px = t.GetPixels32(); int ink = 0;
            // ink = LOCALLY darker than the skin around it (1.0.5, user 2026-10-28: Lonsdale's mid-brown bone tattoo was not "dark"
            // against the whole texture's skin, so it hardly showed; lab/mask_new*.jpg): CIELAB L under the skin-only average of a
            // 49 px (per 1024) window minus 8, where that window is >= 60 % skin (texture islands' edges and clothing stay out)
            int w = t.width, hgt = t.height, cs = Math.Max(1, w / 512), gw = (w + cs - 1) / cs, gh = (hgt + cs - 1) / cs;
            var Ls = new float[px.Length]; var cellL = new float[gw * gh]; var cellN = new float[gw * gh]; var cellC = new float[gw * gh];
            for (int i = 0; i < px.Length; i++)
            {
                Color pc = px[i]; Ls[i] = ToLab(pc).x; int x = i % w, y = i / w, ci = (y / cs) * gw + x / cs; cellC[ci] += 1;
                if (SkinW(pc) > 0.5f) { cellL[ci] += Ls[i]; cellN[ci] += 1; }
            }
            int R = Math.Max(1, (int)Math.Round(24f * w / 1024f / cs));
            float[] Blur(float[] f) { var o = new float[f.Length]; var tmp = new float[f.Length];
                for (int y = 0; y < gh; y++) { float acc = 0; for (int x = -R; x < gw + R; x++) { if (x + R < gw) acc += f[y * gw + x + R]; if (x - R - 1 >= 0) acc -= f[y * gw + x - R - 1]; if (x >= 0 && x < gw) tmp[y * gw + x] = acc; } }
                for (int x = 0; x < gw; x++) { float acc = 0; for (int y = -R; y < gh + R; y++) { if (y + R < gh) acc += tmp[(y + R) * gw + x]; if (y - R - 1 >= 0) acc -= tmp[(y - R - 1) * gw + x]; if (y >= 0 && y < gh) o[y * gw + x] = acc; } }
                return o; }
            var sL = Blur(cellL); var sN = Blur(cellN); var sC = Blur(cellC);
            for (int i = 0; i < px.Length; i++)
            {
                int x = i % w, y = i / w, ci = (y / cs) * gw + x / cs;
                bool isInk = sC[ci] > 0 && sN[ci] / sC[ci] >= 0.6f && Ls[i] < sL[ci] / Math.Max(1f, sN[ci]) - 8f;
                // "ink_all" (1.2.0, sheet 2026-10-29: Scorpion's sleeves are nearly all ink, so no window is 60 % skin): every texel that
                // is not skin-coloured is ink (the item's triangles cover only its bare-arm patch). Skin here is narrower than SkinW
                // (hue 2-40 deg, saturation 0.15-0.62, value > 0.3) so crimson ink stays ink (checked on its texture in python)
                if (inkAll && !isInk) { Color.RGBToHSV(px[i], out float ih, out float isat, out float iv); ih *= 360f; isInk = !(ih >= 2f && ih <= 40f && isat >= 0.15f && isat <= 0.62f && iv > 0.3f); }
                px[i].a = isInk ? (byte)255 : (byte)0; if (isInk) ink++;
            }
            t.SetPixels32(px); t.Apply(true); t.name = src.name + "_WristAccInk"; t.wrapMode = src.wrapMode; UnityEngine.Object.DontDestroyOnLoad(t);
            c = new Material(std) { name = m.name + "_WristAccInk" }; UnityEngine.Object.DontDestroyOnLoad(c);
            c.SetTexture("_MainTex", t); c.SetColor("_Color", Color.white); c.SetFloat("_Mode", 1f); c.SetOverrideTag("RenderType", "TransparentCutout");
            c.EnableKeyword("_ALPHATEST_ON"); c.SetFloat("_Cutoff", 0.5f); c.renderQueue = 2450; c.SetFloat("_Glossiness", 0.25f); c.SetFloat("_Metallic", 0f);
            // no _BumpMap: the game's normal maps are packed for its own shaders; under Standard they lit the ink white (SeamTest 2026-10-27)
            c.SetFloat("_Glossiness", 0.1f); c.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); c.SetFloat("_SpecularHighlights", 0f); c.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); c.SetFloat("_GlossyReflections", 0f);
            InkCache[key] = c;
            Plugin.Log.LogInfo($"[WristAccessories] tattoos ({who}): {item} ink {100f * ink / Math.Max(1, px.Length):0}%{(inkAll ? " (all non-skin)" : "")} of {src.name} ({src.width}x{src.height}), Standard cut-out");
            return c;
        }
        catch (Exception e) { if (t != null) UnityEngine.Object.Destroy(t); Plugin.Log.LogError($"[WristAccessories] tattoos {item}: {e.Message}"); return m; }
    }

    // ---- skin tone: the handwear's skin pixels moved to the player's own skin colour (user 2026-10-27: the other outfit's
    // hand showed its own tone at the wrist). Skin = the hue/saturation/value range lab/scan_hands.py uses; per-channel gain
    // = own mean / item mean over skin pixels, applied to skin pixels only (tape, gloves, straps keep their colours).
    static bool IsSkin(Color c) { Color.RGBToHSV(c, out float h, out float s, out float v); h *= 360f; return (h < 50f || h > 340f) && s > 0.12f && s < 0.7f && v > 0.3f && c.r - c.g > 0.1f; }   // r - g: skin, not brown cloth
    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    static readonly Dictionary<int, (Color mean, float frac)> Stats = new Dictionary<int, (Color, float)>();
    static readonly Dictionary<string, Material> TonedCache = new Dictionary<string, Material>();
    // the game's textures are not readable: a copy through a render texture (sRGB in, sRGB out)
    static Texture2D Copy(Texture src, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active; Graphics.Blit(src, rt); RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); return t;
    }
    static (Color mean, float frac) SkinOf(Texture tex)
    {
        if (tex == null) return (Color.clear, 0);
        if (Stats.TryGetValue(tex.GetInstanceID(), out var st)) return st;
        var t = Copy(tex, 256, 256); var px = t.GetPixels(); UnityEngine.Object.Destroy(t);
        double r = 0, g = 0, b = 0; int n = 0;
        foreach (var c in px) if (IsSkin(c)) { r += c.r; g += c.g; b += c.b; n++; }
        st = n > 0 ? (new Color((float)(r / n), (float)(g / n), (float)(b / n)), n / (float)px.Length) : (Color.clear, 0f);
        Stats[tex.GetInstanceID()] = st; return st;
    }
    static readonly List<string> TonedOrder = new List<string>();      // oldest first: the 12 newest toned copies are kept
    static Material Toned(Material m, Color own, Item it, string who)
    {
        if (m == null || !m.HasProperty("_MainTex")) return m;
        var tex = m.GetTexture("_MainTex"); var st = SkinOf(tex);
        string item = it.name;
        if (it.skin == null && st.frac < 0.03f) return m;                   // a full glove: nothing to match
        if (it.skin != null) st.mean = it.skin.Value;                       // the piece's own skin (lab), not the whole texture's
        var gain = new Color(Mathf.Clamp(own.r / Mathf.Max(st.mean.r, 0.01f), 0.5f, 2f), Mathf.Clamp(own.g / Mathf.Max(st.mean.g, 0.01f), 0.5f, 2f), Mathf.Clamp(own.b / Mathf.Max(st.mean.b, 0.01f), 0.5f, 2f));
        string k = m.GetInstanceID() + "|" + Hex(own);
        if (TonedCache.TryGetValue(k, out var c) && c != null) return c;
        Texture2D t = null;
        try
        {
            t = Copy(tex, tex.width, tex.height); var px = t.GetPixels32(); int changed = 0;
            for (int i = 0; i < px.Length; i++)
            {
                Color p = px[i]; if (!IsSkin(p)) continue;
                px[i] = new Color(Mathf.Clamp01(p.r * gain.r), Mathf.Clamp01(p.g * gain.g), Mathf.Clamp01(p.b * gain.b), p.a); changed++;
            }
            t.SetPixels32(px); t.Apply(true); t.Compress(true); t.Apply(false, true);
            t.name = tex.name + "_WristAccTone"; t.wrapMode = tex.wrapMode; t.filterMode = tex.filterMode; t.anisoLevel = tex.anisoLevel;
            UnityEngine.Object.DontDestroyOnLoad(t);
            c = new Material(m) { name = m.name + "_WristAccTone" }; c.SetTexture("_MainTex", t); UnityEngine.Object.DontDestroyOnLoad(c);
            TonedCache[k] = c; TonedOrder.Add(k);
            while (TonedOrder.Count > 12)
            {
                var old = TonedOrder[0]; TonedOrder.RemoveAt(0);
                if (TonedCache.TryGetValue(old, out var oc) && oc != null && oc != m) { UnityEngine.Object.Destroy(oc.GetTexture("_MainTex")); UnityEngine.Object.Destroy(oc); }
                TonedCache.Remove(old);
            }
            Plugin.Log.LogInfo($"[WristAccessories] handwear ({who}): {item} skin {Hex(st.mean)} -> your {Hex(own)} on {changed} texels of {tex.name} ({st.frac * 100:0}% skin)");
            return c;
        }
        catch (Exception e) { if (t != null) UnityEngine.Object.Destroy(t); Plugin.Log.LogError($"[WristAccessories] handwear skin tone {item}: {e.Message}"); TonedCache[k] = m; return m; }
    }

    // one second after it is put on: where the piece really is (its drawn bounds against the palm bone) - a piece drawn unskinned
    // or stretched shows here (user 2026-10-27: a glove drawn as a huge cone in first person)
    // also the wrist items (WristRig: watches, bracelets, rings, sleeves - their own size and distance limits)
    public class Check : MonoBehaviour
    {
        public string Who, Name, Ref = "the palm"; public Transform Palm; public float MaxSize = 0.5f, MaxAway = 0.3f; float _t;
        void Update()
        {
            if ((_t += Time.unscaledDeltaTime) < 1f) return;
            var rs = GetComponentsInChildren<Renderer>(false).Where(x => x.enabled).ToArray();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var x in rs.Skip(1)) b.Encapsulate(x.bounds);
                float size = b.size.magnitude, away = Palm != null ? (b.center - Palm.position).magnitude : -1f;
                var msg = $"[WristAccessories] drawn ({Who}): {Name} {size * 1000:0} mm across, middle {away * 1000:0} mm from {Ref}, visible {rs.Any(x => x.isVisible)}";
                if (size > MaxSize || away > MaxAway) Plugin.Log.LogWarning(msg + " - TOO BIG / AWAY"); else Plugin.Log.LogInfo(msg);
            }
            Destroy(this);
        }
    }

    public static void Restore(List<Swap> swaps)
    {
        for (int i = swaps.Count - 1; i >= 0; i--)                      // newest first: a seam copy made on top of another swap
        {
            var s = swaps[i];
            if (s.r != null && s.bones != null) s.r.bones = s.bones;
            if (s.r != null && s.mesh != null) { var cur = s.r.sharedMesh; s.r.sharedMesh = s.mesh; if (cur != null && cur != s.mesh) UnityEngine.Object.Destroy(cur); }   // own boots back
            foreach (var p in s.points) if (p != null) UnityEngine.Object.Destroy(p);
        }
        swaps.Clear();
    }
}
