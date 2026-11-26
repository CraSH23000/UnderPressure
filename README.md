# Under Pressure

**Couch co-op pipe-laying chaos for 1–4 players.** Grab pipe pieces off a looping conveyor belt, lay them on a grid, and route the liquid from the spout before it leaks. It's set in a sterile test lab that gets grimier, and the liquid gets nastier, with every chamber you clear.

- **Drop-in co-op:** up to 4 players on any mix of gamepads and a shared keyboard (two people per keyboard).
- **Juice everywhere:** springy squash and stretch, hit-stop, screen shake, confetti, banners and music that reacts to the flow.
- **Loops pay:** cross your own pipes to make loops. Nested loops multiply the payout.
- **Between chambers:** charge up a tesla-coil teleporter together, get struck by lightning, and rematerialise in the next room.
- **The base:** a hub with lockers for cosmetics, vending machines for team upgrades (backpacks, lucky peg legs, filter pieces…), and a practice board.
- **Built almost entirely in code:** the level, effects, sounds, music and HUD are all generated at runtime. The only imported art is the critter models and UI icons.

## Play it (no Unity needed)

1. Go to the [**Releases**](https://github.com/CraSH23000/UnderPressure/releases) page and download the latest `UnderPressure-Windows-x64-vX.Y.Z.zip`.
2. Unzip it anywhere (keep all the files together).
3. Run `UnderPressure.exe`.

Windows 10/11, 64-bit. If SmartScreen warns about an unrecognised app, choose **More info → Run anyway** (the build isn't code-signed).

### Reporting bugs

Please include the **version number**. It's in the bottom-right corner of the screen and of the pause menu (e.g. `v0.0.230`).

If the game crashed or misbehaved, attach the log too (paste these paths into File Explorer's address bar):
- **Log of the last session:** `%USERPROFILE%\AppData\LocalLow\CraSH23000\UnderPressure\Player.log`
  (`Player-prev.log` next to it is the session before; the version is on its first lines.)
- **Crash reports:** `%TEMP%\CraSH23000\UnderPressure\Crashes\`. Zip the newest `Crash_…` folder.

## Controls

Nobody is in the level until someone joins. Press your join button at any time to drop in (up to 4 players). Two people can share one keyboard.

| | Gamepad (any number) | Keyboard: WASD side | Keyboard: Arrows side |
|---|---|---|---|
| **Join** | A, X or Start | Space or E | Enter or Right Ctrl |
| Move | Left stick / D-pad | WASD | Arrow keys |
| Jump (vault one pipe, climb onto a run) | A | Space | Right Ctrl (Numpad 0) |
| Use: grab · place · steal · **hold** to unscrew/swap | X | E | Enter (Numpad Enter) |
| Rotate CCW / CW | Y / B | F / G | `;` / `'` (Numpad 1 / 2) |
| Dash | Left Trigger | Left Shift | Right Shift (Numpad .) |
| Inventory | View (Select) | Tab | Backspace |
| Item buttons 1 · 2 · 3 | RT · LB · RB | 1 · 2 · 3 | End · PgDn · PgUp |
| Pause (R / Select restarts from it) | Start | Esc | Esc |
| Play again (end screen) | A / X / Start | Space / E | Enter |

Start on a controller nobody is playing on joins instead of pausing.

**Ghost (mouse):** click to join as a ghost that floats after your cursor. A glowing lure marks your cursor. Click the floor to float there (hold to chase the cursor); click something (a piece, a tile, the valve) and the ghost floats over and does it. Scroll = rotate, right click = cancel / drop. No jumping, items or lockers; a good seat for anyone who doesn't play with a controller. In menus: stick, D-pad, WASD or arrows move; A, Start, Space, Enter or a click confirms; B, Esc or Backspace goes back.

How everything works (rules, liquids, items, unlocks and every tuning setting) is in [`docs/GAME.md`](docs/GAME.md).

## Build from source

**Requirements**
- Unity **6000.6.9f1** (Unity 6.6) with Windows Build Support. Unity Hub will offer to install it when you open the project.
- [Git LFS](https://git-lfs.com/): the character models (`.fbx`) and icons (`.png`) are stored with LFS.

**Steps**
```
git lfs install
git clone https://github.com/CraSH23000/UnderPressure.git
```
1. Open the folder in Unity Hub (**Add → Add project from disk**).
2. Open `Assets/UnderPressure/Scenes/UnderPressure.unity` and press **Play**.
   If the scene is missing or broken, use the menu **Under Pressure → Create Playable Scene**.
3. To make a player build: **File → Build Profiles → Windows → Build**. The editor script `UnderPressureBuild` runs first and saves the keep-alive materials that pull the URP shaders into the build (you can also run **Under Pressure → Prepare Shaders For Build** by hand). Without them a build shows only an empty sky.

Every gameplay number is tunable in the `GameConfig` asset (`Assets/UnderPressure/GameConfig.asset`), even during Play mode.

## Project layout

```
Assets/UnderPressure/
├─ Scripts/            all game code (Core, Board, Pipes, Conveyor, Flow, Player, UI, Style, Juice)
├─ Editor/             menu items, build shader keep-alive, icon import rules
├─ Resources/          critter FBX models, UI icons, shader keep-alive materials
├─ CharacterFactory~/  Blender (bpy) scripts that generate the critters and icons (Unity ignores this folder)
├─ Scenes/             the bootstrap scene
├─ Resources/Songs/    music stems
└─ GameConfig.asset    every tunable value
docs/
├─ GAME.md             how the game works: every system and its config settings
└─ HANDOFF.md          how we work, design decisions, the to-do list, playtest checks
```

## Making a release (maintainers)

1. Bump **Project Settings → Player → Version**.
2. Build to `bin/` (it's git-ignored).
3. Zip the build plus a copy of `LICENSE`, **without** the `*_BackUpThisFolder_ButDontShipItWithYourGame` folder, and name it `UnderPressure-Windows-x64-vX.Y.Z.zip`.
4. On GitHub: **Releases → Draft a new release**, tag `vX.Y.Z`, and attach the zip.

## Status

An early, actively developed prototype. Expect rough edges, placeholder art in places, and balance that changes often. See [`docs/HANDOFF.md`](docs/HANDOFF.md) for what's planned.

## License

[MIT](LICENSE) © 2026 CraSH23000. You're free to use, modify and share it; just keep the copyright notice.
