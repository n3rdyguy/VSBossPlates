using System;
using System.Collections.Generic;
using UnityEngine;
using VampireSurvivors.Data;
using VampireSurvivors.Objects;
using VampireSurvivors.Objects.Characters;
using Object = UnityEngine.Object;

namespace VSBossPlates;

/// <summary>
/// Tracks which bosses are alive and owns one plate per boss.
///
/// ## Why there are no Harmony patches
///
/// The first version registered bosses from postfixes on Stage.SpawnBoss and
/// EnemyController.AfterSpawningAsBoss. It killed the game outright:
///
///     Fatal error. System.AccessViolationException: Attempted to read or write protected memory.
///        at UnityEngine.Object.GetInstanceID()
///        at VSBossPlates.BossRegistry.Register(EnemyController, String)
///        at VSBossPlates.BossPlatePatches.AfterSpawningAsBossPostfix(EnemyController)
///
/// The EnemyController handed to that postfix is not safe to touch. GetInstanceID was simply
/// the first native call made on it, so it is where the process died; any other read would
/// have done the same.
///
/// Two things follow, and the second is the important one:
///
///   1. Do not read a game object from inside a Harmony patch on this game's enemy code.
///   2. **A try/catch cannot save you here.** AccessViolationException is a corrupted-state
///      exception; .NET 6 does not deliver it to managed handlers, so the process dies whether
///      or not the call is guarded. The defensive try/catch used everywhere else in this mod
///      is worthless against it. The only fix is to not make the call.
///
/// So discovery does not use patches at all. Every scan interval the mod walks the Stage's own
/// SpawnedEnemies list from LateUpdate, where objects are fully constructed and reads are safe.
/// A boss therefore gets its plate up to one scan interval late, which for something that lives
/// for tens of seconds is not worth a crash class to avoid.
///
/// ## Why teardown is not patched either
///
/// Enemies are pooled, so an EnemyController is never destroyed. It is deactivated, returned to
/// its pool, and later handed out as a completely different enemy. The obvious teardown hooks -
/// OnRecycleEnemy, Despawn, Disappear - are virtual, and EnemyControllerBoss overrides them, so
/// a patch on the base method would silently miss exactly the enemies this mod cares about.
///
/// Instead every tracked entry is revalidated each frame. The check that actually catches
/// recycling is comparing the current EnemyType against the type recorded when tracking began:
/// an instance that came back as a different enemy no longer reports the type it was registered
/// with, whichever code path released it.
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

        /// <summary>A scheduled stage boss, as opposed to a mini-boss, chest carrier or bonus
        /// enemy. Recorded once at registration rather than asked per frame, because the answer
        /// cannot change for a given instance and type.</summary>
        internal bool IsMajor;
    }

    private static readonly List<Entry> Tracked = new List<Entry>();

    private struct Rejection
    {
        internal EnemyType Type;
        internal float At;
        internal int Count;
    }

    /// <summary>
    /// Enemies already judged and turned down, so the scan does not re-examine them twice a
    /// second for the rest of the run. Keyed on instance id, holding the type it was rejected
    /// as, because pooling means the same id returns later as a different enemy that deserves a
    /// fresh look.
    ///
    /// A first rejection is provisional. Whether an enemy carries a chest is decided by
    /// AttachTreasure, and nothing guarantees that has run by the time the enemy first appears
    /// in the stage's list - a scan landing in that gap would otherwise condemn a mini-boss
    /// permanently on a half-built object. One re-judgement a second and a half later closes
    /// that window; after that the rejection stands.
    /// </summary>
    private static readonly Dictionary<int, Rejection> Rejected = new Dictionary<int, Rejection>();

    private const float RejectRecheckDelay = 1.5f;
    private const int RejectAttempts = 2;

    /// <summary>Bound on the reject memory. Enemy instances are pooled, so the set of ids is
    /// naturally small, but a long run should not be able to grow this without limit.</summary>
    private const int MaxRejectMemory = 4096;
    private static float _nextScan;
    private static bool _warnedNoStage;
    private static bool _warnedPlateCap;

    /// <summary>Per-frame validate, position and refresh, plus a throttled discovery scan.
    /// Called from LateUpdate.</summary>
    internal static void Tick()
    {
        if (Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + Plugin.ScanInterval;
            Scan();
        }

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
    /// Walks the Stage's own live enemy list looking for bosses that are not tracked yet.
    ///
    /// Indexed rather than enumerated: an Il2Cpp list enumerator allocates a wrapper per step,
    /// and this runs several times a second against a list that can hold thousands of entries.
    /// </summary>
    private static void Scan()
    {
        Stage stage = GameAccess.GetStage();
        if (stage == null)
        {
            if (!_warnedNoStage)
            {
                _warnedNoStage = true;
                Plugin.Dbg("no Stage yet - not in a run");
            }
            return;
        }
        _warnedNoStage = false;

        try
        {
            var enemies = stage.SpawnedEnemies;
            if (enemies == null) return;

            int count = enemies.Count;
            for (int i = 0; i < count; i++)
            {
                EnemyController enemy = enemies[i];
                if ((Object)(object)enemy == (Object)null) continue;

                // Checked here as well as in StillValid, and not merely for speed. Without it
                // the scan registered enemies that the very next validation pass dropped
                // again, forever: one Boss Rash enemy sitting inactive in the list produced
                // 147 register/drop pairs in a single run.
                if (!IsUsable(enemy)) continue;

                if (!IsPlateworthy(enemy)) continue;
                Register(enemy);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Boss scan failed: " + ex.Message);
        }
    }

    private static void Register(EnemyController enemy)
    {
        try
        {
            int id = enemy.GetInstanceID();
            EnemyType type = enemy.EnemyType;

            for (int i = 0; i < Tracked.Count; i++)
            {
                if (Tracked[i].InstanceId == id) return;
            }

            // Already judged and turned down. Without this the scan re-examines and re-logs
            // every rejected enemy twice a second: a forest run produced hundreds of identical
            // "skipped BAT4" lines, because ordinary bats are in the boss type set.
            //
            // Keyed on the type as well as the instance, because instances are pooled: the same
            // id comes back as a different enemy, and that one deserves a fresh judgement.
            if (Rejected.TryGetValue(id, out Rejection prior) &&
                prior.Type == type &&
                (prior.Count >= RejectAttempts ||
                 Time.unscaledTime - prior.At < RejectRecheckDelay))
            {
                return;
            }

            if (prior.Count == 0) LogDataProfile(enemy);

            if (Plugin.RequireBestiaryEntry && !HasBestiaryEntry(enemy))
            {
                if (HasTreasure(enemy))
                {
                    Plugin.Dbg($"kept {type} - no Bestiary entry, but it is carrying a chest");
                }
                else if (IsBonusEnemy(enemy))
                {
                    Plugin.Dbg($"kept {type} - no Bestiary entry, but worth {ReadXp(enemy):0} xp");
                }
                else
                {
                    Reject(id, type);
                    Plugin.Dbg($"skipped {type} - no Bestiary entry, no chest, not worth enough xp");
                    return;
                }
            }

            // The mini-boss tier casts a wide net and some of those types may arrive in
            // numbers. A screen of health bars is worse than no health bars, so the cap holds
            // whatever was found first rather than letting the plates pile up.
            // Deliberately not remembered as a rejection: the cap is a passing condition, and
            // this boss should get a plate once something else dies.
            if (Tracked.Count >= Plugin.MaxPlates)
            {
                if (!_warnedPlateCap)
                {
                    _warnedPlateCap = true;
                    Plugin.Log.LogWarning(
                        $"Hit the {Plugin.MaxPlates} plate cap - further bosses will not get " +
                        "one until some die. Raise MaxPlates, or turn off IncludeMiniBosses " +
                        "if this happens constantly.");
                }
                return;
            }

            var entry = new Entry
            {
                Enemy = enemy,
                InstanceId = id,
                Type = enemy.EnemyType,
                Plate = null,
                HasBeenDamaged = false,
                IsMajor = IsBoss(enemy)
            };
            Tracked.Add(entry);
            Plugin.Dbg($"registered {entry.Type} (id {id}, {Tracked.Count} tracked)");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Could not register boss: " + ex.Message);
        }
    }

    /// <summary>
    /// Five independent reasons to stop drawing. Any interop read here can throw once the
    /// native object is gone, so a failure is treated as "no longer valid" rather than
    /// propagating.
    /// </summary>
    private static bool StillValid(Entry entry)
    {
        try
        {
            EnemyController enemy = entry.Enemy;
            if (!IsUsable(enemy)) return false;

            // Recycled as something else. This is the check that makes pooling safe.
            if (enemy.EnemyType != entry.Type) return false;

            if (!IsPlateworthy(enemy)) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Destroys every plate and forgets every tracked boss. Called when plates are switched off
    /// mid-run: the per-frame work is gated on Enabled, so without this the plates already on
    /// screen would freeze in place rather than disappear.
    ///
    /// Turning plates back on costs nothing - the next scan finds the same bosses again.
    /// </summary>
    internal static void ClearAll()
    {
        for (int i = Tracked.Count - 1; i >= 0; i--)
        {
            Drop(Tracked[i], i);
        }
        Rejected.Clear();
        _warnedPlateCap = false;
    }

    private static void Reject(int id, EnemyType type)
    {
        if (Rejected.Count >= MaxRejectMemory)
        {
            Rejected.Clear();
            Plugin.Dbg("reject memory full, cleared");
        }

        int count = 1;
        if (Rejected.TryGetValue(id, out Rejection prior) && prior.Type == type)
        {
            count = prior.Count + 1;
        }

        Rejected[id] = new Rejection { Type = type, At = Time.unscaledTime, Count = count };
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
            entry.Plate = BossPlate.Create(enemy, ResolveName(enemy), entry.IsMajor);
            if (entry.Plate == null) return;
        }

        entry.Plate.SetVisible(true);
        entry.Plate.Refresh(fraction, current, max);
        entry.Plate.PositionAbove(enemy);
    }

    /// <summary>
    /// Not everything the game flags as a boss is a boss you fight.
    ///
    /// BULLET_W - the water that rises from the bottom of the screen on Bat Country - comes
    /// back with IsBoss true, because the game reuses the boss machinery for stage hazards.
    /// It has no health worth reading and no name worth showing, and a plate over it is noise.
    ///
    /// The Bestiary is the game's own answer to "is this a creature the player is meant to know
    /// about". A hazard is not catalogued: it has no bName, or it is explicitly marked bIgnore.
    /// A real boss is. So the Bestiary record, not the boss flag, decides whether a plate is
    /// drawn.
    ///
    /// If this ever hides a boss it should not, the log line from LogDataProfile shows exactly
    /// which field disagreed.
    /// </summary>
    private static bool HasBestiaryEntry(EnemyController enemy)
    {
        try
        {
            var data = enemy.CurrentEnemyData;
            if (data == null) return false;

            if (data.bIgnore) return false;

            return !string.IsNullOrWhiteSpace(data.bName);
        }
        catch
        {
            // Cannot tell. Prefer showing a plate that might be wrong over hiding a real boss.
            return true;
        }
    }

    /// <summary>
    /// The exception to the Bestiary rule: an enemy the game does not catalogue, but which is
    /// clearly not filler.
    ///
    /// The blue glowing bat in Mad Forest is the case this exists for. It reads
    /// bName='' xp=30 maxHp=5 - no Bestiary record at all, and five hit points, so neither the
    /// Bestiary test nor any health-based test would keep it. What marks it out is the reward:
    /// thirty experience where an ordinary enemy gives one or two.
    ///
    /// XP is the honest signal here, and health is not. A bonus enemy is defined by being worth
    /// killing, not by being hard to kill.
    ///
    /// Still bounded by the boss type set, so this can only ever admit one of those 198 types
    /// and cannot start plating ordinary enemies late in a run when their XP has scaled up.
    /// </summary>
    private static bool IsBonusEnemy(EnemyController enemy)
    {
        if (Plugin.BonusXpThreshold <= 0) return false;
        return ReadXp(enemy) >= Plugin.BonusXpThreshold;
    }

    private static float ReadXp(EnemyController enemy)
    {
        try
        {
            var data = enemy.CurrentEnemyData;
            return data == null ? 0f : data.xp;
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>
    /// Dumps every field that could plausibly separate a real boss from a stage hazard, so the
    /// rule in HasBestiaryEntry can be corrected against evidence rather than re-guessed.
    /// Logged once per registration, behind DebugVerbose.
    /// </summary>
    private static void LogDataProfile(EnemyController enemy)
    {
        if (!Plugin.DebugVerbose) return;

        try
        {
            EnemyType type = enemy.EnemyType;
            var data = enemy.CurrentEnemyData;
            if (data == null)
            {
                Plugin.Log.LogInfo($"[Data] {type} has no EnemyData");
                return;
            }

            string term = "";
            try { term = data.GetLocalizedBestiaryNameTerm(type) ?? ""; } catch { }

            int places = 0;
            try { places = data.bPlaces == null ? -1 : data.bPlaces.Count; } catch { places = -2; }

            // Which stage this was seen on. The enemy data itself does not say - bPlaces is a
            // Bestiary field and a hazard has none - and the question comes up every time
            // something unexpected gets a plate.
            string stage = "?";
            try
            {
                Stage s = GameAccess.GetStage();
                if (s != null) stage = s._stageType.ToString();
            }
            catch { }

            Plugin.Log.LogInfo(
                $"[Data] {type} on {stage} bName='{data.bName}' " +
                $"bDesc={(string.IsNullOrEmpty(data.bDesc) ? "no" : "yes")} " +
                $"bInclude={data.bInclude} bIgnore={data.bIgnore} bHighlight={data.bHighlight} " +
                $"bIndex={data.bIndexNumber} bPlaces={places} xp={data.xp:0} maxHp={data.maxHp:0} " +
                $"power={data.power:0} chest={HasTreasure(enemy)} " +
                $"flag='{data.flagName}' tex='{data.textureName}' term='{term}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Could not read enemy data profile: " + ex.Message);
        }
    }

    /// <summary>
    /// Whether this enemy deserves a plate. Two tiers, because the game's own boss flag is
    /// narrower than what a player calls a boss.
    ///
    /// EnemyController.IsBoss marks a **scheduled stage boss** - the thing the timer sends at
    /// a set minute. BOSS_XLDEATH, the Reaper, answers true. BOSS_XLMUMMY and the other strong
    /// mini-bosses answer false, even though they are exactly the enemies whose remaining
    /// health you want to know.
    ///
    /// EnemyFactory._bossTypes is the wider set: every type the game is willing to use as a
    /// boss, 198 of them. That is much closer to "strong enemy worth a plate", so with
    /// IncludeMiniBosses on, membership of that set is enough.
    ///
    /// The set is wide enough to be worth watching. It contains entries such as BAT4 and
    /// SKELEGLOW whose ordinary spawn behaviour is unknown, so MaxPlates exists to stop a
    /// horde of them turning the screen into a wall of health bars.
    /// </summary>
    /// <summary>
    /// Alive, present, and worth looking at further. Deliberately shared between the scan and
    /// the per-frame validation so the two cannot disagree - when they did, the scan kept
    /// registering enemies that validation dropped again on the same tick.
    ///
    /// The inactive check is the load-bearing one: the pool deactivates rather than destroys,
    /// and a deactivated enemy stays in the stage's list.
    /// </summary>
    private static bool IsUsable(EnemyController enemy)
    {
        try
        {
            if ((Object)(object)enemy == (Object)null) return false;

            GameObject go = enemy.gameObject;
            if ((Object)(object)go == (Object)null) return false;
            if (!go.activeInHierarchy) return false;

            return !enemy.IsDead;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPlateworthy(EnemyController enemy)
    {
        if (IsBoss(enemy)) return true;

        // A chest carrier is a mini-boss by the game's own reckoning, whatever its type or
        // stats say. Checked before the type set, and deliberately not restricted by it.
        if (Plugin.IncludeTreasureCarriers && HasTreasure(enemy)) return true;

        if (!Plugin.IncludeMiniBosses) return false;

        try
        {
            return GameAccess.IsBossType(enemy.EnemyType);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether this enemy is carrying a chest.
    ///
    /// This is the signal the other tests were reaching for and missing. FANGEL3 in the Chapel
    /// reads bName='' xp=3 maxHp=15 - no Bestiary record, three experience, fifteen hit points.
    /// Nothing about its data marks it out, and yet it is a mini-boss, because it drops a
    /// treasure chest.
    ///
    /// The important part is that this is a property of the **instance**, not of the type. The
    /// same enemy id is a mini-boss when the stage attached a chest to it and ordinary filler
    /// when it did not, so no type-based rule could ever have got this right.
    /// </summary>
    private static bool HasTreasure(EnemyController enemy)
    {
        try
        {
            return enemy._hasATreasure;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// IsBoss is the backing property, IsBossEnemy() is a method that may answer for cases the
    /// property does not. Neither is documented, so both are consulted and either is enough.
    /// A type-name list was rejected: there are two dozen boss controller subclasses today and
    /// every DLC adds more.
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
    /// bName is a Bestiary *family* name, so on a variant enemy it names the family rather than
    /// the row - the Evolution Helper mod hit exactly this and had to prefer the row's own
    /// label. Bosses are not usually variants, so bName is right in practice here.
    ///
    /// EnemyData does carry localization helpers - GetLocalizedNameTerm,
    /// GetLocalizedBestiaryNameTerm, GetLocalizedDescription - so bName is not the only source
    /// available. They return I2 *terms* rather than text, which would mean taking on the
    /// localization assembly to resolve. Worth doing when the mod is translated; bName is the
    /// English name and is correct today. The term is logged by LogDataProfile so the switch
    /// can be made against real values rather than assumptions.
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

    /// <summary>
    /// Turns an enum id such as GIANT_BAT into "Giant Bat", so an enemy with no Bestiary record
    /// still reads as a name rather than as shouting.
    ///
    /// Trailing digits are dropped: the ids carry variant numbers that mean nothing to a player,
    /// and this path is reached mostly by bonus enemies, where BAT4 should read as "Bat". Two
    /// variants collapsing to the same word is the right outcome - they are the same creature.
    /// </summary>
    private static string Humanize(string id)
    {
        if (string.IsNullOrEmpty(id)) return "Boss";

        string[] parts = id.Split('_');
        var words = new List<string>();

        foreach (string raw in parts)
        {
            string p = raw.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (p.Length == 0) continue;
            words.Add(char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant());
        }

        return words.Count == 0 ? "Boss" : string.Join(" ", words);
    }
}
