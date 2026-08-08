using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace VSBossPlates;

/// <summary>
/// Draws a health plate above every boss that is alive on screen: a fill bar, the boss name,
/// and current/max HP.
///
/// The game ships no boss health UI of any kind. Every health bar type in the game
/// (HealthBar, HealthBarUi) and its only overhead-icon type (OverheadIconGizmo) is typed to
/// CharacterController, which is the player. EnemyController is a different branch of the
/// hierarchy entirely, so none of it can be reused and the plate is built from scratch.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BasePlugin
{
    public const string PluginGuid = "com.n3rdyguy.vsbossplates";
    public const string PluginName = "VS Boss Plates";
    public const string PluginVersion = "0.1.0";

    internal static new ManualLogSource Log;

    // Config mirrors. Each key needs all four of: this field, the ConfigEntry below,
    // a Config.Bind call, and a line in ApplyConfigValues. Miss one and the key silently
    // keeps its default forever.
    internal static bool Enabled;
    internal static bool ShowName;
    internal static bool ShowNumbers;
    internal static bool HideWhenFull;
    internal static float VerticalOffset;
    internal static float PlateScale;
    internal static bool DebugVerbose;

    private ConfigEntry<bool> _enabled;
    private ConfigEntry<bool> _showName;
    private ConfigEntry<bool> _showNumbers;
    private ConfigEntry<bool> _hideWhenFull;
    private ConfigEntry<float> _verticalOffset;
    private ConfigEntry<float> _plateScale;
    private ConfigEntry<bool> _debugVerbose;

    public override void Load()
    {
        Log = base.Log;

        _enabled = Config.Bind(
            "Plates",
            "Enabled",
            true,
            "Draw health plates above bosses. Turning this off stops all per-frame work.");

        _showName = Config.Bind(
            "Plates",
            "ShowName",
            true,
            "Show the boss name above the bar.");

        _showNumbers = Config.Bind(
            "Plates",
            "ShowNumbers",
            true,
            "Show current and maximum HP on the bar.");

        _hideWhenFull = Config.Bind(
            "Plates",
            "HideWhenFull",
            false,
            "Only show a plate once the boss has taken damage, so an untouched boss is not " +
            "given away before you reach it.");

        _verticalOffset = Config.Bind(
            "Plates",
            "VerticalOffset",
            0.35f,
            new ConfigDescription(
                "Extra gap in world units between the top of the boss sprite and the plate. " +
                "The plate already sits above the sprite bounds, so this is a nudge, not the " +
                "whole distance.",
                new AcceptableValueRange<float>(-2f, 5f)));

        _plateScale = Config.Bind(
            "Plates",
            "PlateScale",
            0.012f,
            new ConfigDescription(
                "World units per plate unit. The plate is 200x56 units, so 0.012 draws it " +
                "about 2.4 world units wide. Raise it if the plate is hard to read.",
                new AcceptableValueRange<float>(0.002f, 0.06f)));

        _debugVerbose = Config.Bind(
            "Debug",
            "DebugVerbose",
            false,
            "Log every boss registration and teardown. Noisy; only useful when a plate does " +
            "not appear or appears over the wrong enemy.");

        ApplyConfigValues();

        Log.LogInfo($"{PluginName} {PluginVersion} loading...");
        Log.LogInfo(
            $"Plates: Enabled={Enabled} ShowName={ShowName} ShowNumbers={ShowNumbers} " +
            $"HideWhenFull={HideWhenFull} VerticalOffset={VerticalOffset:0.##} " +
            $"PlateScale={PlateScale:0.####} DebugVerbose={DebugVerbose}");

        try
        {
            BossPlatePatches.Apply();
        }
        catch (Exception ex)
        {
            Log.LogError("Failed to apply patches: " + ex);
        }

        ClassInjector.RegisterTypeInIl2Cpp<BossPlateBehaviour>();
        AddComponent<BossPlateBehaviour>();

        Log.LogInfo($"{PluginName} initialized.");
    }

    private void ApplyConfigValues()
    {
        Enabled = _enabled.Value;
        ShowName = _showName.Value;
        ShowNumbers = _showNumbers.Value;
        HideWhenFull = _hideWhenFull.Value;
        VerticalOffset = _verticalOffset.Value;
        PlateScale = Mathf.Clamp(_plateScale.Value, 0.002f, 0.06f);
        DebugVerbose = _debugVerbose.Value;
    }

    internal static void Dbg(string message)
    {
        if (DebugVerbose) Log.LogInfo("[DBG] " + message);
    }
}

/// <summary>
/// Unity-side host. Positioning happens in LateUpdate rather than Update so the enemy has
/// already moved for this frame - doing it in Update leaves the plate one frame behind, which
/// reads as the plate sliding around on a moving boss.
///
/// The try/catch that disables the component is the safety valve VSGodMode uses: this is the
/// only code in the mod that runs every frame, so an exception here would otherwise repeat
/// sixty times a second for the rest of the run.
/// </summary>
public class BossPlateBehaviour : MonoBehaviour
{
    public BossPlateBehaviour(IntPtr ptr) : base(ptr) { }

    private void LateUpdate()
    {
        try
        {
            if (!Plugin.Enabled) return;
            BossRegistry.Tick();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("Boss plate update failed, disabling: " + ex);
            enabled = false;
        }
    }
}
