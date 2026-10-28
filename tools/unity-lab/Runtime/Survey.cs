#if UNITY_EDITOR
// Survey: every first-person hands outfit (vanilla + mods, list = tattoo/acc/all_scan_hands.json) drawn at rest with its own
// materials, both forearms from above and below -> PNGs, to find accessories baked into outfits (bracelets, bands, patches).
// Env: LAB_OUT (folder), LAB_IDS (comma list of hands ids, default all), LAB_LIMIT (max outfits)
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
    public static class Survey
    {
        const string SPT = "SPT/";

        public static void Run()
        {
            int code = 0;
            try { Go(); } catch (Exception e) { Debug.LogError("[Survey] " + e); code = 1; }
            foreach (var b in Gallery.Loaded.Values) if (b) b.Unload(true);
            EditorApplication.Exit(code);
        }

        static readonly string[] Words = { "watch", "wach", "glass", "tracer", "luminox", "suunto", "g-shock", "g_shock", "montana", "rolex", "tactix", "garmin", "casio" };
        static string Env(string k, string d) { var v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        static string KeyOf(string path)
        {
            path = path.Replace('\\', '/');
            int i = path.IndexOf("/StreamingAssets/Windows/", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return path.Substring(i + "/StreamingAssets/Windows/".Length);
            i = path.IndexOf("/bundles/", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? path.Substring(i + "/bundles/".Length) : path;
        }

        static void Go()
        {
            string outDir = Env("LAB_OUT", "WORK/wristacc/lab/survey"); Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Gallery.Index(); Gallery.Load("shaders"); Gallery.Load("cubemaps");
            var cubes = Gallery.Loaded["cubemaps"].LoadAllAssets<Cubemap>(); var cube = cubes.FirstOrDefault(c => c.name.ToLower().Contains("shopping")) ?? cubes.FirstOrDefault();
            if (cube) { RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom; RenderSettings.customReflection = cube; }
            var lightGo = GameObject.Find("Directional Light"); lightGo.transform.rotation = Quaternion.Euler(50, -30, 0); lightGo.GetComponent<Light>().intensity = 1.6f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.52f);
            var cam = Camera.main; cam.orthographic = true; cam.nearClipPlane = 0.001f; cam.farClipPlane = 10f; cam.backgroundColor = new Color(0.32f, 0.3f, 0.27f); cam.clearFlags = CameraClearFlags.SolidColor;
            const int T = 256;
            var rt = new RenderTexture(T, T, 24); cam.targetTexture = rt; var comp = new Texture2D(T * 4, T * 2, TextureFormat.RGB24, false);

            bool hideWatch = Env("LAB_HIDEWATCH", "0") == "1";     // the outfits' watches hidden: what is left on a wrist is something else
            var invisible = new Material(Shader.Find("Sprites/Default")) { name = "invisible", color = new Color(0, 0, 0, 0) };
            var scan = JObject.Parse(File.ReadAllText("WORK/tattoo/acc/all_scan_hands.json"));
            var ids = Env("LAB_IDS", "").Split(',').Where(s => s.Length > 0).ToHashSet();
            int limit = int.Parse(Env("LAB_LIMIT", "100000")), done = 0; var report = new List<string>();
            foreach (var p in scan.Properties())
            {
                if (ids.Count > 0 && !ids.Contains(p.Name)) continue;
                if (done >= limit) break;
                string key = KeyOf((string)p.Value["path"]), name = (string)p.Value["name"];
                var root = new GameObject("survey");
                try
                {
                    var b = Gallery.Load(key); if (b == null) { report.Add($"{p.Name}\t{name}\tNOT LOADED {key}"); continue; }
                    var smrs = b.LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        .Where(r => r.sharedMesh != null && r.name.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) < 0).ToList();
                    // the highest detail only: per distinct mesh name stem, skip LOD1+ copies
                    smrs = smrs.Where(r => !System.Text.RegularExpressions.Regex.IsMatch(r.sharedMesh.name, @"LOD[1-9]", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
                    var seen = new HashSet<Mesh>(); var bounds = new Bounds(); bool first = true;
                    if (smrs.Count == 0) Debug.Log($"[Survey] {name}: no skinned mesh; renderers: " + string.Join(", ", b.LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Select(r => r.GetType().Name + ":" + r.name)));
                    foreach (var r in smrs)
                    {
                        if (!seen.Add(r.sharedMesh)) continue;
                        var g = new GameObject(r.sharedMesh.name); g.transform.SetParent(root.transform, false);
                        g.AddComponent<MeshFilter>().sharedMesh = r.sharedMesh; g.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials.Select(m => hideWatch && m != null && Words.Any(x => m.name.ToLowerInvariant().Contains(x)) ? invisible : m).ToArray();
                        if (first) { bounds = r.sharedMesh.bounds; first = false; } else bounds.Encapsulate(r.sharedMesh.bounds);
                    }
                    if (first) { report.Add($"{p.Name}\t{name}\tNO MESH"); continue; }
                    // bind pose: usually arms hang down (-Y), one each side of x = centre -> per arm the lower 50%. Some outfits lie
                    // along another axis (longest extent): then the hands are at both ends of it -> per end the outer 40%.
                    // Seen from 4 sides round the arm -> one 1024x512 composite (rows = arms)
                    var c0 = bounds.center; float big = Mathf.Max(1f, bounds.size.magnitude); var sz = bounds.size;
                    cam.farClipPlane = 10f * big;
                    int L = sz.y >= sz.x && sz.y >= sz.z ? 1 : (sz.x >= sz.z ? 0 : 2);
                    for (int arm = 0; arm < 2; arm++)
                    {
                        Vector3 c, axis; float h, w;
                        if (L == 1)
                        {
                            float x0 = arm == 0 ? bounds.min.x : c0.x, x1 = arm == 0 ? c0.x : bounds.max.x;
                            float y0 = bounds.min.y, y1 = bounds.min.y + sz.y * 0.5f;
                            c = new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, c0.z); axis = Vector3.up;
                            h = (y1 - y0) / 2f * 1.04f; w = Mathf.Max(x1 - x0, sz.z) / 2f * 1.04f;
                        }
                        else
                        {
                            axis = L == 0 ? Vector3.right : Vector3.forward; float len = sz[L];
                            float s0 = arm == 0 ? bounds.min[L] : bounds.max[L] - 0.4f * len, s1 = s0 + 0.4f * len;
                            c = c0; c[L] = (s0 + s1) / 2f; if (arm == 1) axis = -axis; else axis = axis;   // up in the picture = toward the elbow
                            if (arm == 0) axis = -axis;
                            h = 0.2f * len * 1.04f; w = Mathf.Max(sz[(L + 1) % 3], sz[(L + 2) % 3]) / 2f * 1.04f;
                        }
                        cam.orthographicSize = Mathf.Max(h, w);
                        var side = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.forward)) > 0.9f ? Vector3.up : Vector3.forward).normalized;
                        for (int vi = 0; vi < 4; vi++)
                        {
                            var dir = Quaternion.AngleAxis(90f * vi, axis) * side;
                            cam.transform.position = c + dir * 3f * big; cam.transform.LookAt(c, -axis * (L == 1 ? -1f : 1f));
                            cam.Render(); RenderTexture.active = rt; comp.ReadPixels(new Rect(0, 0, T, T), vi * T, (1 - arm) * T); RenderTexture.active = null;
                        }
                    }
                    comp.Apply(); File.WriteAllBytes($"{outDir}/{p.Name}.png", comp.EncodeToPNG());
                    report.Add($"{p.Name}\t{name}\t{seen.Count} mesh(es)\t{string.Join(",", smrs.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct())}\tbounds {bounds.size.x:0.00}x{bounds.size.y:0.00}x{bounds.size.z:0.00}");
                    done++;
                }
                catch (Exception e) { report.Add($"{p.Name}\t{name}\tFAIL {e.Message}"); }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    if (Gallery.Loaded.TryGetValue(key, out var lb) && lb) { lb.Unload(true); Gallery.Loaded.Remove(key); }
                }
            }
            File.WriteAllLines(outDir + "/report.tsv", report);
            Debug.Log($"[Survey] {done} outfits rendered");
        }
    }
}
#endif
