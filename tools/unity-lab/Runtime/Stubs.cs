// Bead lab stubs: the few game / BepInEx things WristFit.cs and BraceletPhysics.cs use, so the plugin's own code runs here.
using System.Collections.Generic;
using UnityEngine;

namespace WristAccessories
{
    public class LabLog { public void LogInfo(object o) => Debug.Log(o); public void LogWarning(object o) => Debug.LogWarning(o); public void LogError(object o) => Debug.LogError(o); }
    public class LabCfg<T> { public T Value; public LabCfg(T v) { Value = v; } }
    static class Plugin
    {
        internal static LabLog Log = new LabLog();
        internal static LabCfg<bool> BraceletPhysicsOn = new LabCfg<bool>(true), LiveSleeveFit = new LabCfg<bool>(false), MatchSkinTone = new LabCfg<bool>(false), SeamBlend = new LabCfg<bool>(true);
        internal static LabCfg<float> SkinShade = new LabCfg<float>(0f);
        internal static LabCfg<float> PhysicsStrength = new LabCfg<float>(1f);
    }
}

namespace EFT.Visual
{
    public class LoddedSkin : MonoBehaviour { public Renderer[] R; public IEnumerable<Renderer> GetRenderers() => R; }
}

#if UNITY_EDITOR
namespace BepInEx { static class Paths { public static string GameRootPath = "SPT"; } }
#endif

// the plugin's Fallback (missing-material fix) is not needed in the lab: materials as given
namespace WristAccessories { static class Fallback { public static UnityEngine.Material[] Fix(UnityEngine.Material[] m, int n, string what) => m; } }
