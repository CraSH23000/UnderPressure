# Under Pressure — Hand-off Summary

*As of 2026-11-22. Covers everything designed and built so far, how the project is organised, and everything still on the list.*

---

## 1. The game in one paragraph

**Under Pressure** (formerly "Pipe Dream") is a couch co-op spin on *Pipe Dream*. Up to 4 cute critters grab pipe pieces off a looping conveyor belt and lay them on a grid to route liquid from a spout before it leaks. It's set in a sterile, Portal-like test lab that gets grimier with every chamber, and it's built for AAA "juice": springy squash, popups, confetti, screen shake, banners and music that reacts to the action. Between chambers the team teleports via tesla-coil teleporters; a **base** ("chamber 00") is the hub with lockers, a shop and a practice board.

---

## 2. How we work

- **Engine:** Unity **6.6 (6000.6.9f1)**, URP, new Input System. Everything is built in code at runtime (procedural primitives, runtime URP materials); there are no hand-made scenes or prefabs beyond a bootstrap.
- **Roles:** Claude writes all the code; the user tests in Unity and sends screenshots and feedback.
- **Delivery:** a zip named **`UnderPressure.zip`** whose top folder is `UnderPressure/`, containing:
  - `Scripts/`, `Editor/`, `README.md`;
  - `Resources/Characters/` (FBX critters), `Resources/Icons/` (UI icons);
  - `CharacterFactory~/` (the Blender scripts; the `~` keeps Unity from importing it).

  No `Assets/` folder above it: the user drops `UnderPressure/` into their project's Assets.
- **Config:** every tunable lives in **`GameConfig`** (a ScriptableObject), grouped by category, not by version. Sections:

  Board · Round & flow · Money & bonuses · Conveyor belt · Player movement · Interaction · Items & vending machine · Pit · Base · Chambers & liquids · Camera · Lockers · Unlocks · Music · Feel & sound

  New fields pick up their defaults on an existing asset.
- **Editor menu (Under Pressure):** Create Playable Scene (also prunes missing build scenes), Create Config Asset, Reset Unlocks and Stats, Log Unlock Progress.
- **Builds:** `Editor/UnderPressureBuild.cs` creates keep-alive materials in `Resources/UnderPressureShaderKeep` so player builds keep the URP shaders and variants. This fixed an empty-sky build and transparent effects rendering solid. `Editor/UnderPressureIconImport.cs` keeps icons un-resized and uncompressed.
- **Saves:** PlayerPrefs, all keys prefixed `up_`. **Reset Unlocks and Stats** wipes unlocks, stats, items, bottle contents and the deepest-chamber record.
- **Domain reload is off:** statics are reset in `Game.Clear()`.
- **Compile checking:** Claude compiles against a stub copy of the Unity API outside the zip. Code is compile-checked but **not run** before delivery; the user's playtests are the real test.

### Standing design decisions

- **One shared bank** ("lab funding") for the team, saved between sessions (`up_bank`, a `long`, so up to $9.2 quintillion).
- **Cosmetics** are earned or found, never bought. **Shop items** use the bank and are bought once for the whole team.
- **Consumables** (the filter piece) are bought one at a time by whoever presses the button.
- Lockers are usable any time. The identity floor ring isn't a cosmetic: it's the **player color**.
- **Character direction:**
  - PlateUp / Gang Beasts vacant faces with deadpan dot eyes.
  - Monsters have puppy-type charm, not blush.
  - Slim bodies and big heads, except the large size.
  - Three sizes (S/M/L) per species, and cosmetics fit all three.
  - Humans are parked (uncanny valley); their body and jumpsuit are reused for the Chihuahua.
- **Items on the critter:** wild passive items don't necessarily show on the critter (Haste-style), so there's no visual clutter at 50 items.
- **Absurd numbers are welcome:** Balatro-style shenanigans like 1000% luck and 1000% money are the intended late game, which is why prices scale exponentially.
- **US spelling:** "color", not "colour", everywhere.

---

## 3. What's built (by system)

### Core loop
- **Board, spout and flow:** a grid board, a spout with a countdown, a flow valve (players can speed the flow up or slow it down) and a vertical fill indicator.
- **Belt and pit:** a looping conveyor delivers pieces from a spawn bag (straights, corners, crosses). Unused pieces fall into a pit. Players can fall in too: they're reprinted at their locker, costing `pitRespawnCost` ($50, free at the base).
- **Scoring:**
  - every filled pipe pays;
  - crosses make loops, and nested loops multiply;
  - money goes to the round total, then to the bank on a win.
- **Panic:** an alarm, music and a red warning blob over the most urgent stream when fewer than `panicPiecesAhead` (4) connected empty pipes remain. The look-ahead walks every stream in lockstep, so two streams heading for each other only count half the shared run each.
- **Pipe burst:** two streams that meet head-on in one channel (e.g. a filter's outlets routed into each other) burst the pipe where they meet: the tube there is blown away and both torn ends peel back in strips like an exploding cigar (`TornEnd`), each colored by its stream. The puddle lands under the tear, PIPE BURST! shows, and it ends the round like any leak. Counted in the team stat `pipebursts`. A filter fed a second liquid shatters its canister instead (glass stump, flying shards, the cap pops off; FILTER SHATTERED!), counted the same way.
- **Off the clock:** after a round the belt idles slowly (free play) and the lever still works. The music drops the lead and harmony.
- **Teleporters:**
  - **Rising:** at round end they rise out of the floor like a secret elevator: hazard doors retract, the teleporter rides up the shaft and lands with a thunk.
  - **Choices:** a win gives RETRY / **BACK TO BASE** (middle) / NEXT; a loss gives RETRY / BACK TO BASE.
  - **Weighted vote:** everyone stands on a pad, a capacitor charges, and a random ready player's pad wins. The current roulettes between the occupied pads before the strike.
- **Arrival pads:** joins and teleports beam in on arrival pads.

### Base ("chamber 00")
- **Starting point:** the game starts here (`startInBase`). It's a normal room with no clock: the board and belt run in free play for practice, and the pit is free (`freePitInBase`).
- **Grime:** as grimy as the deepest chamber reached, and uses that chamber's liquid color.
- **Layout:**
  - **4 lockers** on the far left of the back wall;
  - **4 vending machines** on the right of it (`baseVendingMachines`): Backpack, A Count Ant, Lucky Pirate's Foot, Water (placeholder);
  - the wall monitor shows the deepest chamber, lab funding, bottles and subjects.
- **Leaving:** once someone joins, the teleporters rise: **FROM THE TOP: CHAMBER 01** and **CONTINUE: CHAMBER N**. Passing a chamber unlocks the next.

### Chambers
- **Same layout as the base:** lockers far left, machines on the back wall's right.
- **Spawning:** each player spawns at **their own locker** (joins, teleports, pit respawns).
- **Machines per chamber:**

  | Chamber | Machines |
  |---|---|
  | 1 | Water bottle |
  | 2 | Funnel |
  | 3 | Crossing Sign |
  | 4 onward | Filter Piece, plus that chamber's own item when it gets one |
  | No item yet | One OUT OF ORDER machine |

- **Liquid by chamber:** set per chamber (`fromChamber` list), e.g. water, toxic slime, irradiated, void.

### Lockers (cosmetics), one per player
- **All three old closets in one.** The separate Bio-Reactor, Spray Booth and Wardrobe code still exists, but no room builds them.
- **Rows:**
  - PLAYER (player color)
  - SPECIES
  - SIZE
  - COLOR (body)
  - HAT
  - TORSO
  - (ARMS)
  - (LEGS)
  - FEET

  ARMS and LEGS appear only once something in them is unlocked.
- **Each row plays its old closet's show:**
  - species and size: a glass tube rises from under the grate, floods, swaps the critter and drains;
  - color: wall nozzles spray;
  - clothes: a wardrobe puff.
- **Ownership:** only the owner can use a locker. Unjoined lockers are grey and dim, and light up in the player's color on join.
- **NEW! notifications:**
  - a floating NEW! sign with confetti above the locker;
  - in the panel, a **gold starburst NEW! sticker** on every row with something new (the label turns gold);
  - land on the new item and the sticker grows, wobbles and bursts confetti.

### Colors: one list
- **`CritterLook.Colors`**, 25 entries:

  SKY, BUBBLEGUM, TANGERINE, LILAC, MINT, LEMON, CRIMSON (dark), TEAL, MOSS, COCOA, SNOW, CHARCOAL, LIME, CHERRY, HONEY, GOLD, GUNMETAL, CONCRETE, ASH, TOXIC, HAZMAT, BLEACH, CHROME, IRIDESCENT, BLACKLIGHT

- **The player flag:** each color has a `player` flag. Player colors exclude dark, grey, white and special ones, and colors other players hold are skipped. Picks are saved per slot.
- **Finishes (`CritterLook.Finish`):**
  - GOLD, GUNMETAL, CHROME: metallic;
  - TOXIC and BLACKLIGHT: glow;
  - IRIDESCENT: hue-shifts as you move (a simple version, not a true thin-film shader);
  - HAZMAT: glossy.
- **Adding colors:** add new ones **at the end** of the list, because saves store the index.

### Characters (Blender → FBX)
- **Species:** Yeti, Skeleton, Chihuahua, Sharc, Slime, each at S/M/L. Made by the **Character Factory** (`CharacterFactory~`, Python + headless `bpy` 5.3, Cycles CPU renders).
- **Part contract** (what Unity looks for in each model):
  - `Body`, with `Head` under it (a mesh, or an empty for the slime);
  - `Eye_L` / `Eye_R` under the Head;
  - `Anchor_Hat`, `Anchor_Torso`;
  - `Arm_L/R` (pivot at the shoulder) and `Hand_L/R`;
  - `Leg_L/R` (pivot at the hip), with `Shoe_L/R` under them.
- **Material contract:**
  - `Tint` / `TintShade` take the body color;
  - `Slime` is a transparent tint and marks the model as a blob;
  - `Glow` is emissive;
  - `Pupil`;
  - anything else keeps its own color.
- **Runtime rig (`PlayerRig`):** orients the model, puts the feet on the floor, and builds springy pivots: arms aim at the hands, legs swing, and things are carried at chest height. Cosmetics are fitted to the measured body. Blobs carry cosmetics floating inside and leave a slime trail.
- **Commands:**

  | Script | What it makes |
  |---|---|
  | `python export.py` | the FBX models |
  | `python render.py` | preview renders |
  | `python icons.py <out_dir>` | all icons |
  | `python icons.py badge` / `filter` / `new` / `backpack` | single icons |

### Movement and input
- **Schemes:** KeyboardWASD, KeyboardArrows and Gamepad, with maps built in code.
- **Dash** (replaced sprint, Overcooked-style): `dashDistance`, `dashSpeed`, `dashCooldown`. The old sprint settings are kept but unused.
- **Item buttons:**

  | Scheme | Inventory | Item buttons |
  |---|---|---|
  | WASD | Tab | 1 / 2 / 3 |
  | Arrows | Backspace | End / PgDn / PgUp |
  | Gamepad | View (select) | RT / LB / RB |

- **Walking on pipes:** crosses can be walked over and connected pipes act as ramps.

### UI
- **HUD:** a screen-space overlay with closet and locker panels (up to 9 rows), the valve guide, banners and confetti.
- **Inventory:**
  - a big screen-space panel pinned above your critter, sized by `inventoryWidth`, `inventoryRowHeight`, `inventoryTextScale`, `inventoryPadding` and `inventoryOpacity`;
  - two pages in one panel: the list, and an item's details;
  - **button bar:** 3 slots showing what's on each item button, with **key glyphs** underneath;
  - prompts sit where they act: `Tab ×` in the corner, `E ›` on the highlighted row;
  - ▲▼ arrows appear only with 2+ items, and empty slots pulse when the highlighted item isn't on a button.
- **Glyphs:** blank Blender-rendered shapes with the key name written on top: key cap, wide key, trigger, bumper, face button (in Xbox colors) and pill.
- **Item icons:** bottle, backpack, ant, peg leg, STOP paddle, funnel, filter, plus the NEW! badge.
- **Monitor and pause menu:** the wall monitor shows live stats, results and unlocks. The pause menu has a Controls page.

### Items (shop) — `Items.cs`
- **Every item has:**
  - a home chamber (0 = base, n = chamber n, or "every chamber from n");
  - a sign text and color;
  - a base price, price growth and max level;
  - optionally `requires`, `consumable` or `everyChamberFrom`.
- **Vending machines** are generic, built from the item:
  - lit sign with the big icon, the same icon on both sides, and products on the shelves in a real recessed window;
  - a price display with short money ($12.5K, $4.9B);
  - prompts and banners;
  - they're solid (critters walk around them).
- **Out-of-order machines:** faded paint, a dead "???" sign, dusty empty shelves and a crooked taped OUT OF ORDER sign. Using one gets a thump.

| Item | Where | Price | What it does |
|---|---|---|---|
| **Backpack** | Base | $500, ×5 each upgrade, up to LV 10 | Carry `backpackPiecesPerLevel` (1) spare piece per level. **Button:** stow the piece in your hands, or take one out. Grabbing with full hands auto-stows; placing a piece auto-pops the next. **Looks:** LV1 camping pack → LV2 fanny pack → **LV3+ white lab hover rig**, with pieces floating in a blue beam behind you starting by your legs (2 columns once it holds more than 4), then twin emitters, orange trim, fins and a halo. Rides on the body (jumps and crawls). Stage names: BACKPACK, FANNY PACK ATTACHMENT, HOVER RIG, TALLER BEAM, TWIN EMITTERS, SAFETY TRIM, STABILIZER FINS, OVERCLOCKED BEAM, EXPERIMENTAL FIELD, HALO TOWER. |
| **A Count Ant** | Base | $1,500 | All earnings +`countAntBonus` (10%). |
| **Lucky Pirate's Foot** (a peg leg) | Base | $2,000 | Luck +`pirateFootLuck` (50%): every lucky chance ×(1+luck). A green **LUCKY!** pops up when a result only happened thanks to luck. |
| **Spider Monkey Serum** | Base | $1,800; refills $500 | 4 doses. A dose lasts the whole chamber: walking into a pipe climbs or vaults it (no jump), and climbing/crawling on top goes at walk speed. Once owned, its machine sells refills (everyone back to 4). Pink corked vial with a monkey tail. |
| **Spoon** | Base | $2,500 | "Pipes aren't real, they can't hurt you." Passive, ON DASH: dashes pass through pipes (the climbing layer), and a dash that would end inside one carries on until clear. Items now have an `effect` line (plain words, shown in the details page under the flavour text). |
| **Water bottle** | Chamber 1 (and the base, as a placeholder) | $750 | 10 sips each. A sip lasts 45 s: walk ×1.2 and dash twice. **Use:** pull out → lift → head tilts back → glugs → put away. |
| **Funnel** | Chamber 2 | $2,500 (needs the bottle) | Stand in the leak when a chamber ends: water refills the bottle; any other stream **contaminates** it. Sip effects: TOXIC = faster + extra dash + hiccups; IRRADIATED = much longer dashes; VOID = lots of long dashes; FILTERED/PURE water = better sips; `+`/`++` ×2/×3; **`+++` or deionized is lethal** ("DON'T DRINK THAT", charred, reprinted at your locker for free). |
| **Crossing Sign** (a crossing guard's STOP paddle) | Chamber 3 | $3,000 | +`crossingSignBoost` (25%) crosses. Extra crosses ride the belt with the paddle waving over them, plus LUCKY! if luck tipped it. |
| **Filter Piece** (consumable) | Chamber 4+ | $400 each | Goes straight into the buyer's hands. A T-pipe with a glass canister: liquid entering any arm **splits into two streams** out the other arms. **Clockwise outlet = purified, other = concentrate.** Grades 0–3 (what each side becomes is data: `purifiesTo` / `concentratesTo` on each liquid; `NAME+` = endless chain NAME+, ++, +++, +4...). Each stream has its own color, and each grade pays +`filterGradeBonus` (50%). If either stream leaks, the round ends. |

- **Lucky breaks so far:** a filled pipe paying double ("BONUS x2", `bonusPipeChance` 5%) and the Crossing Sign's extra crosses.
- **Flow engine:** it now runs **multiple liquid fronts**, each with its own grade, blob and trail. The panic count follows both filter outlets and takes the shorter one.
- **Testing toggles:** `testOwnAllItems` (the team owns every item at level 1+; bottles start empty), `unlockAllCosmetics`, `resetUnlocksOnPlay`.

### Unlocks (earned cosmetics)

| Unlock | How | Where |
|---|---|---|
| Shower cap | Get sprayed by a water leak `unlockShowerCapSprays` (1) time | Locker, hat |
| Swim trunks | Get sprayed 10 times | Locker, legs |
| Arm floaties | 10 water hops | Locker, arms |
| SHARC | The team fills 1,000 clean pipes | Locker, species |

Unlocks get a banner and appear on the results monitor (NEW! rows first).

---

## 4. NOT yet implemented (the to-do list)

### Items and economy
- **Delete items from your inventory:** roguelike insurance, so a build that's gone off the rails (too many wild upgrades) can be undone. Deleting keeps the record of what you bought: re-buying the serum costs full price (more than a refill); a deleted backpack can be picked up again for free, at the level you'd upgraded it to. Anything upgradeable keeps its upgrades.
- **Hazmat suit** (planned for chamber 5): −50% toxic slime vulnerability. **Blocked:** toxic slime doesn't hurt yet (a spray only splashes). First design a "sick" state (e.g. slowed, green, hiccupping) from toxic sprays, then the suit halves it. It could also be wearable.
- **Balatro-style scaling:** later items like +1000% luck or money, so the 5× backpack prices become reachable.
- **More luck hooks:** e.g. better belt pieces, a spill grace (one extra pipe), unlock rate. Always show LUCKY! when luck made the difference.
- **Count Ant and peg leg on the critter:** deliberately skipped for now (Haste-style). The peg leg would replace a leg (one shoe + one peg).
- **Backpack at high levels:** if the hover rig gets too much, make it gradually transparent. **Tune the pack and stack position per species** (it's estimated from body size).
- **Drill / jackhammer items;** a rivets item idea that was saved for the drill.
- **Bottle refills via a fountain** (the Funnel partly covers this).
- **Fill the remaining out-of-order slots:** future chambers' own items. "Every future chamber has a filter machine" is already in.

### Pipe Burster (achievement) and the pipe bomb
- **Pipe Burster achievement** from the `pipebursts` team stat, with bigger tiers later (e.g. 10, 100). Tie cosmetics to it (soot-blackened finish, a frayed pipe-stub hat or cigar) and the pipe bomb unlock.
- **Burst grade effects (backburner):** a burst should splash all three: each torn end's own grade plus the merged puddle. For now the spill grade is just the worse of the two streams.
- **Pipe bomb (shop item, unlocked by the achievement):** single use, leaves the inventory when used, cheaper than most items and rebuyable.
  - The pipe on the bomb's tile is destroyed, leaving a scorch mark (no un-pickupable halves to get stuck on).
  - The surrounding pieces are knocked off their brackets and become loose pieces.
  - Bombing a pipe with liquid in it bursts it and ends the run (counts toward the burst stat).
  - Critters in the blast get a short stun (the stun state is also needed for debuffs later).

### Filter future ("pie in the sky")
- **Liquid chain ideas:** VOID purifies to IRRADIATED and IRRADIATED to TOXIC SLIME, so the nastiest liquids take the longest to clean. PRIMORDIAL SOUP / DARK MATTER as named stops before the ??? chain's `+` begins. Maybe fold VOID into the ??? world.
- More purity effects per grade, and very pure water being valuable, possibly tied to **etching silicon wafers**.
- Maybe a visual cue for which outlet purifies (e.g. a blue band on the clockwise arm once liquid enters), if the clockwise rule isn't readable.
- **Liquid proximity effects** (liquids affecting nearby things) and the "filter split" idea around them.

### Easter egg
- **Lab almanac terminal (base):** a clunky lab computer in the shop area where the critters "type up" their mandatory science time logs: lifetime stats, achievements, discoveries. Home for the achievement system below (and the unlocks).
- **Money tick feel:** the HUD counts every digit with an NES tick (`moneyTickSound`). If it gets annoying in play, try casino-style or juicier sounds, or design a sound for the fast, ramped-up count.
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
- **Blockers with an area effect:** a blocker can affect the tiles around it and **change the liquid flowing near it**: e.g. freeze it (slower flow), boil it (faster, steam), contaminate it (toxic), purify it, or charge it. Route past it for the bonus, or around it to avoid the penalty. This is the same idea as "Liquid proximity effects" under Filter future, so build them as one system.
- **Money curve:** if endless pays more as chambers get harder, plan the payout curve in the money rebalance so it doesn't run away by chamber 30.

### Characters and cosmetics
- **Remaining species from the list:** **gremlin** and **robot** (square robot). The **human** is parked (uncanny valley).
- **Skeleton extras:** chattering jaw, player-colored glowing eyes; maybe rework the large skeleton's pelvis.
- **Sharc:** maybe blend the head into the body.
- **Remake the torso cosmetics** (the user called them bad placeholders), in 3 sizes.
- **Hair and beard cosmetics** (code exists in the parked human factory).

### Visual polish
- **Reflection probe** in the lab so CHROME, GOLD and GUNMETAL look truly metallic.
- **True iridescence** (a thin-film shader) instead of a whole-body hue shift.
- Generalise the vending machine "product" models if items get their own Blender models (one model for shelf, tray and icon).

---

## 5. Known things to verify in playtests

- Pipe burst: do the torn ends read at gameplay distance on straights, corners, both cross channels and filter arms? Is the panic warning now on time when outlets are routed into each other?
- Filter: is the clockwise-purifies rule readable? Do two streams feel good-hectic? Does the canister sit well next to crosses when walking over?
- Lethal drink flow (grade 3), contaminated sip effects, Funnel catching the right stream.
- Crossing Sign tags (they now persist for the whole belt ride).
- Backpack hover rig placement on each species; stack height at LV 9–10.
- Locker panel with 9 rows fits on screen. Does the leftmost locker's floating NEW! sign clash with the chamber number sign? Does the monitor crowd the sign?
- Pit respawn at the locker: is the walk back too punishing?
- Chamber camera framing after removing the right-wall machine.
- `testOwnAllItems`: bottles start empty (no purchase ran).

---

## 6. File map

```
UnderPressure/
├─ Scripts/
│  ├─ Core/   Game, GameBootstrap (rooms, base vs chamber, layout, spawns, machines), GameManager (rounds,
│  │          money, teleporters, base mode, funnel catch), GameConfig, Items (shop), Progress (unlocks,
│  │          stats, deepest chamber), ReadyPad (teleporters + elevator), Mats, Prims, Dir
│  ├─ Flow/   FlowController (multi-stream flow, filters, panic count), LiquidGrade (purity/concentration)
│  ├─ Pipes/  PipeBuilder (procedural pipes incl. the filter T + canister), PipePiece
│  ├─ Board/  GridBoard, Spout, SpoutHologram
│  ├─ Conveyor/ Conveyor (spawn bag, Crossing Sign), BeltLever
│  ├─ Player/ PlayerController, PlayerRig (+ Iridescent), PlayerBackpack (+ Backpacks), PlayerInputs,
│  │          CritterLook (colors, finishes, parts), Closet (lockers), Accessories, VendingMachine,
│  │          JoinManager (slots, player colors), ArrivalPad, NewBadge, PlacementPreview, SlimeTrail
│  ├─ UI/     Hud (panels, NEW! stickers, UI confetti), InventoryPanel, Glyphs (+ KeyGlyph),
│  │          FloatingTag, PauseMenu, GameSettings
│  ├─ Style/  ChamberBuilder (room, walls, doorways, decor), Theme (grime, liquids), WallMonitor
│  └─ Juice/  Juice, Fx, Popups/Banner, Sfx, Music, CameraRig, CharredRemains, SlimePuddle
├─ Editor/    UnderPressureMenu, UnderPressureBuild (shader keep-alive), UnderPressureIconImport
├─ Resources/ Characters/ (5 species × S/M/L FBX), Icons/ (glyphs, items, badge)
├─ CharacterFactory~/  cf.py helpers + one script per species, export.py, render.py, icons.py
└─ README.md  (player-facing feature documentation, kept up to date with each delivery)
```
