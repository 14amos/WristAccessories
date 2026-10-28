using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using UnityEngine;

namespace WristAccessories;

// Manimal-WatchAnims (soft dependency, reached by reflection only): it plays a "look at the watch" clip on the timer keys,
// watch_bear (watch on the inside of the wrist) or watch_usec (on the back of the wrist), picked from its own list of
// suit ids. (1) Our fitted watch decides the clip instead: its face direction around the arm (0 = back of the hand; the
// game's own outfits agree: USEC Hawaii's GMT at +30 deg -> usec clip, BEAR Hawaii's Montana at -140 deg -> bear clip).
// (2) The hideout: his patch (GamePlayerOwner.TranslateCommand) never runs there - HideoutPlayerOwner returns early for the
// timer keys, and in game (0.5.0 test) the command did not even reach HideoutPlayerOwner (another input node takes it). So we
// run his handler where the command enters the input tree (InputNodeAbstract.TranslateInput, before any node can drop it),
// once per frame, only while the main player is the hideout player.
static class WatchAnimsCompat
{
    const string Guid = "com.manimal.watchanims";
    static MethodInfo _handler;

    public static void TryPatch(Harmony h)
    {
        if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(Guid, out var info) || info.Instance == null)
        { Plugin.Log.LogInfo("[WristAccessories] Manimal-WatchAnims not loaded: no watch-animation hookup"); return; }
        var t = info.Instance.GetType().Assembly.GetType("Manimal.WatchAnims.WatchAnimPatch");
        var pick = t != null ? AccessTools.Method(t, "GetAnimIndex") : null;
        _handler = t != null ? AccessTools.Method(t, "Postfix") : null;
        if (pick != null) h.Patch(pick, prefix: new HarmonyMethod(typeof(WatchAnimsCompat), nameof(GetAnimIndexPrefix)));
        var hideout = AccessTools.Method(typeof(EFT.InputSystem.InputNodeAbstract), nameof(EFT.InputSystem.InputNodeAbstract.TranslateInput));
        if (_handler != null && hideout != null) h.Patch(hideout, prefix: new HarmonyMethod(typeof(WatchAnimsCompat), nameof(TranslateInputPrefix)));
        Plugin.Log.LogInfo($"[WristAccessories] Manimal-WatchAnims {info.Metadata.Version}: clip pick {(pick != null ? "hooked" : "NOT FOUND")}, hideout keys {(_handler != null && hideout != null ? "hooked" : "NOT FOUND")}");
    }

    static bool GetAnimIndexPrefix(Player player, ref int __result)
    {
        var face = WristRig.MainWatchFace;
        if (player == null || !player.IsYourPlayer || face == null)
        {
            Plugin.Log.LogInfo("[WristAccessories] watch animation: no fitted watch on the left wrist -> Manimal's own pick (by suit)");
            return true;
        }
        float deg = Mathf.DeltaAngle(0f, face.Value * Mathf.Rad2Deg);
        __result = Mathf.Abs(deg) < 90f ? 1 : 0;
        Plugin.Log.LogInfo($"[WristAccessories] watch animation: {WristRig.MainWatchName} faces {deg:0} deg from the back of the hand -> {(__result == 1 ? "watch_usec (back of the wrist)" : "watch_bear (inside of the wrist)")}");
        return false;
    }

    static int _firedFrame = -1, _diagPresses;
    static readonly List<string> _diagNodes = new List<string>();

    static void TranslateInputPrefix(EFT.InputSystem.InputNodeAbstract __instance, List<ECommand> commands)
    {
        if (commands == null || commands.Count == 0) return;
        ECommand cmd = commands.Contains(ECommand.DisplayTimer) ? ECommand.DisplayTimer : commands.Contains(ECommand.DisplayTimerAndExits) ? ECommand.DisplayTimerAndExits : 0;
        if (cmd == 0) return;
        int frame = Time.frameCount;
        if (frame != _firedFrame) _diagNodes.Clear();
        if (_diagPresses <= 3) _diagNodes.Add(__instance.GetType().Name);          // which nodes see it (first presses only)
        if (frame == _firedFrame) return;
        _firedFrame = frame;
        var gw = Comfort.Common.Singleton<GameWorld>.Instantiated ? Comfort.Common.Singleton<GameWorld>.Instance : null;
        bool hideout = gw?.MainPlayer is HideoutPlayer;
        if (_diagPresses++ < 3) StaticManagerLog(cmd, hideout);
        WristRig.RunLiveChecks(cmd.ToString());                                    // the arm in its real pose right now (diagnostic)
        if (!hideout) return;
        try
        {
            Plugin.Log.LogInfo($"[WristAccessories] hideout {cmd}: running Manimal-WatchAnims");
            _handler.Invoke(null, new object[] { cmd });
        }
        catch (Exception e) { Plugin.Log.LogError("[WristAccessories] hideout watch animation: " + (e.InnerException ?? e)); }
    }

    // the input nodes that saw the key, logged a moment later (they are collected during the same frame)
    static async void StaticManagerLog(ECommand cmd, bool hideout)
    {
        await System.Threading.Tasks.Task.Yield();
        for (int i = 0; i < 3; i++) await System.Threading.Tasks.Task.Delay(50);
        Plugin.Log.LogInfo($"[WristAccessories] {cmd} key ({(hideout ? "hideout" : "not hideout")}) seen by input nodes: {string.Join(" > ", _diagNodes)}");
    }
}
