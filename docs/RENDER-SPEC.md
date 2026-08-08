# Render spec

Engineering notes for VS Boss Plates. Read this before changing anything that draws or
tracks. Every rule below is written with the reason that produced it, because the reasons are
not visible in the interop assemblies.

Verified against Vampire Survivors **1.15.114**, Unity **6000.0.62f1**, BepInEx 6 **be.785**.

---

## 1. Nothing in the game can be reused

The two findings that shape the whole mod.

### 1.1 There is no boss health UI to hook

Searching every type in `VampireSurvivors.Runtime.dll` for `boss` returns 24 types, all of
them `EnemyController` subclasses or arena helpers. `Assembly-CSharp.dll` contains **zero**
types matching `enemy`, `health` or `boss`. There is no `BossHealthBar`, no boss banner, no
boss bar at the top of the screen. Nothing exists to extend, reskin or imitate.

### 1.2 Every health bar in the game is the player's

| Type | Blocking member |
|------|-----------------|
| `VampireSurvivors.UI.Player.HealthBar` | `CharacterController _character` |
| `VampireSurvivors.UI.Player.HealthBarUi` | `void Initialize(CharacterController character)` |
| `VampireSurvivors.App.Scripts.Graphics.OverheadIconGizmo` | `void Play(..., CharacterController character, ...)` |

`EnemyController` derives from `BasePoolableSpriteBehaviour`, which derives from
`ArcadeSprite`. It is **not** a `CharacterController` and cannot be passed to any of the
above. `CharacterController.ShowHealthBar` and `HealthBarScale` are the player's own settings
and have nothing to do with enemies.

Both `HealthBar` and `HealthBarUi` also run an `Update()` that has no plausible source for its
fill other than `_character`, so instantiating one of their prefabs and leaving `_character`
null would throw a NullReference every frame under IL2CPP.

---

## 2. Why the plate is world space, not screen space

The obvious design is a screen-space overlay canvas positioned with
`Camera.WorldToScreenPoint`. That is what the original roadmap note assumed. It is wrong for
this game.

**The game renders through a render texture.** `VampireSurvivors.Tools.CameraExtensions`
carries `GetRtZoomScaling()` and `GetRenderTextureSize()` for exactly this reason. When a
camera has a `targetTexture`, `WorldToScreenPoint` returns coordinates in that texture's
space, not in backbuffer pixels. A screen-space plate would therefore need the render-texture
scale *and* any letterbox offset applied, and both would have to be re-derived on every window
resize and every camera zoom change.

There is a second camera to trip over: `VampireSurvivors.UICamera` holds a static `_cameraUI`
separate from the gameplay camera, plus a `UIToGame(Vector3)` helper. A screen-space plate
would have to pick the right one of the two.

A **world-space `Canvas`** avoids all of it. The gameplay camera draws the plate in the same
pass as the sprites, so zoom, resolution and letterboxing are handled by the same projection
that already places the boss.

Practical consequences:

- Set `canvas.worldCamera`. `EnemyController.MainCamera` is a per-enemy cached reference and
  is the cheapest handle; `Camera.main` is the fallback, and is a tagged lookup.
- Put the plate on **the enemy's layer**. A default-layer canvas is not guaranteed to be in
  the gameplay camera's culling mask.
- The scene is 2D and orthographic looking down `-Z`, so an identity rotation faces the
  camera. Set it explicitly anyway; a canvas that has been reparented can inherit rotation.
- `sortingOrder` 500 draws above sprites and below the game's screen-space UI, so a plate can
  never cover the pause menu.

If a world-space canvas ever fails to render through the pixel-perfect render texture, the
fallback is a screen-space overlay plus `WorldToScreenPoint` scaled by
`CameraExtensions.GetRtZoomScaling()`. That is more arithmetic but is known to be possible.

---

## 3. Pooling is the thing that must be right

Enemies come from **QFSW MOP2** pools, owned by `VampireSurvivors.App.Framework.EnemyFactory`
(`Dictionary<EnemyType, ObjectPool> _cachedEnemyPools`, plus a
`HashSet<EnemyType> _bossTypes`). `ObjectPool.Release(GameObject)` puts an instance back and
`GetObject()` hands the same one out again. `EnemyController.InitialiseLocalData(EnemyType)`
then re-keys it to a different enemy.

**An `EnemyController` is never destroyed.** Any map keyed on object identity will keep a boss
plate alive over whatever that instance became next.

### 3.1 Why teardown is not patched

The game offers plenty of hooks: `OnRecycleEnemy()`, `Despawn()`, `Disappear()`, and a static
`Action<EnemyController> OnKilledImmediate` event. None of them are used, on purpose.

`EnemyControllerBoss` **overrides** `OnRecycleEnemy` and `Despawn`. A Harmony patch on the
base `EnemyController` method does not fire for an instance whose subclass overrides it, so
patching the base would silently miss exactly the enemies this mod tracks. Patching every
declaring subclass means enumerating two dozen types that grow with every DLC.

### 3.2 What is done instead

Every tracked entry is re-validated in `LateUpdate` against five conditions, any one of which
drops it:

1. the controller is null
2. its GameObject is null
3. its GameObject is inactive - the pool deactivates rather than destroys, so this catches a
   release directly
4. `IsDead`
5. **`EnemyType` no longer matches the type recorded at registration** - this is the one that
   catches recycling, and it holds no matter which hook the game used to release the instance
6. it no longer reports as a boss

Validation is fully wrapped in try/catch: an interop read against a native object that has
gone away throws, and a throw here means "no longer valid", not "propagate".

The per-frame cost is a walk over a list that holds at most a handful of entries.

---

## 4. Registration

Two postfixes, because neither is provably complete alone:

| Hook | Catches |
|------|---------|
| `VampireSurvivors.Objects.Stage.SpawnBoss()` | ordinary stage bosses; returns the controller already initialised |
| `EnemyController.AfterSpawningAsBoss()` | a boss arriving by some other route |

Registering the same boss twice is a no-op, so the overlap costs nothing.

**Do not patch `Stage.SpawnEnemy<T>(...)`.** It is generic, and patching a generic IL2CPP
method is painful for no gain here.

Methods are resolved **by name**, not by signature. Overload lists drift between versions; a
name lookup that finds nothing logs a warning, whereas a stale signature throws at load.

Boss membership is tested with `IsBoss` and `IsBossEnemy()`, never a type-name list. There are
two dozen boss controller subclasses today (`EnemyControllerBoss_BatDragon`,
`EX_Boss_Colossus`, `TP_ADV_BOSS_*`, `LEMON_BOSS_*`, `EME_TeleporterBoss`, and so on) and any
list will rot on the next DLC. `EnemyFactory._bossTypes` is the authoritative set if a
type-only test is ever needed.

---

## 5. Plate layout

Canvas units, scaled to world units by `PlateScale`.

```
+------------------------------------------+  200 x 56 units
|              Boss Name                   |  anchors (0, 0.42) - (1, 1)
+------------------------------------------+
| ####################                     |  anchors (0, 0) - (1, 0.42)
+------------------------------------------+
```

- The bar background doubles as the border; the fill is inset 2 units on every side.
- Fill width is driven by the fill rect's **anchor**, not by `Image.fillAmount`. `fillAmount`
  needs `Image.type = Filled`, whose behaviour with a plain sprite is one more thing that can
  differ between Unity versions. An anchor is arithmetic.
- Both images use a shared 1x1 white sprite. `Image` does render with a null sprite, but that
  depends on the UI default material being present; an explicit sprite costs four bytes and
  removes the question.
- Text borrows a `TMP_FontAsset` from whatever `TextMeshProUGUI` is already in the scene,
  the same way VS Evolution Helper does. Side effect: the plate is drawn in the game's own
  font rather than an Arial fallback. If no TMP text exists, the plate draws the bar only and
  warns once.

### 5.1 Vertical placement

The plate sits on top of the sprite's own bounds
(`EnemyController.EnemyRenderer.bounds.max.y`), not at a fixed height above the transform
origin. Bosses differ enormously in size, so a fixed offset either overlaps the small ones or
floats far above the large ones. `VerticalOffset` is a nudge on top of that, not the whole
distance.

### 5.2 LateUpdate, not Update

Positioning runs in `LateUpdate` so the boss has already moved for this frame. In `Update` the
plate is one frame behind, which reads as the plate sliding around on a moving boss.

The per-frame body is wrapped in a try/catch that sets `enabled = false`. This is the only
code in the mod that runs every frame; without the valve, one exception repeats sixty times a
second for the rest of the run.

---

## 6. Boss names

`EnemyController.CurrentEnemyData.bName`.

This is a Bestiary **family** name, not a row name. VS Evolution Helper hit this on the
Bestiary page, where `bName` printed "Spirit" on a row the game itself labels "Calamity", and
had to prefer the row's own label. Bosses are not usually variants, so `bName` is right here in
practice.

There is no better source:

- `EnemyData` has **no** localization helper. `WeaponData` has `GetLocalizedNameTerm`;
  enemies have no equivalent.
- There is **no** enemy or bestiary term namespace in the game's localization data.
- `EnemyItemUI._Name.text` is already localized but only exists on the Bestiary menu page, not
  during a run.

If a boss is ever misnamed, add a small `EnemyType` to name override table. Do not build a new
lookup path.

---

## 7. What the interop assemblies cannot tell you

Il2CppInterop assemblies carry field and method **stubs with no bodies**. These questions can
only be answered by running the game:

- whether `EnemyController.MainCamera` is populated at the moment a spawn hook fires
- whether a world-space canvas renders correctly through the pixel-perfect render texture
- how the Bestiary turns `EnemyData` into a display name
- how `lives`, `shieldDuration` and phase behaviour are consumed. Note that `lives` and
  `shieldDuration` exist on `EnemyData` but **no** lives, phase or shield field exists on
  `EnemyController` at all, so multi-life boss behaviour can only be observed, never read.

---

## 8. Related docs

- [`../README.md`](../README.md) - install and config
- [`USER-GUIDE.md`](USER-GUIDE.md) - player-facing description
- [`SMOKE-TEST.md`](SMOKE-TEST.md) - pre-release checklist
- [`../game-api/README.md`](../game-api/README.md) - the enemy subsystem API
