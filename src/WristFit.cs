using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WristAccessories;

// Several accessories per wrist (both wrists) and a ring, built at runtime around the sleeve of the outfit you wear.
// Offline data (tools in WORK\wristacc\tools and WORK\tattoo\acc):
//   outlines.json   - per hands id and side: the outermost sleeve radius per 2 mm along the forearm x 5 deg, the max over
//                     27 wrist poses (bend, deviation, twist), in <S>Forearm3 bone space; same for the ring finger
//   acc/acc_*.json  - per game item: every vertex's place along a band curve (s), out (r), along the arm (h), sideways
//                     (dt), its piece and whether the piece follows the curve (strand), stays rigid or hangs (pendant)
// Placement = tools/fit.py (the reference this ports): strands follow the outline per vertex, rigid pieces sit on the
// outline under them (+ the sagitta of a flat piece on a round sleeve), 2 mm clearance; items stack from where the
// sleeve stops following the hand toward the elbow, 3 mm apart, so they clear each other.
static class WristFit
{
    const float CLR = 0.002f, GAP = 0.003f;

    public class Outline
    {
        public Vector3 w, ax, e1, e2; public float a0, da, start; public int na, nd; public float[] tab;
        public float T(int a, int k) => tab[a * nd + k];
    }

    public class AccMesh
    {
        public string mesh, bundle; public int mat = -1; public float[] s, dt, r, h; public int[] pid, kind; public Vector3[] n; public Vector4[] t; public Vector2[] uv; public int[][] tris; public int[] swing;
        public float[] sFix, hFix;   // Straighten: hole closed, slant taken out (null = as measured)
    }

    /// <summary>Fitted watches sat turned 180 deg from how the game wears them (user 2026-10-26: "is he wearing the watch the right
    /// way?"; lab: the BT_3 outfit's own Garmin reads upside down in the view where our placed Garmin read upright, crown toward
    /// the elbow on the left wrist). A turn of 180 deg about the dial's own axis (radial): round the loop and along the arm
    /// mirrored about the head, tangential and along-arm parts of offsets, normals, tangents and the moving hands' axes negated
    /// - a rotation, not a mirror, so dials and text stay readable.</summary>
    static void TurnWatch(Acc A)
    {
        if (A.meshes.Count == 0 || A.meshes[0].s == null) return;
        // the head: the biggest rigid piece (the dial's piece when known)
        int head = A.dial != null ? A.dial.pid : A.meshes.SelectMany(m => Enumerable.Range(0, m.pid.Length).Where(i => m.kind[i] > 0).Select(i => m.pid[i])).GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => g.Key).DefaultIfEmpty(-1).First();
        var hs = new List<float>(); var hh = new List<float>();
        foreach (var m in A.meshes) for (int i = 0; i < m.s.Length; i++) if (m.kind[i] > 0 && m.pid[i] == head) { hs.Add(m.s[i]); hh.Add(m.h[i]); }
        if (hs.Count == 0) { Plugin.Log.LogWarning($"[WristAccessories] {A.name}: no head piece - not turned"); return; }
        float sc = hs.Average(), hc = hh.Average();
        foreach (var m in A.meshes)
        {
            for (int i = 0; i < m.s.Length; i++)
            {
                m.s[i] = Mathf.Repeat(2 * sc - m.s[i], A.L); m.h[i] = 2 * hc - m.h[i]; m.dt[i] = -m.dt[i];
                m.n[i] = new Vector3(-m.n[i].x, m.n[i].y, -m.n[i].z); m.t[i] = new Vector4(-m.t[i].x, m.t[i].y, -m.t[i].z, m.t[i].w);
            }
            // the triangles keep their winding: a rotation does not flip faces
        }
        if (A.dial != null)
        {
            A.dial.dt = -A.dial.dt; A.dial.h = 2 * hc - A.dial.h;
            A.dial.axes = A.dial.axes.Select(v => new Vector3(-v.x, v.y, -v.z)).ToArray();
        }
        A.hmid = 2 * hc - A.hmid;
        Plugin.Log.LogInfo($"[WristAccessories] {A.name}: turned 180 deg about its dial (head piece {head}, {hs.Count} verts)");
    }

    /// <summary>Closed outfit bracelets as measured slant along the arm (their source arm's tilt, 10-35 mm round the loop) and a few
    /// have holes (Killa's ID chain 78 mm, Gladiator bar 38 mm, Bomber silver chain 13 mm); in first person a slanted loop
    /// runs along the arm on one side and looks open (user 2026-10-26: "they dont fully connect"). Set by WristRig.</summary>
    public static bool StraightenOn = true;

    /// <summary>Per closed bracelet (not watches' heads, not open cuffs): the biggest hole in the loop squeezed to the usual step,
    /// and the loop's slant (the first harmonic of its strand's along-arm offset round the loop) taken out, per strand.</summary>
    static void Straighten(Acc A)
    {
        if (!A.closed || A.L <= 0 || A.meshes.Count == 0 || A.meshes[0].s == null) return;
        bool cuff = (A.note ?? "").IndexOf("cuff", StringComparison.OrdinalIgnoreCase) >= 0;
        // the hole: arc positions (rigid pieces by their tangential extent too)
        var arc = new List<float>();
        foreach (var m in A.meshes) for (int i = 0; i < m.s.Length; i++) arc.Add(Mathf.Repeat(m.kind[i] > 0 ? m.s[i] + m.dt[i] : m.s[i], A.L));
        arc.Sort(); var steps = new List<float>(); float gap = 0, gEnd = 0;
        for (int i = 0; i < arc.Count; i++)
        {
            float d = i + 1 < arc.Count ? arc[i + 1] - arc[i] : arc[0] + A.L - arc[i];
            if (d > 1e-4f) steps.Add(d);
            if (d > gap) { gap = d; gEnd = i + 1 < arc.Count ? arc[i + 1] : arc[0]; }
        }
        steps.Sort(); float typ = steps.Count > 0 ? steps[(int)(steps.Count * 0.95f)] : 0;
        bool close = !cuff && gap > 0.008f && gap > 3 * typ;
        string why = "";
        foreach (var m in A.meshes) m.sFix = (float[])m.s.Clone();
        if (close)
        {
            float k = A.L / (A.L - gap + typ);
            foreach (var m in A.meshes) for (int i = 0; i < m.s.Length; i++) m.sFix[i] = Mathf.Repeat(m.s[i] - gEnd, A.L) * k;
            why += $"hole {gap * 1000:0.0} mm closed (usual step {typ * 1000:0.0} mm)";
        }
        // the slant, per strand (mesh) whose band (non-rigid vertices) goes round at least 2/3 of the loop, else the whole item's
        float[] Fit(IEnumerable<AccMesh> ms)
        {
            const int NB = 24; var bins = new List<float>[NB]; for (int b = 0; b < NB; b++) bins[b] = new List<float>();
            foreach (var m in ms) for (int i = 0; i < m.s.Length; i++) if (m.kind[i] == 0) bins[(int)(Mathf.Repeat(m.sFix[i], A.L) / A.L * NB) % NB].Add(m.h[i]);
            if (bins.Count(b => b.Count > 0) < 16)               // all rigid (bead strings, link chains): the pieces themselves
                foreach (var m in ms) for (int i = 0; i < m.s.Length; i++) bins[(int)(Mathf.Repeat(m.sFix[i], A.L) / A.L * NB) % NB].Add(m.h[i]);
            int have = 0; double c00 = 0, c01 = 0, c11 = 0, y0 = 0, y1 = 0, cs = 0, sn = 0, y = 0;
            for (int b = 0; b < NB; b++)
            {
                if (bins[b].Count == 0) continue; have++;
                bins[b].Sort(); float med = bins[b][bins[b].Count / 2]; double th = 2 * Math.PI * (b + 0.5) / NB;
                cs += Math.Cos(th); sn += Math.Sin(th); y += med; c00 += Math.Cos(th) * Math.Cos(th); c01 += Math.Cos(th) * Math.Sin(th); c11 += Math.Sin(th) * Math.Sin(th);
                y0 += med * Math.Cos(th); y1 += med * Math.Sin(th);
            }
            if (have < 16) return null;
            // least squares h = c + a cos + b sin (centre removed first)
            double mc = cs / have, ms_ = sn / have, my = y / have;
            double A00 = c00 - have * mc * mc, A01 = c01 - have * mc * ms_, A11 = c11 - have * ms_ * ms_, B0 = y0 - have * mc * my, B1 = y1 - have * ms_ * my;
            double det = A00 * A11 - A01 * A01; if (Math.Abs(det) < 1e-9) return null;
            return new[] { (float)((B0 * A11 - B1 * A01) / det), (float)((A00 * B1 - A01 * B0) / det) };
        }
        var whole = Fit(A.meshes); float maxTilt = 0; int per = 0;
        foreach (var m in A.meshes)
        {
            var ab = Fit(new[] { m }); if (ab != null) per++; else ab = whole;
            m.hFix = (float[])m.h.Clone(); if (ab == null) continue;
            for (int i = 0; i < m.s.Length; i++)
            {
                double th = 2 * Math.PI * Mathf.Repeat(m.sFix[i], A.L) / A.L;
                m.hFix[i] = m.h[i] - (float)(ab[0] * Math.Cos(th) + ab[1] * Math.Sin(th));
            }
            maxTilt = Math.Max(maxTilt, 2 * Mathf.Sqrt(ab[0] * ab[0] + ab[1] * ab[1]));
        }
        why += (why != "" ? ", " : "") + $"slant {maxTilt * 1000:0.0} mm taken out ({per}/{A.meshes.Count} strands on their own)";
        Plugin.Log.LogInfo($"[WristAccessories] {A.name}: straightened: {why}");
    }

    public class Dial { public string prefab; public int pid; public float dt, r, h; public Vector3[] axes; }   // a watch's moving hands, relative to its head
    public class Acc { public string name, note, bundle; public bool closed; public float L, width, hmid; public List<AccMesh> meshes = new List<AccMesh>(); public Dial dial; public string texture; public float fitR; public bool loose; public string accDir; public Src src; }
    /// <summary>A watch as its own outfit wears it, in the LForearm3 bone space all outfits share: the outfit (its outline), the
    /// strap loop's centre on the arm axis, the face direction.</summary>
    public class Src { public string hid; public Vector3 c, face; public List<(Vector3[] P, Vector3[] N, Vector4[] T)> meshes; }   // meshes: exact original shape, per acc mesh

    static Dictionary<string, JObject> _outlines, _bodyOutlines;
    /// <summary>0 = fit the arm at rest, 1 = clear every measured wrist pose (Plugin's \"Sleeve fit\" setting).</summary>
    /// <summary>Items cut from other modders' outfits (ISB-Aishi; WTT-Artem removed entirely 2026-10-27): left out (user 2026-10-26: "remove all items that
    /// come from other mods, like ISB. keep the content backport ones"). Files moved to BACKUPS\wristacc-removed-othermods.</summary>
    static readonly HashSet<string> OtherMods = new HashSet<string> { "outfit_003ce70e_L0", "outfit_07af517b_L0", "outfit_097ef7fc_L0", "outfit_20f8ddd0_L0", "outfit_67685766_L0", "outfit_67685766_R1", "outfit_6771c3db_L0", "isb_sleeve", "mixed_sleeve", "beads_angelite_loose", "beads_gems_loose" };   // + the loose bead bracelets (user 2026-10-26: removed)
    static int skipped;
    public static float PoseBlend = 1f;
    /// <summary>Per item: an extra turn round the arm in degrees (setting "Item angles", the tab's ANGLE row; user 2026-10-26: the
    /// angle setting for every bracelet). Set by WristRig before placing.</summary>
    public static readonly Dictionary<string, float> TurnDeg = new Dictionary<string, float>();
    static float Turn(string name) => TurnDeg.TryGetValue(name, out float d) ? d * Mathf.Deg2Rad : 0f;
    /// <summary>Per item: moved along the arm, mm (+ toward the hand), setting "Item slides" (user 2026-10-26: beads "hanging on
    /// nothing" on the sleeve cuff - move them down onto the wrist where the outfit's own bands are). Items after it stack on.</summary>
    public static readonly Dictionary<string, float> SlideMm = new Dictionary<string, float>();
    public static void ParseSlides(string cfg) { ParseInto(cfg, SlideMm); }
    static void ParseInto(string cfg, Dictionary<string, float> into)
    {
        into.Clear();
        foreach (var kv in (cfg ?? "").Split(';'))
        {
            var p = kv.Split('='); if (p.Length != 2) continue;
            if (float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d) && Math.Abs(d) > 0.01f) into[p[0].Trim()] = d;
        }
    }
    public static void ParseTurns(string cfg)
    {
        TurnDeg.Clear();
        foreach (var kv in (cfg ?? "").Split(';'))
        {
            var p = kv.Split('='); if (p.Length != 2) continue;
            if (float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d) && Math.Abs(d) > 0.01f) TurnDeg[p[0].Trim()] = d;
        }
    }
    /// <summary>Most room (m) the outline leaves for the posed bulge (stiff cuffs fold out far: items floated 4-6 mm at rest; the
    /// live fit handles the rest). Infinity = no cap.</summary>
    public static float PoseCap = float.PositiveInfinity;
    /// <summary>The outfit's baked watch is hidden (another watch or none picked): use the outlines measured without it (tabnw /
    /// tab0nw, where the hands have one). Its invisible watch inflated the wrist ~9 mm (Hands_BT_3's Garmin).</summary>
    public static bool NoOutfitWatch;
    /// <summary>Loose items (e.g. beads_angelite_loose): this many sizes bigger than what fits (Plugin's "Loose bracelet size").</summary>
    public static int LooseSteps = 0;   // first-person hands / third-person upper bodies
    public static readonly Dictionary<string, Acc> Accs = new Dictionary<string, Acc>();
    static string _dir;
    public static string Dir => _dir;

    public static void Load(string dir)
    {
        _dir = dir;
        try
        {
            var t0 = DateTime.Now;
            _outlines = JObject.Parse(File.ReadAllText(Path.Combine(dir, "outlines.json"))).Properties().ToDictionary(p => p.Name, p => (JObject)p.Value);
            var bp = Path.Combine(dir, "outlines_body.json");
            _bodyOutlines = File.Exists(bp) ? JObject.Parse(File.ReadAllText(bp)).Properties().ToDictionary(p => p.Name, p => (JObject)p.Value) : new Dictionary<string, JObject>();
            foreach (var f in Directory.GetFiles(Path.Combine(dir, "acc"), "acc_*.json"))
            {
                if (OtherMods.Contains(Path.GetFileNameWithoutExtension(f).Substring(4))) { skipped++; continue; }   // an old install's copy: the addons bring them now
                var a = ParseAcc(JObject.Parse(File.ReadAllText(f))); a.accDir = Path.Combine(dir, "acc");
                Accs[a.name] = a;
            }
            HandWear.Items.Clear(); HandWear.Load(Path.Combine(dir, "acc"));
            LoadAddons(dir);
            DogTags.Load(new[] { Path.Combine(dir, "acc") }.Concat(AddonDirs.Select(a => Path.Combine(a, "acc"))));
            Plugin.Log.LogInfo($"[WristAccessories] fit data: {_outlines.Count} hands + {_bodyOutlines.Count} upper-body outlines, accessories {string.Join(", ", Accs.Keys)}, {skipped} from other mods left out ({(DateTime.Now - t0).TotalMilliseconds:0} ms)");
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] fit data: " + e); }
    }

    /// <summary>Addons (user 2026-10-26: other mods' items as addons of this mod): plugin folders next to ours holding
    /// wristacc_addon.json {"name", "requires": [paths from the game folder]} and an acc folder (acc_*.json, icons/, textures).
    /// An addon whose required mod is not installed is skipped (its items' bundles would be missing).</summary>
    public static readonly List<string> AddonDirs = new List<string>();     // the loaded addons' folders (their watchshadow data)
    static void LoadAddons(string dir)
    {
        AddonDirs.Clear();
        var plugins = Directory.GetParent(dir)?.FullName; if (plugins == null) return;
        foreach (var ad in Directory.GetDirectories(plugins))
        {
            var mf = Path.Combine(ad, "wristacc_addon.json"); if (!File.Exists(mf)) continue;
            try
            {
                var m = JObject.Parse(File.ReadAllText(mf)); string name = (string)m["name"] ?? Path.GetFileName(ad);
                var missing = (m["requires"]?.ToObject<string[]>() ?? new string[0]).Where(r => !Directory.Exists(Path.Combine(BepInEx.Paths.GameRootPath, r)) && !File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, r))).ToList();
                if (missing.Count > 0) { Plugin.Log.LogWarning($"[WristAccessories] addon {name}: skipped, needs {string.Join(", ", missing)}"); continue; }
                var accDir = Path.Combine(ad, "acc"); int n = 0;
                foreach (var f in Directory.GetFiles(accDir, "acc_*.json")) { var a = ParseAcc(JObject.Parse(File.ReadAllText(f))); a.accDir = accDir; Accs[a.name] = a; n++; }
                HandWear.Load(accDir); AddonDirs.Add(ad);
                Plugin.Log.LogInfo($"[WristAccessories] addon {name}: {n} accessories");
            }
            catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] addon {ad}: {e.Message}"); }
        }
    }

    static Acc ParseAcc(JObject j)
    {
        var a = new Acc { name = (string)j["name"], note = (string)j["note"], bundle = (string)j["bundle"], closed = (bool)j["closed"], L = (float)j["L"], width = (float)j["width"], hmid = (float)j["hmid"], texture = (string)j["texture"], fitR = j["fitR"] != null ? (float)j["fitR"] : 0f, loose = j["loose"] != null && (bool)j["loose"] };
        foreach (JObject m in j["meshes"])
        {
            var am = new AccMesh
            {
                mesh = (string)m["mesh"], bundle = (string)m["bundle"], mat = m["mat"] != null ? (int)m["mat"] : -1,
                s = m["s"].ToObject<float[]>(), dt = m["dt"].ToObject<float[]>(), r = m["r"].ToObject<float[]>(), h = m["h"].ToObject<float[]>(),
                pid = m["pid"].ToObject<int[]>(), kind = m["kind"].ToObject<int[]>(),
                n = m["n"].ToObject<float[][]>().Select(v => new Vector3(v[0], v[1], v[2])).ToArray(),
                t = m["t"].ToObject<float[][]>().Select(v => new Vector4(v[0], v[1], v[2], v[3])).ToArray(),
                uv = m["uv"].ToObject<float[][]>().Select(v => new Vector2(v[0], v[1])).ToArray(),
                tris = m["tris"].ToObject<int[][]>(),
                swing = m["swing"] != null ? m["swing"].ToObject<int[]>() : null,   // a hanging piece: [pivot vertex, tip vertex]
            };
            if (string.IsNullOrEmpty(am.bundle)) am.bundle = a.bundle;
            a.meshes.Add(am);
        }
        if (a.fitR <= 0 && !a.loose && (a.note ?? "").IndexOf("rigid case", StringComparison.Ordinal) < 0) Straighten(a);   // watches keep theirs: a head is not a slant (addon watches made rigid by lab/rigid_watch.py too)
        if (a.fitR > 0) TurnWatch(a);
        if (j["src"] is JObject sj)                                          // where its own outfit wears it (watches.py SRC_ONLY)
            a.src = new Src { hid = (string)sj["hid"], c = V3(sj["c"]), face = V3(sj["face"]),
                meshes = sj["meshes"] is JArray ma ? ma.Select(m => (m["P"].Select(V3).ToArray(), m["N"].Select(V3).ToArray(), m["T"].Select(t => new Vector4((float)t[0], (float)t[1], (float)t[2], (float)t[3])).ToArray())).ToList() : null };
        if (j["dial"] is JObject d)
            a.dial = new Dial { prefab = (string)d["prefab"], pid = (int)d["pid"], dt = (float)d["dt"], r = (float)d["r"], h = (float)d["h"],
                                axes = d["axes"].ToObject<float[][]>().Select(v => new Vector3(v[0], v[1], v[2])).ToArray() };
        return a;
    }

    /// <summary>The outline id for the hands (or upper body) the game actually draws. The customization id can be stale: a
    /// live re-dress (HideoutLiveClothing) swaps the skin without PlayerBody.Init (user 2026-10-26: items floated 10 mm, outline
    /// of BT_3 on the bare Hawaii arm). Kept when its outline's mesh is among the drawn meshes, else the first outline with
    /// a drawn mesh (outfits sharing a mesh differ only in textures).</summary>
    public static string DrawnId(string id, IEnumerable<string> drawnMeshes, bool body, out string note)
    {
        note = null;
        var src = body ? _bodyOutlines : _outlines;
        var drawn = new HashSet<string>(drawnMeshes.Where(m => m != null).Select(m => m.Replace(" Instance", "").Replace("(Clone)", "").Trim()));
        if (src == null || drawn.Count == 0) return id;
        if (src.TryGetValue(id ?? "", out var rec) && (rec["mesh"] == null || drawn.Contains((string)rec["mesh"]))) return id;
        var hit = src.FirstOrDefault(kv => kv.Value["mesh"] != null && drawn.Contains((string)kv.Value["mesh"]));
        if (hit.Key == null) { note = $"customization {id} ({(string)rec?["mesh"] ?? "no outline"}) not drawn, no outline for drawn mesh(es) {string.Join(", ", drawn)}"; return id; }
        note = $"customization {id} ({(string)rec?["mesh"] ?? "no outline"}) not drawn: using {hit.Key} ({(string)hit.Value["name"]}) for drawn mesh {(string)hit.Value["mesh"]}";
        return hit.Key;
    }

    public static Outline GetOutline(string id, string part, out string how, bool body = false)
    {
        how = "own";
        var src = body ? _bodyOutlines : _outlines;
        if (src == null || src.Count == 0) { how = "none"; return null; }
        if (!src.TryGetValue(id ?? "", out var rec))
        {
            // outfits we have no measurement for (e.g. other mods' outfits): the widest measured sleeve, so nothing clips
            rec = src.Values.OrderByDescending(r => MaxRadius(r, part)).First(); how = "fallback " + (string)rec["name"];
        }
        var o = (JObject)rec[part];
        var ol = new Outline
        {
            w = V3(o["w"]), ax = V3(o["ax"]).normalized, e1 = V3(o["e1"]).normalized, a0 = (float)o["a0"], da = (float)o["da"], na = (int)o["na"], nd = (int)o["nd"], start = (float)o["start"],
        };
        ol.e2 = Vector3.Cross(ol.ax, ol.e1);
        bool nw = NoOutfitWatch && o["tabnw"] != null;
        if (nw) how += ", without the outfit's watch";
        var raw = Convert.FromBase64String((string)o[nw ? "tabnw" : "tab"]);
        ol.tab = new float[raw.Length / 2];
        for (int i = 0; i < ol.tab.Length; i++) ol.tab[i] = BitConverter.ToUInt16(raw, i * 2) / 10000f;
        // rest pose -> every wrist pose: the outline used = rest + PoseBlend x (posed - rest); old data without a rest table: posed
        if (o[nw ? "tab0nw" : "tab0"] != null && PoseBlend < 0.999f)
        {
            var raw0 = Convert.FromBase64String((string)o[nw ? "tab0nw" : "tab0"]);
            if (raw0.Length == raw.Length)
                for (int i = 0; i < ol.tab.Length; i++) { float r0 = BitConverter.ToUInt16(raw0, i * 2) / 10000f; ol.tab[i] = r0 + Math.Min(PoseBlend * (ol.tab[i] - r0), PoseCap); }
            how += $", rest+{PoseBlend:0.00}x pose" + (float.IsPositiveInfinity(PoseCap) ? "" : $" (at most {PoseCap * 1000:0.#} mm)");
        }
        return ol;
    }

    static float MaxRadius(JObject rec, string part)
    {
        var raw = Convert.FromBase64String((string)rec[part]["tab"]); float m = 0;
        for (int i = 0; i < raw.Length / 2; i++) m = Math.Max(m, BitConverter.ToUInt16(raw, i * 2));
        return m;
    }

    static Vector3 V3(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);

    // ---- fit.py port ----
    static float RCells(Outline o, IEnumerable<float> phis, float aLo, float aHi)
    {
        var ks = new HashSet<int>();
        foreach (var p in phis)
        {
            float ph = Mathf.Repeat(p, 2 * Mathf.PI);
            int k = Mathf.RoundToInt(ph / (2 * Mathf.PI / o.nd));
            ks.Add(((k % o.nd) + o.nd) % o.nd); ks.Add(((k - 1) % o.nd + o.nd) % o.nd); ks.Add(((k + 1) % o.nd + o.nd) % o.nd);
        }
        int j0 = Mathf.Clamp(Mathf.FloorToInt((aLo - o.a0) / o.da), 0, o.na - 1), j1 = Mathf.Clamp(Mathf.FloorToInt((aHi - o.a0) / o.da), 0, o.na - 1);
        float m = 0;
        for (int j = j0; j <= j1; j++) foreach (var k in ks) m = Math.Max(m, o.T(j, k));
        return m;
    }

    /// <summary>Lowest anchor height at which every vertex of a rigid piece clears the sleeve cell under it: a vertex sits at
    /// radius sqrt((H + r)^2 + dt^2), angle phi0 - atan(dt / (H + r)), so H >= sqrt(S^2 - dt^2) - r (fixed point).</summary>
    static float RigidHeight(Outline o, float phi0, List<float> a, List<float> dt, List<float> r)
    {
        float H = RCells(o, new[] { phi0 }, a.Min(), a.Max());
        for (int it = 0; it < 4; it++)
        {
            float need = 0;
            for (int i = 0; i < a.Count; i++)
            {
                float S = RCells(o, new[] { phi0 - Mathf.Atan2(dt[i], Math.Max(H + r[i], 0.005f)) }, a[i] - 0.002f, a[i] + 0.002f);
                if (S > Math.Abs(dt[i])) need = Math.Max(need, Mathf.Sqrt(S * S - dt[i] * dt[i]) - r[i]);
            }
            bool done = Math.Abs(need - H) < 0.0002f; H = need;
            if (done) break;
        }
        return H;
    }

    static int Turns(Acc A, Outline o, float aC)
    {
        if (A.closed) return 1;
        int j = Mathf.Clamp(Mathf.FloorToInt((aC - o.a0) / o.da), 0, o.na - 1);
        double sum = 0; int n = 0;
        for (int a = Math.Max(0, j - 3); a <= Math.Min(o.na - 1, j + 3); a++) for (int k = 0; k < o.nd; k++) { sum += o.T(a, k); n++; }
        float Rbar = (float)(sum / n) + CLR + 0.003f;
        return Math.Max(1, Mathf.RoundToInt(A.L / (2 * Mathf.PI * Rbar)));
    }

    static void Extent(Acc A, int turns, out float lo, out float hi, out float pitch)
    {
        float sw = 0;
        foreach (var m in A.meshes)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < m.h.Length; i++) if (m.kind[i] == 0) { mn = Math.Min(mn, m.h[i]); mx = Math.Max(mx, m.h[i]); }
            if (mx >= mn) sw = Math.Max(sw, mx - mn);
        }
        pitch = sw + 0.0015f;
        lo = A.meshes.Min(m => m.h.Min()) - A.hmid; hi = A.meshes.Max(m => m.h.Max()) - A.hmid;
        if (!A.closed && turns > 1) { lo -= pitch * turns / 2; hi += pitch * turns / 2; }
    }

    public class Built { public string mesh, bundle; public int mat = -1; public Vector3[] pos, nrm; public Vector4[] tan; public Vector2[] uv; public int[][] tris; public int[] swing, pid, kind; }
    public struct DialPose { public bool ok; public string prefab; public Vector3 pos; public Quaternion rot; }

    /// <summary>Where a watch's moving hands go: the head's placement (same frame and radius as its vertices).</summary>
    public static DialPose LastDial;

    /// <summary>Per watch: its head's direction around the arm in radians, 0 = back of the hand (set by Place).</summary>
    public static readonly Dictionary<string, float> FaceAngle = new Dictionary<string, float>();

    public static List<Built> Place(Acc A, Outline o, float aC, int turns, float phi0)
    {
        Extent(A, turns, out _, out _, out float pitch);
        var res = new List<Built>();
        LastDial = default;
        // pass 1: where along the band and the arm every vertex goes
        var pre = new List<(AccMesh m, float[] phi, float[] a)>();
        foreach (var m in A.meshes)
        {
            int nv = m.s.Length; var phi = new float[nv]; var a = new float[nv];
            for (int i = 0; i < nv; i++)
            {
                bool st = StraightenOn && m.sFix != null && m.hFix != null;
                float u = (st ? m.sFix[i] : m.s[i]) / A.L;
                phi[i] = phi0 - 2 * Mathf.PI * u * (A.closed ? 1 : turns);
                float helix = (A.closed || turns == 1) ? 0 : pitch * (turns * u - turns / 2f);
                a[i] = aC - A.hmid + helix + (st ? m.hFix[i] : m.h[i]);
            }
            pre.Add((m, phi, a));
        }
        // a watch's face direction around the arm (0 = back of the hand): where its biggest rigid piece (the head) sits
        if (A.fitR > 0)
        {
            var big = pre.SelectMany(x => Enumerable.Range(0, x.m.pid.Length).Where(i => x.m.kind[i] > 0).Select(i => (p: x.m.pid[i], ph: x.phi[i])))
                         .GroupBy(v => v.p).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (big != null) FaceAngle[A.name] = big.First().ph;
        }
        // rigid / pendant pieces: ONE height per piece across meshes (a watch case and its glass are two meshes, one piece)
        var Hp = new Dictionary<int, float>();
        foreach (var p in pre.SelectMany(x => Enumerable.Range(0, x.m.pid.Length).Where(i => x.m.kind[i] > 0).Select(i => x.m.pid[i])).Distinct().OrderBy(q => q))
        {
            var la = new List<float>(); var ld = new List<float>(); var lr = new List<float>(); float? ph = null;
            foreach (var x in pre)
                for (int i = 0; i < x.m.pid.Length; i++)
                    if (x.m.pid[i] == p) { if (ph == null) ph = x.phi[i]; la.Add(x.a[i]); ld.Add(x.m.dt[i]); lr.Add(x.m.r[i]); }
            Hp[p] = RigidHeight(o, ph.Value, la, ld, lr);
        }
        if (A.dial != null && Hp.TryGetValue(A.dial.pid, out float hh))
            Plugin.Log.LogInfo($"[WristAccessories] {A.name}: head {hh * 1000:0.0} mm from the arm axis (sleeve right under it {RCells(o, new[] { phi0 }, aC - A.hmid - 0.002f, aC - A.hmid + 0.002f) * 1000:0.0} mm)");
        foreach (var (m, phi, a) in pre)
        {
            int nv = m.s.Length; var R = new float[nv];
            for (int i = 0; i < nv; i++)
                R[i] = m.kind[i] > 0 ? Hp[m.pid[i]] : RCells(o, new[] { phi[i] }, a[i] - 0.004f, a[i] + 0.004f);
            if (A.dial != null && !LastDial.ok)
            {
                int hi = Array.FindIndex(m.pid, p => p == A.dial.pid);
                if (hi >= 0)
                {
                    float c0 = Mathf.Cos(phi[hi]), s0 = Mathf.Sin(phi[hi]);
                    Vector3 rn0 = c0 * o.e1 + s0 * o.e2, tn0 = -s0 * o.e1 + c0 * o.e2;
                    float ad = aC - A.hmid + A.dial.h;
                    Vector3 Map(Vector3 v) => -tn0 * v.x + rn0 * v.y + o.ax * v.z;
                    LastDial = new DialPose { ok = true, prefab = A.dial.prefab, pos = o.w + o.ax * ad + rn0 * (R[hi] + CLR + A.dial.r) - tn0 * A.dial.dt,
                                              rot = FromAxes(Map(A.dial.axes[2]), Map(A.dial.axes[1])) };
                }
            }
            var b = new Built { mesh = m.mesh, bundle = m.bundle, mat = m.mat, pos = new Vector3[nv], nrm = new Vector3[nv], tan = new Vector4[nv], uv = m.uv, tris = m.tris, swing = m.swing, pid = m.pid, kind = m.kind };
            for (int i = 0; i < nv; i++)
            {
                float c = Mathf.Cos(phi[i]), sn = Mathf.Sin(phi[i]);
                Vector3 rn = c * o.e1 + sn * o.e2, tn = -sn * o.e1 + c * o.e2;
                var p = o.w + o.ax * a[i] + rn * (R[i] + CLR + m.r[i]);
                if (m.kind[i] > 0) p += -tn * m.dt[i];
                b.pos[i] = p;
                b.nrm[i] = (-tn * m.n[i].x + rn * m.n[i].y + o.ax * m.n[i].z).normalized;
                var t3 = -tn * m.t[i].x + rn * m.t[i].y + o.ax * m.t[i].z;
                b.tan[i] = new Vector4(t3.x, t3.y, t3.z, m.t[i].w == 0 ? 1 : m.t[i].w);
            }
            res.Add(b);
        }
        return res;
    }

    /// <summary>Quaternion.LookRotation in plain math (forward = local z, up = local y).</summary>
    public static Quaternion FromAxes(Vector3 forward, Vector3 up)
    {
        var f = forward.normalized; var r = Vector3.Cross(up, f).normalized; var u = Vector3.Cross(f, r);
        float m00 = r.x, m01 = u.x, m02 = f.x, m10 = r.y, m11 = u.y, m12 = f.y, m20 = r.z, m21 = u.z, m22 = f.z;
        float tr = m00 + m11 + m22; float x, y, z, w;
        if (tr > 0) { float S = (float)Math.Sqrt(tr + 1) * 2; w = 0.25f * S; x = (m21 - m12) / S; y = (m02 - m20) / S; z = (m10 - m01) / S; }
        else if (m00 > m11 && m00 > m22) { float S = (float)Math.Sqrt(1 + m00 - m11 - m22) * 2; w = (m21 - m12) / S; x = 0.25f * S; y = (m01 + m10) / S; z = (m02 + m20) / S; }
        else if (m11 > m22) { float S = (float)Math.Sqrt(1 + m11 - m00 - m22) * 2; w = (m02 - m20) / S; x = (m01 + m10) / S; y = 0.25f * S; z = (m12 + m21) / S; }
        else { float S = (float)Math.Sqrt(1 + m22 - m00 - m11) * 2; w = (m10 - m01) / S; x = (m02 + m20) / S; y = (m12 + m21) / S; z = 0.25f * S; }
        return new Quaternion(x, y, z, w);
    }

    /// <summary>Items from the wrist toward the elbow, each clear of the one before.</summary>
    public static List<(Acc acc, List<Built> built, float lo, float hi, int turns, DialPose dial)> Stack(IEnumerable<Acc> items, Outline o, float watchPhi = 0f)
    {
        float edge = o.start; var res = new List<(Acc, List<Built>, float, float, int, DialPose)>();
        Skipped.Clear(); LastLoose.Clear();
        bool firstItem = true;
        foreach (var A0 in items)
        {
            // bracelets (not watches) sit on the wrist bone, 12 mm up from where the sleeve stops following the hand: right at
            // the joint the arm's shape changes most when the hand bends (live arm 27 vs outline 45 mm there, 0.5.8 test)
            if (firstItem && A0.fitR <= 0) edge = Math.Min(edge, o.start - 0.012f);
            firstItem = false;
            if (SlideMm.TryGetValue(A0.name, out float slide)) edge += slide / 1000f;    // the item's own slide (the ones after it follow)
            var A = PickSize(A0, o, edge);
            int turns = Turns(A, o, edge - 0.01f);
            Extent(A, turns, out float lo, out float hi, out _);
            float aC = edge - hi;
            if (A.fitR > 0)
            {
                // a watch keeps its size: only on sleeves no wider than the one it came from (mean over directions, same measure)
                int j = Mathf.Clamp(Mathf.FloorToInt((aC + A.hmid * 0 - o.a0) / o.da), 0, o.na - 1); double sum = 0; int n = 0;
                for (int a = Math.Max(0, j - 2); a <= Math.Min(o.na - 1, j + 2); a++) for (int k = 0; k < o.nd; k++) { sum += o.T(a, k); n++; }
                float mean = (float)(sum / n);
                if (mean > A.fitR) { Skipped.Add($"{A.name} (sleeve {mean * 1000:0.0} mm, fits up to {A.fitR * 1000:0.0} mm)"); continue; }
            }
            // loose items: a round loop (its own size), not hugging the sleeve - BraceletPhysics lets it hang and slide
            float ringR = A0.loose ? A.L / (2 * Mathf.PI) - A.width / 2 - CLR : 0f;
            var built = Place(A, A0.loose ? Circle(o, ringR) : o, aC, turns, (A.fitR > 0 ? watchPhi : 0f) + Turn(A.name));   // watches: face on the back of the wrist or the inner wrist
            res.Add((A, built, aC + lo, aC + hi, turns, LastDial));
            LastLoose.Add(A0.loose ? ringR : 0f);
            edge = aC + lo - GAP;
        }
        return res;
    }

    /// <summary>Items made in several sizes (name~N, e.g. our bead bracelets with N beads): the smallest whose loop closes round
    /// the sleeve where it sits without stretching (loop = the outline's perimeter there, pushed out by clearance + half the
    /// item's width); none big enough -> the biggest.</summary>
    static Acc PickSize(Acc A, Outline o, float edge)
    {
        string bn = A.loose && A.name.EndsWith("_loose") ? A.name.Substring(0, A.name.Length - 6) : A.name;
        var sizes = Accs.Values.Where(a => !a.loose && (a.name == bn || a.name.StartsWith(bn + "~"))).OrderBy(a => a.L).ToList();
        if (sizes.Count < 2) return sizes.Count == 1 ? sizes[0] : A;
        int j0 = Mathf.Clamp(Mathf.FloorToInt((edge - A.width - o.a0) / o.da), 0, o.na - 1), j1 = Mathf.Clamp(Mathf.FloorToInt((edge - o.a0) / o.da), 0, o.na - 1);
        float need = 0; Vector2 prev = default, first = default;
        for (int k = 0; k <= o.nd; k++)
        {
            int kk = k % o.nd; float R = 0; for (int j = j0; j <= j1; j++) R = Math.Max(R, o.T(j, kk));
            R += CLR + A.width / 2; float ang = kk * 2 * Mathf.PI / o.nd; var p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R;
            if (k == 0) first = p; else need += (p - prev).magnitude;
            prev = p;
        }
        var pick = sizes.FirstOrDefault(a => a.L >= need) ?? sizes.Last();
        if (A.loose)
        {
            // a round loop has to go round the whole cross-section: radius >= the farthest sleeve point from its centre
            var sec = Section(o, edge - A.width, edge); var c = Vector2.zero; foreach (var q in sec) c += q; c /= sec.Length;
            float far = sec.Max(q => (q - c).magnitude) + CLR + 0.001f;
            // the smallest size that goes round the arm (a natural loose bangle), + LooseSteps sizes (lab: +3 on top of the snug
            // fit made a 67 mm hula hoop on a 45 mm arm)
            int i = 0;
            while (i < sizes.Count - 1 && sizes[i].L / (2 * Mathf.PI) - sizes[i].width / 2 - CLR < far) i++;
            pick = sizes[Math.Min(i + LooseSteps, sizes.Count - 1)];
            Plugin.Log.LogInfo($"[WristAccessories] {A.name}: loose size {pick.name} (+{LooseSteps} sizes; loop radius {(pick.L / (2 * Mathf.PI) - pick.width / 2 - CLR) * 1000:0} mm, arm needs >= {far * 1000:0} mm)");
            return pick;
        }
        Plugin.Log.LogInfo($"[WristAccessories] {A.name}: size {pick.name} (loop {pick.L * 1000:0} mm, sleeve needs {need * 1000:0} mm)");
        return pick;
    }

    /// <summary>Per item of the last Stack: the inner radius of a loose item's round loop, 0 = fitted to the sleeve.</summary>
    public static readonly List<float> LastLoose = new List<float>();

    /// <summary>An outline that is a circle of radius r round the arm axis (same frame and axial range as o).</summary>
    static Outline Circle(Outline o, float r)
    {
        var c = new Outline { w = o.w, ax = o.ax, e1 = o.e1, e2 = o.e2, a0 = o.a0, da = o.da, na = o.na, nd = o.nd, start = o.start, tab = new float[o.tab.Length] };
        for (int i = 0; i < c.tab.Length; i++) c.tab[i] = r;
        return c;
    }

    /// <summary>The sleeve's cross-section at axial band [lo, hi]: the outermost point per direction (bone space, relative to w).</summary>
    public static Vector2[] Section(Outline o, float lo, float hi)
    {
        int j0 = Mathf.Clamp(Mathf.FloorToInt((lo - o.a0) / o.da), 0, o.na - 1), j1 = Mathf.Clamp(Mathf.FloorToInt((hi - o.a0) / o.da), 0, o.na - 1);
        var p = new Vector2[o.nd];
        for (int k = 0; k < o.nd; k++)
        {
            float R = 0; for (int j = j0; j <= j1; j++) R = Math.Max(R, o.T(j, k));
            float ang = k * 2 * Mathf.PI / o.nd; p[k] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R;
        }
        return p;
    }

    /// <summary>Items the last Stack left out because the sleeve is too wide for them.</summary>
    public static readonly List<string> Skipped = new List<string>();

    /// <summary>A ring: centred on the finger's ring spot.</summary>
    public static List<Built> PlaceRing(Acc A, Outline o)
    {
        Extent(A, 1, out float lo, out float hi, out _);
        return Place(A, o, o.start - (lo + hi) / 2, 1, 0f);
    }
}

