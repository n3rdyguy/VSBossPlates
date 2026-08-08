# Render spec

Engineering notes for VS Boss Plates. Read this before changing anything that draws or
tracks. Every rule below is written with the reason that produced it, because the reasons are
not visible in the interop assemblies.

Verified against Vampire Survivors **1.15.114**, Unity **6000.0.62f1**, BepInEx 6 **be.785**.

Also played on the **1.16 public beta** with no problems observed. Everything below was
established against 1.15.114 and the dumps have not been regenerated since, so treat 1.16 as "the
enemy API did not move far enough to break this", not as re-verified. Re-dump against 1.16 when
it leaves beta: a beta build is not a stable thing to write a spec against.

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
`Action<EnemyController> OnKilledImmediate` event. None of them are used, on purpose, and for
two independent reasons.

First, the one in section 4: **reading an `EnemyController` from inside a Harmony postfix on
this game's enemy code kills the process.** That alone rules out every hook in the list.

Second, even if it did not, `EnemyControllerBoss` **overrides** `OnRecycleEnemy` and
`Despawn`. A Harmony patch on the base `EnemyController` method does not fire for an instance
whose subclass overrides it, so patching the base would silently miss exactly the enemies this
mod tracks. Patching every declaring subclass means enumerating two dozen types that grow with
every DLC.

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

## 4. Registration: do not patch, scan

**There are no Harmony patches in this mod.** That is not a stylistic preference. It is the
result of the first build killing the game.

### 4.1 What happened

The first version registered bosses from postfixes on `Stage.SpawnBoss()` and
`EnemyController.AfterSpawningAsBoss()`. The game died within seconds of the first scene load,
leaving this in `BepInEx/ErrorLog.log`:

```
Fatal error. System.AccessViolationException: Attempted to read or write protected memory.
   at Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(IntPtr, IntPtr, Void**, IntPtr ByRef)
   at UnityEngine.Object.GetInstanceID()
   at VSBossPlates.BossRegistry.Register(EnemyController, String)
   at VSBossPlates.BossPlatePatches.AfterSpawningAsBossPostfix(EnemyController)
   at DynamicClass.DMD<EnemyController::AfterSpawningAsBoss>(EnemyController)
```

The `EnemyController` handed to that postfix is not safe to touch. `GetInstanceID()` was only
the first native call made on it, so it is where the process died; any other read would have
done the same.

### 4.2 The rule that follows, which is easy to get wrong

**A try/catch cannot save you from this.** `AccessViolationException` is a corrupted-state
exception. Since .NET 4, and by default in .NET 6, the runtime does not deliver it to managed
handlers - the process is terminated whether or not the call sits inside a `try`. The
defensive try/catch used everywhere else in this mod is worthless against it.

There is no guard to add. The only fix is to not make the call.

### 4.3 What is done instead

Discovery walks `Stage.SpawnedEnemies` from `LateUpdate`, every `ScanIntervalSeconds`
(default 0.5). Objects reached that way are fully constructed and safe to read.

The cost is that a boss gets its plate up to one scan interval late. For something that lives
for tens of seconds, that is not worth a crash class to avoid.

The list is walked **by index, not with `foreach`**: an Il2Cpp list enumerator allocates a
wrapper per step, and this runs several times a second against a list that can hold thousands
of entries late in a run.

Also worth keeping: `Stage.SpawnEnemy<T>(...)` is generic, and patching a generic IL2CPP method
is painful. It was never a good option either.

### 4.4 Boss membership

Tested with `IsBoss` and `IsBossEnemy()`, never a type-name list. There are
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
- Both texts use **TMP auto-sizing**, filling their parent inset by a few units, with a floor
  at 45% of the nominal size. A health plate is a fixed box: the text has to give, not the box.
  The first version pinned the HP text to the bar rect with wrapping off and overflow allowed,
  and `393k / 393.2k` spilled out past both ends of the bar.
- The name sits on its own **backing panel**, not on bare transparency. White text alone
  disappears against the pale stone floors, and an outline would mean building a TMP material
  variant per plate.
- The HP pair shares one unit, chosen from the maximum. Formatting each side independently
  gave `393k / 393.2k`, where the two halves disagree on precision and an untouched boss looks
  damaged.

### 5.3 Small plates are blurry, and no setting fixes that

The game draws to a **low-resolution render texture** and scales it up; that is what keeps the
art looking like clean pixel art. The plate is drawn into that same texture, so its text gets
only as many real pixels as its size on that small image allows.

At `PlateScale` 0.012 the HP numbers have roughly a dozen pixels of height and look sharp. At
0.004 they have about four. No font tuning recovers the other eight - the pixels are not there.

This is the real cost of the world-space choice in section 2, and it is worth stating next to
the benefits. If small **and** sharp is ever required, the plate has to move to a screen-space
overlay canvas, which draws at backbuffer resolution rather than render-texture resolution, and
pay for it with `WorldToScreenPoint` plus `CameraExtensions.GetRtZoomScaling()`. Nothing else
gets those pixels back.

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

`bName` is the English name and is correct today, but it is **not** the only source. An earlier
version of this document claimed `EnemyData` had no localization helpers. That was wrong.
It has several:

| Member | Returns |
|--------|---------|
| `GetLocalizedNameTerm(EnemyType)` | an I2 term |
| `GetLocalizedBestiaryNameTerm(EnemyType)` | an I2 term |
| `GetLocalizedDescription(EnemyType)` | text |
| `GetLocalizedBestiaryDescription(EnemyType)` | text |
| `GetLocalizedTips(EnemyType)` | text |

The name helpers return **terms**, not text, so using them means taking on the localization
assembly (`l2localization`) to resolve each one. Worth doing when the mod is translated. The
term is included in the `[Data]` diagnostic line, so that switch can be made against real
values rather than assumptions.

`EnemyItemUI._Name.text` is already localized but only exists on the Bestiary menu page, not
during a run.

There **is** an enemy term namespace, contrary to what this document originally said:
`GetLocalizedBestiaryNameTerm(BAT4)` returns `enemiesLang/{BAT4}bName`, composed from the enemy
id. It is returned even for an enemy with no Bestiary record, so a term existing says nothing
about whether it resolves to anything.

## 6.1 Not every boss is a boss

The game reuses its boss machinery for **stage hazards**. `BULLET_W`, the water that rises
from the bottom of the screen, reports `IsBoss` true and got a plate showing HP nobody can
act on.

The Bestiary is the game's own answer to "is this a creature the player is meant to know
about". A hazard is not catalogued; a real boss is. So the plate is gated on having a Bestiary
record - a non-empty `bName` and not `bIgnore` - rather than on the boss flag alone. Config
`RequireBestiaryEntry`, default on.

**And `EnemyFactory._bossTypes` is not a list of bosses either.** A Mad Forest run showed
`BAT4` going through the boss path with `bName='' xp=30 maxHp=5` - five hit points, an ordinary
bat. The set is closer to "types the boss spawner is allowed to use", 198 of them. It is only
useful for the mini-boss tier *combined* with the Bestiary test; on its own it would put a
health bar over every bat in the forest.

Worth noting that `bIgnore` was false for both `BULLET_W` and `BAT4`, so `bIgnore` alone is not
the discriminator. An empty `bName` is.

### 6.2 The best test is the chest

`EnemyController._hasATreasure`, alongside `_treasure` and `AttachTreasure(Treasure)`.

This is the signal every earlier rule was reaching for and missing. `FANGEL3` in the Chapel
reads `bName='' xp=3 maxHp=15` - no Bestiary record, three experience, fifteen hit points.
Nothing in its data marks it out at all, and it is still a mini-boss, because it drops a chest.

**It is a property of the instance, not of the type.** The same enemy id is a mini-boss when
the stage attached a chest to it and ordinary filler when it did not, so no type-based rule
could ever have got this right. `EnemyData` carries no drop, chest, treasure or reward field of
any kind - the whole set of its public fields was checked - which is why this had to be read
off the controller.

Checked before the type set and deliberately not restricted by it: a chest carrier qualifies
whatever it is.

One consequence for the reject memory. Whether an enemy carries a chest is decided by
`AttachTreasure`, and nothing guarantees that has run by the time the enemy first appears in
the stage's list. A scan landing in that gap would condemn a mini-boss permanently on the
strength of a half-built object, so a first rejection is provisional and is re-judged once
after 1.5 seconds. After that it stands.

### 6.3 The other exception: bonus enemies

`BAT4` is also the case that shows the Bestiary rule cannot be the whole story. It is the blue
glowing bat, and a player chases it deliberately. Nothing structural keeps it: no Bestiary
record, and five hit points, so no health-based test would either.

What marks it is the reward - `xp=30` against one or two for filler. So `BonusXpThreshold`
(default 25) admits an enemy on experience alone.

**XP is the honest signal and health is not.** A bonus enemy is defined by being worth killing,
not by being hard to kill. Health would have been the intuitive test and would have been wrong.

Still bounded by the boss type set, so the exception can only admit one of those 198 types. It
cannot start plating ordinary enemies late in a run once their XP has scaled up.

Rejections are remembered, keyed on instance id **and** type. Without that the scan re-judges
and re-logs every rejected enemy twice a second - the same forest run produced hundreds of
identical `skipped BAT4` lines. The type is part of the key because instances are pooled: the
same id returns later as a different enemy and deserves a fresh judgement. The plate cap is
deliberately **not** remembered as a rejection, because it is a passing condition.

Every registration logs a `[Data]` line under `DebugVerbose` carrying `bName`, `bInclude`,
`bIgnore`, `bHighlight`, `bIndexNumber`, `bPlaces`, `xp`, `maxHp`, `flagName`, `textureName`
and the localization term. If the rule ever hides a real boss, that line shows which field
disagreed - the rule should be corrected from it, not re-guessed.

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
