using System;
using System.Collections.Generic;
using UnityEngine;
using EFT.Visual;

namespace WristAccessories {
// A long item (kill sleeve, ~21 cm up the forearm) follows the sleeve as the game draws it (user shot 2026-10-25: strands
// and board edges sinking into the BD outfit's sleeve in first person). Items are placed on the outline = the arm at rest +
// "Sleeve fit" x the posed bulge; over that length the posed first-person sleeve folds out further. Every 0.2 s the hands
// are baked (BraceletPhysics.BakeArm) and every vertex goes out along its own radial direction by however far the live
// sleeve under it rose above the outline it was placed on - never inward. Rigid pieces move as one (their biggest push),
// hanging tags (TagSwing children) by their ring. Purely visual and local; forearm bone frame like the meshes.
class LiveFit : MonoBehaviour
{
    public WristFit.Outline O; public LoddedSkin Hands; public string Name; public float Lo, Hi;
    /// <summary>A watch's moving hands (a separate prefab under the item): they follow the rigid piece DialPid.</summary>
    public Transform Dial; public int DialPid = -1;
    Vector3 _dialBase; readonly Dictionary<int, Vector3> _grpDir = new Dictionary<int, Vector3>(); float _dialPush;
    /// <summary>Set by the lab (Unity editor test scene): it calls Step itself with its own clock.</summary>
    public bool ManualStep;
    // bake 10x/s; between bakes every frame eases toward the newest target: out fast (no clipping), back in slowly (no jitter)
    const float Every = 0.1f, TauOut = 0.03f, TauIn = 0.25f, Margin = 0.0005f, Stray = 0.012f;

    class Part
    {
        public Mesh mesh; public Transform tf; public bool tag;
        public Vector3[] basePos, cur, u; public float[] armO, push, target; public int[] j0, j1, k, grp;
        public Vector3 basePiv;
    }
    readonly List<Part> _parts = new List<Part>();
    bool _init, _off; float _bakeAt, _logAt, _candAt; Mesh _baked;
    readonly BraceletPhysics.Surface _surf = new BraceletPhysics.Surface();   // surface bake: arm triangles near this item (rebuilt every 2 s), 3 slices

    void OnDestroy() { if (_baked) Destroy(_baked); }

    void Init()
    {
        _init = true; int verts = 0, tags = 0;
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || mf.transform.parent != transform) continue;
            var p = new Part { mesh = mf.sharedMesh, tf = mf.transform, tag = mf.GetComponent<TagSwing>() != null };
            if (p.tag)
            {
                p.basePiv = p.tf.localPosition; Cell(p, new[] { p.basePiv }); tags++;
            }
            else
            {
                p.basePos = p.mesh.vertices; p.cur = (Vector3[])p.basePos.Clone(); Cell(p, p.basePos); verts += p.basePos.Length;
                var b = Src.TryGetValue(mf.sharedMesh, out var bb) ? bb : null;
                p.grp = new int[p.basePos.Length];
                for (int i = 0; i < p.grp.Length; i++) p.grp[i] = b?.kind != null && b.pid != null && i < b.kind.Length && b.kind[i] > 0 ? b.pid[i] : -1;   // -1 = own vertex
            }
            _parts.Add(p);
        }
        // rigid pieces (a watch head, a case and its glass in two meshes) move as one: along their mean outward direction
        foreach (var p in _parts)
            if (!p.tag) for (int i = 0; i < p.grp.Length; i++) if (p.grp[i] >= 0) _grpDir[p.grp[i]] = (_grpDir.TryGetValue(p.grp[i], out var d) ? d : Vector3.zero) + p.u[i];
        foreach (var k in new List<int>(_grpDir.Keys)) _grpDir[k] = _grpDir[k].sqrMagnitude > 1e-12f ? _grpDir[k].normalized : O.e1;
        foreach (var p in _parts)
            if (!p.tag) for (int i = 0; i < p.grp.Length; i++) if (p.grp[i] >= 0) p.u[i] = _grpDir[p.grp[i]];
        if (Dial != null) _dialBase = Dial.localPosition;
        Plugin.Log.LogInfo($"[WristAccessories] live fit on {Name}: {_parts.Count} mesh(es), {verts} verts, {tags} hanging tags, {_grpDir.Count} rigid piece(s), moving hands {(Dial != null && _grpDir.ContainsKey(DialPid) ? "follow piece " + DialPid : "none")}, {Lo * 1000:0}..{Hi * 1000:0} mm");
    }

    // per point: its outline cells (direction +-1, +-4 mm along the arm, like WristFit.Place) and the outline's radius there
    void Cell(Part p, Vector3[] pts)
    {
        int n = pts.Length; p.j0 = new int[n]; p.j1 = new int[n]; p.k = new int[n]; p.u = new Vector3[n]; p.armO = new float[n]; p.push = new float[n]; p.target = new float[n];
        for (int i = 0; i < n; i++)
        {
            var rel = pts[i] - O.w; float al = Vector3.Dot(rel, O.ax); var perp = rel - O.ax * al;
            p.u[i] = perp.sqrMagnitude > 1e-10f ? perp.normalized : O.e1;
            p.j0[i] = Mathf.Clamp(Mathf.FloorToInt((al - 0.004f - O.a0) / O.da), 0, O.na - 1);
            p.j1[i] = Mathf.Clamp(Mathf.FloorToInt((al + 0.004f - O.a0) / O.da), 0, O.na - 1);
            p.k[i] = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(Vector3.Dot(perp, O.e2), Vector3.Dot(perp, O.e1)), 2 * Mathf.PI) / (2 * Mathf.PI / O.nd)) % O.nd;
            p.armO[i] = Max(O.tab, p, i);
        }
    }

    float Max(float[] tab, Part p, int i)
    {
        float m = 0;
        for (int j = p.j0[i]; j <= p.j1[i]; j++)
            for (int d = -1; d <= 1; d++) m = Math.Max(m, tab[j * O.nd + (p.k[i] + d + O.nd) % O.nd]);
        return m;
    }

    /// <summary>The placed meshes' source data (pid / kind for rigid pieces), filled by WristRig.</summary>
    public readonly Dictionary<Mesh, WristFit.Built> Src = new Dictionary<Mesh, WristFit.Built>();

    void LateUpdate() { if (!ManualStep) Step(Time.time, Time.deltaTime); }

    public void Step(float now, float dt)
    {
        if (O == null || Hands == null) return;
        var bone = transform.parent; if (bone == null) return;
        if (!_init) Init();
        if (!Plugin.LiveSleeveFit.Value) { if (!_off) { Targets(null); Ease(-1f); _off = true; } return; }
        _off = false;
        if (now >= _bakeAt) { _bakeAt = now + Every; Bake(bone, now); }
        Ease(dt);
    }

    void Bake(Transform bone, float now)
    {
        try
        {
            if (now >= _candAt) { _candAt = now + 2f; _surf.Cands.Clear(); }
            var live = BraceletPhysics.BakeArm(Hands, bone, O, Lo - 0.006f, Hi + 0.006f, ref _baked, out _, _surf);
            int outl = 0, bigKept = 0; float kept = 0;
            if (live != null)
                for (int i = 0; i < live.tab.Length; i++)
                {
                    float ex = live.tab[i] - O.tab[i];
                    // stray points (thumb, palm) only in the 30 mm at the hand end: further up a big rise is the sleeve itself (lab
                    // 2026-10-26: a twisted forearm lifts the kill sleeve's elbow end > 12 mm over the outline; dropping it clipped up to 20 mm)
                    if (ex > Stray && O.a0 + (i / O.nd + 0.5f) * O.da > Hi - 0.03f) { live.tab[i] = O.tab[i]; outl++; }
                    else { kept = Math.Max(kept, ex); if (ex > Stray) bigKept++; }
                }
            Targets(live);
            if (now >= _logAt)
            {
                _logAt = now + 15f; int pushed = 0, total = 0, tp = 0, tags = 0; float mx = 0;
                foreach (var p in _parts)
                    for (int i = 0; i < p.push.Length; i++)
                    {
                        if (p.tag) { tags++; if (p.target[i] > 0.0002f) tp++; } else { total++; if (p.target[i] > 0.0002f) pushed++; }
                        mx = Math.Max(mx, p.target[i]);
                    }
                Plugin.Log.LogInfo($"[WristAccessories] live fit {Name}: {pushed}/{total} verts going out, {tp}/{tags} tags going out, max {mx * 1000:0.0} mm, live arm {(live != null ? "yes" : "no (hidden)")}, {outl} stray cells (> {Stray * 1000:0} mm over the outline, 30 mm at the hand end) ignored, {bigKept} such rises further up kept, biggest bulge kept {kept * 1000:0.0} mm (surface bake)");
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"[WristAccessories] live fit {Name}: {e.Message}"); }
    }

    // where every vertex should be: out by the live sleeve's rise over the outline under it (live == null: placed positions)
    void Targets(WristFit.Outline live)
    {
        var grpMax = new Dictionary<int, float>();
        foreach (var p in _parts)
        {
            int n = p.push.Length;
            for (int i = 0; i < n; i++) { float d = live == null ? 0 : Max(live.tab, p, i) - p.armO[i]; p.target[i] = d > 0 ? d + Margin * Mathf.Clamp01(d / 0.001f) : 0; }   // margin ramps in: no 0.5 mm jumps from noise
            if (p.tag) continue;
            grpMax.Clear();
            for (int i = 0; i < n; i++) if (p.grp[i] >= 0) grpMax[p.grp[i]] = Math.Max(grpMax.TryGetValue(p.grp[i], out float g) ? g : 0, p.target[i]);
            for (int i = 0; i < n; i++) if (p.grp[i] >= 0) p.target[i] = grpMax[p.grp[i]];
        }
    }

    // dt < 0: jump straight to the targets
    void Ease(float dt)
    {
        float aOut = dt < 0 ? 1 : 1 - Mathf.Exp(-dt / TauOut), aIn = dt < 0 ? 1 : 1 - Mathf.Exp(-dt / TauIn);
        foreach (var p in _parts)
        {
            int n = p.push.Length; bool moved = false;
            for (int i = 0; i < n; i++)
            {
                float d = p.target[i] - p.push[i]; if (d == 0) continue;
                float v = Math.Abs(d) < 2e-5f ? p.target[i] : p.push[i] + d * (d > 0 ? aOut : aIn);
                if (Math.Abs(v - p.push[i]) > 1e-5f || v == p.target[i]) moved = true;
                p.push[i] = v;
            }
            if (!moved) continue;
            if (p.tag) p.tf.localPosition = p.basePiv + p.u[0] * p.push[0];
            else
            {
                for (int i = 0; i < n; i++) p.cur[i] = p.basePos[i] + p.u[i] * p.push[i];
                p.mesh.vertices = p.cur; p.mesh.RecalculateBounds();
                if (Dial != null && _grpDir.TryGetValue(DialPid, out var dd))
                    for (int i = 0; i < n; i++) if (p.grp[i] == DialPid) { _dialPush = p.push[i]; Dial.localPosition = _dialBase + dd * _dialPush; break; }
            }
        }
    }
}
}
