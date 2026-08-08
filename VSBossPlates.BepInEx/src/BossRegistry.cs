using System;
using System.Collections.Generic;
using UnityEngine;
using VampireSurvivors.Data;
using VampireSurvivors.Objects.Characters;
using Object = UnityEngine.Object;

namespace VSBossPlates;

/// <summary>
/// Tracks which bosses are alive and owns one plate per boss.
///
/// The hard part here is not finding bosses, it is knowing when to stop drawing one.
/// Enemies are pooled - QFSW MOP2, via EnemyFactory - so an EnemyController is never
/// destroyed. It is deactivated, handed back to its pool, and later handed out again as a
/// completely different enemy through InitialiseLocalData. Anything keyed on object identity
/// alone will therefore keep drawing a boss plate over, say, a bat.
///
/// The teardown could have been hung off the game's own hooks: OnRecycleEnemy, Despawn,
/// Disappear, and the static OnKilledImmediate event all exist. It deliberately is not.
/// Those are virtual and several boss subclasses override them, so patching the base method
/// would silently miss exactly the enemies this mod cares about. Instead every tracked entry
/// is re-validated each frame against five conditions, of which the EnemyType comparison is
/// the one that actually catches recycling: an instance that came back as a different enemy
/// no longer reports the type it was registered with.
/// </summary>
internal static class BossRegistry
{
    private sealed class Entry
    {
        internal EnemyController Enemy;
        internal int InstanceId;
        internal EnemyType Type;
        internal BossPlate Plate;
        internal bool HasBeenDamaged;
    }

    private static readonly List<Entry> Tracked = new List<Entry>();

    /// <summary>
    /// Called from the spawn postfixes. Safe to call repeatedly for the same boss - both
    /// hooks fire for an ordinary stage boss, and that is intentional: either one alone would
    /// miss some spawn route, and the duplicate is cheap to reject.
    /// </summary>
    internal static void Register(EnemyController enemy, string source)
    {
        if (!Plugin.Enabled) return;

        try
        {
            if ((Object)(object)enemy == (Object)null) return;
            if (!IsBoss(enemy)) return;

            int id = enemy.GetInstanceID();
            for (int i = 0; i < Tracked.Count; i++)
            {
                if (Tracked[i].InstanceId == id) return;
            }

            var entry = new Entry
            {
                Enemy = enemy,
                InstanceId = id,
                Type = enemy.EnemyType,
                Plate = null,
                HasBeenDamaged = false
            };
            Tracked.Add(entry);
            Plugin.Dbg($"registered {entry.Type} via {source} (id {id}, {Tracked.Count} tracked)");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Could not register boss from {source}: {ex.Message}");
        }
    }

    /// <summary>Per-frame validate, position and refresh. Called from LateUpdate.</summary>
    internal static void Tick()
    {
        for (int i = Tracked.Count - 1; i >= 0; i--)
        {
            Entry entry = Tracked[i];

            if (!StillValid(entry))
            {
                Drop(entry, i);
                continue;
            }

            try
            {
                UpdatePlate(entry);
            }
            catch (Exception ex)
            {
                // One bad boss should not take the others down with it.
                Plugin.Log.LogWarning($"Plate update failed for {entry.Type}: {ex.Message}");
                Drop(entry, i);
            }
        }
    }

    /// <summary>
    /// Five independent reasons to stop drawing. Any interop read here can throw once the
    /// native object is gone, so the whole thing is treated as "no longer valid" on failure
    /// rather than propagating.
    /// </summary>
    private static bool StillValid(Entry entry)
    {
        try
        {
            EnemyController enemy = entry.Enemy;
            if ((Object)(object)enemy == (Object)null) return false;

            GameObject go = enemy.gameObject;
            if ((Object)(object)go == (Object)null) return false;

            // Released back to its pool. The pool deactivates rather than destroys.
            if (!go.activeInHierarchy) return false;

            if (enemy.IsDead) return false;

            // Recycled as something else. This is the check that makes pooling safe.
            if (enemy.EnemyType != entry.Type) return false;

            if (!IsBoss(enemy)) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Drop(Entry entry, int index)
    {
        try
        {
            if (entry.Plate != null)
            {
                entry.Plate.Destroy();
                entry.Plate = null;
            }
        }
        catch { }

        Tracked.RemoveAt(index);
        Plugin.Dbg($"dropped {entry.Type} (id {entry.InstanceId}, {Tracked.Count} tracked)");
    }

    private static void UpdatePlate(Entry entry)
    {
        EnemyController enemy = entry.Enemy;

        float max = ReadMaxHp(enemy);
        float current = ReadCurrentHp(enemy);
        float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;

        if (fraction < 0.999f) entry.HasBeenDamaged = true;

        if (Plugin.HideWhenFull && !entry.HasBeenDamaged)
        {
            if (entry.Plate != null) entry.Plate.SetVisible(false);
            return;
        }

        if (entry.Plate == null)
        {
            entry.Plate = BossPlate.Create(enemy, ResolveName(enemy));
            if (entry.Plate == null) return;
        }

        entry.Plate.SetVisible(true);
        entry.Plate.Refresh(fraction, current, max);
        entry.Plate.PositionAbove(enemy);
    }

    /// <summary>
    /// IsBoss is the backing property, IsBossEnemy() is a method that may answer for cases the
    /// property does not. Neither is documented, so both are consulted and either one is
    /// enough. A type-name list was rejected: there are two dozen boss controller subclasses
    /// today and every DLC adds more.
    /// </summary>
    private static bool IsBoss(EnemyController enemy)
    {
        try
        {
            if (enemy.IsBoss) return true;
        }
        catch { }

        try
        {
            return enemy.IsBossEnemy();
        }
        catch { }

        return false;
    }

    private static float ReadMaxHp(EnemyController enemy)
    {
        try
        {
            float v = enemy.MaxHp();
            if (v > 0f) return v;
        }
        catch { }

        try
        {
            return enemy._maxHp;
        }
        catch { }

        return 0f;
    }

    private static float ReadCurrentHp(EnemyController enemy)
    {
        try
        {
            return enemy.Hp;
        }
        catch { }

        try
        {
            return enemy.CurrentHealth();
        }
        catch { }

        return 0f;
    }

    /// <summary>
    /// bName is a Bestiary *family* name, so on a variant enemy it names the family rather
    /// than the row - the Evolution Helper mod hit exactly this and had to prefer the row's
    /// own label. Bosses are not usually variants, so bName is right in practice here, and
    /// there is no better source: EnemyData has no localization helper (unlike WeaponData,
    /// which has GetLocalizedNameTerm) and there is no enemy term namespace in the game's
    /// localization at all. If a boss ever shows the wrong name, the fix is a small
    /// EnemyType-to-name override table, not a new lookup path.
    /// </summary>
    private static string ResolveName(EnemyController enemy)
    {
        try
        {
            var data = enemy.CurrentEnemyData;
            if (data != null)
            {
                string name = data.bName;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { }

        try
        {
            return Humanize(enemy.EnemyType.ToString());
        }
        catch { }

        return "Boss";
    }

    /// <summary>Turns an enum id such as GIANT_BAT into "Giant Bat" so a missing record
    /// still reads as a name rather than as shouting.</summary>
    private static string Humanize(string id)
    {
        if (string.IsNullOrEmpty(id)) return "Boss";

        string[] parts = id.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            if (p.Length == 0) continue;
            parts[i] = char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant();
        }
        return string.Join(" ", parts);
    }
}
