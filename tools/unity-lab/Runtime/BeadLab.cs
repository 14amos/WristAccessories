#if UNITY_EDITOR
// Bead lab: the WristAccessories plugin's own fit + physics code on the user's arm (Hands_BT_3, left forearm bone space) with
// the game's real materials/shaders (loaded from EFT's bundles), driven through a scripted arm routine and rendered to PNGs.
// Run: Unity.exe -batchmode -projectPath <CatTest> -executeMethod WristAccessories.BeadLab.Run -logFile <log>
// Args (env): LAB_ACC (default beads_angelite_loose), LAB_OUT (frames folder), LAB_SECONDS (default 12)
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
    public static class BeadLab
    {
        const string W = "SPT/EscapeFromTarkov_Data/StreamingAssets/Windows/";
        const string Data = "WORK/wristacc";
        const string Hid = "6436a4c2b6b75d4c89077fbd";

        public static void Run()
        {
            int code = 0;
            try { Lab(); }
            catch (Exception e) { Debug.LogError("[BeadLab] " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        static string Env(string k, string d) { var v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        static void Lab()
        {
            string accName = Env("LAB_ACC", "beads_angelite_loose"), outDir = Env("LAB_OUT", Data + "/lab/out");
            float seconds = float.Parse(Env("LAB_SECONDS", "12"), System.Globalization.CultureInfo.InvariantCulture);
            Directory.CreateDirectory(outDir); foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // the game's files: shaders + cubemaps first (dependencies), then the items
            var loaded = new List<AssetBundle>();
            foreach (var k in new[] { "shaders", "cubemaps", "assets/commonassets/physics/physicsmaterials.bundle",
                                      "assets/content/items/spec/item_spec_lightkeeper_amulet/item_spec_lightkeeper_amulet.bundle",
                                      "assets/content/hands/hands_arena_bt_3/hands_arena_bt_3.skin.bundle",
                                      "assets/content/hands/hands_boss_blackknight_quest/hands_boss_blackknight_quest.skin.bundle" })
            {
                var b = AssetBundle.LoadFromFile(W + k); if (b == null) throw new Exception("bundle not loaded: " + k); loaded.Add(b);
            }
            // materials as the plugin finds them: from the item prefab's renderer (EFT keeps them inside the prefabs)
            var amuletPrefab = loaded[3].LoadAllAssets<GameObject>();
            var amulet = amuletPrefab.SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true)).First(mf => mf.sharedMesh != null && mf.sharedMesh.name == "item_spec_lightkeeper_amulet_LOD0").GetComponent<MeshRenderer>().sharedMaterial;
            var handMats = loaded[4].LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            Debug.Log($"[BeadLab] amulet shader {amulet.shader.name} (supported {amulet.shader.isSupported}), hands materials {string.Join(", ", handMats.Select(m => m.name + "/" + m.shader.name))}");

            // the arm, rigid on its forearm bone (bone space = the plugin's frame)
            var bone = new GameObject("Base HumanLForearm3").transform;
            var arm = JObject.Parse(File.ReadAllText(Data + "/lab/arm_bt3_L.json"));
            var am = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            am.vertices = arm["pos"].Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToArray();
            am.normals = arm["nrm"].Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToArray();
            am.uv = arm["uv"].Select(v => new Vector2((float)v[0], (float)v[1])).ToArray();
            var subs = arm["subs"].Select(s => s.ToObject<int[]>()).ToArray(); am.subMeshCount = subs.Length;
            for (int i = 0; i < subs.Length; i++) am.SetTriangles(subs[i], i);
            am.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, am.vertexCount).ToArray();
            am.bindposes = new[] { Matrix4x4.identity }; am.RecalculateBounds(); am.RecalculateTangents();
            var armGo = new GameObject("Hands_BT_3"); armGo.transform.SetParent(bone, false);
            var smr = armGo.AddComponent<SkinnedMeshRenderer>(); smr.sharedMesh = am; smr.bones = new[] { bone }; smr.rootBone = bone;
            var mnames = arm["mats"].Select(x => (string)x).ToArray();
            // like the plugin: the outfit's own watch hidden (another watch / none picked) - invisible material, not part of the arm
            var invisible = new Material(Shader.Find("Sprites/Default")) { name = "WristAcc_invisible", color = new Color(0, 0, 0, 0) };
            string[] ww = { "watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin" };
            smr.sharedMaterials = mnames.Select(n => ww.Any(w => n.ToLowerInvariant().Contains(w)) ? invisible : (handMats.FirstOrDefault(m => m.name == n) ?? handMats[0])).ToArray();
            smr.updateWhenOffscreen = true;
            var skin = armGo.AddComponent<EFT.Visual.LoddedSkin>(); skin.R = new Renderer[] { smr };

            // the plugin's fit
            WristFit.Load(Data); WristFit.PoseBlend = 0.25f; WristFit.LooseSteps = 0; WristFit.NoOutfitWatch = true;
            var o = WristFit.GetOutline(Hid, "L", out string how);
            var placed = WristFit.Stack(new[] { WristFit.Accs[accName] }, o, 0f);
            var (acc, built, lo, hi, _, _) = placed[0];
            var go = new GameObject("WristAcc_L_" + acc.name); go.transform.SetParent(bone, false);
            // kill sleeves: the Predator hands' own material + the sleeve texture; their dogtags (mod bundles) get the amulet's
            var predMats = loaded[5].LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            bool sleeve = acc.bundle != null && acc.bundle.Contains("blackknight");
            var mat = sleeve ? (acc.texture != null ? Variant(predMats[0], Data + "/acc/" + acc.texture) : predMats[0]) : Variant(amulet, Data + "/acc/" + acc.texture);
            var swings = new List<TagSwing>();
            foreach (var b in built)
            {
                var mesh = new Mesh { name = acc.name }; mesh.vertices = b.pos; mesh.normals = b.nrm; mesh.tangents = b.tan; mesh.uv = b.uv;
                mesh.subMeshCount = b.tris.Length; for (int s = 0; s < b.tris.Length; s++) mesh.SetTriangles(b.tris[s], s); mesh.RecalculateBounds();
                var c = new GameObject(b.mesh); c.transform.SetParent(go.transform, false); c.AddComponent<MeshFilter>().sharedMesh = mesh;
                c.AddComponent<MeshRenderer>().sharedMaterial = sleeve && b.bundle != acc.bundle ? amulet : mat;
                if (b.swing != null && b.swing.Length == 2) { var ts = TagSwing.Attach(c, mesh, b, o); ts.ManualStep = true; swings.Add(ts); }
            }
            BraceletPhysics ph = null;
            if (WristFit.LastLoose.Count > 0 && WristFit.LastLoose[0] > 0f)
            {
                ph = go.AddComponent<BraceletPhysics>(); ph.ManualStep = true;
                ph.O = o; ph.RingR = WristFit.LastLoose[0]; ph.Lo = lo; ph.Hi = hi; ph.Name = acc.name; ph.Hands = skin;
                ph.ZMax = Math.Max(0f, o.start + 0.010f - hi); ph.ZMin = Math.Min(0f, (lo - 0.035f) - lo);
            }
            Debug.Log($"[BeadLab] placed {acc.name} at {lo * 1000:0}..{hi * 1000:0} mm ({how}), physics {(ph != null)}");

            // light + camera
            // a game environment cubemap for reflections (in game the scene is reflected; an empty lab hid the problem)
            var cubes = loaded[1].LoadAllAssets<Cubemap>(); Debug.Log($"[BeadLab] cubemaps: {string.Join(", ", cubes.Take(12).Select(c => c.name))}");
            var cube = cubes.FirstOrDefault(c => c.name.ToLower().Contains(Env("LAB_CUBE", "hideout"))) ?? cubes.FirstOrDefault();
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom; RenderSettings.customReflection = cube;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r.sharedMaterial.HasProperty("_Cube")) r.sharedMaterial.SetTexture("_Cube", cube);
                Debug.Log($"[BeadLab] reflections: {cube.name}");
            }
            var lightGo = GameObject.Find("Directional Light"); lightGo.transform.rotation = Quaternion.Euler(50, -30, 0); lightGo.GetComponent<Light>().intensity = 1.8f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.38f);
            var cam = Camera.main; cam.nearClipPlane = 0.01f; cam.fieldOfView = 40f; cam.backgroundColor = new Color(0.32f, 0.3f, 0.27f); cam.clearFlags = CameraClearFlags.SolidColor;
            var rt = new RenderTexture(640, 480, 24); cam.targetTexture = rt; var shot = new Texture2D(640, 480, TextureFormat.RGB24, false);

            // the routine: forearm pointing forward (back of hand up), raise, lower, swing, twist, hold; pivot at the elbow
            var csv = new List<string> { "t,pitch,yaw,twist,z_mm,dx_mm,dy_mm,hang_mm,tilt,spin,pull_along,pull_e1,pull_e2,fitfails" };
            Vector3 elbow = new Vector3(0, 1.2f, 0); float fps = 60f; int frames = Mathf.RoundToInt(seconds * fps);
            for (int f = 0; f <= frames; f++)
            {
                float t = f / fps; Pose(t, out float pitch, out float yaw, out float twist);
                var dir = Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
                var up = Quaternion.AngleAxis(twist, dir) * Vector3.ProjectOnPlane(Vector3.up, dir).normalized;
                // bone rotation: local ax -> dir, local e1 (back of the hand) -> up
                var local = Matrix4x4.identity; local.SetColumn(0, o.ax); local.SetColumn(1, o.e1); local.SetColumn(2, Vector3.Cross(o.ax, o.e1));
                var world = Matrix4x4.identity; world.SetColumn(0, dir); world.SetColumn(1, up); world.SetColumn(2, Vector3.Cross(dir, up));
                bone.rotation = (world * local.transpose).rotation;
                bone.position = elbow + dir * 0.13f - bone.rotation * o.w * 0f;
                foreach (var ts in swings) ts.Step(1f / fps);
                if (swings.Count > 0) csv.Add($"{t:0.000},{pitch:0},{yaw:0},{twist:0},tags lift avg {swings.Average(q => q.LiftDeg):0.0} max {swings.Max(q => q.LiftDeg):0.0} sway max {swings.Max(q => Mathf.Abs(q.SwayDeg)):0.0}");
                if (ph != null)
                {
                    ph.Step(1f / fps, t);
                    var sec = WristFit.Section(o, lo + ph.Z, hi + ph.Z); var cen = Vector2.zero; foreach (var q in sec) cen += q; cen /= sec.Length;
                    csv.Add($"{t:0.000},{pitch:0},{yaw:0},{twist:0},{ph.Z * 1000:0.0},{ph.D.x * 1000:0.0},{ph.D.y * 1000:0.0},{(ph.D - cen).magnitude * 1000:0.0},{ph.TiltDeg:0.0},{ph.SpinDeg:0.0},{ph.LastPull.x:0.00},{ph.LastPull.y:0.00},{ph.LastPull.z:0.00},{ph.FitFails}");
                }
                if (f % 6 == 0)
                {
                    var wrist = bone.TransformPoint(o.w + o.ax * ((lo + hi) / 2f));
                    var side = Vector3.Cross(Vector3.up, dir).normalized;
                    if (sleeve)
                    {
                        var mid = bone.TransformPoint(o.w + o.ax * ((lo + hi) / 2f));
                        cam.transform.position = mid + side * 0.42f - Vector3.up * 0.12f - dir * 0.05f; cam.transform.LookAt(mid);
                    }
                    else { cam.transform.position = wrist - dir * 0.32f + Vector3.up * 0.16f + side * 0.10f; cam.transform.LookAt(wrist); }
                    cam.Render(); RenderTexture.active = rt; shot.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); shot.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes($"{outDir}/f_{f / 6:000}.png", shot.EncodeToPNG());
                }
            }
            File.WriteAllLines(outDir + "/physics.csv", csv);
            Debug.Log($"[BeadLab] done: {frames / 6 + 1} frames in {outDir}");
            foreach (var b in loaded) b.Unload(true);
        }

        // pitch (deg, + = forearm raised), yaw (deg), twist (deg) over time
        static void Pose(float t, out float pitch, out float yaw, out float twist)
        {
            pitch = 0; yaw = 0; twist = 0;
            if (t < 2) { }
            else if (t < 4) pitch = 50f * Mathf.SmoothStep(0, 1, (t - 2) / 1.2f);
            else if (t < 6) pitch = Mathf.Lerp(50f, -50f, Mathf.SmoothStep(0, 1, (t - 4) / 1.2f));
            else if (t < 8) { pitch = Mathf.Lerp(-50f, 0f, Mathf.SmoothStep(0, 1, (t - 6) / 0.6f)); yaw = 30f * Mathf.Sin((t - 6) * Mathf.PI * 2f * 1.5f) * Mathf.Clamp01((t - 6.6f) * 3f); }
            else if (t < 10) twist = 80f * Mathf.Sin((t - 8) * Mathf.PI * 1.5f);
        }

        // the plugin's Variant(): our texture + gloss/spec map + flat normal + weak reflections
        internal static Material Variant(Material m, string tex)
        {
            var v = new Material(m) { name = m.name + "_" + Path.GetFileName(tex) };
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true); t.LoadImage(File.ReadAllBytes(tex + "_d.png")); v.SetTexture("_MainTex", t);
            if (Env("LAB_OLDMAT", "") == "" && File.Exists(tex + "_m.png") && Shader.Find("Standard") != null)
            {
                var mg = new Texture2D(2, 2, TextureFormat.RGBA32, true, true); mg.LoadImage(File.ReadAllBytes(tex + "_m.png"));
                var sv = new Material(Shader.Find("Standard")); sv.SetTexture("_MainTex", t); sv.SetColor("_Color", Color.white);
                sv.SetTexture("_MetallicGlossMap", mg); sv.EnableKeyword("_METALLICGLOSSMAP"); sv.SetFloat("_GlossMapScale", 1f); sv.SetFloat("_Metallic", 0f);
                return sv;
            }
            if (File.Exists(tex + "_s.png")) { var s = new Texture2D(2, 2, TextureFormat.RGBA32, true); s.LoadImage(File.ReadAllBytes(tex + "_s.png")); v.SetTexture("_SpecMap", s); v.SetColor("_ReflectColor", new Color(0.06f, 0.06f, 0.06f, 0.5f)); }
            if (File.Exists(tex + "_n.png")) { var n = new Texture2D(4, 4, TextureFormat.RGBA32, false, true); n.SetPixels32(Enumerable.Repeat(new Color32(128, 128, 255, 255), 16).ToArray()); n.Apply(); v.SetTexture("_BumpMap", n); }
            return v;
        }
    }
}
#endif
