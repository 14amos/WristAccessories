using System;
using System.Collections.Generic;
using UnityEngine;
using EFT.Visual;

namespace WristAccessories {
// A loose bracelet (round loop bigger than the sleeve, e.g. beads_angelite_loose) that hangs, slides, tilts and lags on the
// forearm. Purely visual and local. Everything in the forearm bone's frame (the frame of the outline and the meshes).
// 0.6.1 (user: jittery, stiff, wrong direction, clips/floats):
//   - collides with the LIVE arm: the hands' skinned mesh baked ~5x/s in its current pose -> outermost radius per 5 deg per 2 mm
//     (the offline outline is up to 18 mm off in game); falls back to the outline where the bake has no vertices
//   - gravity / arm acceleration into bone space with the full matrix (mirrored left bones flip an axis; rotation-only was wrong)
//   - arm acceleration low-pass filtered (camera bob / animation noise made it twitch)
//   - state: centre offset d (hang), slide z, tilt (low side swings toward the low end of the arm, limited by the clearance),
//     spin (lags behind forearm twist, springs back); light damping
class BraceletPhysics : MonoBehaviour
{
    public WristFit.Outline O; public float RingR, Lo, Hi, ZMin, ZMax; public string Name; public LoddedSkin Hands;
    float _z, _zPrev, _tilt, _tiltV, _spin, _spinV; Vector2 _d, _dPrev; Vector3 _lastPos, _lastVel, _accF, _tiltAxis = Vector3.right;
    Quaternion _lastRot; bool _init; float _logAt, _bakeAt; int _fitFails, _frames; float _outlLogAt; WristFit.Outline _live; Mesh _baked;
    const float Mu = 0.32f, DampSide = 2.2f, DampAxial = 3.5f, AccTau = 0.07f;   // lab: mu .18 / damp 1.6 slid 60 mm in 0.5 s

    void OnDestroy() { if (_baked) Destroy(_baked); }

    /// <summary>Current state (bead lab readout): slide, loop centre offset, tilt, spin, last pull along / across the arm.</summary>
    public float Z => _z; public Vector2 D => _d; public float TiltDeg => _tilt; public float SpinDeg => _spin; public Vector3 LastPull; public int FitFails => _fitFails;
    /// <summary>Set by the bead lab (Unity editor test scene): it calls Step itself with its own clock.</summary>
    public bool ManualStep;
    void LateUpdate() { if (!ManualStep) Step(Time.deltaTime, Time.time); }

    public void Step(float rawDt, float now)
    {
        var bone = transform.parent;
        if (bone == null || O == null || !Plugin.BraceletPhysicsOn.Value) { if (_init) { transform.localPosition = Vector3.zero; transform.localRotation = Quaternion.identity; _init = false; } return; }
        float dt = Mathf.Clamp(rawDt, 1e-4f, 0.05f);
        if (now >= _bakeAt) { _bakeAt = now + 0.2f; BakeLive(bone, now); }
        var arm = _live ?? O;
        if (!_init)
        {
            var sec = WristFit.Section(arm, Lo, Hi); var c = Vector2.zero; foreach (var q in sec) c += q; _d = _dPrev = c / sec.Length;
            _z = _zPrev = 0f; _tilt = _tiltV = _spin = _spinV = 0f; _lastPos = bone.position; _lastVel = Vector3.zero; _accF = Vector3.zero; _lastRot = bone.rotation; _init = true;
            Plugin.Log.LogInfo($"[WristAccessories] physics on {Name}: loop radius {RingR * 1000:0} mm, can slide {ZMin * 1000:0}..{ZMax * 1000:0} mm, live arm {(_live != null ? "yes" : "no (outline)")}");
        }
        // drive: gravity minus the bone's (filtered) acceleration, into bone space with the full matrix (mirrored bones)
        var vel = (bone.position - _lastPos) / dt; var acc = (vel - _lastVel) / dt; _lastPos = bone.position; _lastVel = vel;
        if (acc.magnitude > 60f) acc = Vector3.zero;                                                     // teleport / camera cut
        _accF = Vector3.Lerp(_accF, acc, 1f - Mathf.Exp(-dt / AccTau));
        var gw = (new Vector3(0f, -9.81f, 0f) - Vector3.ClampMagnitude(_accF, 25f)) * Plugin.PhysicsStrength.Value;
        var g = bone.worldToLocalMatrix.MultiplyVector(gw);
        float ga = Vector3.Dot(g, O.ax); var gs = new Vector2(Vector3.Dot(g, O.e1), Vector3.Dot(g, O.e2));
        LastPull = new Vector3(ga, gs.x, gs.y);

        // hang (across the arm): damped Verlet toward the pull, kept outside the live arm
        var dn = _d + (_d - _dPrev) * Mathf.Exp(-DampSide * dt) + gs * dt * dt;
        // slide (along the arm): kinetic / static friction against the sideways pull
        float vz = (_z - _zPrev) / dt, fr = Mu * gs.magnitude;
        float az = Mathf.Abs(vz) < 0.01f && Mathf.Abs(ga) <= fr ? -vz / dt : ga - fr * Mathf.Sign(Mathf.Abs(vz) > 0.01f ? vz : ga);
        float zn = _z + vz * dt * Mathf.Exp(-DampAxial * dt) + az * dt * dt;
        bool hit = zn <= ZMin || zn >= ZMax; zn = Mathf.Clamp(zn, ZMin, ZMax);
        if (!Fit(arm, zn, ref dn))
        {
            // no room at the new place: stop sliding, but still hang/swing (best fit at the old place). 0.6.1 reset the hang too,
            // so one unfittable point froze the bracelet completely
            _fitFails++; zn = _z; hit = true; Fit(arm, zn, ref dn);
        }
        _zPrev = hit ? zn : _z; _z = zn; _dPrev = _d; _d = dn;

        // tilt: the loop's low side swings toward the low end of the arm, up to what the clearance allows
        // tilt room from the arm's own centre (lab: measured from the hanging loop's centre it was always 0 = never tilted)
        var sec2 = WristFit.Section(arm, Lo + _z, Hi + _z); var cen2 = Centroid(sec2); float far = 0f; foreach (var q in sec2) far = Math.Max(far, (q - cen2).magnitude);
        float maxTilt = Mathf.Acos(Mathf.Clamp01(far / RingR)) * Mathf.Rad2Deg * 0.8f;
        var down3 = (O.e1 * gs.x + O.e2 * gs.y); if (down3.sqrMagnitude > 1e-6f) _tiltAxis = Vector3.Cross(down3.normalized, O.ax).normalized;
        float target = maxTilt * Mathf.Clamp(ga / Mathf.Max(g.magnitude, 0.1f) * 1.6f, -1f, 1f);
        _tiltV += ((target - _tilt) * 40f - _tiltV * 5f) * dt; _tilt += _tiltV * dt; _tilt = Mathf.Clamp(_tilt, -maxTilt, maxTilt);

        // spin: lags behind the forearm's own twist, springs back (beads rolling round the arm)
        var dq = bone.rotation * Quaternion.Inverse(_lastRot); _lastRot = bone.rotation;
        dq.ToAngleAxis(out float ang, out var axW); if (ang > 180f) ang -= 360f;
        float twist = ang * Vector3.Dot(axW, bone.TransformDirection(O.ax).normalized);
        if (Mathf.Abs(twist) < 45f) _spin -= twist * 0.7f;
        _spinV += (-_spin * 6f - _spinV * 2.5f) * dt; _spin += _spinV * dt; _spin = Mathf.Clamp(_spin, -120f, 120f);

        // pose: rotate about the loop's own centre, then offset
        var c0 = O.w + O.ax * ((Lo + Hi) / 2f);
        var rot = Quaternion.AngleAxis(_tilt, _tiltAxis) * Quaternion.AngleAxis(_spin, O.ax);
        transform.localRotation = rot;
        transform.localPosition = c0 + O.ax * _z + O.e1 * _d.x + O.e2 * _d.y - rot * c0;

        _frames++;
        if (now >= _logAt) { _logAt = now + 15f; Plugin.Log.LogInfo($"[WristAccessories] physics {Name}: fit failed {_fitFails}/{_frames} frames, {_z * 1000:0} mm along the arm, hangs {(_d - Centroid(sec2)).magnitude * 1000:0} mm off the arm's centre toward ({_d.x * 1000:0},{_d.y * 1000:0}), tilt {_tilt:0} deg (max {maxTilt:0}), spin {_spin:0} deg, pull along/across {ga:0.0}/{gs.magnitude:0.0} m/s2"); _fitFails = _frames = 0; }
    }

    static Vector2 Centroid(Vector2[] s) { var c = Vector2.zero; foreach (var q in s) c += q; return c / s.Length; }

    // push the loop centre d so every arm point at this place is inside the loop; false = it cannot fit here
    bool Fit(WristFit.Outline arm, float z, ref Vector2 d)
    {
        var sec = WristFit.Section(arm, Lo + z, Hi + z); float worst = 0;
        for (int it = 0; it < 8; it++)
        {
            worst = 0;
            foreach (var p in sec)
            {
                var diff = p - d; float dist = diff.magnitude;
                if (dist > RingR) { d += diff / dist * (dist - RingR); worst = Math.Max(worst, dist - RingR); }
            }
            if (worst < 0.0002f) return true;
        }
        return worst < 0.002f;
    }

    /// <summary>Vertices drawn with a visible material (null = all): the outfit watch the plugin hides (material
    /// WristAcc_invisible / *_hidden) must not count as arm.</summary>
    static Vector3[] _pts = new Vector3[0];
    static readonly Dictionary<Mesh, int[]> _tris = new Dictionary<Mesh, int[]>();

    /// <summary>A caller's surface-bake state (LiveFit): candidate triangles per mesh (clear to rebuild) and one table per
    /// third of them - each bake samples the next third, the arm is the max of the three (cost spread, <= 0.3 s stale).</summary>
    public class Surface { public const int N = 3; public readonly Dictionary<Mesh, List<int>> Cands = new Dictionary<Mesh, List<int>>(); public float[][] Slices; public int Next; }

    // one point (bone space, relative to the outline's wrist point) into a table: true when it raised a cell
    static bool Hit(float[] tab, WristFit.Outline O, Vector3 p, float aLo, float aHi)
    {
        float al = Vector3.Dot(p, O.ax); if (al < aLo || al > aHi) return false;
        var perp = p - O.ax * al; float rad = perp.magnitude; if (rad > 0.12f) return false;
        int j = Mathf.FloorToInt((al - O.a0) / O.da); if (j < 0 || j >= O.na) return false;
        int k = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(Vector3.Dot(perp, O.e2), Vector3.Dot(perp, O.e1)), 2 * Mathf.PI) / (2 * Mathf.PI / O.nd)) % O.nd;
        int i = j * O.nd + k; if (rad > tab[i]) { tab[i] = rad; return true; }
        return false;
    }

    public static bool[] VisibleVerts(SkinnedMeshRenderer smr, Mesh baked)
    {
        var mats = smr.sharedMaterials; bool anyHidden = false;
        foreach (var m in mats) if (m != null && (m.name == "WristAcc_invisible" || m.name.EndsWith("_hidden"))) anyHidden = true;
        if (!anyHidden) return null;
        var vis = new bool[baked.vertexCount];
        for (int s = 0; s < baked.subMeshCount && s < mats.Length; s++)
        {
            var m = mats[s]; if (m != null && (m.name == "WristAcc_invisible" || m.name.EndsWith("_hidden"))) continue;
            foreach (var i in baked.GetTriangles(s)) vis[i] = true;
        }
        return vis;
    }


    /// <summary>The arm as the game draws it now: hands mesh baked, outermost radius per outline cell (per 2 mm along the arm
    /// between aLo..aHi, per direction; bone space). Cells the bake missed: the neighbours' value, else the outline's. Null when
    /// the hands are hidden (fewer than 50 cells hit). surface: also the triangles between the vertices (LiveFit: first-person
    /// forearms have a vertex only every ~30 deg round the arm, so a twisted sleeve rising between them went unseen - lab
    /// 2026-10-26: half of the kill sleeve's twist clips).</summary>
    public static WristFit.Outline BakeArm(LoddedSkin hands, Transform bone, WristFit.Outline O, float aLo, float aHi, ref Mesh baked, out int n, Surface surf = null)
    {
        n = 0;
        if (baked == null) baked = new Mesh();
        var live = new WristFit.Outline { w = O.w, ax = O.ax, e1 = O.e1, e2 = O.e2, a0 = O.a0, da = O.da, na = O.na, nd = O.nd, start = O.start, tab = new float[O.tab.Length] };
        int slice = 0; float[] stab = null;
        if (surf != null)
        {
            if (surf.Slices == null || surf.Slices[0].Length != O.tab.Length) surf.Slices = new[] { new float[O.tab.Length], new float[O.tab.Length], new float[O.tab.Length] };
            slice = surf.Next; surf.Next = (surf.Next + 1) % Surface.N; stab = surf.Slices[slice]; Array.Clear(stab, 0, stab.Length);
        }
        foreach (var r in hands.GetRenderers())
        {
            if (!(r is SkinnedMeshRenderer smr) || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;
            smr.BakeMesh(baked, true); var M = bone.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            var vis = VisibleVerts(smr, baked); var verts = baked.vertices;
            if (_pts.Length < verts.Length) _pts = new Vector3[verts.Length];
            for (int vi = 0; vi < verts.Length; vi++)
            {
                var p = _pts[vi] = M.MultiplyPoint3x4(verts[vi]) - O.w;
                if (vis != null && !vis[vi]) continue;                                                 // the hidden outfit watch
                if (Hit(live.tab, O, p, aLo, aHi)) n++;
            }
            if (surf == null || smr.sharedMesh == null) continue;
            if (!_tris.TryGetValue(smr.sharedMesh, out var tris)) _tris[smr.sharedMesh] = tris = smr.sharedMesh.triangles;   // topology never changes
            // the caller's candidate list (triangles near its range, refreshed by the caller every few seconds): empty = rebuild
            // from every triangle now (30 mm margin: a bent wrist moves the hand end, a twist turns the sleeve round its axis)
            if (!surf.Cands.TryGetValue(smr.sharedMesh, out var cand)) surf.Cands[smr.sharedMesh] = cand = new List<int>();
            bool fill = cand.Count == 0;
            int nt = fill ? tris.Length / 3 : cand.Count;
            for (int c = 0; c < nt; c++)
            {
                int t = fill ? c * 3 : cand[c];
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                if (i0 >= verts.Length || i1 >= verts.Length || i2 >= verts.Length) continue;
                Vector3 P0 = _pts[i0], P1 = _pts[i1], P2 = _pts[i2];
                float l01 = (P1 - P0).magnitude, l12 = (P2 - P1).magnitude, l20 = (P0 - P2).magnitude;
                if (l01 <= 0.006f && l12 <= 0.006f && l20 <= 0.006f) continue;          // small triangles: their corners already cover the cells
                float a0 = Vector3.Dot(P0, O.ax), a1 = Vector3.Dot(P1, O.ax), a2 = Vector3.Dot(P2, O.ax);
                if (fill) { if ((a0 < aLo - 0.03f && a1 < aLo - 0.03f && a2 < aLo - 0.03f) || (a0 > aHi + 0.03f && a1 > aHi + 0.03f && a2 > aHi + 0.03f)) continue; cand.Add(t); }
                if (((fill ? cand.Count - 1 : c) % Surface.N) != slice) continue;      // this bake's third of the candidates
                if (vis != null && !(vis[i0] && vis[i1] && vis[i2])) continue;
                if ((a0 < aLo && a1 < aLo && a2 < aLo) || (a0 > aHi && a1 > aHi && a2 > aHi)) continue;
                // samples ~4 mm apart (a cell is 2 mm x 5 deg = 3.5 mm at the wrist), stepped along the longest edge and the shortest
                // from the corner they share: a long thin forearm triangle gets a row, not a grid. Corners were done above
                Vector3 A, B, C;                                                       // A = shared corner, B = far end of the longest edge, C = of the shortest
                if (l01 >= l12 && l01 >= l20) { if (l12 <= l20) { A = P1; B = P0; C = P2; } else { A = P0; B = P1; C = P2; } }
                else if (l12 >= l20) { if (l01 <= l20) { A = P1; B = P2; C = P0; } else { A = P2; B = P1; C = P0; } }
                else { if (l01 <= l12) { A = P0; B = P2; C = P1; } else { A = P2; B = P0; C = P1; } }
                int nu = Mathf.Clamp(Mathf.CeilToInt((B - A).magnitude / 0.004f), 1, 16), nv = Mathf.Clamp(Mathf.CeilToInt((C - A).magnitude / 0.004f), 1, 16);
                for (int u = 0; u <= nu; u++)
                    for (int v = 0; v <= nv; v++)
                    {
                        float fu = (float)u / nu, fv = (float)v / nv;
                        if (fu + fv > 1.0001f || (u == 0 && v == 0) || (u == nu && v == 0) || (u == 0 && v == nv)) continue;
                        Hit(stab, O, A + (B - A) * fu + (C - A) * fv, aLo, aHi);
                    }
            }
        }
        if (n < 50) return null;
        // directions the bake missed (sparse mesh, ~4 mm cells): the raw neighbours' value, never above the outline, else the
        // outline's. 0.6.9 filled in place, so one value ran on through every empty cell after it (lab: up to 12 mm over the
        // arm at rest -> LiveFit pushed bracelets off a plain arm)
        var raw = (float[])live.tab.Clone();
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] > 0) continue;
            int j = i / O.nd, k = i % O.nd; float nb = Math.Max(raw[j * O.nd + (k + 1) % O.nd], raw[j * O.nd + (k + O.nd - 1) % O.nd]);
            live.tab[i] = nb > 0 ? Math.Min(nb, O.tab[i]) : O.tab[i];
        }
        if (surf != null)                                                                            // the triangles between the vertices: only raise
            foreach (var st in surf.Slices) for (int i = 0; i < st.Length && i < live.tab.Length; i++) if (st[i] > live.tab[i]) live.tab[i] = st[i];
        return live;
    }

    // the arm as the game draws it now (BakeArm) + clearance, stray points dropped, eased between bakes
    void BakeLive(Transform bone, float now)
    {
        try
        {
            if (Hands == null) return;
            var live = BakeArm(Hands, bone, O, Lo + ZMin - 0.006f, Hi + ZMax + 0.006f, ref _baked, out int n);
            if (live == null) { _live = null; return; }                                                // hands hidden: keep the outline
            int outl = 0;
            for (int i = 0; i < live.tab.Length; i++)
            {
                live.tab[i] += 0.002f;                                                                     // clearance
                if (live.tab[i] > O.tab[i] + 0.012f) { live.tab[i] = O.tab[i]; outl++; }                 // stray points (thumb, folds)
            }
            if (now >= _outlLogAt) { _outlLogAt = now + 30f; Plugin.Log.LogInfo($"[WristAccessories] physics {Name}: live arm {n} cells, {outl} outliers ignored"); }
            if (_live != null) for (int i = 0; i < live.tab.Length; i++) live.tab[i] = Mathf.Lerp(_live.tab[i], live.tab[i], 0.6f);   // no snapping between bakes
            _live = live;
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] physics live arm: " + e.Message); _live = null; }
    }
}
}
