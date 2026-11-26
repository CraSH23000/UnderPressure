# Under Pressure: how the game works

The reference for everything that's **built**: what each system does and which `GameConfig` setting tunes it. Plans, ideas and the to-do list live in [HANDOFF.md](HANDOFF.md); controls and build steps live in the [root README](../README.md).

**Rule for keeping this up to date:** when a feature is built, describe it here (once) and delete its to-do from HANDOFF.md. Nothing is described in two places.

Numbers in parentheses are the values in `Assets/UnderPressure/GameConfig.asset` when this was written. The asset is the source of truth: every setting has a tooltip, and changes apply live in Play mode.

**Contents:** [Overview](#overview) · [Project basics](#project-basics) · [A round](#a-round) · [Liquids and filters](#liquids-and-filters) · [Between chambers](#between-chambers) · [The base](#the-base) · [Rooms and machines](#rooms-and-machines) · [Items](#items) · [Lockers, colors and unlocks](#lockers-colors-and-unlocks) · [Characters](#characters) · [HUD, holograms and menus](#hud-holograms-and-menus) · [Look and feel](#look-and-feel) · [Music](#music) · [Code map](#code-map) · [Known limits](#known-limits)

---

## Overview

**Under Pressure** (formerly "Pipe Dream") is a couch co-op spin on *Pipe Dream*. Up to 4 cute critters grab pipe pieces off a looping conveyor belt and lay them on a grid to route liquid from a spout before it leaks. It's set in a sterile, Portal-like test lab that gets grimier with every chamber, and the liquid gets nastier. It's built for AAA "juice": springy squash, popups, confetti, screen shake, banners and music that reacts to the action. Between chambers the team teleports via tesla-coil teleporters; a **base** ("chamber 00") is the hub with lockers, a shop and a practice board.

---

## Project basics

- **Engine:** Unity **6.6 (6000.6.9f1)**, URP, new Input System (*Active Input Handling* = Input System Package or Both).
- **Built in code:** the level, pipes, effects, sounds, synth music and HUD are all generated at runtime from procedural primitives and runtime URP materials. The only imported assets are the critter models (`Resources/Characters`), UI icons (`Resources/Icons`) and music stems (`Resources/Songs`). The only scene is a bootstrap (`Scenes/UnderPressure.unity`).
- **GameConfig:** every tunable lives in one ScriptableObject, grouped by what it changes: Board · Round & flow · Money & bonuses · Conveyor belt · Player movement · Interaction · Items & vending machine · Pit · Base · Chambers & liquids · Holograms · Camera · Lockers · Unlocks · Music · Feel & sound. New fields pick up their code defaults on an existing asset.
- **Editor menu (Under Pressure):**
  - **Create Playable Scene**: builds the bootstrap scene (and prunes missing build scenes).
  - **Create Config Asset**: creates a `GameConfig` and links it to the scene.
  - **Reset Unlocks and Stats**: wipes unlocks, stats, bought items, bottle contents and the deepest-chamber record (also `resetUnlocksOnPlay`).
  - **Log Unlock Progress**: prints where each slot stands.
  - **Prepare Shaders For Build**: see below.
- **Player builds:** materials are made in code, so before every build `Editor/UnderPressureBuild.cs` saves keep-alive materials in `Resources/UnderPressureShaderKeep`. They pull the URP Lit, Unlit and Particles shaders and the variants the game uses into the build. Without them a build shows only an empty sky (and transparent effects render solid). The exe's name comes from *Project Settings → Player → Product Name*.
- **Icon import:** `Editor/UnderPressureIconImport.cs` keeps icons full size and uncompressed. If they look stretched or blurry, right-click the Icons folder and choose Reimport.
- **Layers:** user layers **8–12** are used by index (placed pipes, climbing player, loose pieces, walk-over crosses and pit guard, player). Naming them in *Tags & Layers* is optional.
- **Saves:** PlayerPrefs, keys prefixed `up_` (looks are `pd_look0`…`pd_look3`).
- **Domain reload is off:** statics are reset in `Game.Clear()`.
- **Restart** rebuilds the level in place rather than reloading the scene.

---

## A round

### Joining
Nobody is in the level until someone joins, and the countdown waits for the first player. Press a join button any time to drop in (see the controls in the root README). Start on a controller nobody is playing on joins too, and a mouse click joins [the ghost](#the-ghost-mouse-player). Two people can share one keyboard. Each player gets their own input map locked to their device (or keyboard half), placement preview, progress ring and player color. Joined slots survive a restart. Players can leave from the pause menu (see [Menus](#pause-menu)).

### The ghost (mouse player)
An assist-style player for anyone who'd rather not (or can't) use a gamepad or keyboard: click the mouse to join as a **ghost**, a see-through wisp in the player's color with a glowing heart, dot eyes, an "o" mouth and a tail of puffs that trails and sways as it drifts. One mouse, so one ghost at most; it takes a normal player slot.
- **The lure:** a little will-o'-the-wisp in the ghost's color floats at the cursor, so you can always see where you're pointing. Its floor ring sits under it, or on the ghost's destination while it's on its way.
- **Click to move:** the ghost stays put until you click. Click open floor and it floats there; **hold** the button and it chases the cursor until you let go, then finishes the trip to where you let go. (Always following the cursor fought the follow-cam off the clock and at the base.) It moves at up to `ghostSpeedMultiplier` (1.6) × walking speed, so it doesn't teleport: carrying a piece across the room is still a trip. The move is critically damped: it eases in and stops on the spot, never overshooting; `ghostFollow` (6) sets how snappy. It floats at `ghostHoverHeight` over pipes, the belt and the pit, and nothing blocks it (or is blocked by it).
- **Clicks are orders:** click a belt piece, a floor piece, a tile, the valve or the lever, and if the ghost isn't there yet it goes and does it on arrival (a small tick confirms; the lure's ring widens while it's on an errand). The preview shows on the target the whole way. If the target changed by the time it arrives (the piece was taken, the tile filled) you get a nope. A new click replaces the order; right click calls it off, and so does rotating the piece while it's on its way to place it (it was ordered with the old rotation). Orders expire after 4 s.
- **Controls:** **left click** is Use (grab, place, steal, unscrew/swap, the valve or lever); **scroll** rotates (up = clockwise, also the valve and lever); **right click** calls off an errand, cancels an unscrew, drops what it's carrying, or lets go of the valve. Unscrewing and swapping don't need the button held: one click starts it.
- **Its locker:** click near its locker and it floats in. It only gets two rows: PLAYER (the lure, floor ring and HUD color) and COLOR (the wisp's body color, with the spray show). Scroll changes the value, click steps to the next row, right click is done. Body color is saved per slot, so it carries over if that slot plays as a critter later.
- **What it skips:** no items (no backpack, bottle or item buttons), no vending machines, no species, size or outfits, no jumping or dashing, no pit. It can't get sprayed or catch a leak (so no spray unlocks), and nobody can steal from it (your hand goes straight through). It can still steal from others.
- **Teleporters:** it floats onto a pad like anyone. The lightning strike just pops it (it's already dead) instead of charring it.
- `enableGhost` turns it off (Ghost section of GameConfig).

### The board and the belt
- **Belt:** loops around the outside of the board, level with the floor. It comes out of the left wall at the top-left and turns clockwise at each corner (pieces rotate with it). Walk onto it to grab pieces, ride it to move faster, or drop a piece on it to send it back around. Anything still riding it at the end drops into the pit just below the entrance and costs `wastedPiecePenalty` ($5). Only the first `beltPrefill` (30%) is pre-filled at the start.
- **Spawn bag:** pieces are dealt from a shuffled bag (`spawnBag`: straights, corners, crosses), so you never go long without a useful piece.
- **Belt lever:** a floor lever in the walkway corner by the belt entrance. Face it, press Use to grab it, rotate to step through `beltSpeedSteps` (0.25×–16× of `beltSpeed`). The arm swings left (slow, blue) to right (fast, red) as seen from the camera, and a lamp row shows the step, red above 1×. Use, jump or moving lets go. The lever resets to `beltSpeedDefault` at the end of every round. A faster belt brings more pieces into play, and more of them risk ending in the pit.
- **The belt waits** until someone joins. Between chambers and at the base it idles at `freePlayBeltSpeed` (0.35×); the lever still speeds that up or slows it down.

### Carrying and placing
- **Use** is an Overcooked-style context button: grab, place, steal, and **hold** to unscrew or swap.
- **Target tile** (`placeProbeTiles` 0.35): the tile under a point a third of a tile in front of you. Behind your tile's centre that's the tile you're standing on (NES-style); if the new tube would overlap your critter it hops off backwards (or sideways if a pipe is behind it). Past the centre it's the next tile. While crawling on pipes it's always the next tile. `pickupRadius` only controls grabbing belt pieces, floor pieces and steals.
- **Preview:** a frame plus a ghost of your piece. **Green** = it will latch to the spout network, **white** = placed but not connected, **orange** = hold to unscrew/swap, **red** = blocked. The ghost's solidity is `previewOpacity` (0.7) and its glow `previewGlow` (0.6); pale colors glow less so the white one keeps its shading on the white floor and you can read how it's rotated.
- **Rotating:** only the piece you're carrying (placed pieces can't be rotated). Face the belt while carrying to put a piece back.
- **Pieces in hand**, on the belt or on the floor are just the pipe. The base plate and bolts snap out from underneath with a clunk when you install one. A highlighted piece lights up its flanges.
- **Brackets vs. latches:** a piece not connected to the spout gets ground brackets that slam up to bolt it to its tile. When it joins the spout network the brackets retract and gold clamps pop on at each joint. One placement connecting several pieces latches them in a cascade (sparks, rising-pitch clanks) with a `LINKED xN!` popup.
- **Replacing:** hold Use. An unconnected piece unbolts in `unboltSeconds` (0.35 s, so a tap doesn't remove it by accident); a latched piece unscrews in `unscrewSeconds` (1.4 s, the "bad piece" penalty). You're locked in place while it unscrews. Empty-handed you pick it up; carrying a piece you swap, and the old one pops out onto the floor. Pieces with liquid in them can't be removed.
- **Stealing:** with empty hands, press Use near someone carrying a piece to take it. A stolen piece can't be stolen back for `stealCooldown` (0.75 s).
- **Loose pieces** lie on the floor. You walk through them, but each one underfoot slows you down (`loosePieceSlowdown`) and gets shoved around.

### Pieces
- **Straight, corner, cross** and the **filter** (see [Filters](#filters)). Tubes are one seamless swept mesh, so bends and bridges meet their flanges cleanly.
- **Crosses:** the lower channel dips into a trench in the plate, so the bridge only needs a gentle arch. Installed crosses are low enough to **walk straight over** in any direction, and they're ramps: walk up one and keep going onto any pipe it's joined to, which puts you on top of the run (crawl speed) with no jump.

### Moving around
- **Your body matters:** placed pipes block walking, but only the tube and its flanges; the bare base plate around it is free, so you can cut across the inside of a corner.
- **Jump** only when you need to go over a tube; it picks the tube right in front of you, even in your own tile. One pipe with a free tile behind it: you **vault** over (`vaultSeconds`). A run of pipes: you **clamber on top and crawl** at `crawlSpeedMultiplier` (40%) and climb down automatically when you step off.
- **Dash** (replaced sprint, Overcooked-style): tap for a quick burst in the direction you're pushing (or facing), also while carrying. `dashDistance` (2.2 m), `dashSpeed` (15 m/s), `dashCooldown` (0.35 s). `sprintMultiplier` is still in the config but unused.
- **The pit is open:** walk or ride into it and your critter tumbles in, dropping what it held. Reprinting costs `pitRespawnCost` ($50) from the round's earnings, or from LAB FUNDING off the clock (the bank can go negative); free at the base (`freePitInBase`). After `pitRespawnDelay` (1.6 s) it comes back on an arrival pad at its locker. Loose pieces are kept out by a guard.

### The flow
- **Countdown:** `countdownSeconds` (32 s). The spout's wheel spins faster, it shakes and puffs steam, its red eye follows the nearest player and glows hotter, and its sight glass fills over `spoutFillSeconds` (8 s). Then the liquid bursts out.
- **Fill speed:** each pipe channel takes `secondsPerPiece` (4 s) at 1×. A red pulsing warning appears at the exit when the next tile can't take the liquid.
- **Flow valve:** face the dispenser (a "[X] FLOW VALVE" prompt) and press Use to grab it. A guide shows your own buttons: rotate counter-clockwise for slower, clockwise for faster, through `flowSpeedSteps` (0.25×–16×, default step `flowSpeedDefault`). Use, jump or walking away lets go. Faster flow pays more per pipe (`moneyScalesWithFlowSpeed`). Opening the valve during the countdown also shortens it; slowing doesn't lengthen it. The valve is off once the round is over.
- **Multiple streams:** the flow runs any number of liquid fronts, each with its own grade, color, blob and trail (filters make more; see below).
- **Time to spill:** the flow looks ahead along every stream (connected, empty pipes before it would leak) and turns that into seconds at the current flow speed, so speeding up the valve brings the danger closer. The look-ahead walks every stream in lockstep, so two streams heading for each other only count half the shared run each.
- **Tension (music):** from `musicRiserSeconds` (20 s) before the spill, the music's [tension riser](#tension-riser) creeps in and climbs, topping out at the spill; laying pipe drops it back.
- **Panic:** under `panicSeconds` (8 s) to the spill, a LEAK WARNING! klaxon fires, a red warning blob sits over the most urgent stream, and the spout reads a blinking "LEAK IN n!" (seconds). Lay pipe ahead and it ends with a little "PHEW".
- **The spill:** when a stream reaches an open end, it leaks: tape-stop on the music, slow-mo, a glossy 3D slime blob with satellite droplets that splats and keeps jiggling, and the round ends.
- **Pipe burst:** two streams that meet head-on in one channel (e.g. a filter's outlets routed into each other) burst the pipe where they meet. That tube is blown away and both torn ends peel back in strips like an exploding cigar (`TornEnd`), each in its stream's color, with inner walls so the tube isn't see-through. On a cross's lower channel the whole bridge is blown off. The puddle lands under the tear, PIPE BURST! shows, and the round ends like any leak. Counted in the team stat `pipebursts`.

### Money and loops
- **Pipes:** each filled pipe pays `moneyPerPiece` ($1), times the flow speed if `moneyScalesWithFlowSpeed`, times the stream's [filter multiplier](#filter-pay). A filled pipe has a `bonusPipeChance` (5%) lucky chance to pay double (BONUS x2).
- **Loops:** when the liquid crosses a cross for the second time, that's a **LOOP** (paid the moment it crosses the centre). A plain loop pays `loopBonus` ($10). If finished loops are nested inside it (opened after it and closed before it), it pays `loopBonus × (nested + 1) × nestedLoopMultiplier`: four loops nested inside each other pay $10, $40, $60, $80. Bigger nests get bigger banners, hit-stop, confetti and a fanfare.
- **Speed bonuses (on a win):** your **average belt speed** over the round (first join to spill) earns `(average − 1) × beltBonusPerExtraSpeed` (0.5) of your earnings; `beltSlowPenalty` makes a slow belt cost the same. The **average flow speed** while the liquid runs earns `(average − 1) × flowBonusPerExtraSpeed` (0.2), smaller because faster flow already pays more per pipe. Both apply to earnings before any bonus and are listed on the results.
- **Items** multiply on top (A Count Ant, luck).
- **Win/lose:** reach `goalDistance` (8) pipes before the spill and the chamber is complete; the liquid keeps flowing for bonus money until it spills. Earnings go to LAB FUNDING on a win and are FORFEITED on a loss.

### Off the clock
After the spill nothing stops working: you can still pick up, place, rotate, unscrew and steal, and use your locker. Nothing scores, the liquid stays where it is, the monitor reads OFF THE CLOCK, and the flow valve is the only thing switched off.

---

## Liquids and filters

### The liquid list
`liquids` (Chambers & liquids) is a list of entries: `name`, `fromChamber`, `color`, HDR `glow`, `purifiesTo`, `concentratesTo`.
- **Chamber liquid:** each chamber uses the last entry whose `fromChamber` it has reached (on a tie, the later entry). Entries with `fromChamber` 0 never come out of a spout; they only exist as filter results.
- **Current spout liquids:** WATER (1), TOXIC SLIME (4), IRRADIATED (6), VOID (8), ??? (10). Filter-only: DIRTY WATER, SLIME, FILTERED WATER, PURE WATER, DEIONIZED WATER, ULTRAPURE WATER, PROTOMATTER, ANTIMATTER.
- **Water:** a name containing WATER counts as water (sprays, unlocks, the funnel refilling the bottle).
- **Where the color shows:** pipes as they fill, the liquid front, the spill, particles, the dispenser's sight glass, the lockers' reactor tube, the spout hologram's name, the base's grime tint, and the wall monitor (MEDIUM). Dark liquids like VOID stay dark in the pipes with a faint sheen; their glow shows on the front and splashes, and text uses the glow color so it stays readable.

### Links and `+` chains
- `purifiesTo` / `concentratesTo` name another entry that a filter's purified or other outlet turns the liquid into. Blank purify = generic water one grade purer; blank concentrate = it stays as it is.
- **Endless chains:** a link ending in `+` needs no entry per step. `TOXIC SLIME+` makes TOXIC SLIME+, then ++, +++, +4, +5… An entry literally named with pluses (e.g. "TOXIC SLIME+") wins over the generated one, so a single step can get its own color or links.
- **Walking back:** going against a `+` chain steps back one: purifying TOXIC SLIME++ gives TOXIC SLIME+, then TOXIC SLIME, and only then the entry's own link (SLIME).
- **Colors:** `+` steps derive their color: concentrate steps go deeper and glow more, purify steps paler and brighter. Each step changes it less, so +7 still differs slightly from +6.
- **Current chain:** ULTRAPURE WATER+ ↔ DEIONIZED ↔ PURE ↔ FILTERED ↔ WATER ↔ DIRTY WATER ↔ SLIME ↔ TOXIC SLIME → TOXIC SLIME+…; IRRADIATED and VOID purify to FILTERED WATER and concentrate into their own `+` chains; ??? purifies to PROTOMATTER(+…) and concentrates to ANTIMATTER(+…), a closed world that never links back to water.

### Filters
- **The piece:** a T-pipe with a glass canister, placed in any orientation (bought one at a time; see [Items](#items)). Liquid entering any arm fills to the canister and **splits out the other two**: two streams to keep fed, and if either leaks, the round ends.
- **Which way:** the first arm clockwise from where it came in gets the **purified** stream; the other gets the **concentrate**.
- **Split feedback:** a SPLIT! popup at the canister, and a small label over each outlet naming what comes out in that liquid's color. Labels sit at the tile edge and grow away from the filter so a filter's two labels point apart. `filterLabelScale` sizes them; `filterLabelShowsMultiplier` (off) adds the pay multiplier after the name.
- **Shatter:** feed a filter a second liquid (into an arm that already has one) and its canister **shatters**: a glass stump, flying shards, the cap pops off and clatters to the floor, FILTER SHATTERED!. It ends the round and counts toward `pipebursts`.

### Filter pay
Unfiltered spout liquid pays the plain rate. A filtered stream pays `1 + passes × filterGradeBonus + distance × filterDistanceBonus`:
- every filter pass adds `filterGradeBonus` (+50%), whichever way the filter took it (filtering is a risk, so it always pays);
- every step the result sits from water in the links (either direction, each `+` counts) adds `filterDistanceBonus` (+25%). "Water" is the first chamber's liquid. A group that never links back to water (??? and friends) counts as one step past the furthest connected liquid.

Examples: WATER → FILTERED WATER ×1.75; TOXIC SLIME → TOXIC SLIME+ ×2.5.

### Drinking it (with the funnel)
The funnel catches whichever stream leaked into the water bottle (see [Items](#items)). Water refills it; anything else **contaminates** it and changes the sip:
- **TOXIC:** faster, one extra dash, and the hiccups (random hops, HIC!).
- **IRRADIATED:** dashes go 1.7× as far, and you shed glowing yellow-green sparks.
- **VOID:** two extra dashes that go twice as far, but no extra walking speed.
- **FILTERED water:** a better sip (faster, an extra dash); **PURE** more so.
- **`+` doubles** a liquid's side effects, **`++` triples** them.
- **`+++` or deionized kills you** ("TOO... STRONG..." / "TOO... PURE...", charred remains); you're reprinted at your locker for free.
- Anything else tastes funny (normal sip effects). Catch clean water again to wash it out.

---

## Between chambers

When the liquid spills:
- **Results:** the big back-wall monitor shows distance, loops, earnings, wasted pieces, the belt and flow speed bonuses, LAB FUNDING and per-player performance notes (most pipes laid, master thief, demolition expert, butterfingers, bunny hopper). New unlocks go at the top. During play it shows an ominous "monitoring subjects" screen.
- **Camera:** drops in closer, tilts up to show the wall, and follows the group. Everyone can roam, jump and dash, and new players can join.
- **Teleporters rise** from under the floor in the top walkway like a secret elevator, one after the other: hazard doors slide open, the teleporter rides up the shaft and lands with a thunk, and the frame stays as its platform (standing on one only counts once it has landed). Each has a riveted steel deck with a hazard ring, an arch of pillars wrapped in copper and chrome pipes, a downward emitter, twin Tesla coils and an amperage meter. Its plaque reads ~~INCINERATOR~~ ~~CLONE REINTEGRATION~~ TELEPORTER, with an LED strip showing the destination.
- **Choices:** a win gives **RETRY**, **BACK TO BASE** (middle) and **NEXT**; a loss gives RETRY and BACK TO BASE.
- **Charging:** while *every* player stands on *a* teleporter, the charge builds over `readyCountdownSeconds` (3). Coils hum louder and higher, arcs crawl between coils and emitter, the meter climbs blue → yellow → red, and the HUD and monitor count up "CHARGING 74% · 6,660 AMPS". Stepping off bleeds it away fast.
- **The strike:** a random ready player is picked and the game goes to their pad (3 on NEXT and 1 on RETRY is a 3-in-4 chance of NEXT). The current roulettes between the occupied pads, then lightning strikes them all: white flash, thunder, a big shake, and every critter gets a half-second cartoon electrocution (flickering to an x-ray skeleton in a shower of sparks), chars black in a puff of smoke, stands for a beat and crumbles to dust. The coils go dead, the music cuts, and after about 2 seconds of silence everyone materialises in the next room while the music fades back in.
- **Next chamber:** the chamber number goes up, grime rises by `grimePerChamber` (0.2), and a winning chamber's earnings are added to LAB FUNDING. Retrying keeps the bank as it is. Passing a chamber unlocks the next.

### Arrival pads
Joining, arriving in a new chamber and coming back from the pit all use the same little elevator at your locker: a hatch slides open, a pad rises in a beam of your player color, your critter materialises, the pad sinks and the hatch shuts. In a new chamber the pads come up one after another.

---

## The base

The game starts here (`startInBase`). It's "chamber 00": the same kind of room, with no clock.
- **Practice:** the board and belt run in free play; the pit is free.
- **Grime:** as grimy as the deepest chamber reached, using that chamber's liquid color.
- **Layout:** the same as every chamber: lockers on the far left of the back wall, then a row of `baseVendingMachines` (6) machines: Backpack, A Count Ant, Lucky Pirate's Foot, Spider Monkey Serum, Spoon, and the water bottle as a placeholder.
- **Monitor:** the deepest chamber, lab funding, whether you have water bottles, and how many subjects have joined.
- **Leaving:** once someone has joined, the teleporters rise: the first time just CHAMBER 01; after that **FROM THE TOP: CHAMBER 01** and **CONTINUE: CHAMBER N** (the deepest reached, saved between sessions). Everyone stands on one, the same as between chambers.
- **Coming back:** pick BACK TO BASE at the end of any chamber.

---

## Rooms and machines

Every room has the same layout: lockers on the far left of the back wall (behind a strip of floor past the top belt, `closetStripDepth`), machines on the right of it. Each chamber has its own item's machine:

| Chamber | Machines |
|---|---|
| Base | Backpack, A Count Ant, Lucky Pirate's Foot, Spider Monkey Serum, Spoon, Water bottle (placeholder) |
| 1 | Water bottle |
| 2 | Funnel |
| 3 | Crossing Sign |
| `filterFromChamber` (4) onward | Filter Piece, plus that chamber's own item when it gets one |
| No item yet | One OUT OF ORDER machine |

The liquid for each chamber comes from the [liquid list](#the-liquid-list). The Inspector's **Grime** and **Chamber Number** on the bootstrap are starting values.

---

## Items

### Shop rules
- **One shared bank** (LAB FUNDING, `up_bank`, a `long`). Items are bought once for the whole team; **consumables** (the filter piece) are bought one at a time by whoever presses the button. Bought items are saved between sessions, and items bought before a player joins still reach them.
- **Item definitions** (`Items.All`): a home chamber (0 = base, n = chamber n, or "every chamber from n"), sign text and color, base price, `priceGrowth` and max level, and optionally `requires`, `consumable` or `everyChamberFrom`. Upgradeable items (the backpack) can be bought again at the same machine for `priceGrowth` × the last price. Items that need another item first say so on the machine. Each item has an `effect` line shown on its details page.
- **Vending machines** are generic, built from the item: a lit sign with the big icon (also on both sides), products on shelves in a real recessed window, a price display with short money ($12.5K, $1.6M, $4.9B), prompts and banners. They're solid, so critters walk around them. **Out-of-order** machines have faded paint, a dead "???" sign, dusty empty shelves and a crooked taped OUT OF ORDER sign; Use gets a thump.
- `enableVendingMachine` turns the shop off.

### The items

| Item | Where | Price | What it does |
|---|---|---|---|
| **Backpack** | Base | `backpackPrice` $500, ×`backpackPriceGrowth` (5) each upgrade, up to `backpackMaxLevel` (10) | Carry `backpackPiecesPerLevel` (1) spare piece per level. See below. |
| **A Count Ant** (a tiny accountant) | Base | `countAntPrice` $1,500 | Everything the team earns pays `countAntBonus` (+10%) more. |
| **Lucky Pirate's Foot** (just a peg leg) | Base | `pirateFootPrice` $2,000 | Luck +`pirateFootLuck` (50%): every lucky-break chance is ×(1 + luck). A green **LUCKY!** pops up whenever something only happened thanks to luck. Lucky breaks so far: BONUS x2 pipes and the Crossing Sign's extra crosses. |
| **Spider Monkey Serum** (pink corked vial with a monkey tail) | Base | `serumPrice` $1,800; refills `serumRefillPrice` $500 | `serumCharges` (4) doses. A dose lasts the whole chamber: walk into a pipe and you climb or vault it with no jump, and climbing and crawling on top go at walking speed. Once owned, its machine sells refills (everyone back to full). |
| **Spoon** | Base | `spoonPrice` $2,500 | "Pipes aren't real, they can't hurt you." Passive, ON DASH: dashes go through pipes (placed and loose); a dash that would end inside one carries on until clear. |
| **Water bottle** | Chamber 1 (and the base, as a placeholder) | `waterBottlePrice` (code default $750; the asset currently has $0) | `waterBottleCharges` (10) sips, each player's own. A sip lasts `waterBottleBuffSeconds` (45 s): walk ×`waterBottleSpeedMultiplier` (1.2) and dash `waterBottleDashes` (2) times in a row. **Sipping:** the critter pulls the bottle into its right hand, lifts it, tips its head back for a few glugs and puts it away (about 2 s); you can keep moving. |
| **Funnel** | Chamber 2 | `funnelPrice` $2,500 (needs the bottle) | Stand in the leak when a chamber ends to catch it in your bottle. See [Drinking it](#drinking-it-with-the-funnel). |
| **Crossing Sign** (a crossing guard's STOP paddle) | Chamber 3 | `crossingSignPrice` $3,000 | Crosses come down the belt `crossingSignBoost` (+25%) more often: some pieces turn into crosses as they're dealt, and each rides the belt with the paddle held up over it, waving (plus LUCKY! if luck tipped it). |
| **Filter Piece** (consumable) | Chamber 4 onward | `filterPrice` $400 each | Drops straight into the buyer's hands (what you held goes in your backpack, or on the floor). See [Filters](#filters). |

### Backpack
- **Stash:** holding a piece with room in the pack, grab another and the one in your hands shrinks and flies onto your back.
- **Unpack:** place a piece and the next spare pops into your hands. Empty-handed with nothing in reach, Use takes one out.
- **Its button:** press it holding a piece to stow it (PACK FULL if there's no room), or empty-handed to take the top one out.
- **Looks:** LV1 camping backpack, LV2 adds a fanny pack. From LV3 it's a white lab **hover rig**: a back plate with an arm down to a glowing emitter, spare pieces floating in a soft blue beam behind you starting by your legs (two columns once it holds more than 4). Later levels add twin emitters, orange safety trim, fins and a halo. It rides with your body (jumps, tips forward when you crawl). Stage names, each with a banner: BACKPACK, FANNY PACK ATTACHMENT, HOVER RIG, TALLER BEAM, TWIN EMITTERS, SAFETY TRIM, STABILIZER FINS, OVERCLOCKED BEAM, EXPERIMENTAL FIELD, HALO TOWER.

### Inventory and item buttons
- **Item buttons:** press an item's button any time to use it. Items you own without a button get put on a free one automatically. Passive items show a label (ALWAYS ON, ON DASH, ON SPILL) instead of a button.
- **Inventory panel:** a big screen-space panel pinned above your critter, readable at any camera distance. While it's open your critter stands still.
  - Up/down picks an item (▲▼ show with 2+ items); each row shows the icon and charges.
  - The highlighted row shows `[E] ›`: Use flips to that item's **details page** (big icon, description, effect, charges, button). Use or Jump goes back.
  - The **button bar** has one slot per item button, showing what's on it with the button glyph underneath. Press an item button to put the highlighted item there (it swaps if it was elsewhere). If the highlighted item isn't on a button, the empty slots pulse.
  - The inventory button (`Tab ×` in the corner) or Jump closes it.
  - Size: `inventoryWidth`, `inventoryRowHeight`, `inventoryTextScale`, `inventoryPadding`, `inventoryOpacity` (live in Play mode).
- **Glyphs:** `Resources/Icons` holds blank input glyphs (key cap, wide key cap, trigger, bumper, face button in Xbox colors, pill) that the game writes the key's name on, plus the item icons (bottle, backpack, ant, peg leg, STOP paddle, funnel, filter, serum, spoon) and the NEW! badge. They're rendered in Blender by `CharacterFactory~/icons.py`.

### Testing toggles
`testOwnAllItems` (the team owns every item at level 1+ without buying; bottles start empty), `unlockAllCosmetics`, `resetUnlocksOnPlay`.

---

## Lockers, colors and unlocks

### Lockers
One locker per player in every room, on the far left of the back wall, with the player's color on the header, door posts and lamp ("P1 LOCKER"). A locker stays grey and dim until someone joins in that slot. Each player appears in front of their own locker. Lockers work any time (mid-flow, between rounds, while others charge a teleporter), and only for their owner.
- **Use:** walk onto the hazard plate in front of the door and press Use. You drop what you're holding and hop inside. Up/down picks a row; left/right (or either rotate key) changes it live; Use or Jump steps back out. The panel docks along the bottom of the screen, one column per player (up to 9 rows).
- **Rows:** PLAYER (player color), SPECIES, SIZE, COLOR (body), HAT, TORSO, (ARMS), (LEGS), FEET. ARMS and LEGS appear only once something in them is unlocked.
- **Each row plays a show:**
  - **Species and size (the Bio-Reactor):** YETI, SKELETON, CHIHUAHUA, SLIME, or SHARC once unlocked, each SMALL, MEDIUM or LARGE. A glass tube rises from under the floor grate, floods with this chamber's liquid, swaps your body when full, then drains.
  - **Body color (the Spray Booth):** four nozzles blast you with the new color. Your floor ring keeps your player color, so you can always find yourself.
  - **Outfit (the Wardrobe):** a puff. Hats: baseball cap, cowboy hat, sombrero, shower cap. Torso: lab coat, flannel shirt, biker jacket. Feet: clown shoes, sneakers, military boots.
- **Saved looks:** per player slot (`pd_look0`…`pd_look3`), so P2 comes back as P2.
- **Config (Lockers):** `enableLockers`; `closetStripDepth`. With lockers off the room goes back to its old depth. (The separate Bio-Reactor, Spray Booth and Wardrobe closets are still in `Closet`, but no room builds them.)

### Colors: one list
- **`CritterLook.Colors`**, 25 entries shared by bodies and player colors (a MINT body and a MINT ring are the same mint): SKY, BUBBLEGUM, TANGERINE, LILAC, MINT, LEMON, CRIMSON, TEAL, MOSS, COCOA, SNOW, CHARCOAL, LIME, CHERRY, HONEY, GOLD, GUNMETAL, CONCRETE, ASH, TOXIC, HAZMAT, BLEACH, CHROME, IRIDESCENT, BLACKLIGHT.
- **Player colors:** each entry's `player` flag. Dark, muted, grey, white and special ones (CRIMSON, MOSS, COCOA, CHARCOAL, GUNMETAL, CONCRETE, ASH, BLEACH, CHROME, IRIDESCENT, BLACKLIGHT) are excluded so the floor ring and HUD stay visible. Colors other players hold are skipped. Your pick changes your floor ring, the HUD, your panels and your locker, and is remembered for your slot (if someone else has it by then, you get your default or the first free one).
- **Finishes (`CritterLook.Finish`):** GOLD, GUNMETAL and CHROME are metal; TOXIC glows neon green and BLACKLIGHT violet-white like under a UV lamp; IRIDESCENT shimmers through the rainbow, faster while you move (a whole-body hue shift, not a thin-film shader); HAZMAT is glossy.
- **Add new colors at the end of the list**, because saves store the index.

### Unlocks (earned cosmetics)
Cosmetics are earned or found, never bought. Locked items are skipped in the locker until earned. Personal unlocks belong to a player slot.

| Unlock | How | Where |
|---|---|---|
| Shower cap | Be standing in the spray when clean water leaks out, `unlockShowerCapSprays` (1) time | Locker, hat |
| Swim trunks | Get sprayed like that `unlockSwimTrunksSprays` (10) times | Locker, legs |
| Arm floaties | Hop over a pipe full of clean water `unlockFloatiesWaterHops` (10) times (the same pipe counts every time) | Locker, arms |
| **SHARC** | The whole team fills `unlockSharcCleanPipes` (1,000) pipes with clean water, all-time | Locker, species |

"Clean water" is any liquid whose name contains WATER. `sprayReach` (2.2 m) sets how far a leak's spray reaches.

**After an unlock:** a banner (about 3 s), a fanfare and confetti; a `NEW!  P1  unlocked SHOWER CAP (YOUR LOCKER)` line at the top of the monitor's PERFORMANCE NOTES (added straight away if the results are already up); a small gold hologram with NEW! and confetti over that player's locker until they've been inside; and in the locker, a gold starburst NEW! sticker on every row with something new (its label turns gold). Land on the new item itself and the sticker grows, wobbles and bursts confetti.

**Team stats** are saved too (e.g. `pipebursts`, clean pipes filled).

---

## Characters

- **Species:** Yeti, Skeleton, Chihuahua, Slime and Sharc, each at S/M/L. Character direction: PlateUp / Gang Beasts vacant faces with deadpan dot eyes; monsters have puppy-type charm, not blush; slim bodies and big heads, except the large size.
  - **Skeleton:** the bones take your body color, the teeth stay white, and the eyes are little glowing dots deep in the sockets.
  - **Sharc:** an upright chubby shark on stubby legs, tail flopped on the floor. The top takes your body color; belly, chin and soles stay white. A wide grin of little triangle teeth, beady eyes, gills and a dorsal fin.
  - **Slime:** a see-through half-dome of goo in your body color, with dot eyes, goo-blob hands and bubbles inside. The hat sits on top; shirts, trunks and shoes float inside. It squashes along instead of walking and leaves goo blobs that shrink away after about 5 s.
  - **Chihuahua:** a Chihuahua's head on the human test-subject body. The jumpsuit takes your body color; fur, muzzle and ears keep their colors.
- **Character Factory** (`CharacterFactory~/`, Unity ignores it): Python + headless Blender `bpy` 5.3, Cycles CPU renders. Exports to `Resources/Characters/<Species>_S/M/L.fbx`.

  | Script | What it makes |
  |---|---|
  | `python export.py` | the FBX models |
  | `python render.py` | preview renders |
  | `python icons.py <out_dir>` | all icons |
  | `python icons.py badge` / `filter` / `new` / `backpack` | single icons |

- **Part contract** (what Unity looks for): `Body` with `Head` under it (a mesh, or an empty for the slime); `Eye_L`/`Eye_R` under the Head; `Anchor_Hat`, `Anchor_Torso`; `Arm_L/R` (pivot at the shoulder) and `Hand_L/R`; `Leg_L/R` (pivot at the hip) with `Shoe_L/R` under them.
- **Material contract:** `Tint` / `TintShade` take the body color; `Slime` is a transparent tint and marks the model as a blob; `Glow` is emissive; `Pupil`; anything else keeps its own color.
- **Runtime rig (`PlayerRig`):** loads the FBX, turns it upright and facing forward whatever axes it used, and puts the feet on the floor. It animates each part in code: eyes blink, drift and track whatever you're about to grab, the head shakes, the body squashes and bobs, hands float and carry at chest height, arms aim at the hands, legs swing from the hip. Critters cheer on loops and goals and droop when the liquid spills. Hats, shirts, trunks, floaties and shoes are fitted to the measured body; the floor ring and collider grow with the size.

---

## HUD, holograms and menus

### Where the info lives
- **Spout hologram:** projected above the dispenser, always facing the camera. During the countdown, a big number (red for the last 5 s) over the liquid's name in its own color; while it flows, the name takes the big line; during panic, "LEAK IN 3!". A small line under both shows the flow speed and the pay per pipe. `spoutHologramScale` (1.15) sizes it (it grows upward from its bottom edge). It flickers and shimmers more with grime.
- **Pipes goal (top-right):** "3 / 8 PIPES" with a fill bar, gold at the goal ("PIPES GOAL!"). Hidden at the base.
- **Money hologram (top-left):** in the spout hologram's style, with **EARNINGS** (this round) and **LAB FUNDING** (the bank); at the base only LAB FUNDING. Both count digit by digit with an NES-style tick. If counting one at a time would take longer than `moneyCountMaxSeconds` (2.5 s), each step grows by `moneyCountRamp` (×1.18) so big amounts speed up and the tick climbs in pitch; spending counts down with a lower tick. On a win earnings drain into LAB FUNDING; on a spill they turn red and read FORFEITED. `moneyTickSound`, `moneyTickVolume`, `moneyTicksPerSecond` (30) are under Feel & sound. Amounts of $1 billion and up are shortened ($1.25B, $3.40T … up to Qi).
- **Player list (top-left, under the money):** one player per line in slot order (P1, P2…) with their scheme, and a faint "P2 JOIN" on the next free line.
- **Bottom-left:** "Esc / Start menu · controls". Bottom-right: the build version.
- **Back-wall monitor:** during a round a live status board (status, distance, loops, earnings, flow and belt speed, wasted pieces, subjects joined, lab funding, how to join); between rounds the results.
- **Big centre text** only appears before anyone joins and between rounds.
- **Screen-space overlay:** locker panels, the valve guide, the inventory, banners and UI confetti.
- **Filter outlet labels:** see [Filters](#filters).

### Pause menu
**Esc / Start** opens a framed panel: **Resume** (selected by default; Esc or Start again also resumes), **Controls**, **Settings**, **Players**, **Restart Chamber** (R / Select also restarts from the pause screen) and **Quit Game**.
- **Navigating:** stick, D-pad, WASD or arrows move; A, Start, Space, Enter or a click confirms; B, Esc or Backspace goes back.
- **Controls:** one scrolling list (keyboard, then controller) with aligned columns.
- **Settings:** display mode (borderless, fullscreen, windowed), resolution, V-Sync, anti-aliasing (off, FXAA, SMAA), render scale, master and music volume, screen shake. Left/right or the `<` / `>` buttons change a value; volumes and shake also have a draggable bar. Saved between sessions. Render scale only applies in builds (so it can't change the project's URP asset in the editor).
- **Players:** lists everyone; picking one makes them leave (they poof with confetti, drop what they're holding, and their slot and device are free again). It opens on the player whose controller opened the menu. Removing the last player closes the menu.

### Camera
`gameplayCameraPitch` (48) and `gameplayCameraZoom` (0.85; 1 = fit the room, higher = closer) set the round camera. Between rounds the follow-cam fits every player in frame and zooms out as they spread, tilting from `intermissionCameraPitch` (27, flatter, showing the wall) when the group is at the back to `intermissionCameraPitchFront` (20.1) at the front; `intermissionCameraZoom` (0.598) scales it. All four update live.

---

## Look and feel

- **Style (on the UnderPressure Bootstrap object):** **Grime** (0–1): 0 is a pristine white test chamber; 1 is rusted, stained and flickering, with missing or crooked wall panels, cracked observation glass, fog, film grain, chromatic aberration and a warmer, dimmer light. **Chamber Number** sets the big segment-display number on the wall. **Decor Seed** keeps the stains and broken panels the same on every restart. In play, grime comes from the chamber (`grimePerChamber`).
- **Environment characters:** the dispenser's single red eye (follows the nearest player, glows hotter as the countdown runs out) and eyes that occasionally appear behind the observation window.
- **Feel kit:** spring squash and stretch on everything, hit-stop (`hitStop`), trauma-based camera shake (`screenShakeScale`), FOV punches, slow-mo on the spill, cartoon particles, bloom on the glowing liquid, cartoon debris (`Tossed`), and code-synthesized sound effects (`sfxVolume`; the clips are public fields on `Sfx`, so real clips can be assigned).

---

## Music

Two modes, picked by `musicRealAudio`.

### Synth (built in code)
An original 80s-arcade loop with synced layers:
- **Groove** (bass, arpeggios, drums) plays all the time.
- **Lead and harmony** ride one envelope: they creep in over the countdown's last 8 seconds (to half volume) and play fully while the liquid flows. `musicLeadVolume` and `musicHarmonyVolume` are their mix levels (the harmony is a third below the melody on its second time round).
- **Spill:** the music tape-stops. **Off the clock** it comes back as just the groove.
- **Pitch and tempo:** `musicPitchSemitones` (-3) and `musicSpeed` (0.84) regenerate the music with transposed notes and a new tempo (drums keep their pitch), in place, once the slider stops moving.

### Recorded stems
A **stem** is one instrument's part of a song as its own file; played together in sync they make the full mix. Songs live in `Resources/Songs/<musicSong>`. Files are named `layer_variant` (`drums_008`, `bass_005`, `synth_012`, `vocals_003`…); files without an underscore (full mixes) are ignored and unloaded, and every stem in a song must be the same length. All variants of every layer play at once, started on the same DSP tick and sample-locked; only the chosen variant of each layer is audible.
- **Variants:** layers in `musicStemFixedLayers` (drums) are picked once; every other layer picks a new random variant (never the same twice in a row) each time the loop comes round, with a ~40 ms crossfade.
- **Lead** (`musicStemLeadLayers`, vocals): with `musicStemLeadInChambers` off (default) they're silent in chamber rounds and play only off the clock and at the base; on, they follow the synth lead's envelope. `musicStemLeadOn` off drops them entirely (not even loaded). `musicLeadVolume` is their level.
- **Flow layers** (`musicStemFlowLayers`, synth): the build-up. Silent at the base and off the clock, they creep in over the last `musicStemFlowInSeconds` (8) of the countdown, play fully while the liquid flows, and drop out for panic.
- **Everything else** (drums, bass) always plays, except during panic.
- **Changing the song or layer settings** in Play mode reloads the stems (the song restarts).
- With the riser off, the panic alarm loop is the synth one; `musicPitchSemitones`, `musicSpeed` and `musicHarmonyVolume` only apply to the synth.

| Moment | Drums + bass | Flow layers (synth) | Lead (vocals) |
|---|---|---|---|
| Base | on | off | on |
| Countdown | on | creeping in over the last 8 s | off |
| Flowing | on | full | off |
| Near a spill | ducked + muffled under the riser (or the alarm loop, riser off) | same | off |
| Spill | tape stop | tape stop | off |
| Off the clock | on | off | on |

### Tension riser
With `musicPanicRiser` on (default), the music never swaps out for panic. Instead, as the [time to spill](#the-flow) drops under `musicRiserSeconds` (20 s):
- **The riser:** pipes building up pressure, generated live (`PanicRiser`). 12 hollow pulse voices start as a low, drifting rumble (60-120 Hz) and, as the spill gets closer, rise while converging on a tense, unresolved minor chord (a minor 9th with an 11th rubbing against it) on `musicRiserRootNote` (57 = A3). A resonant filter opens and its resonance climbs until it whistles like a kettle, and the voices bubble harder as they boil. It never holds: at the spill it cuts into a **splash** (spray sweeping from a hiss to a slosh, a thump, rising bubble blips), sized by how high the tension got. Laying pipe lowers the tension, so it sinks back toward the rumble. It's its own piece, not tied to the song's key. `musicRiserVolume` (0.6).
- **The music** keeps playing underneath, ducked toward `musicPanicDuck` (0.5) and muffled by a low-pass (`musicPanicMuffle` 0.6, like your ears under pressure).
- **Heartbeat** (`musicHeartbeat`): a lub-dub on every pipe the liquid fills, two per pipe when pipes take longer than `musicHeartbeatSplitSeconds` (1.5 s), louder and a touch higher as the tension rises (`musicHeartbeatVolume` 0.8).
- `musicPanicRiser` off brings back the old behavior: at panic the music cuts to the alarm loop (below).

### Shared by both
- **Alarm loop** (riser off): at panic the music cuts to a faster, original alarm loop (`musicPanicVolume` 1.4) with its own pitch and tempo.
- **Tempo follows the valve:** `musicTempoFollowsFlow` (0.3), so 3× flow plays at about 1.6× (1 = exactly the flow speed, 0 = off). Changes start gently and accelerate, so big jumps land in about a second. With stems this is pitch-based, so it raises the pitch too.
- **Worn VHS:** grime wobbles the pitch and adds hiss, crackle, skips, dropouts and tracking warbles; `musicVhsAmount` scales it.
- **Volume:** `musicVolume` overall (plus the music volume in Settings).

---

## Code map

```
Assets/UnderPressure/
├─ Scripts/
│  ├─ Core/     Game (service locator), GameBootstrap (rooms, base vs chamber, layout, spawns, machines),
│  │            GameManager (rounds, money, loops, spills, teleporters, base mode, funnel catch), GameConfig,
│  │            Items (shop), Progress (unlocks, stats, deepest chamber), ReadyPad (teleporters + elevator),
│  │            Cash (money formatting), BuildInfo (version), Mats/Prims (runtime art), Dir (grid directions + layers)
│  ├─ Board/    GridBoard (tiles, occupancy, BFS connectivity), Spout, SpoutHologram
│  ├─ Pipes/    PipeBuilder (procedural geometry & channel paths, the filter T + canister), PipePiece (states,
│  │            fill, latch visuals, bursts, shatter), TornEnd (burst petals), GlassShatter (stump + shards)
│  ├─ Conveyor/ Conveyor (belt, spawn bag, Crossing Sign), BeltLever
│  ├─ Flow/     FlowController (multi-stream flow, filters, loops, look-ahead walkers, bursts),
│  │            LiquidGrade (what a stream is: liquid links, + chains, colors, pay)
│  ├─ Player/   PlayerController (movement, climb, interaction, locker mode, sips, ghost mode), PlayerRig (FBX
│  │            loading + part animation, the ghost wisp, Iridescent), PlayerBackpack, PlayerInputs (per-device action maps),
│  │            JoinManager (slots, player colors, join/leave), CritterLook (colors, finishes, looks + save),
│  │            Closet (lockers), Accessories, VendingMachine, ArrivalPad, NewBadge, PlacementPreview, SlimeTrail
│  ├─ UI/       Hud (roster, pipes goal, panels, NEW! stickers, UI confetti), HoloMoney, InventoryPanel,
│  │            Glyphs (+ KeyGlyph), FloatingTag, OutletLabel, PauseMenu, GameSettings
│  ├─ Style/    ChamberBuilder (room, walls, doorways, decor, Flicker, Watcher), Theme (grime, liquids), WallMonitor
│  └─ Juice/    Juice (hit-stop, slow-mo, tweens, Springy), CameraRig (shake), Fx (particles), Popups (+ Banner),
│               Sfx (synthesized audio), Music (synth + stems, ducking), PanicRiser (live tension synth),
│               CharredRemains, SlimePuddle, Tossed (debris)
├─ Editor/      UnderPressureMenu, UnderPressureBuild (shader keep-alive), UnderPressureIconImport
├─ Resources/   Characters/ (5 species × S/M/L FBX), Icons/ (glyphs, items, badge), Songs/ (music stems),
│               UnderPressureShaderKeep/
├─ CharacterFactory~/  cf.py helpers + one script per species, export.py, render.py, icons.py
├─ Scenes/      the bootstrap scene
└─ GameConfig.asset
```

- **Swapping in real art later:** `PipeBuilder.Build` and `PlayerRig.Build` are the only places that create piece and critter visuals. Replace them with prefab instantiation and keep the same transforms (`clamps`, `staples`, channel `segments`).
- **The ghost** is a mode of `PlayerController` (`IsGhost`, the `Mouse` control scheme), not a separate class: it shares all the targeting and interaction code and only swaps movement (`GhostMove`), input and its body (`PlayerRig.BuildGhost`).
- **Co-op:** `JoinManager` (on the bootstrap object) watches for join presses and keeps the list of joined slots. Each player gets a `PlayerInputs` map locked to its device or keyboard half, its own placement preview and progress ring, and a player color.

---

## Known limits

- The placeholder geometry is primitive-based (hundreds of small renderers). Fine for prototyping; real meshes will batch better.
- Stem memory: every variant of every layer is loaded and playing (muted) at once. Set the clips to *Compressed In Memory*, and keep an eye on Unity's 32-voice limit as songs grow. Everything in `Resources/Songs` ships in builds, including unused full mixes.
- Valve tempo on stems is pitch-based, so fast flow sounds chipmunky.
