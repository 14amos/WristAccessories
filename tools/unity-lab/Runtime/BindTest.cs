#if UNITY_EDITOR
// BindTest: handwear items built by the plugin's own HandWear.Build on a rest skeleton made from the game gloves' bind poses
// (one transform per bone name at inverse(bind)), baked by Unity's SkinnedMeshRenderer and measured (median / max distance
// from the middle, mm). User 2026-10-27: some gloves drawn as a long cone in first person. Python (lab/bindcheck.py
// simulation) says their data is a normal hand; this checks what Unity itself makes of it.
// Run: Unity -batchmode -executeMethod WristAccessories.BindTest.Run. Env LAB_ITEMS (comma list of handwear names).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WristAccessories
{
    public static class BindTest
    {
        public static void Run()
        {
            int code = 0;
            try { Go(); } catch (Exception e) { Debug.LogError("[BindTest] " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        static void Go()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            HandWear.Items.Clear();
            HandWear.Load("WORK/wristacc/acc");
            foreach (var d in Directory.GetDirectories("WORK/wristacc/addons")) HandWear.Load(Path.Combine(d, "acc"));
            // the rest skeleton: per bone name the bind pose of the game's own gloves (no addon, no ContentBackport)
            var bind = new Dictionary<string, Matrix4x4>();
            foreach (var it in HandWear.Items.Values.Where(i => !i.note.Contains("WTT") && !i.note.Contains("ISB")))
                for (int b = 0; b < it.bones.Length; b++) if (!bind.ContainsKey(it.bones[b])) bind[it.bones[b]] = it.bind[b];
            var bones = new List<Transform>();
            foreach (var kv in bind)
            {
                var m = kv.Value.inverse; var t = new GameObject(kv.Key).transform;
                t.position = m.GetColumn(3); t.rotation = m.rotation; t.localScale = m.lossyScale; bones.Add(t);
            }
            Debug.Log($"[BindTest] rest skeleton: {bones.Count} bones");
            // the "player's hands": a host renderer with those bones (one dummy vertex), as the plugin finds them in game
            var host = new GameObject("Hands_Host"); var hsmr = host.AddComponent<SkinnedMeshRenderer>();
            var hm = new Mesh(); hm.vertices = new[] { Vector3.zero }; hm.boneWeights = new[] { new BoneWeight { boneIndex0 = 0, weight0 = 1 } }; hm.bindposes = new[] { Matrix4x4.identity };
            hm.SetTriangles(new int[0], 0); hsmr.sharedMesh = hm; hsmr.bones = bones.ToArray(); hsmr.rootBone = bones.First(b => b.name == "Base HumanLForearm3" || b.name.Contains("Forearm"));
            var skin = host.AddComponent<EFT.Visual.LoddedSkin>(); skin.R = new Renderer[] { hsmr };

            var names = (Environment.GetEnvironmentVariable("LAB_ITEMS") ?? "").Split(',').Where(s => s.Length > 0).ToList();
            if (names.Count == 0) names = HandWear.Items.Keys.ToList();
            var lines = new List<string> { "item\tnote\tverts\tmedian_mm\tmax_mm\tbounds_mm" };
            foreach (var n in names)
            {
                if (!HandWear.Items.ContainsKey(n)) { Debug.LogWarning("[BindTest] no item " + n); continue; }
                var objs = new List<GameObject>(); var swaps = new List<HandWear.Swap>();
                HandWear.Build(skin, new[] { n }, objs, swaps, (b, m) => null, "lab");
                foreach (var o in objs)
                {
                    var r = o.GetComponent<SkinnedMeshRenderer>(); var baked = new Mesh(); r.BakeMesh(baked, true);
                    var v = baked.vertices.Select(p => o.transform.TransformPoint(p)).ToArray();
                    var c = new Vector3(Median(v.Select(p => p.x)), Median(v.Select(p => p.y)), Median(v.Select(p => p.z)));
                    var d = v.Select(p => (p - c).magnitude).OrderBy(x => x).ToArray();
                    lines.Add($"{n}\t{HandWear.Items[n].note}\t{v.Length}\t{d[d.Length / 2] * 1000:0}\t{d[d.Length - 1] * 1000:0}\t{r.bounds.size.magnitude * 1000:0}");
                    UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(o);
                }
                HandWear.Restore(swaps);
            }
            File.WriteAllLines("WORK/wristacc/lab/bindtest.tsv", lines);
            Debug.Log("[BindTest] wrote " + (lines.Count - 1) + " items");
        }
        static float Median(IEnumerable<float> x) { var a = x.OrderBy(v => v).ToArray(); return a[a.Length / 2]; }
    }
}
#endif
