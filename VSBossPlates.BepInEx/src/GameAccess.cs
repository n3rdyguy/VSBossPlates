using System;
using System.Collections.Generic;
using UnityEngine;
using VampireSurvivors.Data;
using VampireSurvivors.Objects;
using Object = UnityEngine.Object;

namespace VSBossPlates;

/// <summary>
/// The one place that reaches into the running game for the Stage and its enemy list.
///
/// Everything here is called from LateUpdate, never from inside a Harmony patch. That
/// distinction is not stylistic - see the comment on BossRegistry.Scan for what happened when
/// the mod read an EnemyController from inside a patch postfix.
/// </summary>
internal static class GameAccess
{
    private static Stage _stage;
    private static List<EnemyType> _bossTypes;

    /// <summary>
    /// The same set as <see cref="_bossTypes"/>, as a managed HashSet. The scan asks "is this a
    /// boss type" once per enemy per pass, against a list that holds thousands of enemies late
    /// in a run, so it needs to be a hash lookup rather than a walk of 198 entries.
    /// </summary>
    private static HashSet<EnemyType> _bossTypeSet;

    /// <summary>
    /// Found by scene scan and cached. A scan must never run every frame, but this one runs
    /// only when the cache is empty, which is once per run. The cache clears itself when the
    /// Stage instance dies, so leaving a run and starting another works.
    /// </summary>
    internal static Stage GetStage()
    {
        if ((Object)(object)_stage != (Object)null) return _stage;

        try
        {
            _stage = Object.FindObjectOfType<Stage>();
            if ((Object)(object)_stage != (Object)null)
            {
                // A new stage may carry a different boss set.
                _bossTypes = null;
                _bossTypeSet = null;
                Plugin.Dbg("captured Stage");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Could not find Stage: " + ex.Message);
        }

        return (Object)(object)_stage == (Object)null ? null : _stage;
    }

    /// <summary>
    /// Stage.BossTypes is the game's own answer to "which enemy types are bosses", which beats
    /// any list this mod could keep: it stays correct across DLC.
    ///
    /// This used to live at EnemyFactory._bossTypes. The 1.16 interop moved it to Stage and made
    /// each element nullable, so both the collection and every value need checking.
    /// </summary>
    internal static List<EnemyType> GetBossTypes()
    {
        if (_bossTypes != null) return _bossTypes;

        Stage stage = GetStage();
        if (stage == null) return null;

        try
        {
            var bossTypes = stage.BossTypes;
            if (bossTypes == null) return null;

            var list = new List<EnemyType>();
            int count = bossTypes.Count;
            for (int i = 0; i < count; i++)
            {
                Il2CppSystem.Nullable<EnemyType> value = bossTypes[i];
                if (value != null && value.HasValue) list.Add(value.Value);
            }

            list.Sort();
            _bossTypes = list;

            _bossTypeSet = new HashSet<EnemyType>();
            foreach (EnemyType t in list) _bossTypeSet.Add(t);

            Plugin.Log.LogInfo($"Read {list.Count} boss types from EnemyFactory.");

            // Dumped in full because the question "is X in this set" keeps coming up and the
            // set is not readable anywhere else - the enemy data lives in compressed
            // Addressables bundles, so it cannot be grepped out of the install.
            Plugin.Dbg("boss types: " + string.Join(", ", list));

            return _bossTypes;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Could not read EnemyFactory boss types: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Whether the game considers this type usable as a boss. Wider than the per-instance
    /// IsBoss flag: BOSS_XLMUMMY is in here but reports IsBoss false when it spawns, because
    /// the flag marks a scheduled stage boss rather than a strong enemy.
    /// </summary>
    internal static bool IsBossType(EnemyType type)
    {
        if (_bossTypeSet == null) GetBossTypes();
        return _bossTypeSet != null && _bossTypeSet.Contains(type);
    }
}
