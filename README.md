# VS Boss Plates

**Version 0.1.0** - Health plates above bosses for **Vampire Survivors** on Unity 6 / BepInEx IL2CPP.

While a boss is alive, a plate follows it showing a health bar, the boss name, and current/max HP. The game itself never tells you how much health a boss has left, or even which boss you are fighting.

| | |
|--|--|
| **Latest release** | not yet released |
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
| Plates | `VerticalOffset` | `0.35` | Extra gap in world units above the boss sprite |
| Plates | `PlateScale` | `0.012` | World units per plate unit; raise it if the plate is hard to read |
| Debug | `DebugVerbose` | `false` | Log every registration and teardown |

## Expected log lines

```
[Info: VS Boss Plates] VS Boss Plates 0.1.0 loading...
[Info: VS Boss Plates] Plates: Enabled=True ShowName=True ...
[Info: VS Boss Plates] Patched boss spawn hooks: Stage.SpawnBoss(0), EnemyController.AfterSpawningAsBoss(0)
[Info: VS Boss Plates] VS Boss Plates initialized.
```

A `No boss spawn hooks patched` warning means the game's enemy API has changed and no plates will appear.

## How it works

The game ships **no boss health UI at all**. Both of its health-bar types (`HealthBar`, `HealthBarUi`) and its only overhead-icon type (`OverheadIconGizmo`) are typed to `CharacterController`, which is the player. `EnemyController` is a different branch of the class hierarchy, so none of it can be reused. Every plate is built from scratch.

Plates are **world-space canvases**, not a screen overlay. The game renders through a render texture, so `Camera.WorldToScreenPoint` returns render-texture coordinates rather than screen pixels, and a screen-space plate would need that conversion redone on every resize and every camera zoom. A world-space canvas is drawn by the gameplay camera in the same pass as the sprites and tracks both for free.

Enemies are **pooled**. An `EnemyController` is never destroyed - it is deactivated, returned to its pool, and later handed out as a completely different enemy. Rather than hooking the game's recycle methods, which several boss subclasses override, every tracked boss is re-validated each frame. The check that actually catches recycling is comparing the enemy's current type against the type it was registered with.

See [`docs/RENDER-SPEC.md`](docs/RENDER-SPEC.md) for the full reasoning and [`docs/USER-GUIDE.md`](docs/USER-GUIDE.md) for the player-facing description.

## Caveats

- Verified against Vampire Survivors 1.15.114 only
- Boss names come from the Bestiary family name, which is right for bosses but would be wrong on a variant enemy. If a boss is ever misnamed, it needs a small override table
- Bosses with more than one life refill their health rather than dying; the bar refills with them
