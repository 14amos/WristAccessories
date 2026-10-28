#if UNITY_EDITOR
// Accessory gallery: every WristAccessories item fitted on the user's arm (Hands_BT_3, left forearm, outfit watch hidden) by the
// plugin's own WristFit code, with the items' real materials (EFT / mod bundles + their dependencies), rendered from 4 sides.
// Run: Unity.exe -batchmode -projectPath <CatTest> -executeMethod WristAccessories.Gallery.Run -logFile <log>
// Env: LAB_ACCS (comma list, default every picker item), LAB_OUT (folder), LAB_HID (hands id), LAB_FACE (back|inner)
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
    public static class Gallery
    {
        const string SPT = "SPT/";
        const string W = SPT + "EscapeFromTarkov_Data/StreamingAssets/Windows/";
        const string Data = "WORK/wristacc";
        static readonly Dictionary<string, (string file, string[] deps)> Keys = new Dictionary<string, (string, string[])>();
        internal static readonly Dictionary<string, AssetBundle> Loaded = new Dictionary<string, AssetBundle>();

        public static void Run()
        {
            int code = 0;
            try { Go(); } catch (Exception e) { Debug.LogError("[Gallery] " + e); code = 1; }
            foreach (var b in Loaded.Values) if (b) b.Unload(true);
            EditorApplication.Exit(code);
        }

        static string Env(string k, string d) { var v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        // bundle keys -> file + dependencies: the game's Windows.json, then every mod's bundles.json (vanilla wins)
        internal static void Index()
        {
            foreach (var p in JObject.Parse(File.ReadAllText(W + "Windows.json")).Properties())
                Keys[p.Name] = (W + p.Name, p.Value["Dependencies"]?.ToObject<string[]>() ?? new string[0]);
            foreach (var mf in Directory.GetFiles(SPT + "SPT_Runtime/user/mods", "bundles.json", SearchOption.AllDirectories))
            {
                try
                {
                    var dir = Path.GetDirectoryName(mf).Replace('\\', '/') + "/bundles/";
                    foreach (var e in JObject.Parse(File.ReadAllText(mf))["manifest"])
                    {
                        string k = (string)e["key"];
                        if (!Keys.ContainsKey(k) && File.Exists(dir + k)) Keys[k] = (dir + k, e["dependencyKeys"]?.ToObject<string[]>() ?? new string[0]);
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[Gallery] {mf}: {e.Message}"); }
            }
        }

        internal static AssetBundle Load(string key)
        {
            if (Loaded.TryGetValue(key, out var b)) return b;
            Loaded[key] = null;
            if (!Keys.TryGetValue(key, out var info)) { Debug.LogWarning("[Gallery] unknown bundle " + key); return null; }
            foreach (var d in info.deps) Load(d);
            b = AssetBundle.LoadFromFile(info.file); Loaded[key] = b;
            if (b == null) Debug.LogWarning("[Gallery] could not load " + key);
            return b;
        }

        static readonly string[] WatchWords = { "watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin" };
        static readonly Dictionary<string, Material[]> MatCache = new Dictionary<string, Material[]>();

        // the plugin's Materials(): the item prefab's renderer for this mesh (MeshFilter, else skinned, else the first renderer)
        internal static Material[] Materials(string bundle, string mesh)
        {
            string k = bundle + "|" + mesh; if (MatCache.TryGetValue(k, out var c)) return c;
            var b = Load(bundle); Material[] mats = null;
            if (b != null)
            {
                var gos = b.LoadAllAssets<GameObject>();
                var mf = gos.SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true)).FirstOrDefault(f => f.sharedMesh != null && f.sharedMesh.name == mesh);
                mats = mf != null ? mf.GetComponent<MeshRenderer>()?.sharedMaterials : null;
                if (mats == null) mats = gos.SelectMany(g => g.GetComponentsInChildren<SkinnedMeshRenderer>(true)).Where(r => r.sharedMesh != null && r.sharedMesh.name == mesh)
                        .OrderByDescending(r => r.sharedMaterials.Count(m => m != null && WatchWords.Any(x => m.name.ToLowerInvariant().Contains(x)))).ThenBy(r => r.sharedMaterials.Count(m => m == null || m.name.StartsWith("Default-Material") || !m.HasProperty("_MainTex") || m.GetTexture("_MainTex") == null)).FirstOrDefault()?.sharedMaterials;
                if (mats == null) mats = gos.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).FirstOrDefault(r => r.sharedMaterials.Length > 0 && r.name.IndexOf("SHADOW", StringComparison.OrdinalIgnoreCase) < 0)?.sharedMaterials;
            }
            if (mats == null) Debug.LogWarning($"[Gallery] material NOT FOUND for {mesh} ({bundle})");
            MatCache[k] = mats; return mats;
        }

        static void Go()
        {
            string outDir = Env("LAB_OUT", Data + "/lab/gallery"), hid = Env("LAB_HID", "6436a4c2b6b75d4c89077fbd");
            Directory.CreateDirectory(outDir); foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Index(); Load("shaders"); Load("cubemaps");
            var handsB = Load("assets/content/hands/hands_arena_bt_3/hands_arena_bt_3.skin.bundle");
            var handMats = handsB.LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();

            // arm (as in BeadLab): rigid on the forearm bone, the outfit's watch hidden like the plugin does
            var bone = new GameObject("Base HumanLForearm3").transform;
            var arm = JObject.Parse(File.ReadAllText(Data + "/lab/arm_bt3_L.json"));
            var am = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            am.vertices = arm["pos"].Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToArray();
            am.normals = arm["nrm"].Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToArray();
            am.uv = arm["uv"].Select(v => new Vector2((float)v[0], (float)v[1])).ToArray();
            var subs = arm["subs"].Select(s => s.ToObject<int[]>()).ToArray(); am.subMeshCount = subs.Length;
            for (int i = 0; i < subs.Length; i++) am.SetTriangles(subs[i], i);
            am.RecalculateBounds(); am.RecalculateTangents();
            var armGo = new GameObject("Hands_BT_3"); armGo.transform.SetParent(bone, false);
            // LAB_BULGE=<mm>: the arm swollen radially (a posed first-person sleeve folding out) as a rigid skinned mesh, so
            // LiveFit can bake it; LAB_LIVEFIT=0 renders without LiveFit (the clipping it fixes)
            float bulge = float.Parse(Env("LAB_BULGE", "0"), System.Globalization.CultureInfo.InvariantCulture) / 1000f; bool liveFit = Env("LAB_LIVEFIT", "1") != "0", folds = Env("LAB_FOLDS", "0") == "1";   // LAB_FOLDS=1: ridges every 15 mm, deeper on one side
            EFT.Visual.LoddedSkin skin = null;
            if (bulge <= 0) armGo.AddComponent<MeshFilter>().sharedMesh = am;
            var invisible = new Material(Shader.Find("Sprites/Default")) { name = "WristAcc_invisible", color = new Color(0, 0, 0, 0) };
            bool ownWatch = Env("LAB_OWNWATCH", "0") == "1";     // LAB_OWNWATCH=1: the outfit's own watch shown (the game's correct way to wear it)
            armGo.AddComponent<MeshRenderer>().sharedMaterials = arm["mats"].Select(x => (string)x).Select(n => !ownWatch && WatchWords.Any(w => n.ToLowerInvariant().Contains(w)) ? invisible : (handMats.FirstOrDefault(m => m.name == n) ?? handMats[0])).ToArray();
            bone.rotation = Quaternion.identity;

            WristFit.Load(Data); WristFit.PoseBlend = float.Parse(Env("LAB_POSE", "0.25"), System.Globalization.CultureInfo.InvariantCulture); WristFit.LooseSteps = 0; WristFit.NoOutfitWatch = true; WristFit.StraightenOn = Env("LAB_STRAIGHT", "1") == "1";
            var o = WristFit.GetOutline(hid, "L", out string how);
            if (bulge > 0)
            {
                var armMats = armGo.GetComponent<MeshRenderer>().sharedMaterials; UnityEngine.Object.DestroyImmediate(armGo.GetComponent<MeshRenderer>());
                var vs = am.vertices;
                for (int i = 0; i < vs.Length; i++) { var rel = vs[i] - o.w; float al = Vector3.Dot(rel, o.ax); var perp = rel - o.ax * al; if (perp.sqrMagnitude > 1e-8f) { float f = 1f; if (folds) { float ph = Mathf.Atan2(Vector3.Dot(perp, o.e2), Vector3.Dot(perp, o.e1)); f = Mathf.Clamp01(0.5f + 0.5f * Mathf.Sin(al / 0.015f * 2 * Mathf.PI + ph * 2f)) * Mathf.Clamp01(0.4f + 0.6f * Mathf.Cos(ph)); } vs[i] += perp.normalized * bulge * f; } }
                am.vertices = vs; am.RecalculateBounds();
                am.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, am.vertexCount).ToArray(); am.bindposes = new[] { Matrix4x4.identity };
                var smr = armGo.AddComponent<SkinnedMeshRenderer>(); smr.sharedMesh = am; smr.bones = new[] { bone }; smr.rootBone = bone; smr.sharedMaterials = armMats;
                skin = armGo.AddComponent<EFT.Visual.LoddedSkin>(); skin.R = new Renderer[] { smr };
            }
            // pose the bone: forearm pointing +Z, back of the hand up (+Y)
            var local = Matrix4x4.identity; local.SetColumn(0, o.ax); local.SetColumn(1, o.e1); local.SetColumn(2, Vector3.Cross(o.ax, o.e1));
            var world = Matrix4x4.identity; world.SetColumn(0, Vector3.forward); world.SetColumn(1, Vector3.up); world.SetColumn(2, Vector3.Cross(Vector3.forward, Vector3.up));
            bone.rotation = (world * local.transpose).rotation; bone.position = new Vector3(0, 1.2f, 0);

            var cubes = Loaded["cubemaps"].LoadAllAssets<Cubemap>(); var cube = cubes.FirstOrDefault(c => c.name.ToLower().Contains("shopping")) ?? cubes.FirstOrDefault();
            if (cube) { RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom; RenderSettings.customReflection = cube; }
            var lightGo = GameObject.Find("Directional Light"); lightGo.transform.rotation = Quaternion.Euler(50, -30, 0); lightGo.GetComponent<Light>().intensity = 1.6f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.42f);
            var cam = Camera.main; cam.nearClipPlane = 0.01f; cam.fieldOfView = 35f; cam.backgroundColor = new Color(0.32f, 0.3f, 0.27f); cam.clearFlags = CameraClearFlags.SolidColor;
            var rt = new RenderTexture(480, 360, 24); cam.targetTexture = rt; var shot = new Texture2D(480, 360, TextureFormat.RGB24, false);
            // LAB_ICONS=1: the ACCESSORIES tab's dropdown pictures - the item alone (arm hidden) on a transparent background,
            // framed from a three-quarter view above, 256 x 256 -> <out>/icons/<name>.png
            bool icons = Env("LAB_ICONS", "0") == "1"; RenderTexture irt = null; Texture2D ishot = null;
            if (icons)
            {
                armGo.SetActive(false); Directory.CreateDirectory(outDir + "/icons");
                irt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 }; ishot = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            }

            if (ownWatch)
            {
                var wv = am.vertices; var subsW = arm["subs"].Select(x => x.ToObject<int[]>()).ToArray(); var mn = arm["mats"].Select(x => (string)x).ToArray();
                var wpts = Enumerable.Range(0, subsW.Length).Where(i => WatchWords.Any(w => mn[i].ToLowerInvariant().Contains(w))).SelectMany(i => subsW[i]).Distinct().Select(i => wv[i]).ToList();
                var c0 = wpts.Aggregate(Vector3.zero, (a, b) => a + b) / Math.Max(1, wpts.Count);
                var rel = c0 - o.w; float alw = Vector3.Dot(rel, o.ax); var pp = rel - o.ax * alw; float ang = Mathf.Atan2(Vector3.Dot(pp, o.e2), Vector3.Dot(pp, o.e1));
                var radw = bone.TransformDirection(Mathf.Cos(ang) * o.e1 + Mathf.Sin(ang) * o.e2).normalized; var atw = bone.TransformPoint(c0);
                var up = Vector3.ProjectOnPlane(Vector3.right, radw).normalized;
                cam.transform.position = atw + radw * float.Parse(Env("LAB_FACEDIST", "0.45"), System.Globalization.CultureInfo.InvariantCulture); cam.transform.LookAt(atw, up);
                cam.Render(); RenderTexture.active = rt; shot.ReadPixels(new Rect(0, 0, 480, 360), 0, 0); shot.Apply(); RenderTexture.active = null;
                File.WriteAllBytes($"{outDir}/ownwatch_face.png", shot.EncodeToPNG());
                File.WriteAllLines(outDir + "/report.txt", new[] { $"own watch: {wpts.Count} verts, face {ang * Mathf.Rad2Deg:0} deg from the back of the hand" });
                Debug.Log($"[Gallery] own watch face {ang * Mathf.Rad2Deg:0} deg"); return;
            }
            var names = Env("LAB_ACCS", "").Split(',').Where(s => s.Length > 0).ToList();
            if (names.Count == 0) names = WristFit.Accs.Keys.Where(n => !n.Contains("~") && n != "skullring").OrderBy(n => n).ToList();
            float watchPhi = (Env("LAB_FACE", "back") == "inner" ? -156f : 33f) * Mathf.Deg2Rad;
            var report = new List<string>();
            foreach (var name in names)
            {
                if (!WristFit.Accs.TryGetValue(name, out var A)) { report.Add($"{name}: NOT IN DATA"); continue; }
                var placed = WristFit.Stack(new[] { A }, o, watchPhi);
                if (name == "skullring")                                    // a ring: on the ring-finger outline, as the plugin places it
                {
                    var ro = WristFit.GetOutline(hid, "ringL", out _);
                    if (ro != null) placed = new List<(WristFit.Acc, List<WristFit.Built>, float, float, int, WristFit.DialPose)> { (A, WristFit.PlaceRing(A, ro), 0f, 0f, 1, default) };
                }
                if (placed.Count == 0) { report.Add($"{name}: SKIPPED ({string.Join("; ", WristFit.Skipped)})"); continue; }
                var (acc, built, lo, hi, _, dial) = placed[0];
                var go = new GameObject("acc_" + name); go.transform.SetParent(bone, false);
                int missing = 0; var src = new Dictionary<Mesh, WristFit.Built>();
                foreach (var b in built)
                {
                    var mats = Materials(b.bundle, b.mesh); if (mats == null) missing++;
                    var mesh = new Mesh { name = b.mesh, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; mesh.vertices = b.pos; mesh.normals = b.nrm; mesh.tangents = b.tan; mesh.uv = b.uv;
                    mesh.subMeshCount = b.tris.Length; for (int s = 0; s < b.tris.Length; s++) mesh.SetTriangles(b.tris[s], s); mesh.RecalculateBounds(); src[mesh] = b;
                    var c = new GameObject(b.mesh); c.transform.SetParent(go.transform, false); c.AddComponent<MeshFilter>().sharedMesh = mesh;
                    if (mats != null && acc.texture != null && b.bundle == acc.bundle) mats = mats.Select(m => BeadLab.Variant(m, Data + "/acc/" + acc.texture)).ToArray();
                    c.AddComponent<MeshRenderer>().sharedMaterials = mats == null ? new[] { invisible } : b.mat >= 0 ? new[] { mats[Math.Min(b.mat, mats.Length - 1)] } : Enumerable.Range(0, b.tris.Length).Select(i => mats[Math.Min(i, mats.Length - 1)]).ToArray();
                }
                if (dial.ok)
                {
                    var db = Load(dial.prefab); var pf = db != null ? db.LoadAllAssets<GameObject>().FirstOrDefault() : null;
                    if (pf != null) { var mh = UnityEngine.Object.Instantiate(pf, go.transform, false); mh.transform.localPosition = dial.pos; mh.transform.localRotation = dial.rot; }
                }
                if (skin != null && liveFit && acc.fitR <= 0 && !(WristFit.LastLoose.Count > 0 && WristFit.LastLoose[0] > 0f))
                {
                    var lf = go.AddComponent<LiveFit>(); lf.ManualStep = true; lf.O = o; lf.Hands = skin; lf.Name = acc.name; lf.Lo = lo; lf.Hi = hi;
                    foreach (var kv in src) lf.Src[kv.Key] = kv.Value;
                    for (int st = 0; st < 40; st++) lf.Step(st * 0.05f, 0.05f);                              // 2 s at 20 fps: eased to the targets
                    if (Env("LAB_DUMPLIVE", "") != "")
                    {
                        Mesh bk = null; var lv = BraceletPhysics.BakeArm(skin, bone, o, -0.22f, 0.01f, ref bk, out int nn);
                        File.WriteAllText(Env("LAB_DUMPLIVE", ""), Newtonsoft.Json.JsonConvert.SerializeObject(new { n = nn, live = lv.tab, outl = o.tab, o.na, o.nd, o.a0, o.da }));
                    }
                }
                // LAB_FACEVIEW=1: a watch's dial seen straight on, the thumb side at the top (a watch worn right reads upright:
                // 12 o'clock toward the thumb, back or inner wrist alike) -> <out>/<name>_face.png
                float fa = 0; if (Env("LAB_FACEVIEW", "0") == "1" && !WristFit.FaceAngle.TryGetValue(acc.name, out fa)) fa = float.Parse(Env("LAB_FACEANGLE", "33"), System.Globalization.CultureInfo.InvariantCulture) * Mathf.Deg2Rad;
                if (Env("LAB_FACEVIEW", "0") == "1")
                {
                    var rad = bone.TransformDirection(Mathf.Cos(fa) * o.e1 + Mathf.Sin(fa) * o.e2).normalized;
                    var at = bone.TransformPoint(o.w + o.ax * ((lo + hi) / 2f)) + rad * 0.035f;
                    var thumbUp = Vector3.ProjectOnPlane(Vector3.right, rad); if (thumbUp.sqrMagnitude < 1e-4f) thumbUp = Vector3.ProjectOnPlane(Vector3.forward, rad);
                    cam.transform.position = at + rad * float.Parse(Env("LAB_FACEDIST", "0.16"), System.Globalization.CultureInfo.InvariantCulture); cam.transform.LookAt(at, thumbUp.normalized);
                    cam.Render(); RenderTexture.active = rt; shot.ReadPixels(new Rect(0, 0, 480, 360), 0, 0); shot.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes($"{outDir}/{name}_face.png", shot.EncodeToPNG());
                    report.Add($"{name}: face view at {fa * Mathf.Rad2Deg:0} deg"); UnityEngine.Object.DestroyImmediate(go); continue;
                }
                if (icons)
                {
                    var rs = go.GetComponentsInChildren<Renderer>(); var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
                    var idir = (Vector3.up * 0.95f + Vector3.right * 0.35f + Vector3.back * 0.55f).normalized; float rad = bb.extents.magnitude;
                    cam.targetTexture = irt; cam.backgroundColor = new Color(0, 0, 0, 0); cam.fieldOfView = 22f;
                    cam.transform.position = bb.center + idir * (rad / Mathf.Sin(11f * Mathf.Deg2Rad) * 1.02f); cam.transform.LookAt(bb.center, Vector3.forward);
                    // the game's shaders do not write alpha: render on black and on white, alpha = 1 - (white - black)
                    Color[] Shot(Color bg) { cam.backgroundColor = bg; cam.Render(); RenderTexture.active = irt; ishot.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); ishot.Apply(); RenderTexture.active = null; return ishot.GetPixels(); }
                    var onB = Shot(Color.black); var onW = Shot(Color.white); var px = new Color[onB.Length];
                    for (int q = 0; q < px.Length; q++)
                    {
                        float al = Mathf.Clamp01(1f - Mathf.Max(onW[q].r - onB[q].r, Mathf.Max(onW[q].g - onB[q].g, onW[q].b - onB[q].b)));
                        px[q] = al > 0.004f ? new Color(Mathf.Clamp01(onB[q].r / al), Mathf.Clamp01(onB[q].g / al), Mathf.Clamp01(onB[q].b / al), al) : new Color(0, 0, 0, 0);
                    }
                    ishot.SetPixels(px); ishot.Apply();
                    File.WriteAllBytes($"{outDir}/icons/{name}.png", ishot.EncodeToPNG());
                    cam.targetTexture = rt; cam.fieldOfView = 35f; cam.backgroundColor = new Color(0.32f, 0.3f, 0.27f);
                    report.Add($"{name}: icon, {built.Count} mesh(es), materials missing {missing}");
                    UnityEngine.Object.DestroyImmediate(go); continue;
                }
                // 4 views round the item's middle
                var mid = bone.TransformPoint(o.w + o.ax * ((lo + hi) / 2f)); float dist = Mathf.Max(0.2f, (hi - lo) * 1.5f + 0.14f);
                var views = new (string n, Vector3 dir)[] { ("top", Vector3.up + Vector3.right * 0.25f), ("thumb", Vector3.left + Vector3.up * 0.2f), ("palm", Vector3.down + Vector3.right * 0.3f), ("fp", Vector3.back * 1.2f + Vector3.up * 0.55f + Vector3.left * 0.25f) };
                for (int vi = 0; vi < views.Length; vi++)
                {
                    cam.transform.position = mid + views[vi].dir.normalized * dist; cam.transform.LookAt(mid, Vector3.up);
                    cam.Render(); RenderTexture.active = rt; shot.ReadPixels(new Rect(0, 0, 480, 360), 0, 0); shot.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes($"{outDir}/{name}_{vi}_{views[vi].n}.png", shot.EncodeToPNG());
                }
                report.Add($"{name}: {acc.name} at {lo * 1000:0}..{hi * 1000:0} mm, {built.Count} mesh(es), materials missing {missing}, moving hands {(dial.ok ? "yes" : "no")}");
                UnityEngine.Object.DestroyImmediate(go);
            }
            File.WriteAllLines(outDir + "/report.txt", report);
            Debug.Log("[Gallery] " + string.Join(" | ", report));
        }
    }
}
#endif
