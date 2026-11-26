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
- **Camera:** fixed, framing the whole board (the route is the puzzle). See "Follow camera" below.
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

## 2. Next up

1. **Chambers 1–10, hand-designed,** each teaching one thing (first filter, first `+` liquid, routing streams so they don't meet). This doubles as the tutorial.
2. **Money rebalance** once the chambers set the pace: flow-speed pay, the two filter bonuses and endless `+` chains all changed recently.
3. **Pipe Burster achievement and the pipe bomb** (below), as one self-contained chunk.

---

## 3. To-do list (not yet built)

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
- **Stem labels:** tag variants (busy, grimy, calm…) so `Music.PickVariant` can choose by game state instead of at random.
- **Time-stretched tempo** for stems (e.g. pre-rendered speed versions via ffmpeg) instead of pitch-based, and slower melancholic music below 1× flow.
- **Riser when you lay pipe:** right now it just sinks back toward the rumble. Idea to try: step it down (e.g. by fourths) so each pipe reads as a clear "phew".
- More songs, longer songs.

### Achievements, almanac and easter eggs
- **Lab almanac terminal (base):** a clunky lab computer in the shop area where the critters "type up" their mandatory science time logs: lifetime stats, achievements, discoveries. Home for the achievement system (and the unlocks).
- **Money tick feel:** if the NES tick gets annoying in play, try casino-style or juicier sounds, or design a sound for the fast, ramped-up count.
- **$8,388,608 (2²³), the original Pipe Dream's integer overflow score:** when the bank crosses it, everything starts glitching as if the game broke, then glass shatters like the dimension is breaking, and an achievement/notification appears: **"Escaped the Dream of Pipes"**. Needs a small achievement/notification system.

### Base and progression
- **Level-select hall:** a long room where the teleporters for unlocked chambers **rise from the floor in a grid** as you enter (mini teleporter booths, each showing its chamber number and liquid color). The big teleporter stays as "continue". Picking works like now: everyone stands on one. *(Alternative: one teleporter with a chamber dial/lever.)* Currently the base only offers FROM THE TOP and CONTINUE.
- **Display wall** in the base of locked or unaffordable items (glass cases with prices and "???" silhouettes).
- **Practice corner** (the base board already works in free play; a dedicated small board was suggested).

### Chambers and endless mode
- **Scripted chambers to 10, then endless:** 1–10 are hand-designed and teach one piece or mechanic at a time. After that, endless generates chambers that each change one thing: pressure speed, piece mix, dead cells, a second spout or drain. "Deepest chamber reached" goes in the almanac.
- **Growing the grid, with a cap:** add a row or column every few chambers, and refit the fixed camera to the board each round. Stop at the largest size where the placed piece and the critter still read at a glance (check with screenshots). Past the cap, difficulty comes from constraints instead. First check whether room geometry (walls, spout and drain placement) assumes a fixed board size.
- **Follow camera: later, if ever.** A full chase cam hides the whole route (the puzzle), the placement preview and off-screen leaks. If the size cap proves too small, try a camera that frames the board but leans toward the critter.
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

## 4. Verify in playtests

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
- Chamber camera framing after removing the right-wall machine.
- `testOwnAllItems`: bottles start empty (no purchase ran).
