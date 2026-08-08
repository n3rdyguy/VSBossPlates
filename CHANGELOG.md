# Changelog

All notable changes to VS Boss Plates are listed here.

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
