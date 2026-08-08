# VS Boss Plates - user guide

**Version 0.1.1** - Vampire Survivors 1.15.x, BepInEx 6 IL2CPP.

## 1. What this mod does

Vampire Survivors never tells you how a boss fight is going. There is no boss health bar, no
boss name, and no indication of whether the thing chewing through your screen is nearly dead
or has barely been scratched. That information exists in the game's data; it is simply never
drawn.

This mod draws it. While a boss is alive, a plate follows it showing:

- a **health bar** that drains as the boss takes damage
- the **boss name**
- **current and maximum HP** as numbers

Nothing else changes. The mod reads the game's state and draws on top of it; it never writes
anything, and it touches no save data.

## 2. What counts as a boss

Two tiers, because the game's own idea of a boss is narrower than yours.

The game flags an enemy as a boss only when it is the **scheduled stage boss** - the one the
timer sends at a set minute. The Reaper counts. The XL mummies and the other strong
mini-bosses do not, even though those are exactly the enemies whose remaining health you want
to know.

**The clearest mark of a mini-boss is the chest it drops.** The game records that on the
individual enemy, so it is the one test that cannot be fooled: the same creature is plated when
the stage gave it a chest and ignored when it did not. Nothing else about a chest carrier need
stand out - the winged enemy in the Chapel has no Bestiary entry, three experience and fifteen
hit points, and is still a mini-boss. Set `IncludeTreasureCarriers` to `false` to ignore them.

Beyond that the mod also plates anything belonging to a type the game is willing to use as a
boss at all. Set `IncludeMiniBosses` to `false` for scheduled stage bosses only.

Both tiers are read from the game itself rather than from a list of names, so DLC bosses and
future bosses are included automatically and nothing needs updating when new ones arrive.

Ordinary enemies do not get a plate.

**Stage hazards do not either, even though the game calls them bosses.** Some hazards are built
on the same machinery a boss is and report themselves as one, while having no health you can do
anything about. The mod tells the two apart by asking whether the Bestiary has an entry: a
creature you fight is catalogued, a hazard is not. Set `RequireBestiaryEntry` to `false` to see
a plate on everything the game calls a boss.

**Bonus enemies are the exception to that rule.** The blue glowing bat in Mad Forest has no
Bestiary record and five hit points, so nothing above would keep it - but it is worth thirty
experience where an ordinary enemy gives one or two, and that is the whole reason you chase it.
Anything worth at least `BonusXpThreshold` experience gets a plate regardless. Set it to `0` if
you would rather not see them.

A bonus enemy is defined by being worth killing, not by being hard to kill, which is why the
test is experience rather than health.

At most `MaxPlates` plates are drawn at once, twelve by default. A wall of health bars is worse
than none.

## 3. When a plate appears and disappears

A plate appears when a boss spawns and disappears when it dies, is despawned for wandering too
far from you, or is removed by anything else.

Set `HideWhenFull` to `true` if you would rather not see a plate until the boss has actually
taken damage. This keeps an approaching boss from being announced before you reach it.

Bosses that have more than one life refill their health instead of dying. The bar refills with
them, which is the honest reading of what is happening.

## 4. Reading the plate

The bar drains left to right. The numbers beside it are current and maximum HP, shortened once
they get large:

| Shown | Means |
|-------|-------|
| `4200` | 4,200 HP |
| `18.5k` | 18,500 HP |
| `2.4M` | 2,400,000 HP |

Boss HP scales with how long the run has lasted, so the same boss will show very different
numbers at five minutes and at twenty-five.

## 5. Config reference

`BepInEx/config/com.n3rdyguy.vsbossplates.cfg`, written the first time you run the game with
the mod installed. Edit it with the game closed.

### Plates

| Key | Default | Meaning |
|-----|---------|---------|
| `Enabled` | `true` | Draw plates at all. Turning this off stops every per-frame check the mod makes, so it costs nothing while off |
| `ShowName` | `true` | Show the boss name above the bar |
| `ShowNumbers` | `true` | Show current and maximum HP on the bar |
| `HideWhenFull` | `false` | Only show a plate once the boss has taken damage |
| `IncludeTreasureCarriers` | `true` | Plate any enemy carrying a treasure chest, whatever else it looks like. The game's own mark of a mini-boss |
| `IncludeMiniBosses` | `true` | Also plate strong mini-bosses, not just the scheduled stage boss |
| `MaxPlates` | `12` | Most plates on screen at once |
| `RequireBestiaryEntry` | `true` | Only draw a plate for a boss the Bestiary knows about, so stage hazards are skipped |
| `BonusXpThreshold` | `25` | Also plate an enemy worth at least this much experience, Bestiary entry or not. Set to `0` to turn the exception off. Range 0 to 10000 |
| `MiniBossMinHp` | `20` | How much base health an enemy needs before belonging to the game's boss type set is enough on its own. Does not apply to stage bosses, chest carriers or bonus enemies. Range 0 to 10000 |
| `VerticalOffset` | `0.35` | Extra gap in world units between the top of the boss sprite and the plate. The plate already sits above the sprite, so this is a nudge rather than the whole distance. Range -2 to 5 |
| `PlateScale` | `0.008` | How large a boss plate is drawn. See the table below. Range 0.001 to 0.03 |
| `MiniBossPlateScale` | `0.005` | The same for mini-bosses, chest carriers and bonus enemies. Range 0.001 to 0.03 |
| `ScanIntervalSeconds` | `0.5` | How often the mod looks for newly spawned bosses. A boss can get its plate up to this late. Lower is more responsive but checks the live enemy list more often, and that list holds thousands of entries late in a run. Range 0.1 to 5 |

**Mini-bosses get their own size, and a smaller one by default.** There are many more of them
than there are stage bosses, and none of them is the thing you are actually worried about; a
plate the size of the Reaper's over every chest carrier turns a busy screen into a wall of
health bars. The two settings are independent, so set them equal if you would rather they
matched.

Plate size is mostly taste, so it is worth knowing what the numbers look like:

| `PlateScale` | Looks like |
|--------------|------------|
| `0.004` | Discreet. Readable if you look at it, easy to ignore otherwise |
| `0.008` | Default. Hard to miss, reads at a glance on a busy screen |
| `0.012` | Theatrical. The boss name alone spans about a third of the screen |
| `0.03` | Absurd, and available on purpose |

**Small also means blurry, and that is not something a setting can fix.** Vampire Survivors
draws the whole game to a low resolution image and scales it up - that is what keeps the art
looking like clean pixel art. The plate is drawn into that same image, so its text gets only as
many real pixels as its size on that small image allows. At `0.012` the HP numbers have roughly
a dozen pixels of height to work with and look sharp. At `0.004` they have about four, and no
amount of font tuning will recover the other eight.

So pick a size for how sharp you want the numbers, not only for how much screen you want the
plate to take.

### Hotkeys

| Key | Default | Meaning |
|-----|---------|---------|
| `TogglePlatesKey` | `F9` | Shows and hides every plate, mid-run, without restarting |
| `ToggleMiniBossesKey` | `F10` | Shows and hides mini-boss plates only, leaving the scheduled stage boss plated |

Both write the new setting back to the config file, so a toggle survives a restart. Set either
to `None` to unbind it.

`ToggleMiniBossesKey` is the one worth knowing about: when a wave of strong enemies makes the
screen busy, it clears the clutter without giving up the plate on the boss that matters.

### Debug

| Key | Default | Meaning |
|-----|---------|---------|
| `DebugVerbose` | `false` | Log every boss the mod starts and stops tracking. Only useful when a plate does not appear, or appears over something that is not a boss |

## 6. Compatibility

- Runs alongside **VS Evolution Helper**. Separate plugin, separate GUID, separate config
  file; neither knows about the other
- **BepInEx 6 IL2CPP win-x64 only.** MelonLoader crashes on Unity 6 builds of the game, and
  this plugin will not load under it
- Single player and the game's own online mode are both fine, because the mod only reads state
  and draws locally. It sends nothing and changes nothing

## 7. Troubleshooting

**No plate appears at all.** Check `BepInEx/LogOutput.log` for
`VS Boss Plates 0.1.1 loading...`. If that line is missing the plugin is not being loaded -
check the DLL is at `BepInEx/plugins/VSBossPlates/VSBossPlates.dll` and that BepInEx is the
IL2CPP build. If the line is present but you also see
`No boss spawn hooks patched`, the game has been updated in a way that moved the API the mod
hooks.

**Plate appears in the wrong place.** Adjust `VerticalOffset`. If it is horizontally wrong or
does not follow the boss at all, that is a bug worth reporting with your resolution and
whether you play windowed or fullscreen.

**Plate is too small or too large.** `PlateScale`.

**A plate over something that is not a boss.** Turn on `DebugVerbose`, reproduce, and send the
log lines. This is the specific failure the mod is built to prevent, so it is worth knowing
about.

## 8. Related docs

- [`../README.md`](../README.md) - install
- [`../CHANGELOG.md`](../CHANGELOG.md) - what changed and when
- [`RENDER-SPEC.md`](RENDER-SPEC.md) - how it works, and why it works that way
