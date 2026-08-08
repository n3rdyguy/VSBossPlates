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

**There is no localization helper for enemy names.** `WeaponData` has
`GetLocalizedNameTerm(WeaponType)`; `EnemyData` has no equivalent, and there is no enemy or
bestiary term namespace in the game's localization data. `bName` names a Bestiary **family**,
so it is wrong on variant rows - the Bestiary page itself prefers the row's own label.

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
