using System;
using System.Collections.Generic;
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
    public const string PluginVersion = "0.1.2";

    internal static new ManualLogSource Log;

    // Config mirrors. Each key needs all four of: this field, the ConfigEntry below,
    // a Config.Bind call, and a line in ApplyConfigValues. Miss one and the key silently
    // keeps its default forever.
    internal static bool Enabled;
    internal static bool ShowName;
    internal static bool ShowNumbers;
    internal static bool HideWhenFull;
    internal static bool RequireBestiaryEntry;
    internal static bool IncludeMiniBosses;
    internal static bool IncludeTreasureCarriers;
    internal static int MaxPlates;
    internal static int BonusXpThreshold;
    internal static float MiniBossMinHp;
    internal static float VerticalOffset;
    internal static float PlateScale;
    internal static float MiniBossPlateScale;
    internal static float ScanInterval;
    internal static bool ShowFps;
    internal static bool DebugVerbose;
    internal static KeyCode TogglePlatesKey;
    internal static KeyCode ToggleMiniBossesKey;

    // Static, unlike the rest, because the toggle hotkeys write back through them so a toggle
    // survives a restart.
    private static ConfigEntry<bool> _enabled;
    private static ConfigEntry<bool> _includeMiniBosses;
    private ConfigEntry<bool> _showName;
    private ConfigEntry<bool> _showNumbers;
    private ConfigEntry<bool> _hideWhenFull;
    private ConfigEntry<bool> _requireBestiaryEntry;
    private ConfigEntry<int> _maxPlates;
    private ConfigEntry<int> _bonusXpThreshold;
    private ConfigEntry<float> _miniBossMinHp;
    private ConfigEntry<bool> _includeTreasureCarriers;
    private ConfigEntry<string> _togglePlatesKey;
    private ConfigEntry<string> _toggleMiniBossesKey;
    private ConfigEntry<float> _verticalOffset;
    private ConfigEntry<float> _plateScale;
    private ConfigEntry<float> _miniBossPlateScale;
    private ConfigEntry<float> _scanInterval;
    private ConfigEntry<bool> _showFps;
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

        _includeMiniBosses = Config.Bind(
            "Plates",
            "IncludeMiniBosses",
            true,
            "Also plate strong mini-bosses, not only the boss the stage timer sends. The " +
            "game's own boss flag is narrow: the Reaper answers to it, but the XL mummies and " +
            "similar do not, even though those are exactly the enemies whose remaining health " +
            "is worth knowing. Turn this off for scheduled stage bosses only.");

        _maxPlates = Config.Bind(
            "Plates",
            "MaxPlates",
            20,
            new ConfigDescription(
                "Most plates on screen at once. A wall of health bars is worse than none. Twelve " +
                "was too few for a boss level, where a dozen bosses can be alive at the same " +
                "time and the ones past the cap simply got nothing. Once the cap is reached " +
                "no further plates appear until something dies.",
                new AcceptableValueRange<int>(1, 60)));

        _includeTreasureCarriers = Config.Bind(
            "Plates",
            "IncludeTreasureCarriers",
            true,
            "Plate any enemy carrying a treasure chest. This is the game's own mark of a " +
            "mini-boss and it beats every other test: it is a property of the individual " +
            "enemy rather than of its type, so the same creature is plated when the stage " +
            "gave it a chest and ignored when it did not. Nothing else about a chest carrier " +
            "need stand out - the Chapel's FANGEL3 has no Bestiary entry, three experience " +
            "and fifteen hit points.");

        _bonusXpThreshold = Config.Bind(
            "Plates",
            "BonusXpThreshold",
            25,
            new ConfigDescription(
                "Also plate an enemy worth at least this much experience, even when the " +
                "Bestiary has no record of it. This is what catches the blue glowing bat in " +
                "Mad Forest: no Bestiary entry and five hit points, but thirty experience " +
                "where an ordinary enemy gives one or two. Set to 0 to turn the exception off.",
                new AcceptableValueRange<int>(0, 10000)));

        _miniBossMinHp = Config.Bind(
            "Plates",
            "MiniBossMinHp",
            20f,
            new ConfigDescription(
                "How much base health an enemy needs before being in the game's boss type set " +
                "earns it a plate. That set contains ordinary enemies - the Tower's Scarleton " +
                "is in it and has two hit points - and the Bestiary does not tell them apart, " +
                "because ordinary enemies are catalogued too. This is health from the enemy's " +
                "data rather than from the live enemy, so it means the same thing at minute " +
                "two and at minute twenty. Does not apply to stage bosses, chest carriers or " +
                "bonus enemies; those qualify on their own.",
                new AcceptableValueRange<float>(0f, 10000f)));

        _requireBestiaryEntry = Config.Bind(
            "Plates",
            "RequireBestiaryEntry",
            true,
            "Only draw a plate for a boss the Bestiary knows about. The game reuses its boss " +
            "machinery for stage hazards - the rising water on Bat Country is flagged as a " +
            "boss - and those have no health worth reading. Turn this off to see a plate on " +
            "everything the game calls a boss.");

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
            0.008f,
            new ConfigDescription(
                "How big the plate is drawn, in world units per plate unit. Useful values: " +
                "0.004 discreet, 0.008 hard to miss, 0.012 theatrical - the name alone spans " +
                "a third of the screen. Note that small also means blurry: the game draws to " +
                "a low resolution image and scales it up, and the plate is drawn into that " +
                "same image, so a small plate has very few real pixels to render text with. " +
                "Below about 0.005 the numbers stop being sharp.",
                new AcceptableValueRange<float>(0.001f, 0.03f)));

        _miniBossPlateScale = Config.Bind(
            "Plates",
            "MiniBossPlateScale",
            0.005f,
            new ConfigDescription(
                "The same, for mini-bosses, chest carriers and bonus enemies. Smaller than " +
                "PlateScale by default, because there are more of them and none of them is " +
                "the thing you are actually worried about - a plate the size of the Reaper's " +
                "over every chest carrier turns a busy screen into a wall of health bars.",
                new AcceptableValueRange<float>(0.001f, 0.03f)));

        _scanInterval = Config.Bind(
            "Plates",
            "ScanIntervalSeconds",
            0.5f,
            new ConfigDescription(
                "How often to look for newly spawned bosses, in seconds. A boss gets its plate " +
                "up to this late. Lower is more responsive and costs a walk over the live enemy " +
                "list more often; that list can hold thousands of entries late in a run.",
                new AcceptableValueRange<float>(0.1f, 5f)));

        _togglePlatesKey = Config.Bind(
            "Hotkeys",
            "TogglePlatesKey",
            "F9",
            "Shows and hides every plate, mid-run, without restarting. The choice is written " +
            "back to this file, so it survives a restart. Any UnityEngine.KeyCode name, or " +
            "None to unbind.");

        _toggleMiniBossesKey = Config.Bind(
            "Hotkeys",
            "ToggleMiniBossesKey",
            "F10",
            "Shows and hides mini-boss plates only, leaving the scheduled stage boss plated. " +
            "Useful when a wave of strong enemies makes the screen busy. Any " +
            "UnityEngine.KeyCode name, or None to unbind.");

        _debugVerbose = Config.Bind(
            "Debug",
            "DebugVerbose",
            false,
            "Log every boss registration and teardown, plus 15-second performance summaries. " +
            "Noisy; only useful for diagnosis and profiling.");

        _showFps = Config.Bind(
            "Debug",
            "ShowFps",
            false,
            "Show a small smoothed FPS counter in the top-left corner.");

        ApplyConfigValues();

        Log.LogInfo($"{PluginName} {PluginVersion} loading...");
        Log.LogInfo(
            $"Plates: Enabled={Enabled} ShowName={ShowName} ShowNumbers={ShowNumbers} " +
            $"HideWhenFull={HideWhenFull} IncludeMiniBosses={IncludeMiniBosses} " +
            $"MaxPlates={MaxPlates} RequireBestiaryEntry={RequireBestiaryEntry} " +
            $"BonusXpThreshold={BonusXpThreshold} MiniBossMinHp={MiniBossMinHp:0} " +
            $"IncludeTreasureCarriers={IncludeTreasureCarriers} " +
            $"VerticalOffset={VerticalOffset:0.##} " +
            $"PlateScale={PlateScale:0.####} MiniBossPlateScale={MiniBossPlateScale:0.####} " +
            $"ScanInterval={ScanInterval:0.##}s " +
            $"ShowFps={ShowFps} DebugVerbose={DebugVerbose}");

        Log.LogInfo(
            $"Hotkeys: {TogglePlatesKey} shows/hides plates, " +
            $"{ToggleMiniBossesKey} shows/hides mini-boss plates.");

        // No Harmony patches. Bosses are found by scanning the Stage's own enemy list from
        // LateUpdate. Reading an EnemyController from inside a patch postfix killed the process
        // with an AccessViolationException - see the comment at the top of BossRegistry.
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
        RequireBestiaryEntry = _requireBestiaryEntry.Value;
        IncludeMiniBosses = _includeMiniBosses.Value;
        IncludeTreasureCarriers = _includeTreasureCarriers.Value;
        MaxPlates = Mathf.Clamp(_maxPlates.Value, 1, 60);
        BonusXpThreshold = Mathf.Clamp(_bonusXpThreshold.Value, 0, 10000);
        MiniBossMinHp = Mathf.Clamp(_miniBossMinHp.Value, 0f, 10000f);
        VerticalOffset = _verticalOffset.Value;
        PlateScale = Mathf.Clamp(_plateScale.Value, 0.001f, 0.03f);
        MiniBossPlateScale = Mathf.Clamp(_miniBossPlateScale.Value, 0.001f, 0.03f);
        ScanInterval = Mathf.Clamp(_scanInterval.Value, 0.1f, 5f);
        ShowFps = _showFps.Value;
        DebugVerbose = _debugVerbose.Value;
        TogglePlatesKey = ParseKey(_togglePlatesKey.Value, KeyCode.F9);
        ToggleMiniBossesKey = ParseKey(_toggleMiniBossesKey.Value, KeyCode.F10);
    }

    /// <summary>
    /// Turning plates off has to tear the existing ones down, not just stop drawing new ones.
    /// The per-frame work is gated on Enabled, so without this the plates already on screen
    /// would simply freeze in place - visibly worse than either state.
    /// </summary>
    internal static void SetPlatesEnabled(bool value)
    {
        Enabled = value;
        Persist(_enabled, value);
        if (!value) BossRegistry.ClearAll();
    }

    /// <summary>
    /// No teardown needed here: the per-frame validation asks whether each tracked enemy still
    /// deserves a plate, and a mini-boss stops qualifying the moment this turns off.
    /// </summary>
    internal static void SetIncludeMiniBosses(bool value)
    {
        IncludeMiniBosses = value;
        Persist(_includeMiniBosses, value);
    }

    private static void Persist(ConfigEntry<bool> entry, bool value)
    {
        try
        {
            if (entry != null) entry.Value = value;
        }
        catch (Exception ex)
        {
            // The toggle still applies for this session; only the memory of it is lost.
            Log.LogWarning("Could not save the toggle to the config file: " + ex.Message);
        }
    }

    /// <summary>
    /// "None" and an empty value both mean deliberately unbound, and must not fall back to the
    /// default - otherwise a key cannot be turned off, only moved.
    /// </summary>
    private static KeyCode ParseKey(string name, KeyCode fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name)) return KeyCode.None;

            if (Enum.TryParse<KeyCode>(name.Trim(), true, out KeyCode parsed)) return parsed;
        }
        catch { }

        Log.LogWarning($"Could not parse key '{name}'; using {fallback}.");
        return fallback;
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
/// The try/catch disables the component rather than merely swallowing the exception. This is
/// the only code in the mod that runs every frame, so without that valve one fault would
/// repeat sixty times a second for the rest of the run, taking the log and the framerate with
/// it.
/// </summary>
public class BossPlateBehaviour : MonoBehaviour
{
    private const float FpsRefreshSeconds = 0.5f;

    private float _fpsElapsed;
    private int _fpsFrames;
    private string _fpsText = "";
    private bool _fpsFailed;

    public BossPlateBehaviour(IntPtr ptr) : base(ptr) { }

    /// <summary>Input only. Deliberately outside the Enabled gate and outside the valve that
    /// LateUpdate trips, so a rendering fault does not also take the test keys away - those are
    /// what you need working in order to reproduce the fault.</summary>
    private void Update()
    {
        Hotkeys.Update();

        if (!Plugin.ShowFps || _fpsFailed) return;

        try
        {
            _fpsElapsed += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsElapsed >= FpsRefreshSeconds)
            {
                int fps = Mathf.RoundToInt(_fpsFrames / _fpsElapsed);
                _fpsText = fps + " FPS";
                _fpsElapsed = 0f;
                _fpsFrames = 0;
            }
        }
        catch (Exception ex)
        {
            _fpsFailed = true;
            Plugin.Log.LogWarning("FPS counter update failed, disabling it: " + ex.Message);
        }
    }

    /// <summary>
    /// IMGUI draws after the game's low-resolution render texture, so this diagnostic stays
    /// small and readable instead of being enlarged with the pixel-art game image.
    /// </summary>
    private void OnGUI()
    {
        if (!Plugin.ShowFps || _fpsFailed || string.IsNullOrEmpty(_fpsText)) return;

        try
        {
            Color previous = GUI.color;
            try
            {
                GUI.color = Color.black;
                GUI.Label(new Rect(9f, 9f, 80f, 24f), _fpsText);
                GUI.color = Color.white;
                GUI.Label(new Rect(8f, 8f, 80f, 24f), _fpsText);
            }
            finally
            {
                // IMGUI color is global state. Never tint the game's own UI if one label fails.
                GUI.color = previous;
            }
        }
        catch (Exception ex)
        {
            _fpsFailed = true;
            Plugin.Log.LogWarning("FPS counter draw failed, disabling it: " + ex.Message);
        }
    }

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
