using System;
using System.Collections.Generic;
using UnityEngine;
using VampireSurvivors.Data;
using VampireSurvivors.Data.Enemies;
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

    /// <summary>
    /// Values used to decide whether one scan candidate deserves a plate. Keeping this snapshot
    /// avoids asking IL2CPP for the same enemy fields again as the candidate moves from discovery
    /// through registration and qualification.
    /// </summary>
    private struct CandidateFacts
    {
        internal EnemyType Type;
        internal bool IsMajor;
        internal bool HasTreasure;
        internal bool HasBestiaryEntry;
        internal float Xp;
        internal float BaseHp;
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

    /// <summary>
    /// How long a rejection stays provisional, and how many times it is revisited.
    ///
    /// Started at one recheck after 1.5 seconds, which assumed AttachTreasure runs almost
    /// immediately after an enemy appears in the stage's list. Nothing guarantees that, and the
    /// cost of being wrong is asymmetric: a chest carrier condemned inside that window never
    /// gets a plate for its whole life, while a rejected ordinary enemy costs one cheap read
    /// every couple of seconds. Ten seconds of grace, five looks.
    ///
    /// Only the first look logs. Repeating "skipped FANGEL3" five times per enemy would undo
    /// the point of having a reject memory at all.
    /// </summary>
    private const float RejectRecheckDelay = 2f;
    private const int RejectAttempts = 5;

    /// <summary>Bound on the reject memory. Enemy instances are pooled, so the set of ids is
    /// naturally small, but a long run should not be able to grow this without limit.</summary>
    private const int MaxRejectMemory = 4096;

    /// <summary>
    /// Instances whose data profile has already been logged, so [Data] appears once per enemy
    /// rather than once per registration. A short-lived enemy that dies and is handed straight
    /// back out of the pool re-registers every scan, and printing its whole profile each time
    /// buried everything else in the log.
    /// </summary>
    private static readonly Dictionary<int, EnemyType> Profiled = new Dictionary<int, EnemyType>();
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

                if (!TryReadCandidateFacts(enemy, out CandidateFacts facts)) continue;
                Register(enemy, facts);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Boss scan failed: " + ex.Message);
        }
    }

    private static void Register(EnemyController enemy, CandidateFacts facts)
    {
        try
        {
            int id = enemy.GetInstanceID();
            EnemyType type = facts.Type;

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

            if (!Profiled.TryGetValue(id, out EnemyType profiledAs) || profiledAs != type)
            {
                if (Profiled.Count >= MaxRejectMemory) Profiled.Clear();
                Profiled[id] = type;
                LogDataProfile(enemy);
            }

            ReadEnemyDataFacts(enemy, ref facts);
            if (!Qualifies(facts, out string why))
            {
                bool first = prior.Count == 0;
                Reject(id, type);
                if (first)
                {
                    Plugin.Dbg(
                        $"skipped {type} - {why} " +
                        $"(will look again for {RejectRecheckDelay * RejectAttempts:0}s)");
                }
                return;
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
                Type = type,
                Plate = null,
                HasBeenDamaged = false,
                IsMajor = facts.IsMajor
            };
            Tracked.Add(entry);

            // Logged here rather than the moment it qualified, because the plate cap can still
            // turn it away - and a "kept" line for an enemy that never got a plate is a lie
            // that repeats every scan.
            Plugin.Dbg($"registered {entry.Type} (id {id}, {Tracked.Count} tracked) - {why}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("Could not register boss: " + ex.Message);
        }
    }

    /// <summary>
    /// Whether this enemy earns a plate, and why - the reason is returned either way, because
    /// "it was skipped" without a cause is the log line that costs an hour later.
    ///
    /// Four ways in, and they are alternatives rather than a chain of gates. Getting that wrong
    /// is easy: an earlier version tested health before reaching the experience rule, which
    /// would have silently killed the bonus-enemy tier. The blue glowing bat has five hit
    /// points, so a health gate in front of the experience rule excludes the exact case that
    /// rule exists for.
    /// </summary>
    private static bool Qualifies(CandidateFacts facts, out string why)
    {
        // Hazards first, and this veto outranks even the game's own boss flag.
        //
        // BULLET_W - a rising wall of water - reports IsBoss true in Boss Rash, so anything
        // that trusts that flag plates it. It gives no experience and the Bestiary has never
        // heard of it, which is the pair that gives it away: four genuine bosses also award no
        // experience (the Reaper, the Maddener, the Stalker, the Trickster) and every one of
        // them is catalogued. Neither test alone is enough; together they are exact.
        if (facts.Xp <= 0f && !facts.HasBestiaryEntry)
        {
            why = "no experience and no Bestiary entry, so scenery rather than a creature";
            return false;
        }

        if (facts.HasTreasure)
        {
            why = "it is carrying a chest";
            return true;
        }

        if (Plugin.BonusXpThreshold > 0 && facts.Xp >= Plugin.BonusXpThreshold)
        {
            why = $"it is worth {facts.Xp:0} xp";
            return true;
        }

        // The health floor applies to the game's boss flag too, which it did not before.
        // MOON_EYE2 in Boss Rash is flagged as a boss and has three hit points; it was
        // registering, dying instantly, and coming straight back out of the pool - 253 times in
        // one run - and each of those held a slot in the plate cap that a real boss then could
        // not have. Being flagged as a boss is not the same as being worth a health bar.
        //
        // Nothing strong is lost by this. The weak bosses that matter are already through:
        // BOSS_HARPY and BOSS_SKULL2 are five hit points and carry chests, DEVIL3 is five and
        // worth thirty experience.
        bool flagged = facts.IsMajor;
        bool catalogued = !Plugin.RequireBestiaryEntry || facts.HasBestiaryEntry;
        float hp = facts.BaseHp;

        if ((flagged || catalogued) && hp >= Plugin.MiniBossMinHp)
        {
            why = flagged
                ? $"the game calls it a stage boss, worth {hp:0} base HP"
                : $"catalogued and worth {hp:0} base HP";
            return true;
        }

        why = !flagged && !catalogued
            ? "no Bestiary entry, no chest, not worth enough xp"
            : $"only {hp:0} base HP, below the {Plugin.MiniBossMinHp:0} needed";
        return false;
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
        Profiled.Clear();
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
    /// Reads the shared EnemyData handle once, then takes the three values qualification needs
    /// from it. Each field remains independently guarded because one bad interop read must not
    /// prevent the remaining safe reads.
    ///
    /// Bestiary failures deliberately mean "present". When the mod cannot tell, showing a plate
    /// that might be wrong is safer than hiding a real boss.
    /// </summary>
    private static void ReadEnemyDataFacts(EnemyController enemy, ref CandidateFacts facts)
    {
        facts.HasBestiaryEntry = true;

        EnemyData data;
        try
        {
            data = enemy.CurrentEnemyData;
        }
        catch
        {
            return;
        }

        if (data == null)
        {
            facts.HasBestiaryEntry = false;
            return;
        }

        try { facts.Xp = data.xp; } catch { }
        try { facts.BaseHp = data.maxHp; } catch { }
        try
        {
            facts.HasBestiaryEntry = !data.bIgnore && !string.IsNullOrWhiteSpace(data.bName);
        }
        catch { }
    }

    /// <summary>
    /// Dumps every field that could plausibly separate a real boss from a stage hazard, so the
    /// qualification rule can be corrected against evidence rather than re-guessed.
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

    /// <summary>
    /// First-stage classification for the discovery scan. These are the values registration
    /// would otherwise read again immediately. EnemyData is intentionally deferred until after
    /// the rejection-memory check, so known ordinary enemies retain their cheap early exit.
    /// </summary>
    private static bool TryReadCandidateFacts(
        EnemyController enemy,
        out CandidateFacts facts)
    {
        facts = default;

        try { facts.Type = enemy.EnemyType; }
        catch { return false; }

        facts.IsMajor = IsBoss(enemy);
        if (Plugin.IncludeTreasureCarriers) facts.HasTreasure = HasTreasure(enemy);

        if (facts.IsMajor || facts.HasTreasure) return true;
        if (!Plugin.IncludeMiniBosses) return false;

        try { return GameAccess.IsBossType(facts.Type); }
        catch { return false; }
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
            if (!GameAccess.IsBossType(enemy.EnemyType)) return false;
        }
        catch
        {
            return false;
        }

        // Deliberately no strength test here. This decides what is worth *looking* at; what is
        // worth a plate is settled in Register, where the chest and experience exceptions can
        // still speak for an enemy that is weak but interesting.
        return true;
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
