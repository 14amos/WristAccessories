using UnityEngine;

namespace WristAccessories;

// A dogtag on a kill sleeve hanging from its ring end (user: "make the dogtags move and hang a little"). Purely visual and
// local. Two hinges at the ring: LIFT (the free end comes off the sleeve, 0..MaxLift - never into it) and SWAY (sideways along
// the sleeve, +-MaxSway). Driven by gravity minus the arm's (filtered) acceleration in the accessory's frame (= the forearm
// bone's): torque of a plate pivoting at one end, alpha = 3/(2L) * (dir x g) on the hinge axis; light damping and a gentle
// spring back to lying flat. A tag on top of the arm stays flat, one on the side / underside hangs and sways.
class TagSwing : MonoBehaviour
{
    public Vector3 B0, U, LiftAxis; public float Len; public bool ManualStep;
    float _lift, _liftV, _sway, _swayV; Vector3 _lastPos, _lastVel, _accF; bool _init;
    // Rest = the chain holding the tag to the sleeve: full gravity gives ~25 deg (lab: rest 5 = every tag pinned at the 55 deg limit)
    const float MaxLift = 40f, MaxSway = 30f, Damp = 18f, Rest = 800f, AccTau = 0.07f;

    /// <summary>Turn a placed tag mesh into a hanging one: vertices re-pivoted on its ring end (b.swing[0]), the child moved
    /// there; tip = b.swing[1]; outward = away from the arm axis at the ring.</summary>
    public static TagSwing Attach(GameObject child, Mesh mesh, WristFit.Built b, WristFit.Outline o)
    {
        Vector3 piv = b.pos[b.swing[0]], tip = b.pos[b.swing[1]];
        var v = mesh.vertices; for (int i = 0; i < v.Length; i++) v[i] -= piv; mesh.vertices = v; mesh.RecalculateBounds();
        child.transform.localPosition = piv;
        var rel = piv - o.w; var u = (rel - o.ax * Vector3.Dot(rel, o.ax)).normalized;
        var b0 = tip - piv; float len = b0.magnitude; b0 /= Mathf.Max(len, 1e-4f);
        var ts = child.AddComponent<TagSwing>();
        ts.B0 = b0; ts.U = u; ts.LiftAxis = Vector3.Cross(b0, u).normalized; ts.Len = Mathf.Max(len, 0.01f);
        return ts;
    }

    public float LiftDeg => _lift; public float SwayDeg => _sway;

    void LateUpdate() { if (!ManualStep) Step(Time.deltaTime); }

    public void Step(float rawDt)
    {
        var frame = transform.parent;
        if (frame == null || !Plugin.BraceletPhysicsOn.Value) { transform.localRotation = Quaternion.identity; _init = false; return; }
        float dt = Mathf.Clamp(rawDt, 1e-4f, 0.05f);
        if (!_init) { _lastPos = frame.position; _lastVel = Vector3.zero; _accF = Vector3.zero; _lift = _sway = _liftV = _swayV = 0f; _init = true; }
        var vel = (frame.position - _lastPos) / dt; var acc = (vel - _lastVel) / dt; _lastPos = frame.position; _lastVel = vel;
        if (acc.magnitude > 60f) acc = Vector3.zero;
        _accF = Vector3.Lerp(_accF, acc, 1f - Mathf.Exp(-dt / AccTau));
        var g = frame.worldToLocalMatrix.MultiplyVector((new Vector3(0f, -9.81f, 0f) - Vector3.ClampMagnitude(_accF, 25f)) * Plugin.PhysicsStrength.Value);

        var swayRot = Quaternion.AngleAxis(_sway, U);
        var dir = swayRot * Quaternion.AngleAxis(_lift, LiftAxis) * B0; var liftAx = swayRot * LiftAxis;
        var tq = Vector3.Cross(dir, g); float k = 1.5f / Len * Mathf.Rad2Deg;
        _liftV += (Vector3.Dot(tq, liftAx) * k - Rest * _lift - Damp * _liftV) * dt;
        _swayV += (Vector3.Dot(tq, U) * k - Rest * _sway - Damp * _swayV) * dt;
        _lift += _liftV * dt; _sway += _swayV * dt;
        if (_lift < 0f) { _lift = 0f; if (_liftV < 0f) _liftV = 0f; } else if (_lift > MaxLift) { _lift = MaxLift; if (_liftV > 0f) _liftV = 0f; }
        if (Mathf.Abs(_sway) > MaxSway) { _sway = Mathf.Sign(_sway) * MaxSway; _swayV = 0f; }
        transform.localRotation = Quaternion.AngleAxis(_sway, U) * Quaternion.AngleAxis(_lift, LiftAxis);
    }
}
