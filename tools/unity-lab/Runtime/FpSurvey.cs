#if UNITY_EDITOR
// First-person survey: the test accessories on EVERY first-person hands outfit (both forearms), placed by the plugin's own
// WristFit (the outfit's own outline, rest + 0.25 x pose like the default "Sleeve fit") and followed by its LiveFit, on that
// outfit's own arm (wristacc/lab/fp_export.py: 389 outfits, one run per distinct geometry) in the 7 key poses of the
// outline builder (rest, hand flex +-35, deviation +-20, forearm twist +-20 deg). Per item and pose: how much of it is
// inside the drawn arm surface (clip) and how far its inner side stands off the arm (float). FP-camera renders at rest +
// the worst pose of every flagged item -> lab/fpsurvey (report.tsv, flags.tsv, PNGs).
// Run: lab/fpsurvey.sh. Env: LAB_OUT, LAB_GEOS (comma list), LAB_LIMIT, LAB_ITEMS (comma list), LAB_RENDER (0 = none)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WristAccessories
{
    public static class FpSurvey
    {
        const string Data = "WORK/wristacc", Fp = Data + "/lab/fpdata";
        // clip: an item vertex more than 1 mm under the arm surface; float: the item's inner side's median gap over 4 mm
        const float ClipDepth = 0.001f, FloatGap = 0.004f;
        static readonly string[] DefaultItems = { "watch_cshock_green", "beads_angelite", "paracord", "prokill", "chain_gold", "outfit_6a720820_R0", "outfit_6847e7ec_L0", "bd_sleeve" };
        static readonly (string n, float flex, float dev, float tw)[] Poses = { ("rest", 0, 0, 0), ("flex+35", 35, 0, 0), ("flex-35", -35, 0, 0), ("dev+20", 0, 20, 0), ("dev-20", 0, -20, 0), ("tw+20", 0, 0, 20), ("tw-20", 0, 0, -20) };
        static readonly string[] WatchWords = { "watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin", "starline", "shock" };

        public static void Run()
        {
            int code = 0;
            try { Go(); } catch (Exception e) { Debug.LogError("[FpSurvey] " + e); code = 1; }
            foreach (var b in Gallery.Loaded.Values) if (b) b.Unload(true);
            EditorApplication.Exit(code);
        }

        static string Env(string k, string d) { var v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }
        static float[] F(JToken t) { var b = Convert.FromBase64String((string)t); var a = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }
        static int[] I(JToken t) { var b = Convert.FromBase64String((string)t); var a = new int[b.Length / 4]; Buffer.BlockCopy(b, 0, a, 0, b.Length); return a; }
        static Vector3[] V3s(float[] f) { var v = new Vector3[f.Length / 3]; for (int i = 0; i < v.Length; i++) v[i] = new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2]); return v; }
        static Vector3 V3(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);

        static string KeyOf(string path)
        {
            path = path.Replace('\\', '/');
            int i = path.IndexOf("/StreamingAssets/Windows/", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return path.Substring(i + "/StreamingAssets/Windows/".Length);
            i = path.IndexOf("/bundles/", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? path.Substring(i + "/bundles/".Length) : path;
        }

        static readonly Dictionary<string, Material> Variants = new Dictionary<string, Material>();
        static readonly Dictionary<string, Material[]> MatCache = new Dictionary<string, Material[]>();
        static Material[] ItemMats(string bundle, string mesh)
        {
            string k = bundle + "|" + mesh; if (MatCache.TryGetValue(k, out var c)) return c;
            var b = Gallery.Load(bundle); Material[] mats = null;
            if (b != null)
            {
                var gos = b.LoadAllAssets<GameObject>();
                var mf = gos.SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true)).FirstOrDefault(f => f.sharedMesh != null && f.sharedMesh.name == mesh);
                mats = mf != null ? mf.GetComponent<MeshRenderer>()?.sharedMaterials : null;
                if (mats == null) mats = gos.SelectMany(g => g.GetComponentsInChildren<SkinnedMeshRenderer>(true)).Where(r => r.sharedMesh != null && r.sharedMesh.name == mesh)
                        .OrderByDescending(r => r.sharedMaterials.Count(m => m != null && WatchWords.Any(x => m.name.ToLowerInvariant().Contains(x)))).FirstOrDefault()?.sharedMaterials;
                if (mats == null) mats = gos.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).FirstOrDefault(r => r.sharedMaterials.Length > 0 && r.name.IndexOf("SHADOW", StringComparison.OrdinalIgnoreCase) < 0)?.sharedMaterials;
            }
            MatCache[k] = mats; return mats;
        }

        // the posed arm's outer surface per outline cell (2 mm x 5 deg): triangles sampled on a 4 x 4 barycentric grid (as
        // build_outlines.py) - vertices alone leave gaps on the sparse forearm
        static float[] Surface(Vector3[] V, List<int[]> tris, WristFit.Outline o)
        {
            var tab = new float[o.na * o.nd];
            foreach (var t in tris)
                for (int q = 0; q + 2 < t.Length; q += 3)
                {
                    Vector3 A = V[t[q]], B = V[t[q + 1]], C = V[t[q + 2]];
                    for (int i = 0; i <= 4; i++)
                        for (int j = 0; j <= 4 - i; j++)
                        {
                            float u = i / 4f, v = j / 4f; var p = A * (1 - u - v) + B * u + C * v - o.w;
                            float al = Vector3.Dot(p, o.ax); int a = Mathf.FloorToInt((al - o.a0) / o.da); if (a < 0 || a >= o.na) continue;
                            var perp = p - o.ax * al; float r = perp.magnitude; if (r > 0.12f) continue;
                            int k = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(Vector3.Dot(perp, o.e2), Vector3.Dot(perp, o.e1)), 2 * Mathf.PI) / (2 * Mathf.PI / o.nd)) % o.nd;
                            if (r > tab[a * o.nd + k]) tab[a * o.nd + k] = r;
                        }
                }
            return tab;
        }

        static void Go()
        {
            string outDir = Env("LAB_OUT", Data + "/lab/fpsurvey"); Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            bool render = Env("LAB_RENDER", "1") != "0";
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);       // LiveFit logs every placement
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Gallery.Index(); Gallery.Load("shaders"); Gallery.Load("cubemaps");
            var cubes = Gallery.Loaded["cubemaps"].LoadAllAssets<Cubemap>(); var cube = cubes.FirstOrDefault(c => c.name.ToLower().Contains("shopping")) ?? cubes.FirstOrDefault();
            if (cube) { RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom; RenderSettings.customReflection = cube; }
            var lightGo = GameObject.Find("Directional Light"); lightGo.transform.rotation = Quaternion.Euler(50, -30, 0); lightGo.GetComponent<Light>().intensity = 1.6f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.42f);
            var cam = Camera.main; cam.nearClipPlane = 0.01f; cam.fieldOfView = 35f; cam.backgroundColor = new Color(0.32f, 0.3f, 0.27f); cam.clearFlags = CameraClearFlags.SolidColor;
            const int TW = 320, TH = 240;
            var rt = new RenderTexture(TW, TH, 24); cam.targetTexture = rt; var shot = new Texture2D(TW * 2, TH, TextureFormat.RGB24, false);
            var invisible = new Material(Shader.Find("Sprites/Default")) { name = "WristAcc_invisible", color = new Color(0, 0, 0, 0) };

            WristFit.Load(Data);
            // LAB_ADDONS=1: the addon folders' items too (WristFit.LoadAddons looks at the folders next to the one it is given)
            if (Env("LAB_ADDONS", "0") == "1") typeof(WristFit).GetMethod("LoadAddons", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.Invoke(null, new object[] { Data + "/addons/x" });
            WristFit.PoseBlend = 0.25f; WristFit.LooseSteps = 0; WristFit.PoseCap = float.Parse(Env("LAB_POSECAP", "1000")) / 1000f; WristFit.StraightenOn = Env("LAB_STRAIGHT", "1") == "1";   // mm
            var items = Env("LAB_ITEMS", "").Split(',').Where(s => s.Length > 0).ToArray(); if (items.Length == 0) items = DefaultItems;
            items = items.Where(n => { bool ok = WristFit.Accs.ContainsKey(n); if (!ok) Debug.LogWarning("[FpSurvey] no accessory " + n); return ok; }).ToArray();
            var index = JObject.Parse(File.ReadAllText(Fp + "/index.json"));
            var byGeo = index.Properties().GroupBy(p => (string)p.Value["geo"]).ToList();
            var only = Env("LAB_GEOS", "").Split(',').Where(s => s.Length > 0).ToHashSet();
            int limit = int.Parse(Env("LAB_LIMIT", "100000")), done = 0;
            var report = new List<string> { "geo\thid\tmesh\toutfits\tside\titem\tpose\tlo_mm\thi_mm\tverts\tclip_pct\tclip_max_mm\tgap_med_mm\tgap_max_mm\tlivefit_max_mm\tflag\tcover_pct\topen_deg\twander_mm" };
            var flags = new List<string> { report[0] };
            var t0 = DateTime.Now;

            foreach (var grp in byGeo)
            {
                if (only.Count > 0 && !only.Contains(grp.Key)) continue;
                if (done >= limit) break;
                var first = grp.First(); string hid = first.Name, geo = grp.Key; int nOut = grp.Count();
                var g = JObject.Parse(File.ReadAllText($"{Fp}/{geo}.json"));
                string mname = (string)g["mesh"]; var matNames = g["mats"].Select(x => (string)x).ToArray();
                // the outfit's own materials (hands bundle)
                string key = KeyOf((string)first.Value["path"]); var hb = Gallery.Load(key);
                var handMats = hb != null ? hb.LoadAllAssets<GameObject>().SelectMany(x => x.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray() : new Material[0];
                try
                {
                    foreach (var S in new[] { "L", "R" })
                    {
                        var sd = g[S]; var P0 = V3s(F(sd["pos"])); var N0 = V3s(F(sd["nrm"])); var uvf = F(sd["uv"]);
                        var wp = F(sd["wp"]); var w2 = F(sd["w2"]); var w1 = F(sd["w1"]); var f3o = V3(sd["f3o"]);
                        var subs = sd["tris"].Select(I).ToArray();
                        var uv = new Vector2[P0.Length]; for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(uvf[2 * i], uvf[2 * i + 1]);
                        bool[] watchSub = Enumerable.Range(0, subs.Length).Select(i => i < matNames.Length && WatchWords.Any(w => matNames[i].ToLowerInvariant().Contains(w))).ToArray();

                        // the arm: a rigid skinned mesh on the Forearm3 bone (LiveFit bakes it, as in Gallery's LAB_BULGE path)
                        var bone = new GameObject($"Base Human{S}Forearm3").transform;
                        var am = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; am.vertices = P0; am.normals = N0; am.uv = uv;
                        am.subMeshCount = subs.Length; for (int i = 0; i < subs.Length; i++) am.SetTriangles(subs[i], i);
                        am.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, P0.Length).ToArray(); am.bindposes = new[] { Matrix4x4.identity }; am.RecalculateBounds();
                        var armGo = new GameObject(mname); armGo.transform.SetParent(bone, false);
                        var smr = armGo.AddComponent<SkinnedMeshRenderer>(); smr.sharedMesh = am; smr.bones = new[] { bone }; smr.rootBone = bone; smr.updateWhenOffscreen = true;
                        var skin = armGo.AddComponent<EFT.Visual.LoddedSkin>(); skin.R = new Renderer[] { smr };
                        var armMats = matNames.Select(n => handMats.FirstOrDefault(m => m.name == n) ?? (handMats.Length > 0 ? handMats[0] : invisible)).ToArray();

                        foreach (var item in items)
                        {
                            var A = WristFit.Accs[item];
                            bool watch = A.fitR > 0 || item.StartsWith("watch_");
                            WristFit.NoOutfitWatch = watch;                           // the plugin hides the outfit's watch when a watch is picked
                            var o = WristFit.GetOutline(Env("LAB_OUTLINE_HID", hid), S, out string how);   // LAB_OUTLINE_HID: another outfit's outline (the 01:25 stale-id bug)
                            if (o == null) { report.Add($"{geo}\t{hid}\t{mname}\t{nOut}\t{S}\t{item}\t-\t\t\t\t\t\t\t\t\tNO OUTLINE"); continue; }
                            smr.sharedMaterials = armMats.Select((m, i) => watch && watchSub[i] ? invisible : m).ToArray();
                            var surfTris = Enumerable.Range(0, subs.Length).Where(i => !(watch && watchSub[i])).Select(i => subs[i]).ToList();
                            // camera frame: forearm along +Z, back of the hand up (as Gallery)
                            var local = Matrix4x4.identity; local.SetColumn(0, o.ax); local.SetColumn(1, o.e1); local.SetColumn(2, Vector3.Cross(o.ax, o.e1));
                            var world = Matrix4x4.identity; world.SetColumn(0, Vector3.forward); world.SetColumn(1, Vector3.up); world.SetColumn(2, Vector3.Cross(Vector3.forward, Vector3.up));
                            bone.rotation = (world * local.transpose).rotation; bone.position = new Vector3(0, 1.2f, 0);

                            var placed = WristFit.Stack(new[] { A }, o, (watch ? -156f : 33f) * Mathf.Deg2Rad);
                            if (placed.Count == 0) { report.Add($"{geo}\t{hid}\t{mname}\t{nOut}\t{S}\t{item}\t-\t\t\t\t\t\t\t\t\tSKIPPED ({string.Join("; ", WristFit.Skipped)})"); continue; }
                            var (acc, built, lo, hi, _, dial) = placed[0];
                            bool loose = WristFit.LastLoose.Count > 0 && WristFit.LastLoose[0] > 0f;
                            float worstScore = -1; string worstPose = null;
                            foreach (var pose in Poses)
                            {
                                // pose the arm like build_outlines.py: Palm + fingers about the wrist joint, twist on Forearm2 / Forearm1
                                var Rp = Quaternion.AngleAxis(pose.flex, o.e2) * Quaternion.AngleAxis(pose.dev, o.e1);
                                var R2 = Quaternion.AngleAxis(pose.tw, o.ax); var R1 = Quaternion.AngleAxis(2 * pose.tw, o.ax);
                                var Vp = new Vector3[P0.Length]; var Np = new Vector3[P0.Length];
                                for (int i = 0; i < P0.Length; i++)
                                {
                                    float wr = 1f - wp[i] - w2[i] - w1[i];
                                    Vp[i] = wr * P0[i] + wp[i] * (Rp * (P0[i] - o.w) + o.w) + w2[i] * (R2 * (P0[i] - f3o) + f3o) + w1[i] * (R1 * (P0[i] - f3o) + f3o);
                                    Np[i] = (wr * N0[i] + wp[i] * (Rp * N0[i]) + w2[i] * (R2 * N0[i]) + w1[i] * (R1 * N0[i])).normalized;
                                }
                                am.vertices = Vp; am.normals = Np; am.RecalculateBounds();
                                var surf = Surface(Vp, surfTris, o);

                                var go = new GameObject("acc_" + item); go.transform.SetParent(bone, false);
                                var src = new Dictionary<Mesh, WristFit.Built>();
                                foreach (var b in built)
                                {
                                    var mesh = new Mesh { name = b.mesh, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.vertices = b.pos; mesh.normals = b.nrm; mesh.tangents = b.tan; mesh.uv = b.uv;
                                    mesh.subMeshCount = b.tris.Length; for (int s = 0; s < b.tris.Length; s++) mesh.SetTriangles(b.tris[s], s); mesh.RecalculateBounds(); src[mesh] = b;
                                    var c = new GameObject(b.mesh); c.transform.SetParent(go.transform, false); c.AddComponent<MeshFilter>().sharedMesh = mesh;
                                    Material[] mats = render ? ItemMats(b.bundle, b.mesh) : null;
                                    if (mats != null && acc.texture != null && b.bundle == acc.bundle) mats = mats.Select(m => { string vk = m.name + "|" + acc.texture; if (!Variants.TryGetValue(vk, out var vm) || !vm) Variants[vk] = vm = BeadLab.Variant(m, (acc.accDir ?? Data + "/acc") + "/" + acc.texture); return vm; }).ToArray();
                                    c.AddComponent<MeshRenderer>().sharedMaterials = mats == null ? new[] { invisible } : b.mat >= 0 ? new[] { mats[Math.Min(b.mat, mats.Length - 1)] } : Enumerable.Range(0, b.tris.Length).Select(i => mats[Math.Min(i, mats.Length - 1)]).ToArray();
                                }
                                GameObject mh = null;
                                if (render && dial.ok) { var db = Gallery.Load(dial.prefab); var pf = db != null ? db.LoadAllAssets<GameObject>().FirstOrDefault() : null; if (pf != null) { mh = UnityEngine.Object.Instantiate(pf, go.transform, false); mh.transform.localPosition = dial.pos; mh.transform.localRotation = dial.rot; } }
                                float lfMax = 0;
                                if (!loose)                                             // the plugin (0.7.3): LiveFit on every L/R item except loose ones; watches as rigid heads
                                {
                                    var lf = go.AddComponent<LiveFit>(); lf.ManualStep = true; lf.O = o; lf.Hands = skin; lf.Name = acc.name; lf.Lo = lo; lf.Hi = hi;
                                    if (watch && A.dial != null && mh != null) { lf.Dial = mh.transform; lf.DialPid = A.dial.pid; }
                                    foreach (var kv in src) lf.Src[kv.Key] = kv.Value;
                                    for (int st = 0; st < 40; st++) lf.Step(st * 0.05f, 0.05f);
                                    foreach (var kv in src) { var a0 = kv.Value.pos; var a1 = kv.Key.vertices; for (int i = 0; i < a0.Length && i < a1.Length; i++) lfMax = Math.Max(lfMax, (a1[i] - a0[i]).magnitude); }
                                }

                                // measure: every item vertex (bone space) against the arm surface in its cell
                                int nv = 0, nclip = 0; float clipMax = 0; var minGap = new float[o.nd]; for (int k = 0; k < o.nd; k++) minGap[k] = float.MaxValue;
                                var occ = new int[o.nd]; var alSum = new double[o.nd];      // closure: which directions round the arm the item reaches, and where along the arm
                                // LAB_DIAG=1: why LiveFit left a clipped vertex under the arm. raw = arm VERTICES per cell (what BakeArm sees),
                                // live = BakeArm's table (empty cells filled), window = LiveFit's (+-4 mm, +-1 direction)
                                bool diag = Env("LAB_DIAG", "0") == "1"; float[] rawT = null; WristFit.Outline liveT = null; var why = new Dictionary<string, int>();
                                if (diag)
                                {
                                    rawT = new float[o.na * o.nd];
                                    foreach (var v in Vp) { var q = v - o.w; float al = Vector3.Dot(q, o.ax); int a = Mathf.FloorToInt((al - o.a0) / o.da); if (a < 0 || a >= o.na) continue; var pp = q - o.ax * al; float rr = pp.magnitude; if (rr > 0.12f) continue; int k = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(Vector3.Dot(pp, o.e2), Vector3.Dot(pp, o.e1)), 2 * Mathf.PI) / (2 * Mathf.PI / o.nd)) % o.nd; if (rr > rawT[a * o.nd + k]) rawT[a * o.nd + k] = rr; }
                                    Mesh bk = null; liveT = BraceletPhysics.BakeArm(skin, bone, o, lo - 0.006f, hi + 0.006f, ref bk, out _);
                                    if (pose.n == "rest") { var sw = System.Diagnostics.Stopwatch.StartNew(); for (int q = 0; q < 20; q++) BraceletPhysics.BakeArm(skin, bone, o, lo - 0.006f, hi + 0.006f, ref bk, out _); double tv = sw.Elapsed.TotalMilliseconds / 20; var cd = new BraceletPhysics.Surface(); BraceletPhysics.BakeArm(skin, bone, o, lo - 0.006f, hi + 0.006f, ref bk, out _, cd); sw.Restart(); for (int q = 0; q < 20; q++) BraceletPhysics.BakeArm(skin, bone, o, lo - 0.006f, hi + 0.006f, ref bk, out _, cd); double ts = sw.Elapsed.TotalMilliseconds / 20; File.AppendAllText(outDir + "/timing.tsv", $"{mname}	{S}	{item}	verts {P0.Length}	cand {cd.Cands.Values.Sum(l => l.Count)}	vertex {tv:0.00} ms	surface {ts:0.00} ms\n"); } if (bk) UnityEngine.Object.DestroyImmediate(bk);
                                }
                                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                                {
                                    if (mh != null && mf.transform.IsChildOf(mh.transform)) continue;
                                    var M = bone.worldToLocalMatrix * mf.transform.localToWorldMatrix; var vs = mf.sharedMesh.vertices;
                                    foreach (var v0 in vs)
                                    {
                                        var p = M.MultiplyPoint3x4(v0) - o.w; float al = Vector3.Dot(p, o.ax); int a = Mathf.FloorToInt((al - o.a0) / o.da); if (a < 0 || a >= o.na) continue;
                                        var perp = p - o.ax * al; float r = perp.magnitude;
                                        int k = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(Vector3.Dot(perp, o.e2), Vector3.Dot(perp, o.e1)), 2 * Mathf.PI) / (2 * Mathf.PI / o.nd)) % o.nd;
                                        occ[k]++; alSum[k] += al;
                                        float R = surf[a * o.nd + k]; if (R <= 0) continue;              // no arm drawn there (past the hand / cut)
                                        nv++; float d = R - r;
                                        if (d > ClipDepth) { nclip++; clipMax = Math.Max(clipMax, d); }
                                        if (diag && d > ClipDepth)
                                        {
                                            int j0 = Mathf.Clamp(Mathf.FloorToInt((al - 0.004f - o.a0) / o.da), 0, o.na - 1), j1 = Mathf.Clamp(Mathf.FloorToInt((al + 0.004f - o.a0) / o.da), 0, o.na - 1);
                                            float rw = 0, lw = 0, ow = 0;
                                            for (int j = j0; j <= j1; j++) for (int dk = -1; dk <= 1; dk++) { int c = j * o.nd + (k + dk + o.nd) % o.nd; rw = Math.Max(rw, rawT[c]); ow = Math.Max(ow, o.tab[c]); if (liveT != null) lw = Math.Max(lw, liveT.tab[c]); }
                                            string w = rw - ow > 0.012f ? "stray" + (al > hi - 0.03f ? "@hand" : al > hi - 0.08f ? "@mid" : "@elbow") : rw <= 0 ? "sparse" : R > rw + 0.001f ? "between" : "short";
                                            why[w] = (why.TryGetValue(w, out int nw) ? nw : 0) + 1;
                                        }
                                        if (-d < minGap[k]) minGap[k] = -d;
                                    }
                                }
                                var gaps = minGap.Where(x => x < float.MaxValue).OrderBy(x => x).ToArray();
                                float gmed = gaps.Length > 0 ? gaps[gaps.Length / 2] : 0, gmax = gaps.Length > 0 ? gaps[gaps.Length - 1] : 0;
                                float clipPct = nv > 0 ? 100f * nclip / nv : 0;
                                int nOcc = occ.Count(c => c > 0), run = 0, open = 0;
                                for (int q = 0; q < 2 * o.nd; q++) { if (occ[q % o.nd] == 0) { run++; open = Math.Max(open, Math.Min(run, o.nd)); } else run = 0; }
                                var alMeans = Enumerable.Range(0, o.nd).Where(q => occ[q] > 0).Select(q => alSum[q] / occ[q]).ToArray();
                                float wander = alMeans.Length > 0 ? (float)(alMeans.Max() - alMeans.Min()) : 0;
                                string flag = (clipPct >= 2f ? "CLIP " : "") + (!loose && gmed > FloatGap ? "FLOAT " : "") + (nv == 0 ? "NO ARM " : "");
                                string row = $"{geo}\t{hid}\t{mname}\t{nOut}\t{S}\t{item}\t{pose.n}\t{lo * 1000:0}\t{hi * 1000:0}\t{nv}\t{clipPct:0.0}\t{clipMax * 1000:0.0}\t{gmed * 1000:0.0}\t{gmax * 1000:0.0}\t{lfMax * 1000:0.0}\t{flag.Trim()}\t{100f * nOcc / o.nd:0}\t{open * 360f / o.nd:0}\t{wander * 1000:0.0}";
                                report.Add(row); if (flag.Length > 0) flags.Add(row);
                                if (diag && nclip > 0) File.AppendAllText(outDir + "/diag.tsv", row + "\t" + string.Join(",", why.Select(kv => kv.Key + "=" + kv.Value)) + "\n");
                                float score = clipPct + (loose ? 0 : Math.Max(0, gmed - FloatGap) * 1000f * 5f);
                                bool shoot = render && (pose.n == "rest" || (flag.Length > 0 && score > worstScore));
                                if (flag.Length > 0 && score > worstScore) { worstScore = score; worstPose = pose.n; }
                                if (shoot)
                                {
                                    // left: side view across the arm (back of the hand up, thumb side); right: first person, from above
                                    // and behind the wrist toward the elbow side, farther out so the sleeve does not fill the view
                                    var mid = bone.TransformPoint(o.w + o.ax * ((lo + hi) / 2f)); float dist = Mathf.Max(0.2f, (hi - lo) * 1.5f + 0.14f);
                                    var side = S == "L" ? Vector3.left : Vector3.right;
                                    var views = new[] { (side + Vector3.up * 0.35f).normalized * dist, (Vector3.back * 0.8f + Vector3.up * 1.0f + side * 0.3f).normalized * dist * 1.4f };
                                    for (int vi = 0; vi < 2; vi++)
                                    {
                                        cam.transform.position = mid + views[vi]; cam.transform.LookAt(mid, vi == 0 ? Vector3.up : Vector3.forward);
                                        cam.Render(); RenderTexture.active = rt; shot.ReadPixels(new Rect(0, 0, TW, TH), vi * TW, 0); RenderTexture.active = null;
                                    }
                                    shot.Apply();
                                    File.WriteAllBytes($"{outDir}/{geo}_{S}_{item}_{(pose.n == "rest" ? "rest" : "worst")}.png", shot.EncodeToPNG());
                                    if (pose.n != "rest") File.WriteAllText($"{outDir}/{geo}_{S}_{item}_worst.txt", row);
                                }
                                UnityEngine.Object.DestroyImmediate(go);
                            }
                        }
                        UnityEngine.Object.DestroyImmediate(bone.gameObject); UnityEngine.Object.DestroyImmediate(am);
                    }
                    done++;
                    Debug.Log($"[FpSurvey] {done}/{byGeo.Count} {geo} {mname} ({nOut} outfits) {(DateTime.Now - t0).TotalSeconds:0} s, flags so far {flags.Count - 1}");
                }
                catch (Exception e) { report.Add($"{geo}\t{hid}\t{mname}\t{nOut}\t-\t-\t-\t\t\t\t\t\t\t\t\tFAIL {e.Message}"); Debug.LogError($"[FpSurvey] {geo}: {e}"); }
                finally { if (!MatCache.Keys.Any(k => k.StartsWith(key + "|")) && Gallery.Loaded.TryGetValue(key, out var lb) && lb) { lb.Unload(true); Gallery.Loaded.Remove(key); } }   // an outfit accessory's own bundle stays (its materials are cached)
                File.WriteAllLines(outDir + "/report.tsv", report); File.WriteAllLines(outDir + "/flags.tsv", flags);
            }
            File.WriteAllLines(outDir + "/report.tsv", report); File.WriteAllLines(outDir + "/flags.tsv", flags);
            Debug.Log($"[FpSurvey] DONE {done} geometries, {report.Count - 1} rows, {flags.Count - 1} flagged, {(DateTime.Now - t0).TotalSeconds:0} s");
        }
    }
}
#endif
