using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using VampireSurvivors.Objects;
using VampireSurvivors.Objects.Characters;

namespace VSBossPlates;

/// <summary>
/// Registration hooks only. Teardown is not patched at all - see BossRegistry for why the
/// per-frame validation is safer than hooking OnRecycleEnemy and friends.
///
/// Two hooks rather than one, because neither is provably complete on its own:
///   - Stage.SpawnBoss() is the single choke point for an ordinary stage boss, and hands back
///     the controller already initialised.
///   - EnemyController.AfterSpawningAsBoss() catches a boss that arrives some other way, such
///     as a stage script or an Arcana effect promoting an enemy.
/// Registering the same boss twice is a no-op, so overlap costs nothing.
///
/// Methods are resolved by name rather than by signature. Overload lists and parameter types
/// drift between game versions, and a name lookup that finds nothing logs a warning, whereas
/// a signature that no longer matches throws at load.
/// </summary>
internal static class BossPlatePatches
{
    private static Harmony _harmony;

    internal static void Apply()
    {
        _harmony = new Harmony(Plugin.PluginGuid);

        var patched = new List<string>();

        patched.AddRange(PatchByName(
            typeof(Stage), "SpawnBoss", nameof(SpawnBossPostfix)));

        patched.AddRange(PatchByName(
            typeof(EnemyController), "AfterSpawningAsBoss", nameof(AfterSpawningAsBossPostfix)));

        if (patched.Count == 0)
        {
            Plugin.Log.LogWarning(
                "No boss spawn hooks patched - no plates will appear. The game's enemy API has " +
                "probably changed; check Stage.SpawnBoss and EnemyController.AfterSpawningAsBoss.");
        }
        else
        {
            Plugin.Log.LogInfo("Patched boss spawn hooks: " + string.Join(", ", patched));
        }
    }

    private static List<string> PatchByName(Type declaringType, string methodName, string postfixName)
    {
        var patched = new List<string>();

        try
        {
            MethodInfo[] methods = declaringType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (MethodInfo m in methods)
            {
                if (m.Name != methodName) continue;
                try
                {
                    _harmony.Patch(m, postfix: new HarmonyMethod(
                        typeof(BossPlatePatches), postfixName));
                    patched.Add($"{declaringType.Name}.{m.Name}({m.GetParameters().Length})");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning(
                        $"Could not patch {declaringType.Name}.{m.Name}: {ex.Message}");
                }
            }

            if (patched.Count == 0)
            {
                Plugin.Log.LogWarning($"{declaringType.Name}.{methodName} not found.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Could not inspect {declaringType.Name}: {ex.Message}");
        }

        return patched;
    }

    private static void SpawnBossPostfix(EnemyController __result)
    {
        BossRegistry.Register(__result, "Stage.SpawnBoss");
    }

    private static void AfterSpawningAsBossPostfix(EnemyController __instance)
    {
        BossRegistry.Register(__instance, "AfterSpawningAsBoss");
    }
}
