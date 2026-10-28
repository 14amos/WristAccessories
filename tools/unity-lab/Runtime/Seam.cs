using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WristAccessories {
// The wrist seam of a swapped BARE hand (user 2026-10-27: "fade/tuck the new hand's edge under my own wrist", "the skin tone is
// still a bit off"; lab/hand_onarm.py TUCK + SEAMTONE2, lab/seamtone.py, data lab/seam_data.py -> hw json "seam_d0"):
//  - the wearer's own hand stays full size up to where the piece starts (per 30 deg sector round the forearm, + SeamOv), then
//    sinks into the shrunk hand over SeamSink: a copy of the hands mesh whose hand-bone weights partly move to one shrunk point;
//  - the piece's first SeamBand is brought to SeamDepth under the wearer's skin (nearest wearer vertices, in or out, <= SeamMax);
//  - the piece's skin texels get the wearer's skin colour AT THE SEAM: Reinhard transfer in CIELAB (mean / sd), statistics from the
//    15 mm before (wearer) and after (piece) the seam; ink darker than 1.2 sd under the piece's skin keeps its colour.
// A wearer whose seam band is not skin (a glove: QILO Topographic's left hand) keeps the old whole-hand shrink.
static partial class HandWear
{
    const float SeamOv = 0.004f, SeamSink = 0.008f, SeamBand = 0.008f, SeamDepth = 0.0007f, SeamMax = 0.004f, SeamColorBand = 0.015f;

    public class SeamInfo
    {
        public string side; public float[] d0; public Vector3 pc, fd, f3, e1, e2, ax;
        public Vector3[] v, n; public float[] d; public Dictionary<string, Matrix4x4> bind = new Dictionary<string, Matrix4x4>();
        public Vector3 labMean, labSd; public bool tone;
        public int Sector(Vector3 q) { var r = q - f3; float a = Mathf.Atan2(Vector3.Dot(r, e2), Vector3.Dot(r, e1)); if (a < 0) a += 2 * Mathf.PI; return ((int)(a / (2 * Mathf.PI) * 12)) % 12; }
        public float D(Vector3 q) => Vector3.Dot(q - pc, fd);
    }

    // anatomical frame from bind poses (lab seam_data.frame): palm origin, toward the middle finger, forearm axis, back of the hand
    static bool SeamFrame(Func<string, Vector3?> o, string S, SeamInfo s)
    {
        var pc = o($"Base Human{S}Palm"); var d3 = o($"Base Human{S}Digit31"); var f3 = o($"Base Human{S}Forearm3");
        var d21 = o($"Base Human{S}Digit21"); var d51 = o($"Base Human{S}Digit51"); var tt = o($"Base Human{S}Digit13");
        if (pc == null || d3 == null || f3 == null || d21 == null || d51 == null || tt == null) return false;
        s.pc = pc.Value; s.f3 = f3.Value; s.fd = (d3.Value - s.pc).normalized; s.ax = (s.pc - s.f3).normalized;
        var nn = Vector3.Cross(d21.Value - s.pc, d51.Value - s.pc).normalized; var back = Vector3.Dot(tt.Value - s.pc, nn) > 0 ? -nn : nn;
        s.e1 = (back - s.ax * Vector3.Dot(back, s.ax)).normalized; s.e2 = Vector3.Cross(s.ax, s.e1);
        return true;
    }

    /// <summary>The wearer's hand on this side kept full size up to the piece's seam (mesh copy + one shrunk point), or null when
    /// it can't be done here (then CutHand as before).</summary>
    static SeamInfo SeamHand(SkinnedMeshRenderer r, Item it, List<Swap> swaps, string who)
    {
        var m = r.sharedMesh; string S = it.side; Mesh c; string how;
        if (m.isReadable) { c = UnityEngine.Object.Instantiate(m); how = "copied"; }
        else { c = BakeCopy(r, who); how = "rebuilt from bakes"; if (c == null) return null; }
        var bones = r.bones; var bind = c.bindposes; var s = new SeamInfo { side = S, d0 = it.seamD0 };
        for (int i = 0; i < bones.Length && i < bind.Length; i++) if (bones[i] != null && !s.bind.ContainsKey(bones[i].name)) s.bind[bones[i].name] = bind[i];
        Vector3? O(string nm) => s.bind.TryGetValue(nm, out var b) ? b.inverse.MultiplyPoint3x4(Vector3.zero) : (Vector3?)null;
        if (!SeamFrame(O, S, s)) { Plugin.Log.LogInfo($"[WristAccessories] seam ({who}): {r.name} lacks {S} hand bones - whole-hand shrink"); UnityEngine.Object.Destroy(c); return null; }
        s.v = c.vertices; s.n = c.normals; var uv = c.uv; int nv = s.v.Length;
        s.d = new float[nv]; var t = new float[nv];
        for (int i = 0; i < nv; i++) { s.d[i] = s.D(s.v[i]); t[i] = Mathf.Clamp01((s.d[i] - s.d0[s.Sector(s.v[i])] - SeamOv) / SeamSink); }
        // the wearer's skin just before the seam: its colour, and is it skin at all (a glove)?
        // glove test on the wearer's back of the hand (5-30 mm past the wrist or the seam start, whichever is further): an outfit bracelet
        // just up the arm from a -5 mm seam is not a glove (every right-hand piece on QILO Choco Chip fell back on its baked paracord)
        var px = new List<Color>(); var pxv = new List<Color>(); var mats = r.sharedMaterials;
        for (int sm = 0; sm < c.subMeshCount && sm < mats.Length; sm++)
        {
            var tex = mats[sm] != null && mats[sm].HasProperty("_MainTex") ? mats[sm].GetTexture("_MainTex") : null; if (tex == null) continue;
            var tri = c.GetTriangles(sm); List<int> band = new List<int>(), vis = new List<int>();
            for (int k = 0; k < tri.Length; k += 3)
            {
                var cen = (s.v[tri[k]] + s.v[tri[k + 1]] + s.v[tri[k + 2]]) / 3f; float dd = s.D(cen), z = s.d0[s.Sector(cen)];
                if (dd > z - SeamColorBand && dd < z + SeamOv) band.Add(k);
                float h0 = Mathf.Max(z, 0f);                                          // the back of the hand itself, not the wrist
                if (dd > h0 + 0.005f && dd < h0 + 0.03f) vis.Add(k);
            }
            if (band.Count == 0 && vis.Count == 0) continue;
            var all = TexPx(tex, out int tw, out int th);
            SamplePx(all, tw, th, uv, tri, band, px); SamplePx(all, tw, th, uv, tri, vis, pxv);
        }
        int skinN = px.Count(p => SkinW(p) > 0.5f), visN = pxv.Count(p => SkinW(p) > 0.5f);
        // a glove: the wearer's own hand there not >= 90% skin, or tan / khaki (CIELAB hue >= 55 deg; skin 34-45 deg). SeamTest over
        // all 356 arms (lab/seamall.tsv, 2026-10-27): tan gloves pass the skin test at 30-80% with hue 53-85 deg, bare hands 98-100%
        Vector3 vlab = pxv.Count > 0 ? LabStats(pxv).mean : Vector3.zero; float hue = Mathf.Atan2(vlab.z, vlab.y) * Mathf.Rad2Deg;
        if (pxv.Count < 40 || visN < pxv.Count * 0.9f || hue >= 55f || skinN < 40)
        {
            Plugin.Log.LogInfo($"[WristAccessories] seam ({who}): your {S} hand where it would stay is {(pxv.Count == 0 ? "not found" : $"{visN * 100 / Math.Max(1, pxv.Count)}% skin ({pxv.Count} texels), hue {hue:0} deg")}, {skinN} skin texels for the colour - a glove: whole-hand shrink as before");
            UnityEngine.Object.Destroy(c); return null;
        }
        (s.labMean, s.labSd) = LabStats(px); s.tone = true;
        // weights: the hand bones' share moves to one shrunk point by t (0 = full size, 1 = as CutHand)
        int palm = Array.FindIndex(bones, b => b != null && b.name.Contains("Human" + S + "Palm"));
        bool Hand(Transform b) => b != null && (b.name.Contains("Palm") || b.name.Contains("Digit")) && b.name.Contains("Human" + S);
        var pt = new GameObject("WristAcc_HandSeam_" + S).transform; pt.SetParent(bones[palm], false); pt.localPosition = Vector3.zero; pt.localRotation = Quaternion.identity; pt.localScale = Vector3.one * HandShrink;
        int P = bones.Length; var nb = bones.Concat(new[] { pt }).ToArray(); var nbind = bind.Concat(new[] { bind[palm] }).ToArray();
        var bw = c.boneWeights; int moved = 0; var ent = new List<(int b, float w)>(5);
        for (int i = 0; i < nv; i++)
        {
            if (t[i] <= 0) continue;
            var w = bw[i]; ent.Clear(); float h = 0;
            void Add(int b, float x) { if (x <= 0) return; if (Hand(bones[b])) { h += x; x *= 1 - t[i]; } if (x > 1e-4f) ent.Add((b, x)); }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            if (h <= 0) continue;
            ent.Add((P, h * t[i])); moved++;
            var top = ent.OrderByDescending(e => e.w).Take(4).ToList(); float sum = top.Sum(e => e.w); while (top.Count < 4) top.Add((0, 0f));
            bw[i] = new BoneWeight { boneIndex0 = top[0].b, weight0 = top[0].w / sum, boneIndex1 = top[1].b, weight1 = top[1].w / sum, boneIndex2 = top[2].b, weight2 = top[2].w / sum, boneIndex3 = top[3].b, weight3 = top[3].w / sum };
        }
        c.boneWeights = bw; c.bindposes = nbind; c.name = m.name + "_WristAccSeam" + S;
        var sw = new Swap { r = r, bones = r.bones, mesh = m }; sw.points.Add(pt.gameObject); swaps.Add(sw);
        r.bones = nb; r.sharedMesh = c;
        Plugin.Log.LogInfo($"[WristAccessories] seam ({who}): your {S} hand full size up to {it.name}'s edge ({s.d0.Min() * 1000:0}..{s.d0.Max() * 1000:0} mm from the wrist), then sinking over {SeamSink * 1000:0} mm: {moved} of {nv} vertices ({how}); skin at the seam {skinN * 100 / Math.Max(1, px.Count)}% of {px.Count} texels, where it stays {visN * 100 / pxv.Count}%, Lab {s.labMean.x:0.0}/{s.labMean.y:0.0}/{s.labMean.z:0.0} sd {s.labSd.x:0.0}/{s.labSd.y:0.0}/{s.labSd.z:0.0}");
        return s;
    }

    /// <summary>The piece's first SeamBand pulled under the wearer's skin (pos changed in place); returns the piece vertices in the
    /// wearer's mesh space (for the colour band).</summary>
    static Vector3[] SeamTuck(Item it, Vector3[] pos, SeamInfo s, string who)
    {
        int n = pos.Length; var A = new Matrix4x4[it.bones.Length]; int missing = 0;
        for (int k = 0; k < A.Length; k++) { if (s.bind.TryGetValue(it.bones[k], out var wb)) A[k] = wb.inverse * it.bind[k]; else { A[k] = Matrix4x4.identity; missing++; } }
        var vw = new Vector3[n];
        Matrix4x4 Blend(BoneWeight b)
        {
            var M = new Matrix4x4();
            void Acc(int i, float w) { if (w <= 0) return; for (int e = 0; e < 16; e++) M[e] += A[i][e] * w; }
            Acc(b.boneIndex0, b.weight0); Acc(b.boneIndex1, b.weight1); Acc(b.boneIndex2, b.weight2); Acc(b.boneIndex3, b.weight3); return M;
        }
        var cand = new List<int>(); float lo = s.d0.Min() - 0.03f, hi = s.d0.Max() + 0.03f;
        for (int i = 0; i < s.v.Length; i++) if (s.d[i] > lo && s.d[i] < hi) cand.Add(i);
        int edge = 0; float maxMv = 0; var best = new (int i, float d2)[4];
        for (int i = 0; i < n; i++)
        {
            var M = Blend(it.bw[i]); vw[i] = M.MultiplyPoint3x4(pos[i]);
            float wt = Mathf.Clamp01(1 - (s.D(vw[i]) - s.d0[s.Sector(vw[i])]) / SeamBand); if (wt <= 0 || cand.Count == 0) continue;
            for (int k = 0; k < 4; k++) best[k] = (-1, float.MaxValue);
            foreach (int j in cand)
            {
                float d2 = (s.v[j] - vw[i]).sqrMagnitude; int w = 0; for (int k = 1; k < 4; k++) if (best[k].d2 > best[w].d2) w = k;
                if (d2 < best[w].d2) best[w] = (j, d2);
            }
            Vector3 q = Vector3.zero, nn = Vector3.zero; int c = 0;
            foreach (var b in best) if (b.i >= 0) { q += s.v[b.i]; nn += s.n[b.i]; c++; }
            if (c == 0) continue; q /= c; nn = nn.normalized;
            // both ways (user 21:45, dark crescent under the seam): a piece lower than the wearer's hand left the wearer's skin sloping
            // down over its edge in shadow; now the band meets the wearer's surface (SeamDepth under it) from either side
            float sd = Vector3.Dot(vw[i] - q, nn), mv = Mathf.Clamp(-SeamDepth - sd, -SeamMax, SeamMax); if (Mathf.Abs(mv) < 1e-5f) continue;
            var dw = nn * (mv * wt); pos[i] += M.inverse.MultiplyVector(dw); vw[i] += dw; edge++; maxMv = Mathf.Max(maxMv, Mathf.Abs(mv) * wt);
        }
        Plugin.Log.LogInfo($"[WristAccessories] seam ({who}): {it.name} edge tucked under your skin: {edge} vertices moved, max {maxMv * 1000:0.0} mm{(missing > 0 ? $", {missing} bones not in your hands" : "")}");
        return vw;
    }

    /// <summary>The piece's material with its skin recoloured to the wearer's skin at the seam, or null (then Toned as before).</summary>
    static Material SeamToned(Material m, Item it, int sub, Vector3[] vw, SeamInfo s, string who)
    {
        if (m == null || !m.HasProperty("_MainTex") || m.GetTexture("_MainTex") == null || !s.tone) return null;
        string key = m.GetInstanceID() + "|seam|" + it.name + "|" + s.labMean.ToString("F1");
        if (TonedCache.TryGetValue(key, out var cached) && cached != null) return cached;
        var tex = m.GetTexture("_MainTex"); var px = new List<Color>(); var tri = it.subs[sub].tris; var band = new List<int>();
        for (int k = 0; k + 2 < tri.Length; k += 3)
        {
            var cen = (vw[tri[k]] + vw[tri[k + 1]] + vw[tri[k + 2]]) / 3f;
            if (s.D(cen) < s.d0[s.Sector(cen)] + SeamColorBand) band.Add(k);
        }
        if (band.Count == 0) return null;
        var t = Copy(tex, tex.width, tex.height); var all = t.GetPixels();
        SamplePx(all, t.width, t.height, it.uv, tri, band, px);
        if (px.Count(p => SkinW(p) > 0.5f) < 20) { UnityEngine.Object.Destroy(t); return null; }
        var (pm, psd) = LabStats(px); var k3 = new Vector3(Mathf.Clamp(s.labSd.x / psd.x, 0.6f, 1.6f), Mathf.Clamp(s.labSd.y / psd.y, 0.6f, 1.6f), Mathf.Clamp(s.labSd.z / psd.z, 0.6f, 1.6f));
        float sh = 1f + Plugin.SkinShade.Value / 100f; int changed = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var p = all[i]; float w = SkinW(p); if (w <= 0) continue;
            var L = ToLab(p); w *= Mathf.Clamp01((L.x - (pm.x - 1.2f * psd.x)) / Mathf.Max(psd.x, 1e-3f)); if (w <= 0) continue;   // ink stays
            var L2 = Vector3.Scale(L - pm, k3) + s.labMean; var q = FromLab(L2);
            q = new Color(Mathf.Clamp01(q.r * sh), Mathf.Clamp01(q.g * sh), Mathf.Clamp01(q.b * sh));
            all[i] = new Color(p.r + (q.r - p.r) * w, p.g + (q.g - p.g) * w, p.b + (q.b - p.b) * w, p.a); changed++;
        }
        t.SetPixels(all); t.Apply(true); t.Compress(true); t.Apply(false, true);
        t.name = tex.name + "_WristAccSeam"; t.wrapMode = tex.wrapMode; t.filterMode = tex.filterMode; t.anisoLevel = tex.anisoLevel; UnityEngine.Object.DontDestroyOnLoad(t);
        var c = new Material(m) { name = m.name + "_WristAccSeam" }; c.SetTexture("_MainTex", t); UnityEngine.Object.DontDestroyOnLoad(c);
        TonedCache[key] = c; TonedOrder.Add(key);
        Plugin.Log.LogInfo($"[WristAccessories] seam ({who}): {it.name} skin matched to yours at the seam: piece Lab {pm.x:0.0}/{pm.y:0.0}/{pm.z:0.0} -> {s.labMean.x:0.0}/{s.labMean.y:0.0}/{s.labMean.z:0.0}, {changed} of {all.Length} texels ({px.Count} seam texels)");
        return c;
    }

    // 4 samples per triangle (centre + towards each corner), as lab hand_onarm SEAMTONE2
    static readonly Vector3[] Bary = { new Vector3(1 / 3f, 1 / 3f, 1 / 3f), new Vector3(0.6f, 0.2f, 0.2f), new Vector3(0.2f, 0.6f, 0.2f), new Vector3(0.2f, 0.2f, 0.6f) };
    static Color[] TexPx(Texture tex, out int w, out int h)
    {
        w = Math.Min(tex.width, 1024); h = Math.Min(tex.height, 1024); var t = Copy(tex, w, h); var all = t.GetPixels(); UnityEngine.Object.Destroy(t); return all;
    }
    static void SamplePx(Color[] all, int w, int h, Vector2[] uv, int[] tri, List<int> band, List<Color> outPx)
    {
        foreach (int k in band)
            foreach (var b in Bary)
            {
                var u = uv[tri[k]] * b.x + uv[tri[k + 1]] * b.y + uv[tri[k + 2]] * b.z;
                float fu = u.x - Mathf.Floor(u.x), fv = u.y - Mathf.Floor(u.y);
                outPx.Add(all[(int)(fv * (h - 1)) * w + (int)(fu * (w - 1))]);           // GetPixels rows start at the bottom (v = 0)
            }
    }

    // soft skin weight (lab seamtone.skin_w; saturation from 0.04: pale hands were left untouched, SeamTest 2026-10-27)
    static float SkinW(Color c)
    {
        Color.RGBToHSV(c, out float hh, out float ss, out float vv); float hd = Mathf.Min(hh * 360f, 360f - hh * 360f);
        float Sm(float x, float a, float b) => Mathf.Clamp01((x - a) / (b - a));
        return Sm(55f - hd, 0f, 10f) * Sm(ss, 0.04f, 0.09f) * Sm(0.75f - ss, 0f, 0.08f) * Sm(vv, 0.18f, 0.26f);
    }
    static (Vector3 mean, Vector3 sd) LabStats(List<Color> px)
    {
        var L = px.Where(p => SkinW(p) > 0.5f).Select(ToLab).ToList(); var mean = Vector3.zero; foreach (var x in L) mean += x; mean /= Math.Max(1, L.Count);
        var v = Vector3.zero; foreach (var x in L) { var d = x - mean; v += Vector3.Scale(d, d); } v /= Math.Max(1, L.Count);
        return (mean, new Vector3(Mathf.Sqrt(v.x) + 1e-3f, Mathf.Sqrt(v.y) + 1e-3f, Mathf.Sqrt(v.z) + 1e-3f));
    }
    static float Lin(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    static float Gam(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(Mathf.Max(c, 0f), 1f / 2.4f) - 0.055f;
    static float Fl(float x) => x > 0.008856f ? Mathf.Pow(x, 1f / 3f) : 7.787f * x + 16f / 116f;
    static float Fi(float f) => f * f * f > 0.008856f ? f * f * f : (f - 16f / 116f) / 7.787f;
    static Vector3 ToLab(Color c)
    {
        float r = Lin(Mathf.Clamp01(c.r)), g = Lin(Mathf.Clamp01(c.g)), b = Lin(Mathf.Clamp01(c.b));
        float x = (0.4124f * r + 0.3576f * g + 0.1805f * b) / 0.9505f, y = 0.2126f * r + 0.7152f * g + 0.0722f * b, z = (0.0193f * r + 0.1192f * g + 0.9505f * b) / 1.089f;
        float fx = Fl(x), fy = Fl(y), fz = Fl(z); return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
    }
    static Color FromLab(Vector3 L)
    {
        float fy = (L.x + 16f) / 116f, fx = fy + L.y / 500f, fz = fy - L.z / 200f;
        float x = Fi(fx) * 0.9505f, y = Fi(fy), z = Fi(fz) * 1.089f;
        float r = 3.2406f * x - 1.5372f * y - 0.4986f * z, g = -0.9689f * x + 1.8758f * y + 0.0415f * z, b = 0.0557f * x - 0.2040f * y + 1.0570f * z;
        return new Color(Mathf.Clamp01(Gam(r)), Mathf.Clamp01(Gam(g)), Mathf.Clamp01(Gam(b)));
    }

    // ---- arm / hand tattoo overlays on any top (kind "tattoo" + "press", lab/arm_ink.py; user 2026-10-27 "any top, hidden under
    // sleeves"): every ink vertex put InkLift over the wearer's own surface (4 nearest vertices of a bake of the hands as they are),
    // triangles dropped where that surface is not skin (a sleeve, a glove, a cuff) or more than InkFar away (nothing modelled there).
    const float InkLift = 0.0006f, InkFar = 0.02f;
    static int[][] PressInk(Item it, Vector3[] pos, SkinnedMeshRenderer r, string who)
    {
        var bones = r.bones; var bm = new Mesh(); r.BakeMesh(bm, true);
        var wv = bm.vertices; var wn = bm.normals; var wuv = bm.uv; int nv = wv.Length;
        var skin = new bool[nv]; var mats = r.sharedMaterials;
        for (int sm = 0; sm < bm.subMeshCount && sm < mats.Length; sm++)
        {
            var tex = mats[sm] != null && mats[sm].HasProperty("_MainTex") ? mats[sm].GetTexture("_MainTex") : null; if (tex == null) continue;
            var all = TexPx(tex, out int tw, out int th);
            foreach (int v in bm.GetTriangles(sm).Distinct())
            {
                var u = wuv[v]; float fu = u.x - Mathf.Floor(u.x), fv = u.y - Mathf.Floor(u.y);
                skin[v] = SkinW(all[(int)(fv * (th - 1)) * tw + (int)(fu * (tw - 1))]) > 0.5f;
            }
        }
        UnityEngine.Object.Destroy(bm);
        var bind = new Dictionary<string, Matrix4x4>();
        for (int b = 0; b < bones.Length; b++) if (bones[b] != null && !bind.ContainsKey(bones[b].name)) bind[bones[b].name] = bones[b].worldToLocalMatrix * r.transform.localToWorldMatrix;
        var A = new Matrix4x4[it.bones.Length]; int missing = 0;
        for (int k = 0; k < A.Length; k++) { if (bind.TryGetValue(it.bones[k], out var wb)) A[k] = wb.inverse * it.bind[k]; else { A[k] = Matrix4x4.identity; missing++; } }
        int n = pos.Length; var vw = new Vector3[n]; var M = new Matrix4x4[n]; var lo = Vector3.one * 1e9f; var hi = -lo;
        for (int i = 0; i < n; i++)
        {
            var b = it.bw[i]; var m = new Matrix4x4();
            void Acc(int j, float w) { if (w <= 0) return; for (int e = 0; e < 16; e++) m[e] += A[j][e] * w; }
            Acc(b.boneIndex0, b.weight0); Acc(b.boneIndex1, b.weight1); Acc(b.boneIndex2, b.weight2); Acc(b.boneIndex3, b.weight3);
            M[i] = m; vw[i] = m.MultiplyPoint3x4(pos[i]); lo = Vector3.Min(lo, vw[i]); hi = Vector3.Max(hi, vw[i]);
        }
        lo -= Vector3.one * 0.03f; hi += Vector3.one * 0.03f; var cand = new List<int>();
        for (int j = 0; j < nv; j++) { var p = wv[j]; if (p.x >= lo.x && p.y >= lo.y && p.z >= lo.z && p.x <= hi.x && p.y <= hi.y && p.z <= hi.z) cand.Add(j); }
        var ok = new bool[n]; var best = new (int i, float d2)[4]; int far = 0, cloth = 0;
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 4; k++) best[k] = (-1, float.MaxValue);
            foreach (int j in cand)
            {
                float d2 = (wv[j] - vw[i]).sqrMagnitude; int w = 0; for (int k = 1; k < 4; k++) if (best[k].d2 > best[w].d2) w = k;
                if (d2 < best[w].d2) best[w] = (j, d2);
            }
            Vector3 q = Vector3.zero, nn = Vector3.zero; int c = 0, sk = 0;
            foreach (var bb in best) if (bb.i >= 0) { q += wv[bb.i]; nn += wn[bb.i]; c++; if (skin[bb.i]) sk++; }
            if (c == 0) { far++; continue; }
            q /= c; nn = nn.normalized; float sd = Vector3.Dot(vw[i] - q, nn);
            if (Mathf.Abs(sd) > InkFar || Mathf.Sqrt(best.Min(x => x.d2)) > InkFar) { far++; continue; }
            if (sk * 2 < c) { cloth++; continue; }
            var dw = nn * (InkLift - sd); pos[i] += M[i].inverse.MultiplyVector(dw); ok[i] = true;
        }
        int kept = 0, total = 0; var outT = new int[it.subs.Count][];
        for (int s = 0; s < it.subs.Count; s++)
        {
            var t = it.subs[s].tris; var l = new List<int>(t.Length);
            for (int k = 0; k + 2 < t.Length; k += 3) { total++; if (ok[t[k]] && ok[t[k + 1]] && ok[t[k + 2]]) { l.Add(t[k]); l.Add(t[k + 1]); l.Add(t[k + 2]); kept++; } }
            outT[s] = l.ToArray();
        }
        Plugin.Log.LogInfo($"[WristAccessories] tattoos ({who}): {it.name} pressed onto your arm: {kept} of {total} ink triangles shown, {cloth} vertices under cloth, {far} with nothing of yours near{(missing > 0 ? $", {missing} bones not in your hands" : "")}");
        return outT;
    }
}
}
