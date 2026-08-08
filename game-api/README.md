# Game API notes - the enemy subsystem

What the game exposes about enemies, bosses and pooling, as of Vampire Survivors **1.15.114**
/ Unity **6000.0.62f1**.

**Everything here comes from Il2CppInterop stubs, which carry signatures but no method
bodies.** You can read what exists and what it is typed as. You cannot read what it does. Any
claim about behaviour in this file was observed in game, not deduced.

## Which assembly

`<game>\BepInEx\interop\`

| Looking for | Assembly |
|-------------|----------|
| `EnemyController`, every boss subclass, `EnemyData`, `Stage`, `EnemyFactory` | **`VampireSurvivors.Runtime.dll`** |
| Object pooling | `QFSW.MOP2.dll` |
| Cameras, `CameraExtensions`, `UICamera` | `VampireSurvivors.Runtime.dll` |

**`Assembly-CSharp.dll` contains no enemy, health or boss types at all.** If another mod's
notes send you there, they are wrong for this subsystem.

Regenerate any of the dumps below with:

```
ilspycmd -t "VampireSurvivors.Objects.Characters.EnemyController" VampireSurvivors.Runtime.dll
ilspycmd -l c VampireSurvivors.Runtime.dll | grep -i boss
```

## EnemyController

`VampireSurvivors.Objects.Characters.EnemyController`

Base chain: `EnemyController : BasePoolableSpriteBehaviour : ArcadeSprite : MonoBehaviour`.
It is **not** a `CharacterController`, which is what rules out every piece of health UI the
game ships.

### Health and identity

| Member | Kind |
|--------|------|
| `float Hp` | property |
| `float NormalizedHp` | property, get only |
| `float _hp`, `float _maxHp` | fields |
| `bool IsDead` | property |
| `bool IsBoss` | property |
| `bool IsBossEnemy()` | method |
| `EnemyType EnemyType` | property |
| `EnemyData CurrentEnemyData` | property |
| `SpriteRenderer EnemyRenderer` | property |
| `Camera MainCamera` | property, backed by a cached `_cachedMainCamera` |
| `float MaxHp()`, `float CurrentHealth()` | virtual methods |
| `void SetHealth(float)`, `void ChangeMaxHealth(float)` | methods |

### Lifecycle

| Member | Notes |
|--------|-------|
| `void InitialiseLocalData(EnemyType)` | re-init; **this is what re-keys a pooled instance** |
| `virtual void InitEnemy(EnemyType, bool asRemote)` | |
| `virtual void AfterSpawningAsBoss()` | boss-specific spawn hook |
| `virtual void OnRecycleEnemy()` | return-to-pool; **overridden by `EnemyControllerBoss`** |
| `virtual void Despawn()`, `virtual void Disappear()` | **`Despawn` is overridden by `EnemyControllerBoss`** |
| `virtual void Die(WeaponType)`, `virtual void Kill(WeaponType)` | |
| `static Action<EnemyController> OnKilledImmediate` | a real C# event; subscribable without Harmony |

### Treasure, and how to tell a mini-boss

| Member | Notes |
|--------|-------|
| `bool _hasATreasure` | **this enemy drops a chest** |
| `Treasure _treasure` | the chest itself |
| `void AttachTreasure(Treasure)` | how it is set, at or after spawn |
| `virtual void GiveReward(Action<Pickup>, WeaponType)` | |
| `void GiveFullReward(Action<Pickup>)` | |
| `virtual void GiveCustomRewards()` | |

`_hasATreasure` is **the** test for "is this a mini-boss". It is a property of the instance
rather than of the type, so the same enemy id carries a chest on one spawn and not on another.

**`EnemyData` has no drop, chest, treasure or reward field at all** - its entire public field
set was checked. Chests are decided by whatever spawns the enemy, not by enemy data, so a
type-based rule cannot answer this question.

`AttachTreasure` is not guaranteed to have run when the enemy first appears in
`Stage.SpawnedEnemies`, so re-check rather than judging once.

### Not present

**No lives, phase, shield or invulnerability field exists on `EnemyController`.** A
case-insensitive search of the whole type for `lives|shield|phase|refill|invuln` returns
nothing. `EnemyData` carries `lives` and `shieldDuration`, but how they are consumed lives in
native code the interop assemblies do not expose. Multi-life boss behaviour can only be
observed.

Status effects that **do** exist: `IsDefanged`, `IsTimeStopped`, `IsTimeSlowed`, `Slow`,
`_freezeTimer`, `_slowedTimer`, `DefangTimer`.

### Boss subclasses

`EnemyControllerBoss : EnemyController` overrides `InitEnemy`, `OnRecycleEnemy`, `OnUpdate`,
`Die` and `Despawn`. Below it: `EnemyControllerBoss_BatDragon`, `_BatDragon2`,
`_TerrainBreaker`, `EnemySusBoss`, `EX_Boss_Colossus`, plus per-DLC bosses
(`TP_ADV_BOSS_*`, `LEMON_BOSS_*`, `EME_TeleporterBoss`, `Enemy_TP_GateBoss`).

**Do not test for a boss with a type-name list.** There are two dozen today and each DLC adds
more. Use `IsBoss` / `IsBossEnemy()`. `EnemyFactory._bossTypes` is a
`HashSet<EnemyType>` if a type-only answer is needed without an instance.

**The override list is why teardown patches are unsafe.** A Harmony patch on
`EnemyController.OnRecycleEnemy` does not fire for an instance whose subclass overrides it.

## Pooling

Third-party **QFSW MOP2**. `QFSW.MOP2.ObjectPool : ScriptableObject`.

| Member | Notes |
|--------|-------|
| `GameObject GetObject()` and overloads | hands out a recycled instance |
| `void Release(GameObject)` | returns one; **deactivates, does not destroy** |
| `Dictionary<int, GameObject> AliveObjects()` | live-set introspection |
| `IEnumerable<GameObject> GetAllActiveObjects()` | |

Owner: `VampireSurvivors.App.Framework.EnemyFactory` with
`Dictionary<EnemyType, ObjectPool> _cachedEnemyPools`, `HashSet<EnemyType> _bossTypes`,
`ObjectPool GetEnemyPool(EnemyType)` and `GeneratePool(...)`.

**The consequence, stated plainly: an `EnemyController` is never destroyed. It is deactivated,
pooled, and later handed out as a completely different enemy. Never key tracking off object
identity alone.** Compare the current `EnemyType` against the type recorded when tracking
began - that comparison holds no matter which code path released the instance.

## Stage

`VampireSurvivors.Objects.Stage`

| Member | Notes |
|--------|-------|
| `EnemyController SpawnBoss()` | **single choke point for boss spawns** |
| `GameObject SpawnEnemy(EnemyType, Vector2, bool, bool)` | |
| `T SpawnEnemy<T>(...) where T : EnemyController` | generic; avoid patching |
| `List<EnemyController> SpawnedEnemies` | live list, via `_spawnedEnemies` |
| `Camera _mainCamera` | public **field**, no property accessor |
| `bool ShouldDespawnEnemyOutsideRect(EnemyController)`, `Rect EnemiesDespawnRect` | culling |

Reachable from `VampireSurvivors.Framework.GameManager.Stage`.

## EnemyData

`VampireSurvivors.Data.Enemies.EnemyData`

Core: `maxHp`, `power`, `speed`, `xp`, `knockback`, `lives` (nullable), `shieldDuration`,
`minimumHpScalingLevel` / `maximumHpScalingLevel` (nullable), `alias`.

Bestiary block: `bName`, `bDesc`, `bPlaces`, `bVariants`, `bIndexNumber`, `bInclude`,
`bIgnore`, `bHighlight`, `bIncludeColorVariants`.

Localization helpers, all taking an `EnemyType`: `GetLocalizedNameTerm`,
`GetLocalizedBestiaryNameTerm` and `GetLocalizedTipsTerm` return **I2 terms**;
`GetLocalizedDescription`, `GetLocalizedBestiaryDescription` and `GetLocalizedTips` return
text. Resolving the terms needs the `l2localization` assembly.

**There is an enemy term namespace, and it is `enemiesLang/`.** An earlier version of this file
said there was none. Observed in game:

```
GetLocalizedBestiaryNameTerm(BAT4) -> "enemiesLang/{BAT4}bName"
```

So the term is composed from the enemy id, and it is returned even for an enemy with no
Bestiary record at all - a term existing says nothing about whether it resolves to anything.

`bName` names a Bestiary **family**, so it is wrong on variant rows - the Bestiary page itself
prefers the row's own label.

**`bName` is the practical test for "is this a real, catalogued enemy".** Two different things
turn up in the boss machinery without one:

- **Stage hazards.** `BULLET_W`, a rising wall of water, reports `IsBoss` true.
- **Ordinary enemies that happen to be in the boss type set.** `BAT4` in Mad Forest reads
  `bName='' bInclude=False bIgnore=False bIndex=0 bPlaces=0 xp=30 maxHp=5`. Five HP: an
  ordinary bat, not a boss of any kind.

Note that `bIgnore` was **false** in both cases, so `bIgnore` alone is not the discriminator -
an empty `bName` is.

`EnemyFactory._bossTypes` is therefore **not** a list of bosses. It is closer to "types the
boss spawner is allowed to use", and it contains 198 entries including ordinary bats. Combine
it with a Bestiary record if you want strong enemies rather than all enemies.

## Cameras

The game renders through a **render texture**, which is what makes screen-space positioning
awkward.

| Member | Notes |
|--------|-------|
| `Stage._mainCamera` | the gameplay camera; a field, not a property |
| `EnemyController.MainCamera` | per-enemy cached reference; cheapest handle |
| `UICamera._cameraUI` | static, a **separate** UI camera |
| `UICamera.UIToGame(Vector3)` | static helper |
| `CameraExtensions.GetRtZoomScaling()` | render-texture scale factor |
| `CameraExtensions.GetRenderTextureSize()` | returns `int2` |

`Camera.WorldToScreenPoint` on the gameplay camera returns **render-texture** coordinates, not
backbuffer pixels. A world-space canvas avoids the conversion entirely.

## No boss UI exists

Searching every type in the game for `boss` returns only `EnemyController` subclasses and
arena helpers. Searching for `health` returns only `VampireSurvivors.UI.Player.HealthBar` and
`HealthBarUi`, both typed to `CharacterController`. The game's only overhead-icon type,
`App.Scripts.Graphics.OverheadIconGizmo`, is also `CharacterController`-typed.

Every piece of overhead and health UI the game ships is player-only. There is nothing to hook.

## Damage numbers, for reference

`VampireSurvivors.Objects.DamageNumberManager` does **not** use Canvas UI. It draws through a
`Blitter` over a sprite atlas, with `static void CreateDamageNumber(int, Vector3)` and a
`static Action<int, Vector3> OnCreateDamageNumber` hook.

That is the game's own answer to "text above a world position, at scale". For a handful of
bosses a Canvas is fine. For hundreds of enemies it would not be, and the Blitter path is
where to look.
