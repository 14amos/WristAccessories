// ShoeTest: the plugin's own HandWear.FitShoe (synced source) on real data, positions written out for lab/shoe_parity.py to
// compare with lab/feet_fit.py. Env SHOE_DIR (hw_shoes_*.json + shoe_profiles.json), SHOE_NAME, SHOE_PANTS (profile key), SHOE_OUT.
using System; using System.Collections.Generic; using System.IO; using System.Linq; using System.Reflection; using UnityEngine;
namespace WristAccessories
{
    public static class ShoeTest
    {
        public static void Run()
        {
            try
            {
                string dir = Environment.GetEnvironmentVariable("SHOE_DIR"), name = Environment.GetEnvironmentVariable("SHOE_NAME"), key = Environment.GetEnvironmentVariable("SHOE_PANTS"), outp = Environment.GetEnvironmentVariable("SHOE_OUT");
                HandWear.Load(dir);
                var it = HandWear.Items[name]; var d = HandWear.PantsData[key];
                var fit = typeof(HandWear).GetMethod("FitShoe", BindingFlags.NonPublic | BindingFlags.Static);
                var args = new object[] { it.pos, it.subs.SelectMany(x => x.tris).ToArray(), d.legs, new List<string>(), null, null };
                var P = (Vector3[])fit.Invoke(null, args);
                File.WriteAllLines(outp, new[] { "cut " + string.Join(",", ((float[])args[4] ?? new float[0]).Select(x => x.ToString("R"))) + " ; " + string.Join(",", ((float[])args[5] ?? new float[0]).Select(x => x.ToString("R"))), string.Join(" | ", (List<string>)args[3]) }.Concat(P.Select(v => $"{v.x:R} {v.y:R} {v.z:R}")));
                Debug.Log($"[ShoeTest] {name} on {key}: {string.Join(" | ", (List<string>)args[3])}");
            }
            catch (Exception e) { Debug.Log("[ShoeTest] FAILED " + e); }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#endif
        }
    }
}
