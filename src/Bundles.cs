using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WristAccessories;

// Is an item's bundle there? (user 2026-10-27: "make sure it doesn't miss any textures"): 122 main items come from WTT-ContentBackport
// outfits - without that mod they would draw with no material. A bundle is there when the game has it (StreamingAssets/Windows/<key>)
// or SPT's server offers it: SPT.Custom.Utils.BundleManager.Bundles, the mod bundle manifest every SPT client (host or Fika
// friend) downloads at start. The WARDROBE lists show only items whose bundle is there.
static class Bundles
{
    static readonly Dictionary<string, bool> Seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    static IDictionary _spt; static bool _looked;

    public static bool Has(string key)
    {
        if (string.IsNullOrEmpty(key)) return true;
        if (Seen.TryGetValue(key, out bool b)) return b;
        b = File.Exists(Path.Combine(Application.streamingAssetsPath, "Windows", key));
        if (!b)
        {
            if (!_looked)
            {
                _looked = true;
                try { _spt = Type.GetType("SPT.Custom.Utils.BundleManager, spt-custom")?.GetField("Bundles")?.GetValue(null) as IDictionary; }
                catch (Exception e) { Plugin.Log.LogWarning($"[WristAccessories] bundles: SPT's bundle list not readable: {e.Message}"); }
                Plugin.Log.LogInfo($"[WristAccessories] bundles: SPT's mod bundle list {(_spt == null ? "NOT found - mod items all shown" : $"has {_spt.Count} bundles")}");
            }
            if (_spt == null) return true;                                   // can't tell: show it (as before)
            if (_spt.Count == 0) return true;                                // not downloaded yet: ask again later
            b = _spt.Contains(key);
        }
        Seen[key] = b;
        if (!b) Plugin.Log.LogInfo($"[WristAccessories] bundles: {key} is not installed - its items are left out of the lists");
        return b;
    }
}
