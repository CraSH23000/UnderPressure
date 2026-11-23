# Under Pressure (Unity 6.3 LTS or 6.6 · URP · Input System)

A playable greybox of the core loop: grab pipes off a moving conveyor, carry them as a little plumber, build from the spout, and survive the flow. Everything (level, character, sounds, effects, HUD) is built from code, so there are **no art or audio assets** to import.

## Setup (about 2 minutes)

1. Unity Hub → **New project** → Unity **6.6** (or 6.3 LTS) → **Universal 3D** template.
2. *Edit → Project Settings → Player → Active Input Handling* = **Input System Package (New)** (or **Both**). The Universal 3D template already includes the Input System package; if yours doesn't, add it from the Package Manager.
3. Copy the `UnderPressure` folder from the zip into your project's `Assets` folder, so you have `Assets/UnderPressure/Scripts`, `Assets/UnderPressure/Editor` and this README.
4. Menu bar → **Under Pressure → Create Playable Scene**, then press **Play**.

**Updating:** copy the new `UnderPressure` folder over the old one and let it overwrite. Overwriting keeps Unity's `.meta` files, so your scene and `GameConfig` asset stay linked to the scripts. Don't delete and re-add the scripts: Unity would give them new IDs, and the scene and config would show "missing script".

Optional: **Under Pressure → Create Config Asset** creates a `GameConfig` asset and links it to the scene, so you can tune every number in the Inspector, even during Play mode.

The config is grouped by what it changes: Board, Round & flow, Money & bonuses, Conveyor belt, Player movement, Interaction, Pit, Chambers & liquids, Camera, Cosmetic closets, Unlocks, Music, Feel & sound.

> **Player builds:** materials are made in code, so before every build the editor script `UnderPressureBuild` saves a few keep-alive materials in `Assets/UnderPressure/Resources/UnderPressureShaderKeep`. These pull the URP Lit, Unlit and Particles shaders, and the variants the game uses, into the build. Without them a build shows only an empty sky. You can also run it by hand: **Under Pressure → Prepare Shaders For Build**. The exe's name comes from *Project Settings → Player → Product Name*.
>
> **Layers:** the game uses user layers **8, 9, 10, 11 and 12** by index (placed pipes, climbing player, loose pieces, walk-over crosses and pit guard, player). Naming them in *Tags & Layers* is optional.

## Controls (drop-in co-op, up to 4 players)

Nobody is in the level until someone joins, and the countdown waits until the first player does. Press your join button at any time to drop in.

| | Gamepad (any number) | Keyboard: WASD side | Keyboard: Arrows side |
|---|---|---|---|
| **Join** | A or X | Space or E | Enter or Right Ctrl |
| Move | Left stick / D-pad | WASD | Arrow keys |
| **Jump** (vaults one pipe, climbs onto a run of pipes) | A | Space | Right Ctrl (Numpad 0) |
| **Use**: grab · place · steal · **hold** to unscrew/swap | X | E | Enter (Numpad Enter) |
| Rotate counter-clockwise / clockwise | Y / B | F / G | `;` / `'` (Numpad 1 / 2) |
| Dash | Left Trigger | Left Shift | Right Shift (Numpad .) |
| Pause (R / Select restarts from the pause screen) | Start | Esc | Esc |
| Play again (end screen) | A / X / Start | Space / E | Enter |

Two people can share one keyboard, so you can test co-op with a single controller (or none).

**Stealing:** with empty hands, press Use near someone carrying a piece to take it. A stolen piece can't be stolen back for 0.75 s (`stealCooldown`).

**Flow valve:** face the dispenser (a "[X] FLOW VALVE" prompt appears) and press Use once to grab the valve. A guide shows your own buttons: rotate counter-clockwise for slower, clockwise for faster, with the current speed and a row of steps. Press Use again, jump, or just walk away to let go. Speeds come from `flowSpeedSteps` (default 0.5×–3×). Faster flow pays more per pipe (`moneyScalesWithFlowSpeed`). Opening the valve during the countdown also shortens the countdown; slowing it doesn't lengthen it.

**Belt lever:** a floor lever in the walkway corner by the belt entrance (top-left). Face it, press Use to grab it, and use rotate to step through `beltSpeedSteps` (default 0.5×–3× of `beltSpeed`). The arm swings left (slow, blue mark) to right (fast, red mark) as seen from the camera, and a lamp row on the base shows the step, red when faster than 1×. Use, jump or moving lets go. On a win, your **average** belt speed over the round (from the first join to the spill) earns `(average − 1) × beltBonusPerExtraSpeed` of your earnings as a bonus. That's +50% at an average of 2× by default. Turn on `beltSlowPenalty` to make a slow belt cost the same percentage. Similarly, the **average flow speed** while the liquid runs earns `(average − 1) × flowBonusPerExtraSpeed`. That's 0.2 by default, so +20% at an average of 2×; it's smaller because faster flow already pays more per pipe. Both bonuses apply to your earnings before any bonus is added, and they're listed on the end screen. A faster belt also brings more pieces into play, so more of them risk ending up in the pit.

**The belt is level with the floor:** walk onto it to grab pieces, ride it to move faster, or drop a piece on it to send it back around. **Crosses are ramps:** walk up a placed cross and keep going onto any pipe it's joined to. That puts you on top of the run, the same as climbing on (crawl speed, difficult terrain), with no jump needed. Walk back onto the cross and you're walking again, down the other side.

**The pit is open:** walk or ride into it and your critter tumbles in, dropping whatever it was holding. Reprinting it costs `pitRespawnCost` ($50). That comes out of the round's earnings, or out of LAB FUNDING when off the clock, and the bank can go negative. After `pitRespawnDelay` (1.6 s) it comes back on an arrival pad at its spawn spot. Loose pieces are still kept out by a guard. The belt waits until someone joins. Between chambers it idles at `freePlayBeltSpeed` (0.35×) so there's always something to play with. The lever goes back to its default step at the end of every round, and you can still use it off the clock to speed that idle up or slow it down.

**Placing** (`placeProbeTiles`, default 0.35): the target is the tile under a point a third of a tile in front of you. Behind your tile's centre, that's the tile you're standing on (NES-style), and your critter hops off the new pipe backwards, or sideways if a pipe is behind it. Once you're past the centre, it's the next tile. While crawling on pipes, it always targets the next tile. `pickupRadius` is separate: it only controls grabbing belt pieces, floor pieces, and steals.

**The belt** loops around the outside of the board. It comes out of the left wall at the top-left and turns clockwise at each corner (pieces rotate with it, so a piece's rotation changes at every turn). Anything still riding it at the end drops into the pit just below the entrance and costs `wastedPiecePenalty` ($5). Only the first 30% of the belt is pre-filled at the start (`beltPrefill`).

## Liquid progression

What flows through the pipes gets worse the deeper you go. `liquids` in the config is a list of name, `fromChamber`, color and HDR glow, and each chamber uses the last entry it has reached:
- Chamber 1: **CLEAN WATER** (pale blue)
- Chamber 3: **TAP WATER?**
- Chamber 4: **TOXIC SLIME**
- Chamber 6: **IRRADIATED**
- Chamber 8: **VOID** (black with a violet glow)

Dark liquids such as VOID stay dark in the pipes with just a faint sheen; their glow shows on the liquid front and the splashes. It colors the pipes as they fill, the liquid front, the spill, the particles and the dispenser's sight glass (just a sliver at the bottom during the countdown, filling up over `spoutFillSeconds` as the spout fills, then full while it flows), and it's listed on the wall monitor as MEDIUM. Add entries or reorder them freely. The spill is now a glossy 3D slime blob with satellite droplets that splats out and keeps jiggling, rather than a flat decal.

## Where the info lives

- **Spout hologram:** a projected display above the dispenser shows the countdown ("FLOW IN 36", red for the last 5 seconds), "FLOWING", or "LEAK IN 3!" during panic, plus the flow speed, money per pipe and a distance bar.
- **Back-wall monitor:** during a round it's a live status board (status, distance, loops, earnings, flow and belt speed, wasted pieces, subjects joined, lab funding, how to join). Between rounds it's the scoreboard.
- **Money hologram (top-left):** a projected readout in the spout hologram's style, with **EARNINGS** (this round) and **LAB FUNDING** (the bank); at the base only LAB FUNDING shows. Both count digit by digit with an NES-style score tick. If counting one at a time would take longer than `moneyCountMaxSeconds` (2.5 s), each step grows by `moneyCountRamp` (×1.18) so big amounts speed up exponentially and the tick climbs in pitch; spending counts down with a lower tick. On a win the earnings drain into LAB FUNDING; on a spill they turn red and read FORFEITED. Turn the tick off with `moneyTickSound`, or change `moneyTickVolume` and `moneyTicksPerSecond` (30), all under Feel & sound. LAB FUNDING is saved between sessions, and amounts of $1 billion and up are shortened ($1.25B, $3.40T … up to Qi).
- **HUD:** kept small, so it works on laptops: the money hologram and the player list (top-left), a small join hint (top-right) and "Esc / Start menu · controls" (bottom-left). The big centre text only appears before anyone joins and between rounds.

## Menu

**Esc / Start** opens a framed pause panel: **Resume** (selected by default; Esc or Start again also resumes), **Controls**, **Settings**, **Restart Chamber** and **Quit Game**.
- **Navigating:** stick, D-pad, WASD or arrows move the selection; A, Start, Space, Enter or a mouse click confirms; B, Esc or Backspace goes back.
- **Controls:** one scrolling list (keyboard, then controller) with aligned columns. Scroll with the mouse wheel, stick, D-pad or arrows.
- **Settings:** display mode (borderless, fullscreen or windowed), resolution, V-Sync, anti-aliasing (off, FXAA, SMAA), render scale, master and music volume, and screen shake. Use left/right, or click the `<` / `>` buttons, to change a value. Volumes and screen shake also have a bar you can click or drag. Settings are saved between sessions. Render scale only applies in builds, so it can't permanently change your project's URP asset in the editor.

## Camera

`gameplayCameraPitch` (48) and `gameplayCameraZoom` (1 = fit the room; higher = closer) set the round camera. Between rounds, the follow-cam fits *every* player in frame and zooms out as they spread. It tilts from `intermissionCameraPitch` (30, flat, showing the wall) when the group is at the back to `intermissionCameraPitchFront` (58, steep) when they're at the front. `intermissionCameraZoom` (1) scales it. Lower pitch = more horizontal = more back wall. All four update live from the config asset.

## Between chambers (no lockdown)

When the liquid spills:
- **Results:** the big monitor on the back wall shows them. That covers distance, loops, earnings, wasted pieces, the belt and flow speed bonuses, **LAB FUNDING** (the shared bank) and per-player performance notes (most pipes laid, master thief, demolition expert, butterfingers, bunny hopper). During play, the monitor shows an ominous "monitoring subjects" screen.
- **Camera:** it drops in closer, tilts up to show the wall, and follows the group. Everyone can roam, jump and dash, and new players can join.
- **Teleporters:** government-lab teleporter chambers rise out of the floor in the top walkway. Each has a riveted steel deck with a hazard ring, an arch of pillars wrapped in copper and chrome pipes, a downward emitter, twin Tesla coils and an amperage meter. A steel plaque on the crossbar reads ~~INCINERATOR~~ ~~CLONE REINTEGRATION~~ TELEPORTER, with the strike-outs painted in the same red, and has an LED strip showing the destination. Winning gives you **RETRY**, **BACK TO BASE** (in the middle) and **NEXT**; losing gives **RETRY** and **BACK TO BASE**. They come up from under the floor like a secret elevator, one after the other.
- **Charging:** while *every* player stands on *a* teleporter, the charge builds over `readyCountdownSeconds` (3). The coils hum louder and higher, arcs crawl between the coils and the emitter, the amperage meter climbs from blue to yellow to red, and the HUD and monitor count up "CHARGING 74% · 6,660 AMPS". Stepping off makes it bleed away fast.
- **The strike:** a random ready player is picked, and the game goes to their pad (3 on NEXT and 1 on RETRY is a 3-in-4 chance of NEXT). The current jumps between the occupied pads, then lightning strikes every occupied teleporter. There's a white flash, thunder and a big shake, and every critter gets a half-second cartoon electrocution: it flickers between its color and an x-ray body with a glowing white skeleton inside, jittering in a shower of sparks. Then the skeleton chars black in a puff of smoke, stands there for a beat, and crumbles to dust on the wind. The coils go dead after the strike. The music cuts dead, and after about 2 seconds of silence everyone materialises in the next room with a crackle of sparks while the music fades back in.
- **Next chamber:** the chamber number goes up, grime rises by `grimePerChamber` (0.2), and a winning chamber's earnings are added to LAB FUNDING. Retrying keeps the bank as it is.

The Inspector's **Grime** and **Chamber Number** are starting values; changing them still takes effect on the next rebuild.

## The base

The game starts at the base (`startInBase`, under Base in GameConfig). It's "chamber 00": the same kind of room, with no clock.
- **Lockers:** one per player in the back wall, with the player's color on the header, door posts and lamp ("P1 LOCKER"). A locker stays grey until someone joins in that slot. Each one is all three closets in one: player color, species, size, body color, hat, torso, the secret rows and feet. Changing species or size raises the reactor tube for the flood-and-swap; body color fires the spray nozzles; clothes get the wardrobe's puff. You can only use your own locker, and that's also where you appear when you join.
- **Player color:** the top row of a locker, PLAYER. Colors other players already have are skipped. Your floor ring, the HUD, your panels and your locker all change with it, and it's remembered for your slot next time (if someone else has it by then, you get your default or the first free one).
- **One color list:** body colors and player colors come from the same list (`CritterLook.Colors`), so a MINT body and a MINT ring are the same mint. Bodies can be any of the 25: SKY, BUBBLEGUM, TANGERINE, LILAC, MINT, LEMON, CRIMSON, TEAL, MOSS, COCOA, SNOW, CHARCOAL, LIME, CHERRY, HONEY, GOLD, GUNMETAL, CONCRETE, ASH, TOXIC, HAZMAT, BLEACH, CHROME, IRIDESCENT, BLACKLIGHT. Some have a special finish on the critter (`CritterLook.Finish`): GOLD, GUNMETAL and CHROME are metal; TOXIC glows neon green and BLACKLIGHT glows violet-white like it's under a UV lamp; IRIDESCENT shimmers through the rainbow, faster while you move; HAZMAT is glossy. Player colors skip the dark, muted, grey, white and special ones (CRIMSON, MOSS, COCOA, CHARCOAL, GUNMETAL, CONCRETE, ASH, BLEACH, CHROME, IRIDESCENT, BLACKLIGHT) that would vanish as a floor ring or on the HUD; each entry's `player` flag decides. Add new colors at the end of the list, because saves store the position.
- **Layout:** the same as every chamber (lockers on the far left of the back wall, the water machine on its right), plus the extra machines in a row.
- **Shop:** the first machine sells water bottles (its lit sign has the big bottle icon and H2O, and the icon is on both its sides too; the machines stand far enough apart to see them). Its bottles sit on shelves in a real recess behind the glass. The others (`baseVendingMachines`, 4 in all) are placeholders: faded paint, a dead "???" sign, empty dusty shelves and a crooked taped-on OUT OF ORDER sign. Pressing Use on one just gets a thump. Machines are solid now, so critters walk around them.
- **Practice:** the board and belt work in free play, like off the clock, so you can learn the controls with nothing at stake. Falling in the pit is free here (`freePitInBase`).
- **Grime:** the base is as grimy as the deepest chamber you've reached, and uses that chamber's liquid color.
- **Monitor:** shows the deepest chamber, lab funding, whether you have water bottles, and how many subjects have joined.
- **Leaving:** once someone has joined, the teleporters rise. (Passing a chamber unlocks the next one, so CONTINUE takes you there even if you went back to base instead of on.) The first time there's one, to chamber 01. After that there are two: **FROM THE TOP: CHAMBER 01** and **CONTINUE: CHAMBER N** (the deepest you've reached). Everyone stands on one, the same as between chambers.
- **Coming back:** pick **BACK TO BASE** at the end of any chamber.

The deepest chamber is saved between sessions. **Reset Unlocks and Stats** clears it too.

## Arrival pads

Joining, arriving in a new chamber, and coming back from the pit all use the same little elevator:
1. A hatch slides open in the floor.
2. A pad rises in a beam of your player color.
3. Your critter materialises on it.
4. The pad sinks and the hatch shuts.

In a new chamber the pads come up one after another.

## Off the clock

After the spill, the round is over, but nothing stops working. You can still pick up, place, rotate, unscrew and steal pieces, and use the closets. Nothing scores, the liquid stays where it is, and the monitor reads OFF THE CLOCK. The only thing switched off is the flow valve.

## Dash

The old sprint button now **dashes**, Overcooked style: tap it for a quick burst in the direction you're pushing (or facing). You can dash while carrying a piece. Tune it in the config's Player movement section:
- `dashDistance` (2.2 m)
- `dashSpeed` (15 m/s; distance ÷ speed is how long it lasts)
- `dashCooldown` (0.35 s)

`sprintMultiplier` is still in the config but isn't used right now.

## Items, inventory and the vending machine

- **Vending machine:** it stands against the back wall on the right in every room, facing you, with the big bottle icon and H2O on its lit sign and on both sides. At the base there's a row of machines (see The base). It sells **water bottles**. Walk up and press Use to buy them once from the shared bank (`waterBottlePrice`, $750 by default). **Everyone** gets a bottle, including players who join later. Bought items are saved between sessions.
- **Shop items:** every item has a home machine (`chamber` in `Items.All`: 0 = the base, n = chamber n), a price, and how many times it can be bought. Items are bought once for the whole team from the shared bank. Upgradeable ones (the backpack) can be bought again at the same machine, each time costing `priceGrowth` times the last; the price display shortens big numbers ($12.5K, $1.6M, $4.9B). The base's row holds the base items (backpack, Count Ant, pirate's foot), then the water bottle as a placeholder. Items that need another item first (the funnel needs the bottle) say so on the machine. Each chamber has its own item's machine (water in 1, funnel in 2, crossing sign in 3), plus a filter machine from chamber 4 on, or one out-of-order machine if there's nothing yet.
- **Backpack (base):** carry spare pieces on your back, bought for everyone. `backpackPrice` ($500) for the first, then each upgrade is `backpackPriceGrowth` (5×) the last, up to `backpackMaxLevel` (10). Each level holds `backpackPiecesPerLevel` (1) more piece.
  - **Stash:** holding a piece with room in the pack, grab another from the belt or the floor and the one in your hands shrinks and flies onto your back.
  - **Unpack:** place a piece on the board and the next spare pops out into your hands. With empty hands and nothing in reach, Use takes one out.
  - **Its button:** the backpack goes on an item button like the water bottle. Press it holding a piece to stow it (PACK FULL if there's no room), or with empty hands to take the top one out. Items you already owned without a button get put on a free one automatically.
  - **Looks:** level 1 is a camping backpack, level 2 adds a fanny pack on top. From level 3 it becomes a white lab **hover rig**: an open back plate with an arm down to a glowing emitter, and the spare pieces float in a soft blue beam behind you, starting down by your legs (two columns side by side once it holds more than 4). Later levels add twin emitters, orange safety trim, fins and a halo at the top of the beam. The pack and stack ride with your body, so they jump and tip forward when you crawl. Each upgrade gets a banner with its stage name.
  - **Inventory:** shows how many you're carrying, the capacity and the level.
- **A Count Ant (base, $1,500):** a tiny accountant. Everything the team earns, pipes and loops, pays `countAntBonus` (+10%) more.
- **Lucky Pirate's Foot (base, $2,000):** it's just a peg leg. Raises the team's luck by `pirateFootLuck` (+50%): every lucky-break chance is multiplied by 1 + luck. Whenever something only happened thanks to luck, LUCKY! pops up over it in green. Lucky breaks so far: a filled pipe paying double (BONUS x2, `bonusPipeChance` 5%) and the Crossing Sign's extra crosses.
- **Crossing Sign (chamber 3, $3,000):** a crossing guard's STOP paddle. Crosses come down the belt `crossingSignBoost` (+25%) more often: some pieces turn into crosses as they're dealt, and each of those arrives with the paddle held up over it, waving. If luck tipped it, LUCKY! shows under the paddle too.
- **Funnel (chamber 2, $2,500, needs the water bottle):** stand in the leak when a chamber ends to catch it in your bottle. Clean water refills it to full. Anything else fills it too, but contaminated (the inventory says with what), and drinking it has side effects:
  - **TOXIC SLIME:** faster, one extra dash, and the hiccups (random hops, HIC!).
  - **IRRADIATED:** dashes go 1.7× as far, and you shed glowing yellow-green sparks.
  - **VOID:** two extra dashes that go twice as far, but no extra walking speed.
  - Anything else: tastes funny (normal sip effects).
  Catch clean water again to wash it out.
- **Filter piece (chamber 4 and every chamber after, $400 each):** buying one drops it straight into your hands (what you were holding goes in your backpack, or on the floor). It's a T-shaped pipe with a glass canister: place it in any orientation. Liquid that comes in one arm fills to the canister and **splits out the other two**, so you now have two streams to keep fed, and if either one leaks, the round ends.
  - **Which way:** the first arm clockwise from where it came in gets the **purified** stream; the other gets the **concentrate**.
  - **Grades:** each pass refines further, up to grade 3. Toxic slime splits into FILTERED WATER and CONCENTRATED TOXIC SLIME. Water splits into a purer grade (FILTERED, PURE, DEIONIZED) and the rest. Concentrates go CONCENTRATED, DOUBLE-CONCENTRATED, HYPER-CONCENTRATED. Each stream shows its own color in the pipes: purer water paler and brighter, concentrates deeper and glowing.
  - **Money:** every pipe a refined stream fills pays `filterGradeBonus` (+50%) more per grade.
  - **With the funnel:** you catch whichever stream leaked. Filtered water is a better sip (faster, an extra dash; PURE more so), concentrates double or triple a liquid's side effects, and **grade 3 of anything kills you** when you drink it (you're reprinted at your locker, free).
- **Water bottle:** `waterBottleCharges` sips (10). A sip lasts `waterBottleBuffSeconds` (45 s). While it lasts you walk `waterBottleSpeedMultiplier` faster (×1.2) and can dash `waterBottleDashes` times in a row (2) before the cooldown. Each player's sips are their own.
- **Inventory:** press the inventory button to open a big panel over your critter. It's drawn on the screen overlay like the valve guide, so it stays readable at any camera distance. Prompts sit on the thing they act on instead of in a line of text, and keys are drawn as little key caps or controller buttons. While it's open:
  - your critter stands still;
  - up/down picks an item (▲▼ show when there's more than one); each row shows the item's icon and charges;
  - the highlighted row shows `[E] ›`: Use flips the panel to that item's **details page** (big icon, description, charges, which button it's on). Use or Jump goes back to the list;
  - the **button bar** at the bottom has one slot per item button, showing which item is on it, with the button underneath. Press an item button to put the highlighted item there (it swaps if it was on another one). If the highlighted item isn't on a button yet, the empty slots pulse;
  - the inventory button (top-right corner, `Tab ×`) or Jump closes it.

  Passive items will show a label such as ALWAYS ON instead of a button. The panel's size is set in GameConfig under Items & vending machine: `inventoryWidth`, `inventoryRowHeight`, `inventoryTextScale`, `inventoryPadding` and `inventoryOpacity`. Changes show up live in Play mode.
- **Icons:** `Resources/Icons` holds blank input glyphs (key cap, wide key cap, trigger, bumper, face button, pill) that the game writes the key's name on, and the item icons. They're rendered in Blender by `CharacterFactory~/icons.py`. An editor import rule keeps them at full size; if they look stretched or blurry, right-click the Icons folder and choose Reimport.
- **Teleporters:** at the end of a round they come up from under the floor like a secret elevator: hazard doors slide open, the teleporter rides up the shaft and lands with a thunk, and the frame stays as its platform. Standing on one only counts once it has landed.
- **Sipping:** the critter pulls the bottle out into its right hand, lifts it to its mouth, tips its head back for a few glugs, then puts it away (about 2 seconds). You can keep moving while it drinks.
- **Using items:** press an item's button any time to use it.

| | Controller | WASD | Arrows |
|---|---|---|---|
| Inventory | Select (View) | Tab | Backspace |
| Item buttons 1 · 2 · 3 | RT · LB · RB | 1 · 2 · 3 | End · PgDn · PgUp |

Settings are in the config's **Items & vending machine** section, with `enableVendingMachine` to turn it off. **Under Pressure > Reset Unlocks and Stats** (and `resetUnlocksOnPlay`) also forgets bought items, for testing.

## Characters

The yetis and skeletons are real models. They're built in Blender by the scripts in `CharacterFactory~/`, which Unity ignores, and exported to `Resources/Characters/<Species>_S/M/L.fbx`.
- **Skeleton:** the bones take your Spray Booth color, the teeth stay white, and the eyes are little glowing dots deep in the sockets.
- **Sharc:** an upright chubby shark on stubby legs, with its tail flopped on the floor. The top takes your Spray Booth color; the belly, chin and soles stay white. It has a wide grin of little triangle teeth, beady eyes, gills and a dorsal fin on its head.
- **Slime:** a see-through half-dome of goo in your Spray Booth color, with dot eyes, goo-blob hands and bubbles suspended inside.
  - **Cosmetics:** the hat sits on top; shirts, trunks and shoes float inside the goo.
  - **Moving:** it squashes along instead of walking.
  - **Trail:** it leaves goo blobs in its color on the floor, which shrink away after about 5 seconds.
- **Chihuahua:** a Chihuahua's head on the human test-subject body. The jumpsuit takes your Spray Booth color; the fawn fur, cream muzzle and pink ears keep their modelled colors.
- **Loading:** the game loads them at runtime, turns them upright and facing forward whatever axes the FBX used, and puts their feet on the floor.
- **Color:** it recolors them by material name. The fur takes your Spray Booth color; the face, claws and eyes stay fixed.
- **Animation:** it animates each part in code. The eyes blink and drift, the head shakes, the body squashes and bobs, the hands float and carry at chest height, the arms aim at the hands, and the legs swing from the hip.
- **Fitting to size:** hats, shirts, trunks, floaties and shoes scale to each body. Your floor ring and collider also grow with the size you pick.

## Lockers (cosmetics)

Every room, the base and each chamber, has one locker per player on the far left of the back wall, behind a strip of floor past the top belt. Each player appears in front of their own locker (joining, arriving by teleporter, coming back from the pit). Lockers work any time: mid-flow, between rounds, while others are charging a teleporter. You can only use your own. A locker is all three of the old closets in one, and each row plays its old closet's show:

- **Use:** walk onto the hazard plate in front of a door and press Use. You drop anything you're holding and hop inside.
  - Up/down picks a row.
  - Left/right (or either rotate key) changes it live.
  - Use or Jump steps back out.
  - The panel docks along the bottom of the screen, one column per player.
- **Player color:** see The base.
- **Species and size (the Bio-Reactor show):** YETI, SKELETON, CHIHUAHUA or SLIME, or SHARC once it's unlocked. All five are modelled characters in SMALL, MEDIUM and LARGE. Changing species or size raises a glass tube from under the floor grate around you, floods it with this chamber's liquid, swaps your body when it's full, then drains.
- **Body color (the Spray Booth show):** any color from the one color list (see The base). Four nozzles blast you with the new color. Your floor ring keeps your player color, so you can always find yourself.
- **Outfit (the Wardrobe show):**
  - **Hat:** baseball cap, cowboy hat, sombrero, shower cap.
  - **Torso:** lab coat, flannel shirt, biker jacket.
  - **Feet:** clown shoes, sneakers, military boots.
- **Secret unlocks:** some items start locked and are skipped in the closets until you earn them. The ARMS and LEGS rows don't appear in the locker at all until you've unlocked something for them. Personal unlocks belong to a player slot. Unlocking one gets a banner, a fanfare and confetti.

  | Item | Where | How (config value) |
  |---|---|---|
  | Shower cap | Locker, hat | Be standing in the spray when clean water leaks out, once (`unlockShowerCapSprays` 1) |
  | Swim trunks | Locker, legs | Get sprayed like that 10 times (`unlockSwimTrunksSprays`) |
  | Arm floaties | Locker, arms | Hop over a pipe full of clean water 10 times. The same pipe counts every time (`unlockFloatiesWaterHops`) |
  | **SHARC** | Locker, species | The whole team fills 1,000 pipes with clean water, all-time (`unlockSharcCleanPipes`) |

  "Clean water" means any chamber whose liquid name contains WATER. `sprayReach` (2.2 m) sets how far the leak's spray reaches. `unlockAllCosmetics` makes everything available for testing, and `testOwnAllItems` gives the team every shop item (at least level 1) without buying. **Under Pressure > Reset Unlocks and Stats** (or the `resetUnlocksOnPlay` checkbox) wipes progress, and **Under Pressure > Log Unlock Progress** prints where each slot stands.
- **After an unlock:**
  - The banner stays up for about 3 seconds.
  - The unlock goes at the top of the wall monitor's PERFORMANCE NOTES as `NEW!  P1  unlocked SHOWER CAP (YOUR LOCKER)`. If the results are already up when it happens, it's added straight away.
  - A small gold hologram, styled like the spout's readout, floats over that player's locker with NEW! and what it is, with confetti drifting through it. It goes away once they've been inside.
  - In the locker, every row with something new gets a gold NEW! sticker slapped on its right end (and its label turns gold), so you know which section to look in. Land on the new item itself and the sticker grows, wobbles and bursts confetti, and keeps sprinkling it while it's showing.
- **SHARC:** a chubby upright shark pup with a rounded snout, beady eyes, a grin of tiny teeth, gill slits, a forked tail, and a dorsal fin that wobbles behind it.
- **Saved looks:** looks are saved per player slot in PlayerPrefs (`pd_look0` … `pd_look3`), so P2 comes back as P2 next session.
- **Config (Lockers section):** `enableLockers` turns them off; `closetStripDepth` sets the floor strip in front of them. With lockers off, the room goes back to its old depth. (The separate Bio-Reactor, Spray Booth and Wardrobe closets are still in the code, but no room builds them any more.)

## Style: test chamber + grime

Select the **UnderPressure (Bootstrap)** object to find a **Style** section:
- **Grime** (0–1): 0 is a pristine white test chamber; 1 is rusted, stained and flickering, with missing or crooked wall panels, cracked observation glass, fog, film grain, chromatic aberration and a warmer, dimmer light. Change it, then finish or restart a round to see the result. The plan is for later chambers to creep toward 1.
- **Chamber Number** sets the big segment-display number on the wall.
- **Decor Seed** makes the stains and broken panels the same on every restart.

The players are original round critters, tinted per player (blue, pink, orange, lilac). Each has huge eyes that track whatever it's about to grab, a glowing antenna that lags behind its movement, and it cheers on loops and goals and droops when the liquid spills. The environment pieces are the dispenser's single red eye (it follows the nearest player and glows hotter as the countdown runs out) and the eyes that occasionally appear behind the observation window.

## What's in the MVP

- **Conveyor:** an always-moving belt on the left. Pieces slide in off-screen, ride past you, and fall off the far end. Pieces are dealt from a shuffled "bag" (3 straights, 4 corners, 1 cross by default), so you never go long without a useful piece. Face the belt while carrying a piece to put it back.
- **Carrying and placing:** an Overcooked-style context button. The target tile shows a frame plus a ghost of your piece: **green** means it will latch to the spout network, **white** means it will be placed but not connected, **orange** means hold to unscrew/swap, and **red** means blocked.
- **Pieces in hand:** pieces in hand, on the belt or on the floor are just the pipe. The base plate and bolts snap out from underneath with a clunk when you install one. A highlighted piece lights up its flanges.
- **Cross pieces:** the lower channel dips down into a trench in the plate, so the bridge only needs a gentle arch over it. Installed crosses are low enough to **walk straight over** in any direction: no climbing, and jumping toward one is just a hop. Pipe tubes are built as one seamless swept mesh, so bends and bridges meet their flanges cleanly.
- **Brackets vs. latches:** a piece that isn't connected to the spout gets ground brackets that slam up to bolt it to its tile. When it joins the spout network, the brackets retract and gold clamps pop on at each joint. If one placement connects several pieces at once, they latch in a cascade (sparks and rising-pitch clanks), and you get a `LINKED xN!` popup.
- **Replacing pieces:** hold the button. An unbracketed (unconnected) piece takes 0.35 s to unbolt; a piece latched to the network takes 1.4 s to unscrew (this is the "bad piece" penalty). Empty-handed, you pick it up. Carrying a piece, you swap: the old one pops out onto the floor. Pieces with liquid in them can't be removed.
- **Your body matters:** placed pipes block walking. Climb with one pipe and a free tile behind it, and you **vault** over. With a run of pipes, you **clamber on top and crawl** at 40% speed (difficult terrain), climbing down automatically when you step off. Dropped pieces lie loose on the floor. You walk through them, but each one underfoot slows you down and gets shoved around.
- **Flow:** after a 30 s countdown (the spout's wheel spins faster, it shakes, and steam puffs out), the liquid bursts out and fills each pipe channel in 2.6 s. A red pulsing warning appears at the exit when the next tile can't take the liquid.
- **Money and loops:** each filled pipe pays $10 (times the flow speed). When the liquid crosses a cross piece for the second time, that's a **LOOP**. A loop opens on the first crossing and closes on the second. A plain loop pays `loopBonus` ($100). If finished loops are nested inside it (they opened after it opened and closed before it closed), it pays `loopBonus × (nested + 1) × nestedLoopMultiplier`. For example, four loops nested inside each other pay $100, $400, $600 and $800. Bigger nests get bigger banners, hit-stop, confetti and a fanfare.
- **Music:** an original 80s-arcade-style loop, built in code, with two synced layers. The bass, arpeggios and drums play all the time. The melody creeps in near the end of the countdown and plays fully while the liquid flows. The music "tape-stops" on the spill. Off the clock it comes back as just the pre-flow groove (bass, arps and drums, no melody or harmony), so it's clearly not pressure music any more. The mix is set in the config: `musicVolume` overall, `musicLeadVolume` for the melody that comes in with the flow, and `musicHarmonyVolume` for the harmony on its second time round. Grime turns the music into a worn VHS tape: wobbling pitch, hiss and crackle, skips where a moment repeats, dropouts and tracking warbles. Scale that with `musicVhsAmount`. **Panic:** when fewer than `panicPiecesAhead` (4) connected, empty pipes are left ahead of the liquid, a "LEAK WARNING!" klaxon fires, the timer turns into a blinking "LEAK IN n!", and the music cuts to a faster, original alarm loop. Lay pipe ahead of the flow and it switches back, with a little "PHEW". **Tempo:** the flow valve also speeds the music up or slows it down. `musicTempoFollowsFlow` (0.3) sets how much, so at 3× flow the music plays at about 1.6× (1 = exactly the flow speed, 0 = off). `musicPitchSemitones` (-3) and `musicSpeed` (0.84) set the normal music's pitch and tempo independently. The music is re-generated with transposed notes and a new tempo, and the drums keep their pitch. Changing either during play regenerates the music in place once the slider stops moving. The panic loop keeps its own pitch and tempo. `musicPanicVolume` (1.4) sets the panic loop's loudness. Tempo changes from the valve start gently and then speed up, so big jumps land in about a second. You can swap the clips in `Music.cs` for real tracks later.
- **Win/lose:** reach the goal distance (12 pipes) before the spill and the level is complete. The liquid keeps flowing for bonus money until it spills.
- **Feel kit:** spring squash and stretch on everything, hit-stop, trauma-based camera shake, FOV punches, slow-mo on the spill, cartoon particles, a ticking money counter, bloom on the glowing liquid, and 16 code-synthesized sound effects.

## Decisions I made that you may want to change

| Topic | What I did | Where to change it |
|---|---|---|
| "Chained / nested" loop multiplier | Simplified to a running count: loop #n pays n×. It never resets. | `GameManager.OnLoop` |
| Removing an unconnected piece | Quick 0.35 s hold, so a tap doesn't remove it by accident | `GameConfig.unboltSeconds` |
| Swap | A hold, not a tap. The old piece is ejected as a loose floor piece. | `PlayerController.CompleteHold` |
| Movement while unscrewing | Locked in place (you're committed) | `PlayerController.Update` |
| Rotating placed pieces | Not allowed. You can only rotate a piece you're carrying. | — |
| Loop bonus timing | Paid the moment the liquid crosses the centre of the cross | `FlowController.Update` |
| Flow-faster button, shop, abilities, difficulty | Left out as you asked; held for after the MVP | — |

## Code map

```
Scripts/
  Core/      GameBootstrap (builds everything), GameConfig (all tunables), GameManager (rules + money),
             Game (service locator), Dir (grid directions + physics layers), Mats/Prims (runtime art)
  Board/     GridBoard (tiles, occupancy, BFS connectivity to spout), Spout
  Pipes/     PipeBuilder (procedural geometry & channel paths), PipePiece (states, fill, latch visuals)
  Conveyor/  Conveyor (belt, bag spawner)
  Player/    PlayerController (movement/climb/interaction/closet mode), PlayerRig (loads FBX characters from Resources/Characters and animates their parts; procedural SHARC),
             CritterLook (species/color/outfit + save), Accessories (hats, torsos, shoes), Closet (the three booths),
             PlayerInputs (per-device action maps), JoinManager (drop-in co-op), PlacementPreview (tile frame + ghost)
  Flow/      FlowController (liquid simulation, loop detection, spill warning)
  Style/     Theme (palette + grime lerp), ChamberBuilder (walls, window, signage, grime, Flicker, Watcher)
  Juice/     Juice (hit-stop, slow-mo, tweens, Springy), CameraRig (shake), Fx (particles),
             Sfx (synthesized audio), Popups (floating text + banners)
  UI/        Hud
Editor/      UnderPressureMenu (Create Playable Scene / Create Config Asset / Reset Unlocks and Stats / Log Unlock Progress)
```

**Swapping in real art later:** `PipeBuilder.Build` and `PlayerRig.Build` are the only places that create visuals. Replace them with prefab instantiation and keep the same transforms (`clamps`, `staples`, channel `segments`). The sound effects are public `AudioClip` fields on `Sfx`, so you can assign real clips.

**Co-op:** `JoinManager` (on the bootstrap object) watches for join presses and keeps a list of joined slots, which survive a restart. Each player gets a `PlayerInputs` action map locked to its own device (or keyboard half), its own placement preview and progress ring, and a color from `JoinManager.PlayerColors`.

## Known limits

- This is untested in the Unity editor. The scripts compile cleanly against stand-in Unity API stubs, but nothing has been run yet. Expect a pass of tuning (and possibly a small fix or two) on first Play.
- The placeholder geometry is primitive-based (hundreds of small renderers). It's fine for prototyping; real meshes will batch better.
- The scene restart rebuilds the level in place rather than reloading the scene.
