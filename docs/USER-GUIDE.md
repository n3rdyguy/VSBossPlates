# VS Boss Plates - user guide

**Version 0.1.0** - Vampire Survivors 1.15.x, BepInEx 6 IL2CPP.

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

Whatever the game itself considers a boss. The mod asks each enemy directly rather than
keeping a list of boss names, so DLC bosses and future bosses are included automatically and
nothing needs updating when new ones arrive.

Ordinary enemies, including the large ones that arrive in waves, do not get a plate.

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
| `VerticalOffset` | `0.35` | Extra gap in world units between the top of the boss sprite and the plate. The plate already sits above the sprite, so this is a nudge rather than the whole distance. Range -2 to 5 |
| `PlateScale` | `0.012` | How large the plate is drawn, in world units per plate unit. Raise it if the plate is hard to read on a large monitor. Range 0.002 to 0.06 |

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
`VS Boss Plates 0.1.0 loading...`. If that line is missing the plugin is not being loaded -
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
