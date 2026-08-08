using System;
using UnityEngine;

namespace VSBossPlates;

/// <summary>
/// The two toggles you actually reach for mid-run: plates on or off, and mini-bosses in or out.
///
/// These only change what is drawn. The mod never alters the run, and there is nothing here
/// that a player would think of as a cheat.
///
/// Both write their new value back to the config file, so a toggle survives a restart. Pressing
/// a key to get rid of the plates and finding them back tomorrow would be its own small
/// annoyance.
/// </summary>
internal static class Hotkeys
{
    internal static void Update()
    {
        try
        {
            if (Pressed(Plugin.TogglePlatesKey))
            {
                Plugin.SetPlatesEnabled(!Plugin.Enabled);
                Plugin.Log.LogInfo("Boss plates " + (Plugin.Enabled ? "shown" : "hidden"));
            }

            if (Pressed(Plugin.ToggleMiniBossesKey))
            {
                Plugin.SetIncludeMiniBosses(!Plugin.IncludeMiniBosses);
                Plugin.Log.LogInfo(
                    "Mini-boss plates " + (Plugin.IncludeMiniBosses ? "shown" : "hidden") +
                    " (stage bosses are unaffected)");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Hotkey failed: " + ex.Message);
        }
    }

    /// <summary>KeyCode.None means the key was deliberately unbound, so never poll for it -
    /// Input.GetKeyDown(None) is meaningless rather than false.</summary>
    private static bool Pressed(KeyCode key)
    {
        return key != KeyCode.None && Input.GetKeyDown(key);
    }
}
