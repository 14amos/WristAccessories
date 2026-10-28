// BakeTest: what SkinnedMeshRenderer.BakeMesh gives from a NON-READABLE mesh in Unity 2022.3 (EFT's version), for the SHOES
// own-boot hiding (WristAccessories 0.10.1): vertices / normals / tangents / uv / submeshes / triangles, vertex order, bindposes
// access. Run: Unity -batchmode -projectPath CatTest -executeMethod WristAccessories.BakeTest.Run -logFile <log>
using System;
using System.Linq;
using UnityEngine;

namespace WristAccessories
{
    public static class BakeTest
    {
        static void L(string s) => Debug.Log("[BakeTest] " + s);
        public static void Run()
        {
            try
            {
                var root = new GameObject("root"); var b0 = new GameObject("b0").transform; var b1 = new GameObject("b1").transform;
                b0.SetParent(root.transform, false); b1.SetParent(root.transform, false); b1.localPosition = new Vector3(0, 1, 0);
                var m = new Mesh { name = "test" };
                var v = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(0, 2, 0), new Vector3(1, 2, 0) };
                m.vertices = v; m.uv = v.Select(x => new Vector2(x.x * 0.5f, x.y * 0.25f)).ToArray(); m.RecalculateNormals(); m.RecalculateTangents();
                m.subMeshCount = 2; m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0); m.SetTriangles(new[] { 2, 4, 3, 3, 4, 5 }, 1);
                m.boneWeights = v.Select(x => new BoneWeight { boneIndex0 = 0, boneIndex1 = 1, weight0 = 1 - x.y / 2, weight1 = x.y / 2 }).ToArray();
                m.bindposes = new[] { b0.worldToLocalMatrix * root.transform.localToWorldMatrix, b1.worldToLocalMatrix * root.transform.localToWorldMatrix };
                m.UploadMeshData(true);
                L($"source readable {m.isReadable}, vertexCount {m.vertexCount}, subMeshCount {m.subMeshCount}");
                try { L($"bindposes on non-readable: {m.bindposes.Length}"); } catch (Exception e) { L("bindposes: " + e.Message); }
                try { L($"GetSubMesh(1): indexStart {m.GetSubMesh(1).indexStart} count {m.GetSubMesh(1).indexCount}"); } catch (Exception e) { L("GetSubMesh: " + e.Message); }
                var go = new GameObject("smr"); go.transform.SetParent(root.transform, false); go.transform.localScale = new Vector3(2, 2, 2);
                var r = go.AddComponent<SkinnedMeshRenderer>(); r.bones = new[] { b0, b1 }; r.sharedMesh = m; r.rootBone = b0;
                b1.localPosition = new Vector3(0.5f, 1, 0);                                              // pose: bone 1 moved
                foreach (bool sc in new[] { false, true })
                {
                    var o = new Mesh(); r.BakeMesh(o, sc);
                    L($"BakeMesh(useScale {sc}): readable {o.isReadable} verts {o.vertexCount} normals {o.normals.Length} tangents {o.tangents.Length} uv {o.uv.Length} subMeshes {o.subMeshCount} tris {string.Join("/", Enumerable.Range(0, o.subMeshCount).Select(i => o.GetTriangles(i).Length))} weights {o.boneWeights.Length} bindposes {o.bindposes.Length}");
                    L($"  v[5] {o.vertices[5].ToString("F3")} (bind 1,2,0), uv[5] {(o.uv.Length > 5 ? o.uv[5].ToString("F3") : "-")}, tris0 {string.Join(",", o.GetTriangles(0))}");
                }
                // weights back from bakes: nudge one bone (children held in place), bake, displacement / nudge = that bone's weight
                var mw = new Mesh(); var rnd = new System.Random(3); int nv = 40;
                var bones = new Transform[4]; for (int i = 0; i < 4; i++) { bones[i] = new GameObject("w" + i).transform; bones[i].SetParent(i == 0 ? root.transform : bones[i - 1], false); bones[i].localPosition = new Vector3(0, 0.5f, 0); bones[i].localRotation = Quaternion.Euler(10 * i, 20 * i, 0); }
                var pv = new Vector3[nv]; var bw = new BoneWeight[nv];
                for (int i = 0; i < nv; i++)
                {
                    pv[i] = new Vector3((float)rnd.NextDouble(), (float)rnd.NextDouble() * 2, (float)rnd.NextDouble());
                    float a = (float)rnd.NextDouble(), b = (float)rnd.NextDouble() * (1 - a), c = 1 - a - b;
                    bw[i] = new BoneWeight { boneIndex0 = i % 4, boneIndex1 = (i + 1) % 4, boneIndex2 = (i + 2) % 4, weight0 = a, weight1 = b, weight2 = c };
                }
                mw.vertices = pv; mw.SetTriangles(Enumerable.Range(0, nv - nv % 3).ToArray(), 0); mw.boneWeights = bw;
                var go2 = new GameObject("smr2"); go2.transform.SetParent(root.transform, false); go2.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f); go2.transform.localPosition = new Vector3(3, 0, 0);
                mw.bindposes = bones.Select(t => t.worldToLocalMatrix * go2.transform.localToWorldMatrix).ToArray(); mw.UploadMeshData(true);
                var r2 = go2.AddComponent<SkinnedMeshRenderer>(); r2.bones = bones; r2.sharedMesh = mw; r2.rootBone = bones[0];
                bones[1].localRotation = Quaternion.Euler(30, 0, 15); bones[3].localPosition = new Vector3(0.2f, 0.4f, 0);   // a pose
                var w = HandWearBake(r2);
                float err = 0; for (int i = 0; i < nv; i++) { var t = new float[4]; t[bw[i].boneIndex0] += bw[i].weight0; t[bw[i].boneIndex1] += bw[i].weight1; t[bw[i].boneIndex2] += bw[i].weight2; for (int k = 0; k < 4; k++) err = Mathf.Max(err, Mathf.Abs(t[k] - w[i, k])); }
                L($"weights from bakes: max error {err:0.00000} over {nv} verts x 4 bones (child bones held)");
            }
            catch (Exception e) { L("FAILED " + e); }
            EditorApplicationExit();
        }
        static float[,] HandWearBake(SkinnedMeshRenderer r)
        {
            var b0 = new Mesh(); r.BakeMesh(b0, true); var v0 = b0.vertices; var bones = r.bones; var w = new float[v0.Length, bones.Length];
            var d = new Vector3(0.1f, 0, 0); var e = r.transform.InverseTransformVector(d);
            for (int j = 0; j < bones.Length; j++)
            {
                var bj = bones[j]; var kids = Enumerable.Range(0, bj.childCount).Select(i => bj.GetChild(i)).Select(c => (c, c.position)).ToList();
                bj.position += d; foreach (var (c, p) in kids) c.position = p;
                var b = new Mesh(); r.BakeMesh(b, true); var v = b.vertices;
                for (int i = 0; i < v.Length; i++) w[i, j] = Vector3.Dot(v[i] - v0[i], e) / e.sqrMagnitude;
                bj.position -= d; foreach (var (c, p) in kids) c.position = p;
            }
            return w;
        }
        static void EditorApplicationExit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#endif
        }
    }
}
