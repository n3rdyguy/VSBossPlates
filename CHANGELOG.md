# Changelog

All notable changes to VS Boss Plates are listed here.

## Unreleased

### Fixed
- **No plates appeared and the log grew by megabytes.** The optimized build cached the root
  `Transform` before adding its Canvas. Unity replaced that component with a `RectTransform`, so
  every plate creation wrote scale through a dead native wrapper and failed. The cache now takes
  the final `RectTransform`, and failed partial builds are destroyed instead of leaked.

### Changed
- **Qualifying bosses disappeared during large waves because twenty plates filled the cap.** The
  default `MaxPlates` is now 60. A stress run reached the old cap for eleven measurement windows;
  repeating it at 60 produced no observable performance drop.
- Boss-type discovery builds again against the current public-branch game API. The game moved
  the type set from `EnemyFactory._bossTypes` to the nullable `Stage.BossTypes` list.
- `DebugVerbose` now emits 15-second plugin timing and Unity-write summaries for comparing builds
  in the same in-game scenario.
- Unchanged bosses no longer resend the same fill, HP text, scale, rotation and inset values to
  Unity every frame. Discovery also carries one snapshot of an enemy's classification data
  through registration instead of repeating IL2CPP reads. These paths are dominated by native
  boundary calls rather than arithmetic, so SIMD or architecture-specific assembly would not
  improve them.

### Added
- `ShowFps` draws a small, smoothed top-left FPS counter for diagnosing perceived changes in game
  speed. It is off by default.

## [0.1.2] - 2026-08-08

Still a development build. Fixes a regression in 0.1.1 and the reason plates went missing on
boss levels.

### Fixed
- **The rising water got a plate again.** It reports itself as a stage boss in Boss Rash, and
  0.1.1 restructured the qualification rules so the boss flag short-circuits past the Bestiary
  check. Hazards are now vetoed ahead of everything, on **no experience combined with no
  Bestiary entry**. Neither half works alone: four genuine bosses award zero experience - the
  Reaper, the Maddener, the Stalker and the Trickster - and every one of them is catalogued.
- **Plates went missing on boss levels, because three-hit-point enemies were eating the limit.**
  `MOON_EYE2` is flagged by the game as a boss and has three hit points; it registered 253 times
  in a single run, dying instantly and returning from the pool each time, and every one of those
  held a slot a real boss then could not have. The health floor now applies to the game's boss
  flag as well as to its boss type set. Nothing strong is lost - the weak bosses that matter are
  already through on the chest and experience rules.
- **`MaxPlates` raised from 12 to 20.** A boss level can have a dozen bosses alive at once, and
  the ones past the limit simply got nothing.
- The log claimed to have kept enemies it never plated. The "kept" line was printed the moment an
  enemy qualified, before the plate limit could turn it away, and then repeated every scan.

### Notes
- Base health turns out to be a weak signal on its own. Real bosses in one Boss Rash run ranged
  from four hit points (`BOSS_MEDUSA1`) to 65535 (the Reaper). Experience separates far more
  cleanly - filler pays five or less, real bosses pay 25 to 50 - which is why the rules lean on
  it, and why the health floor is kept only for the handful of named bosses that pay nothing.
- `BOSS_WITCH2` is deliberately not plated. Its id says boss; the game pays three experience for
  it, the same as ordinary filler. The reward is the game's own statement of what something is
  worth, and it outranks the name a developer gave it.

## [0.1.1] - 2026-08-08

Still a development build. This release is what a day of actually playing with 0.1.0 turned up.

### Fixed
- **Ordinary enemies were getting plates.** The Tower's Scarleton has two hit points, and it was
  being plated, killed instantly, and handed straight back out of the enemy pool as another one.
  It slipped through because it is in the game's boss type set *and* it is in the Bestiary - and
  the Bestiary separates hazards from creatures, not filler from bosses. There is now a base
  health floor on that route, `MiniBossMinHp`, default 20.
- **A boss could lose its plate for its whole life if its chest arrived late.** Whether an enemy
  carries a treasure is decided by the game after the enemy appears, and the mod gave it 1.5
  seconds and one look before deciding permanently. It now gets ten seconds and five looks. The
  cost of being wrong was lopsided: rechecking an ordinary enemy is one cheap read, while a
  mini-boss judged too early never gets a plate at all.
- **Plate text sat high in its box.** TextMeshPro centres on the full line box, ascender and
  descender included, and the game's font has a tall ascent with almost nothing below the
  baseline in these strings. It now centres on the glyphs.
- **The scan registered enemies that validation dropped on the same tick**, forever - one
  inactive enemy produced 147 register/drop pairs in a single run. The two paths now share one
  check rather than each having their own.
- HP numbers used the system's decimal separator, so `1.8M` rendered as `1,8M` on a Danish
  machine. Invariant everywhere now.
- The two halves of the HP pair scaled independently, so an untouched boss could read
  `393k / 393.2k`. One unit is chosen from the maximum and applied to both.

### Added
- **`MiniBossPlateScale`.** Mini-boss plates are drawn smaller than boss plates, and separately
  configurable. There are many more of them and none is the thing you are actually worried about.
- **`MiniBossMinHp`.** How much base health an enemy needs before belonging to the game's boss
  type set is enough on its own. Does not apply to stage bosses, chest carriers or bonus enemies,
  which qualify on their own.
- **An installer**, for Windows, Linux and both Mac architectures. It will not remove BepInEx
  while other mods are using it, which the installer it was forked from does with only a warning.
- The log now says *why* an enemy was kept or skipped, in both directions.

### Notes
- The four ways an enemy can earn a plate are alternatives, not a chain of gates, and that
  ordering matters more than it looks. An earlier cut of the health floor sat in front of the
  experience rule and would have silently dropped the blue glowing bat - five base hit points -
  along with `BOSS_HARPY` and `BOSS_SKULL2`, which are also five and are carried entirely by the
  chest rule.
- `xp=0` alongside an enormous `maxHp` turns out to be the clearest hazard signature in the data:
  `BULLET_W` reads 65535, `BOSS_XLLEDA` reads 8888. Both are correctly excluded today by the
  Bestiary rule; this is held in reserve.

## [0.1.0] - 2026-08-08

First release. **Development build** - the mod works and has been played, but it has been
tested by one person on one machine, and the version number says so.

### Added
- **Boss health plates.** While a boss is alive, a plate follows it showing a health bar, its
  name, and current and maximum HP. The game draws none of this: there is no boss health UI of
  any kind in Vampire Survivors, and it never names the boss you are fighting.
- **Mini-bosses are included, and the game's own boss flag was the wrong test.** That flag marks
  only the boss the stage timer sends, so the Reaper qualified and the XL mummies did not. The
  test that works is the treasure chest: a mini-boss is an enemy carrying one, and that is
  recorded on the individual enemy rather than on its type, so the same creature is a mini-boss
  on one spawn and ordinary filler on the next.
- **Bonus enemies too, judged on experience rather than health.** The blue glowing bat in Mad
  Forest has five hit points and is worth thirty experience. It is defined by being worth
  killing, not by being hard to kill, so `BonusXpThreshold` admits it and no health-based test
  would have.
- **Stage hazards are kept out.** The game builds some hazards on the same machinery a boss
  uses, and a rising wall of water reports itself as a boss while having no health you can act
  on. The Bestiary is what separates a creature from a hazard, so a plate needs a Bestiary
  record. Config `RequireBestiaryEntry`.
- **Mini-boss plates are drawn smaller than boss plates**, and separately configurable. There
  are many more of them and none is the thing you are actually worried about.
- **`F9` hides every plate, `F10` hides only mini-boss plates**, leaving the stage boss plated.
  Both survive a restart.
- Config: `Enabled`, `ShowName`, `ShowNumbers`, `HideWhenFull`, `IncludeMiniBosses`,
  `IncludeTreasureCarriers`, `RequireBestiaryEntry`, `BonusXpThreshold`, `MaxPlates`,
  `VerticalOffset`, `PlateScale`, `MiniBossPlateScale`, `ScanIntervalSeconds`, `DebugVerbose`,
  and the two hotkeys.

### Notes on how it works

- **The plate is a world-space canvas, not a screen overlay.** The game renders through a
  render texture, so `WorldToScreenPoint` returns render-texture coordinates rather than screen
  pixels, and a screen-space plate would need that conversion redone on every window resize and
  every camera zoom. The cost of the world-space choice is sharpness, because the plate is drawn
  into that same low-resolution image - which is why the default size is not smaller than it is.
- **There are no Harmony patches.** The first version registered bosses from a patch postfix and
  killed the game with an `AccessViolationException` on the first native read. That is a
  corrupted-state exception, which .NET does not deliver to managed handlers, so no `try`/`catch`
  could have caught it. Bosses are found by scanning the stage's own enemy list instead.
- **Enemies are pooled and never destroyed**, so a plate keyed on object identity would follow an
  instance into its next life as a different enemy. Every tracked boss is revalidated each frame,
  and the check that catches recycling compares the enemy's current type against the type
  recorded when tracking began.

### Known limitations

- Tested against Vampire Survivors 1.15.114 only.
- Small plates are blurry, and no setting fixes that. The game draws to a low-resolution image
  and the plate is drawn into it, so a small plate simply has few real pixels for its text.
- Boss names come from the Bestiary family name, which is right for bosses but would be wrong on
  a variant enemy.
- No installer yet. Drop the DLL in by hand.
