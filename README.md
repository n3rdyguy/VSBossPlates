# VS Boss Plates

**Version 0.1.2** - Health plates above bosses for **Vampire Survivors** on Unity 6 / BepInEx IL2CPP.

While a boss is alive, a plate follows it showing a health bar, the boss name, and current/max HP. The game itself never tells you how much health a boss has left, or even which boss you are fighting.

| | |
|--|--|
| **Latest release** | [v0.1.2](https://github.com/n3rdyguy/VSBossPlates/releases/tag/v0.1.2) - development build |
| **Game** | Vampire Survivors **1.15.x** (tested **1.15.114**), Unity **6000.0.62f1** |
| **Loader** | [BepInEx 6 IL2CPP](https://builds.bepinex.dev/projects/bepinex_be) (BE / bleeding-edge) |

Independent of [VS Evolution Helper](https://github.com/n3rdyguy/VSEvolutionHelperEx) - separate plugin, separate config, separate GUID. Install either, both, or neither.

---

## Read this first: BepInEx only

**MelonLoader crashes** on current Unity 6 builds (`0x80131506` / CoreCLR). This is a BepInEx plugin and will not work under MelonLoader. Do not run both loaders at once.

You need **BepInEx 6 bleeding-edge (BE)**, **Unity.IL2CPP**, **win-x64**. Three distinctions, all of which matter:

- **6.x bleeding-edge**, not 5.x stable - 5.x has no IL2CPP support for Unity 6
- **Unity.IL2CPP**, not Unity.Mono - the Mono package silently does nothing
- **win-x64**, not win-x86 - the game is 64-bit

BepInEx 6 has no stable release. Bleeding-edge *is* the correct channel here, not a risky choice.

## Install

1. Install BepInEx 6 IL2CPP win-x64 into the game folder
2. Run the game once so BepInEx generates its interop assemblies, then close it
3. Drop `VSBossPlates.dll` into `BepInEx/plugins/VSBossPlates/`

## Config

`BepInEx/config/com.n3rdyguy.vsbossplates.cfg`, written on first run.

| Section | Key | Default | Meaning |
|---------|-----|---------|---------|
| Plates | `Enabled` | `true` | Draw plates. Off stops all per-frame work |
| Plates | `ShowName` | `true` | Boss name above the bar |
| Plates | `ShowNumbers` | `true` | Current and maximum HP on the bar |
| Plates | `HideWhenFull` | `false` | Only show a plate once the boss has taken damage |
| Plates | `IncludeTreasureCarriers` | `true` | Plate any enemy carrying a treasure chest. The game's own mark of a mini-boss |
| Plates | `IncludeMiniBosses` | `true` | Also plate strong mini-bosses, not just the scheduled stage boss |
| Plates | `MaxPlates` | `20` | Most plates on screen at once |
| Plates | `RequireBestiaryEntry` | `true` | Only plate bosses the Bestiary knows about, so stage hazards are skipped |
| Plates | `BonusXpThreshold` | `25` | Also plate an enemy worth at least this much XP, Bestiary entry or not. `0` turns it off |
| Plates | `MiniBossMinHp` | `20` | Base health an enemy needs before the boss type set alone earns it a plate |
| Plates | `VerticalOffset` | `0.35` | Extra gap in world units above the boss sprite |
| Plates | `PlateScale` | `0.008` | Boss plate size. `0.004` discreet, `0.008` hard to miss, `0.012` theatrical. Small is also blurry |
| Plates | `MiniBossPlateScale` | `0.005` | The same for mini-bosses, chest carriers and bonus enemies |
| Plates | `ScanIntervalSeconds` | `0.5` | How often to look for newly spawned bosses |
| Hotkeys | `TogglePlatesKey` | `F9` | Show/hide all plates, mid-run. Saved to this file |
| Hotkeys | `ToggleMiniBossesKey` | `F10` | Show/hide mini-boss plates only. Saved to this file |
| Debug | `DebugVerbose` | `false` | Log every registration and teardown |

## Expected log lines

```
[Info: VS Boss Plates] VS Boss Plates 0.1.2 loading...
[Info: VS Boss Plates] Plates: Enabled=True ShowName=True ShowNumbers=True ...
[Info: VS Boss Plates] VS Boss Plates initialized.
```

Then, once you are in a run:

```
[Info: VS Boss Plates] Read 198 boss types from EnemyFactory.
```

If that line never appears, the mod cannot see the stage and no plates will show. Turn on `DebugVerbose` for a `[Data]` line per boss recording the stage, the name and every field used to decide whether it gets a plate.

## How it works

The game ships **no boss health UI at all**. Both of its health-bar types (`HealthBar`, `HealthBarUi`) and its only overhead-icon type (`OverheadIconGizmo`) are typed to `CharacterController`, which is the player. `EnemyController` is a different branch of the class hierarchy, so none of it can be reused. Every plate is built from scratch.

Plates are **world-space canvases**, not a screen overlay. The game renders through a render texture, so `Camera.WorldToScreenPoint` returns render-texture coordinates rather than screen pixels, and a screen-space plate would need that conversion redone on every resize and every camera zoom. A world-space canvas is drawn by the gameplay camera in the same pass as the sprites and tracks both for free. The cost is sharpness: the plate is drawn into that same low-resolution image, so a small plate has few real pixels for its text.

There are **no Harmony patches**. Reading an `EnemyController` from inside a patch postfix killed the game with an `AccessViolationException`, which is a corrupted-state exception that no try/catch can intercept. Bosses are found by scanning the stage's own enemy list from `LateUpdate` instead.

Enemies are **pooled**. An `EnemyController` is never destroyed - it is deactivated, returned to its pool, and later handed out as a completely different enemy. Every tracked boss is therefore re-validated each frame. The check that actually catches recycling is comparing the enemy's current type against the type it was registered with.

See [`docs/RENDER-SPEC.md`](docs/RENDER-SPEC.md) for the full reasoning and [`docs/USER-GUIDE.md`](docs/USER-GUIDE.md) for the player-facing description.

## Caveats

- Verified against Vampire Survivors 1.15.114 only
- Boss names come from the Bestiary family name, which is right for bosses but would be wrong on a variant enemy. If a boss is ever misnamed, it needs a small override table
- Bosses with more than one life refill their health rather than dying; the bar refills with them
