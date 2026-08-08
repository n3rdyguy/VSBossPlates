# Smoke test

Manual checklist before any release. Game version under test: **1.15.114**. Plugin version
under test: whatever `PluginVersion` currently says in `src/Plugin.cs`.

## Setup

- [ ] MelonLoader is absent or disabled (`version.dll` renamed)
- [ ] BepInEx 6 IL2CPP win-x64 is installed and has generated its interop assemblies
- [ ] The DLL was copied with the game **closed**, and md5 matches on both sides
- [ ] `BepInEx/LogOutput.log` shows the version line, the config echo, and
      `Patched boss spawn hooks: ...`
- [ ] No `No boss spawn hooks patched` warning
- [ ] No `Boss plate update failed, disabling` error

## A plate at all

- [ ] Start any stage and reach the first boss
- [ ] A plate appears above the boss
- [ ] It shows a bar, a name, and current/max HP
- [ ] The name is the boss's actual name, not a shouted enum id such as `GIANT_BAT`
- [ ] The bar drains as the boss takes damage
- [ ] The numbers fall in step with the bar
- [ ] The plate disappears when the boss dies

## It stays attached

- [ ] The plate follows the boss while it moves, with no visible lag or sliding
- [ ] It sits above the sprite, not on top of it, for both a small boss and a large one
- [ ] Resize the game window mid-fight: the plate stays attached and stays legible
- [ ] Let the camera zoom change mid-fight: same
- [ ] Switch between windowed and fullscreen: same

## Pooling - the test that matters

- [ ] Kill a boss, then keep playing through a heavy wave of ordinary enemies
- [ ] **No plate ever appears over a non-boss**
- [ ] Trigger a second boss of a different type; its plate shows the right name and the right
      HP, not the previous boss's
- [ ] Let a boss wander far enough away to be despawned rather than killed; its plate goes with
      it

## Many at once

- [ ] Boss Rash, or any stage event with several bosses at the same time
- [ ] One plate per boss, correctly attached
- [ ] Killing one leaves the others' plates intact and correct
- [ ] No visible framerate change

## Multi-life bosses

- [ ] A boss with more than one life refills its health rather than dying
- [ ] The bar refills with it; the plate does not vanish and reappear

## Config

- [ ] `Enabled=false` leaves no plate at all
- [ ] `ShowName=false` leaves the bar and numbers
- [ ] `ShowNumbers=false` leaves the bar and name
- [ ] `HideWhenFull=true` shows nothing until the boss takes its first damage
- [ ] `VerticalOffset` moves the plate up and down
- [ ] `PlateScale` changes the plate size
- [ ] `DebugVerbose=true` logs a registration and a matching teardown per boss

## Leaving a run

- [ ] Quit to the menu mid-boss-fight; no plate survives into the menu
- [ ] Start a second run; plates still work

## Release package

- [ ] Zip contains `VSBossPlates.dll`, `README.md`, `CHANGELOG.md`
- [ ] Zip is 0 detections on VirusTotal
- [ ] Release carries the full asset set: mod zip, all four installers, `SHA256SUMS.txt`
- [ ] Release notes carry install instructions, the VirusTotal table, and the changelog excerpt
- [ ] The release is marked **Latest**, or every installer in the wild breaks
