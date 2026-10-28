#if UNITY_EDITOR
// SeamTest: the plugin's own HandWear.Build (Seam.cs: wrist seam tuck + seam colour) on a REAL wearer's hands from the game's bundle
// (not readable -> BakeCopy, as in game), screenshots of the back of the hand and the thumb side, with the seam on and off.
// Run: Unity -batchmode -executeMethod WristAccessories.SeamTest.Run. Env LAB_HID (wearer hands id), LAB_ITEMS (hand pieces),
// LAB_OUT (folder), LAB_TONE (1 = Match skin tone on, as the game's default).
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
    public static class SeamTest
    {
        public static void Run()
        {
            int code = 0;
            try { Go(); } catch (Exception e) { Debug.LogError("[SeamTest] " + e); code = 1; }
            EditorApplication.Exit(code);
        }
        static string Env(string k, string d) { var v = Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

        static void Go()
        {
            string outDir = Env("LAB_OUT", "WORK/wristacc/lab/seamtest"); Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Gallery.Index(); Gallery.Load("shaders"); Gallery.Load("cubemaps");
            var scan = JObject.Parse(File.ReadAllText("WORK/tattoo/acc/all_scan_hands.json"));
            HandWear.Items.Clear(); HandWear.Load("WORK/wristacc/acc");
            foreach (var d in Directory.GetDirectories("WORK/wristacc/addons")) HandWear.Load(Path.Combine(d, "acc"));
            HandWear.Load("BACKUPS/wristacc-artem-removed-2026-10-27/source/WristAccessories-Artem/acc");
            Plugin.MatchSkinTone.Value = Env("LAB_TONE", "1") == "1";
            var cubes = Gallery.Loaded["cubemaps"].LoadAllAssets<Cubemap>(); var cube = cubes.FirstOrDefault(c => c.name.ToLower().Contains("shopping")) ?? cubes.FirstOrDefault();
            if (cube) { RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom; RenderSettings.customReflection = cube; }
            var cam = Camera.main; cam.nearClipPlane = 0.01f; cam.fieldOfView = 30f; cam.backgroundColor = new Color(0.12f, 0.12f, 0.12f); cam.clearFlags = CameraClearFlags.SolidColor;
            var rt = new RenderTexture(640, 480, 24); cam.targetTexture = rt; var shot = new Texture2D(640, 480, TextureFormat.RGB24, false);
            var lt = UnityEngine.Object.FindObjectOfType<Light>();

            // LAB_HID=ALL: every arm with a bone list in lab/seamtest (lab/seamtest_bones.py ALL: one outfit per hands bundle)
            var hids = Env("LAB_HID", "6a7209bfab6fee1bb40eb5c5").Split(',').ToList();
            if (hids.Count == 1 && hids[0] == "ALL")
            {
                var have = Directory.GetFiles("WORK/wristacc/lab/seamtest", "*_bones.json").Select(f => Path.GetFileName(f).Substring(0, 8)).ToHashSet();
                hids = scan.Properties().Select(p => p.Name).Where(h => have.Remove(h.Substring(0, 8))).ToList();
            }
            foreach (var hid in hids)
            {
                string path = ((string)scan[hid]["path"]).Replace((char)92, '/'); int i0 = path.ToLower().IndexOf("assets/content/"), j0 = path.ToLower().IndexOf("/bundles/");
                string key = i0 >= 0 ? path.Substring(i0) : path.Substring(j0 + 9);
                AssetBundle b = null; try { b = Gallery.Load(key); } catch (Exception e) { Debug.LogWarning("[SeamTest] bundle " + key + ": " + e.Message); }
                if (b == null) { Debug.LogWarning("[SeamTest] no bundle " + key); continue; }
                foreach (var item in Env("LAB_ITEMS", "tattoo_263bef737747_L").Split(','))
                try
                {
                    if (!HandWear.Items.TryGetValue(item, out var it)) { Debug.LogWarning("[SeamTest] no item " + item); continue; }
                    Debug.Log($"[SeamTest] case {hid.Substring(0, 8)} {item} ({(string)scan[hid]["name"]})");
                    foreach (bool seamOn in Env("LAB_BOTH", "1") == "1" ? new[] { false, true } : new[] { true })
                    {
                        var go = UnityEngine.Object.Instantiate(b.LoadAllAssets<GameObject>().OrderByDescending(g => g.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(r => r.sharedMesh ? r.sharedMesh.vertexCount : 0)).First());
                        // EFT binds the skin to the body's skeleton at runtime (_bonePaths): a rest skeleton from the mesh's own bind poses,
                        // flat (BakeCopy nudges bones with children held), names from lab/seamtest_bones.py
                        var bj = JObject.Parse(File.ReadAllText($"WORK/wristacc/lab/seamtest/{hid.Substring(0, 8)}_bones.json"));
                        var main = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMesh != null && r.sharedMesh.name == (string)bj["mesh"]);
                        if (main == null) { Debug.LogWarning("[SeamTest] no renderer " + bj["mesh"]); UnityEngine.Object.DestroyImmediate(go); continue; }
                        var bnames = bj["bones"].ToObject<string[]>(); var bp = main.sharedMesh.bindposes; var skel = new Transform[bnames.Length];
                        for (int k = 0; k < bnames.Length; k++)
                        {
                            var m = bp[k].inverse; var t = new GameObject(bnames[k]).transform; t.SetParent(go.transform, false);
                            t.position = m.GetColumn(3); t.rotation = m.rotation; skel[k] = t;
                        }
                        main.bones = skel; main.rootBone = skel[0]; main.transform.SetParent(go.transform, false);
                        main.transform.localPosition = Vector3.zero; main.transform.localRotation = Quaternion.identity; main.transform.localScale = Vector3.one;
                        var rends = new[] { main };
                        foreach (var r in rends) { r.gameObject.SetActive(true); r.enabled = true; r.updateWhenOffscreen = true; }
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r != main) r.enabled = false;
                        var skin = go.AddComponent<EFT.Visual.LoddedSkin>(); skin.R = rends;
                        Plugin.SeamBlend.Value = seamOn;
                        var objs = new List<GameObject>(); var swaps = new List<HandWear.Swap>();
                        HandWear.Build(skin, new[] { item }, objs, swaps, Gallery.Materials, "lab");
                        var tr = rends.OrderByDescending(r => r.sharedMesh.vertexCount).First().bones; string S = it.side;
                        Transform B(string n) => tr.FirstOrDefault(x => x != null && x.name == n) ?? go.GetComponentsInChildren<Transform>(true).First(x => x.name == n);
                        Vector3 pc = B($"Base Human{S}Palm").position, d3 = B($"Base Human{S}Digit31").position, f3 = B($"Base Human{S}Forearm3").position;
                        Vector3 d21 = B($"Base Human{S}Digit21").position, d51 = B($"Base Human{S}Digit51").position, tt = B($"Base Human{S}Digit13").position;
                        var nn = Vector3.Cross(d21 - pc, d51 - pc).normalized; var back = Vector3.Dot(tt - pc, nn) > 0 ? -nn : nn; var fd = (d3 - pc).normalized;
                        var side = Vector3.Cross(fd, back) * (S == "L" ? 1 : -1); var c = it.kind == "tattoo" ? pc - fd * 0.07f : pc + fd * 0.02f;
                        int v = 0;
                        foreach (var dir in new[] { back, (side + back * 0.3f).normalized })
                        {
                            cam.transform.position = c + dir * (it.kind == "tattoo" ? 0.6f : 0.42f); cam.transform.LookAt(c, fd);
                            if (lt) lt.transform.rotation = Quaternion.LookRotation(-(dir + fd * 0.3f).normalized, fd);
                            cam.Render(); RenderTexture.active = rt; shot.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); shot.Apply(); RenderTexture.active = null;
                            File.WriteAllBytes($"{outDir}/{hid.Substring(0, 8)}_{item}_{(seamOn ? "seam" : "old")}_{v++}.png", shot.EncodeToPNG());
                        }
                        HandWear.Restore(swaps); foreach (var o in objs) UnityEngine.Object.DestroyImmediate(o); UnityEngine.Object.DestroyImmediate(go);
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[SeamTest] FAIL {hid.Substring(0, 8)} {item}: {e.Message}"); }
                if (hids.Count > 3) { b.Unload(true); Gallery.Loaded.Remove(key); }      // the batch: one arm's bundle at a time
                Resources.UnloadUnusedAssets();
            }
            Debug.Log("[SeamTest] done");
        }
    }
}
#endif
