# Under Pressure: hand-off notes

How we work, the decisions that stand, what's still to build, and what to check in playtests. How the built game works is in [GAME.md](GAME.md); controls and build steps are in the [root README](../README.md).

---

## 1. How we work

- **Roles:** Claude writes the code; the user tests in Unity, sends screenshots and feedback, and commits.
- **Repo:** this git repo is the Unity project (`Assets/UnderPressure` holds everything game-specific). Character models and icons are in Git LFS.
- **Compile checking:** Claude compiles every script under `Assets/UnderPressure/Scripts` (not `Editor`) with Unity's bundled Roslyn against the real Unity assemblies (`Editor/Data/Managed/UnityEngine/*.dll`, `Library/ScriptAssemblies/*.dll` minus Assembly-CSharp, and NetStandard 2.1). Code is compile-checked but **not run**; the user's playtests are the real test.
- **Config first:** every tunable goes in `GameConfig`, grouped by category (not by version), with a tooltip. Avoid hard-coding content names (songs, layers, liquids): read them from data so more can be added without code changes.
- **Docs:**
  - [GAME.md](GAME.md): how everything that's built works, with its config settings. Update it in the same change as the feature.
  - This file: plans and notes. When a to-do is built, move it to GAME.md (described once) and delete it here.
  - [README.md](../README.md): the front door (play, controls, build, release). Controls live only there.
  - Nothing is described in two places; link instead.

### Standing design decisions

- **One shared bank** ("lab funding") for the team, saved between sessions.
- **Cosmetics** are earned or found, never bought. **Shop items** use the bank and are bought once for the whole team. **Consumables** (the filter piece) are bought one at a time by whoever presses the button.
- Lockers are usable any time. The identity floor ring isn't a cosmetic: it's the **player color**.
- **Character direction:** PlateUp / Gang Beasts vacant faces with deadpan dot eyes; monsters have puppy-type charm, not blush; slim bodies and big heads, except the large size; three sizes per species, and cosmetics fit all three. Humans are parked (uncanny valley); their body and jumpsuit are reused for the Chihuahua.
- **Items on the critter:** wild passive items don't necessarily show on the critter (Haste-style), so there's no visual clutter at 50 items.
- **Absurd numbers are welcome:** Balatro-style shenanigans like 1000% luck and 1000% money are the intended late game, which is why prices scale exponentially.
- **Filters always pay**, whichever way they take a stream (it's a risk), and pay more the further the result is from water.
- **Camera:** fixed by default, framing the whole board (the route is the puzzle). A **Follow** option in Settings uses the between-rounds follow-cam during rounds too, for anyone who prefers it; fixed stays the default and the design target.
- **US spelling:** "color", not "colour", everywhere.

### Calls made along the way (easy to change)

| Topic | What it does | Where to change it |
|---|---|---|
| Removing an unconnected piece | A quick 0.35 s hold, so a tap doesn't remove it by accident | `GameConfig.unboltSeconds` |
| Swap | A hold, not a tap. The old piece is ejected as a loose floor piece. | `PlayerController.CompleteHold` |
| Movement while unscrewing | Locked in place (you're committed) | `PlayerController.Update` |
| Rotating placed pieces | Not allowed. Only the piece you're carrying rotates. | — |
| Loop bonus timing | Paid the moment the liquid crosses the centre of the cross | `FlowController` |
| Kicking players | Anyone can make anyone leave from the pause menu's Players page (fine for couch play) | `PauseMenu.BuildPlayers` |

---

## 2. In progress: grime rings (spills change the lab's grime)

Interrupted mid-work by a power cut; **uncommitted and not yet playtested.** The idea: grime stops being a fixed number per chamber and becomes a running value that spills push up or down. Spill something clean and a bright ring washes the grime back from where it landed; spill something filthy and a murky ring rolls out instead.

**What's written so far:**
- **`GameConfig` → each liquid's `spillGrime`:** how much spilling it changes the grime (negative cleans). The asset has DIRTY WATER +0.03, SLIME +0.05, TOXIC SLIME +0.08, IRRADIATED +0.1, VOID +0.12, ??? +0.12, ANTIMATTER +0.15; FILTERED −0.1, PROTOMATTER −0.1, PURE −0.2, DEIONIZED −0.4, ULTRAPURE −0.6 (about three chambers' worth back); WATER 0. Each `+` step adds half again.
- **`LiquidGrade.SpillGrime`:** a stream's value (its entry's `spillGrime` × the `+` steps; generic water without an entry: −0.1 per filter pass if clean, +0.05 if not).
- **`GameManager` (on the spill, chambers only, not the base):** `GameBootstrap.AddGrime(SpillGrade.SpillGrime)`, then `GrimeOverlay.I.Ring(spill point, what actually changed, what was asked for)`.
- **`GameBootstrap`:** `CurrentGrime` is now a running value. `AddGrime` clamps it to 0–1 and returns the real change. `GoToChamber` adds `grimePerChamber` per chamber *on top of wherever spills left it* when going forward, so cleaning carries over, and restarts from that chamber's own grime (`GrimeFor`) when going back. The base now uses the current running grime (`Theme.Apply(CurrentGrime)`), not the deepest chamber's (the `GoToBase` comment still says the old thing). Creates the overlay and calls `Snap` on every level build.
- **`Juice/GrimeOverlay.cs` + `Resources/UnderPressureShaders/GrimeOverlay.shader`** (both untracked): a full-screen film on a screen-space canvas under the HUD (sorting order 50). Each pixel is projected onto the floor, so the film and rings stick to the world as the camera moves. Film = settled level + each active ring's change inside its radius + a transient exaggeration trailing the wavefront (stronger than the real change, easing back to it). Clean rings wash out with a bright edge; grime rings roll in murk with a dark edge. Up to 8 rings (`MaxRings`, must match the shader's `MAX_RINGS`); rings expand at 9 units/s on unscaled time (the spill slow-mo doesn't slow them) and fold into the settled level once they've crossed the whole view. Tuning is in constants (`RingSpeed`, `EdgeWidth`, `TrailLength`) and shader properties (`_FilmColor`, `_MurkColor`, `_CleanGlow`, `_MaxAlpha` 0.3, `_Smudge`).
- **Debug keys** (editor and development builds): **F9** = clean ring at the mouse, **F10** = grime ring at the mouse (±0.3, applied to the real grime too, and logged).

**Still to do / check:**
- **Does it compile and run?** First thing: open Unity, check the Console, press F9/F10 in a chamber.
- **Check it in a player build:** the shader sits in `Resources/UnderPressureShaders` (like `LiquidPattern`), so it ships; if it's missing or unsupported the overlay logs a warning and turns itself off.
- **The room itself doesn't change until the next level build:** `Theme`, the stains, missing panels, flicker and fog are baked when the level is built, so after a spill only the film shows the new level; the room catches up on the next restart or chamber. Decide whether that's fine (the film *is* the live grime) or whether some of `Theme` should update live.
- **Running grime isn't saved:** a new session starts from the Inspector's grime again. Decide whether it belongs in the save (`up_` key) with the deepest chamber.
- **The base:** spills there don't change grime (on purpose, it's practice?), but the base now shows whatever the last chamber left. Confirm that's wanted, then fix the `GoToBase` comment and GAME.md's base "Grime" line.
- **Balance:** a single ULTRAPURE spill undoes three chambers. Check the numbers feel right against `grimePerChamber` (0.2), and whether the music's separate `musicGrimePerChamber` should follow the running grime too.
- **When it's done:** describe it in GAME.md (Look and feel, Liquids, the base) and delete this section.

## 2b. In progress: the room grows with the players (up to 8)

Experimental, behind `GameConfig.roomGrowsWithPlayers` (off = everything as before). The goal: drop-in joins physically stretch the room, one locker built per player, up to 8 players.

**Decided:**
- **Square growth:** the board is `growBaseSize` (6) square for one player, +1 column and +1 row per player (9×9 for 4, 13×13 for 8). The dynamic (follow) camera helps with the size.
- **Top-left stays put:** columns are added on the right and rows at the bottom, so the spout, pit, lever, back wall and lockers never move. Rows below the first get negative indices (`GridBoard.MinRow`), so every existing cell keeps its index and place. With the flag, `spoutCell` counts from the top-left.
- **The player count sets the size** (no per-chamber base size for now).
- **Mid-flow growth is wanted**, to see what it looks like.

**Phase 1 (built, playtested):** the size and lockers come from the player count when the room is (re)built. The teleporter room packs its rows tighter in small rooms and skips the express belts when it's too narrow for them (under about 8 columns).

**Phase 2 (built, playtested with debug joins): the room grows live, mid-flow included.** A join (`SpawnPlayer` with announce) calls `GameBootstrap.GrowRoom()` first, so the new locker is there to arrive at. (The F5/F6 debug keys used to test it are gone.)
- **The wall monitor** is 7–9 wide in every room: where the gap between the sign and the window is narrower (2–3 players) it covers part of the window (and stands 0.2 off the wall there), and it sits above the vending machines (`ChamberBuilder`, `MonitorOutset`).
- `ComputeLayout(players)` works out every size and position; `BuildScenery` builds the shell (`ChamberBuilder` walls, floor, colliders and decor, plus the secret door and its room) under its own `Scenery` root.
- On growth the shell is destroyed and rebuilt at the new size; everything live is kept and patched: `GridBoard.Grow` (bigger cell array, pieces keep their indices, tiles and border rebuilt), `Conveyor.Repath` (the track is re-laid; each riding piece stays on its own run and is re-projected onto it, then glides over), `WallMonitor.Relayout` (moves and resizes, keeps its text), new lockers added, vending machines slid by the wall's move, players' walk area (`SetWalkArea`), the camera frame (`CameraRig.SetFrame`, follow mode carries on) and fog.
- The secret room moves right with the wall: players in it move with it (`PlayerController.ShiftBy`); a door that was open comes back open (`SecretDoor.OpenInstantly`); `GameManager.OnRoomGrown` clears any teleporters and locked hatches and calls them up again (`lastPads`), straight away if someone's in the room.
- Skipped while the lightning has struck (the level is about to be rebuilt anyway). It never shrinks while you're in it.
- The show for now: a camera shake, clank/creak/thunk, and dust along the right wall and front edge.

**Next phases:**
3. **The show:** the new locker revealed by wall panels dropping away (reuse `SecretDoor`'s panel drop); walls and belt runs sliding out instead of jumping.
4. **Leaving:** the room keeps its size until the next rebuild, then shrinks.
5. **Up to 8 players (built, compile-checked, not yet playtested with 8):** with the flag, `JoinManager.MaxPlayers` is 8 (`Capacity`; 4 without the flag). Slot n defaults to the n-th player color (`DefaultColorIndex`, used for the player color and the default body color; slots 1–4 are unchanged). The HUD's progress rings and locker panels are sized for 8; with more than 4 players the locker panels spread across the bottom for whoever is in a locker right now, in player order, and shrink when more than four are open. The roster lists up to 8 lines. **Hardware still to test:** XInput caps Windows at **4 Xbox-type pads** (dongle, wired or Bluetooth alike); players 5–8 need non-XInput pads (DualShock/DualSense, Switch Pro, 8BitDo in Switch/DInput mode), or keyboard halves and the ghost. Plug in 5+ pads and check they can all join.

**Watch for:** the vending machines and monitor in the narrow (1–2 player) rooms; `goalDistance` probably wants to scale with the board.

---

## 3. Next up

1. **Chambers 1–10, hand-designed,** each teaching one thing (first filter, first `+` liquid, routing streams so they don't meet). This doubles as the tutorial.
2. **Money rebalance** once the chambers set the pace: flow-speed pay, the two filter bonuses and endless `+` chains all changed recently.
3. **Pipe Burster achievement and the pipe bomb** (below), as one self-contained chunk.

---

## 4. To-do list (not yet built)

### Items and economy
- **Delete items from your inventory:** roguelike insurance, so a build that's gone off the rails can be undone. Deleting keeps the record of what you bought: re-buying the serum costs full price (more than a refill); a deleted backpack can be picked up again for free, at the level you'd upgraded it to. Anything upgradeable keeps its upgrades.
- **Hazmat suit** (planned for chamber 5): −50% toxic slime vulnerability. **Blocked:** toxic slime doesn't hurt yet (a spray only splashes). First design a "sick" state (e.g. slowed, green, hiccupping) from toxic sprays, then the suit halves it. It could also be wearable.
- **Balatro-style scaling:** later items like +1000% luck or money, so the 5× backpack prices become reachable.
- **More luck hooks:** e.g. better belt pieces, a spill grace (one extra pipe), unlock rate. Always show LUCKY! when luck made the difference.
- **Count Ant and peg leg on the critter:** deliberately skipped for now (Haste-style). The peg leg would replace a leg (one shoe + one peg).
- **Backpack at high levels:** if the hover rig gets too much, make it gradually transparent. **Tune the pack and stack position per species** (it's estimated from body size).
- **Drill / jackhammer items;** a rivets item idea that was saved for the drill.
- **Bottle refills via a fountain** (the Funnel partly covers this).
- **Fill the remaining out-of-order slots** with future chambers' own items.

### Pipe Burster (achievement) and the pipe bomb
- **Pipe Burster achievement** from the `pipebursts` team stat, with bigger tiers later (e.g. 10, 100). Tie cosmetics to it (soot-blackened finish, a frayed pipe-stub hat or cigar) and the pipe bomb unlock.
- **Burst grade effects (backburner):** a burst should splash all three: each torn end's own grade plus the merged puddle. For now the spill grade is just the worse of the two streams.
- **Pipe bomb (shop item, unlocked by the achievement):** single use, leaves the inventory when used, cheaper than most items and rebuyable.
  - The pipe on the bomb's tile is destroyed, leaving a scorch mark (no un-pickupable halves to get stuck on).
  - The surrounding pieces are knocked off their brackets and become loose pieces.
  - Bombing a pipe with liquid in it bursts it and ends the run (counts toward the burst stat).
  - Critters in the blast get a short stun (the stun state is also needed for debuffs later).

### The ghost (mouse player)
- **Its look:** maybe show the old species' ears/horns through the wisp (the locker already picks its colors).
- **Teleporter gag:** during the strike the ghost flickers and grins instead of just popping.
- **Split the interaction code out of `PlayerController`** if a third kind of controller appears (the ghost is a mode flag for now).

### Liquids and filters
- **Liquid chain ideas:** VOID purifies to IRRADIATED and IRRADIATED to TOXIC SLIME, so the nastiest liquids take the longest to clean. PRIMORDIAL SOUP / DARK MATTER as named stops before the ??? chain's `+` begins. Maybe fold VOID into the ??? world.
- **Pay cap:** filter pay is uncapped, but `LiquidGrade.level` (generic water's name and color, and `Lethal`) still stops at 3 passes. Decide whether anything else should keep scaling.
- More purity effects per grade, and very pure water being valuable, possibly tied to **etching silicon wafers**.
- Maybe a visual cue for which outlet purifies (e.g. a blue band on the clockwise arm once liquid enters), if the clockwise rule isn't readable.
- **Liquid proximity effects** (liquids affecting nearby things); see "Blockers with an area effect".

### Music
- **Stem labels:** the Song asset already gates variants by music grime (per chamber). Next: tags like busy or calm so `Music.PickVariant` can also choose by what's happening (countdown, flowing, panic, off the clock).
- **Time-stretched tempo** for stems (e.g. pre-rendered speed versions via ffmpeg) instead of pitch-based, and slower melancholic music below 1× flow.
- **Riser when you lay pipe:** right now it just sinks back toward the rumble. Idea to try: step it down (e.g. by fourths) so each pipe reads as a clear "phew".
- More songs, longer songs.

### Achievements, almanac and easter eggs
- **Lab almanac terminal (base):** a clunky lab computer in the shop area where the critters "type up" their mandatory science time logs: lifetime stats, achievements, discoveries. Home for the achievement system (and the unlocks).
- **Money tick feel:** if the NES tick gets annoying in play, try casino-style or juicier sounds, or design a sound for the fast, ramped-up count.
- **$8,388,608 (2²³), the original Pipe Dream's integer overflow score:** when the bank crosses it, everything starts glitching as if the game broke, then glass shatters like the dimension is breaking, and an achievement/notification appears: **"Escaped the Dream of Pipes"**. Needs a small achievement/notification system.

### Base and progression
- **A second teleporter room for floors 11+:** the current room holds floors 01–10 (with 10 as the boss floor). Deeper floors only get NEXT for now. Maybe a door from the first room, or one room per ten floors. `GameManager.FloorCount` / `BossFloor` and `FloorSpot` are where the layout lives.
- **Boss floor flair:** floor 10's teleporter is only red so far. Ideas: a bigger or meaner teleporter, warning lights, its own sound when it rises.
- **Per-floor records** next to each floor's teleporter: best distance, fastest pass, spills.
- **Display wall** in the base of locked or unaffordable items (glass cases with prices and "???" silhouettes).
- **Practice corner** (the base board already works in free play; a dedicated small board was suggested).

### Chambers and endless mode
- **Scripted chambers to 10, then endless:** 1–10 are hand-designed and teach one piece or mechanic at a time. After that, endless generates chambers that each change one thing: pressure speed, piece mix, dead cells, a second spout or drain. "Deepest chamber reached" goes in the almanac.
- **Growing the grid, with a cap:** add a row or column every few chambers, and refit the fixed camera to the board each round. Stop at the largest size where the placed piece and the critter still read at a glance (check with screenshots). Past the cap, difficulty comes from constraints instead. The room already scales with the board (lanes, belt, pit, lever, secret door and teleporter room); check the spout placement, and that the teleporter room still fits its rows on a much smaller board.
- **Follow camera during rounds:** it exists as an opt-in setting. A full chase cam hides the whole route (the puzzle), the placement preview and off-screen leaks, so fixed stays the default. If the size cap proves too small, try a camera that frames the board but leans toward the critter.
- **Dead cells / blockers:** tiles you can't build on, which force longer routes. Ideas: a **hole in the floor** (maybe dropped or carried pieces fall in), or a **giant futuristic, alien or monster thing** taking up one or more tiles.
- **Blockers with an area effect:** a blocker can affect the tiles around it and **change the liquid flowing near it**: e.g. freeze it (slower flow), boil it (faster, steam), contaminate it (toxic), purify it, or charge it. Route past it for the bonus, or around it to avoid the penalty. Same system as liquid proximity effects.
- **Money curve:** if endless pays more as chambers get harder, plan the payout curve in the money rebalance so it doesn't run away by chamber 30.

### Characters and cosmetics
- **Remaining species:** **gremlin** and **robot** (square robot). The **human** is parked (uncanny valley).
- **Sharc:** maybe blend the head into the body.
- **Remake the torso cosmetics** (bad placeholders), in 3 sizes.
- **Hair and beard cosmetics** (code exists in the parked human factory).

### Visual polish
- **Reflection probe** in the lab so CHROME, GOLD and GUNMETAL look truly metallic.
- **True iridescence** (a thin-film shader) instead of a whole-body hue shift.
- Generalise the vending machine "product" models if items get their own Blender models (one model for shelf, tray and icon).

---

## 5. Verify in playtests

- Filter: is the clockwise-purifies rule readable? Do two streams feel good-hectic? Does the canister sit well next to crosses when walking over? Do the outlet labels still overlap anywhere?
- Liquid chain: do the `+` colors read? Does the pay feel right with both filter bonuses?
- Lethal drink flow (`+++` and deionized), contaminated sip effects, the Funnel catching the right stream. DIRTY WATER counts as water (refills the bottle); rename it if that's wrong.
- Leaving while holding the flow valve or belt lever.
- Tension riser: is 20 s of build-up a hint or a nag? Does the pressure riser build well, and does its splash land at the spill (next to the spill's own sound effect)? Does sliding back when you lay a pipe read as "phew"? Is the duck/muffle too much with the stems? Is the heartbeat audible but not annoying (one vs two per pipe)? Is 8 s the right point for the klaxon and LEAK IN n?
- Ghost: speed and snap (`ghostSpeedMultiplier` 1.6, `ghostFollow` 6)? Do click-orders feel good, and is a 4 s expiry right? Is the lure visible against every floor and liquid? Is scrolling to rotate reliable on a notched wheel and a trackpad? Is it too strong (floating over everything, one-click unscrews) or too weak?
- Crossing Sign tags (they persist for the whole belt ride).
- Backpack hover rig placement on each species; stack height at LV 9–10.
- Locker panel with 9 rows fits on screen. Does the leftmost locker's floating NEW! sign clash with the chamber number sign? Does the monitor crowd the sign?
- Pit respawn at the locker: is the walk back too punishing?
- Grime rings: see "In progress" above.
- `testOwnAllItems`: bottles start empty (no purchase ran).
