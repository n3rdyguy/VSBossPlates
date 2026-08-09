# Smoke test

Manual checklist before any release. Game version under test: **1.15.114**. Plugin version
under test: whatever `PluginVersion` currently says in `src/Plugin.cs`.

**Run against 1.15.114 and against the 1.16 public beta**, both passing. Note which game version a
run was done against when reporting results; "works on the 1.16 beta" and "passed the checklist on
1.16" are different claims and only the second one belongs in the docs as verified. Beta builds
move, so the 1.16 pass is worth re-running when 1.16 ships.

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
- [ ] `DebugVerbose=true` logs one `[Perf]` summary every 15 seconds during a run

## Leaving a run

- [ ] Quit to the menu mid-boss-fight; no plate survives into the menu
- [ ] Start a second run; plates still work

## Release package

- [ ] Zip contains `VSBossPlates.dll`, `README.md`, `CHANGELOG.md`
- [ ] Zip is 0 detections on VirusTotal, scanned **now**, not quoted from an archive note
- [ ] Release carries the full asset set: mod zip, all four installers **zipped**, `SHA256SUMS.txt`
- [ ] Release notes carry install instructions, the VirusTotal table, and the changelog excerpt
- [ ] Every version claim in the notes matches the docs, and a beta is named as a beta
- [ ] The release is marked **Latest**, or every installer in the wild breaks

## After publishing, from outside

Everything above is done while authenticated, so none of it proves a stranger can reach the
release. 0.1.2 was published complete into a private repo and was invisible to every installer.

- [ ] The repo is **public**: `"private": false`
- [ ] `api.github.com/repos/n3rdyguy/VSBossPlates/releases/latest` returns **200 unauthenticated**
- [ ] The installer's own regex resolves to the **mod zip**, not to an installer zip - every
      `.zip` asset matches its pattern, so this is a real failure mode and not a formality
- [ ] All assets download anonymously (HTTP 200 each)
- [ ] The anonymously downloaded zip's sha256 matches `SHA256SUMS.txt`, and the DLL inside it
      matches the md5 of the archived build that was actually play-tested
- [ ] `README.md`'s release link resolves
