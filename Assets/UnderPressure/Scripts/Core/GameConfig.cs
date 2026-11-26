using UnityEngine;

namespace UnderPressure
{
    public enum PipeType { Straight, Corner, Cross, Filter }

    /// <summary>
    /// A liquid. Chamber liquids are chosen per chamber (the last entry whose fromChamber has been reached); entries
    /// with fromChamber 0 never come out of a spout and only exist as what a filter turns something into.
    /// </summary>
    [System.Serializable]
    public class LiquidType
    {
        [Tooltip("Shown on the spout and the filter labels. A name with WATER in it counts as water (sprays, bottles).")]
        public string name = "CLEAN WATER";
        [Tooltip("First chamber that pumps this liquid. 0 = never from a spout (only made by filtering).")]
        public int fromChamber = 1;
        public Color color = new Color(0.55f, 0.85f, 1f);
        [ColorUsage(false, true)] public Color glow = new Color(0.25f, 0.6f, 1.2f);
        [Tooltip("What a filter's purified (clockwise) outlet turns this into: another entry's name, or 'NAME+' for an endless chain. Blank = generic water, one grade purer.")]
        public string purifiesTo = "";
        [Tooltip("What a filter's other outlet turns this into: another entry's name. End it in '+' for an endless chain: \"TOXIC SLIME+\" gives TOXIC SLIME+, ++, +++, +4... Blank = stays as it is.")]
        public string concentratesTo = "";

        [Header("Pattern (optional)")]
        [Tooltip("Solid = one flat color. Anything else swirls 'color' with 'color2' (pipe fill, the stream's front, puddles, the spout slime).")]
        public LiquidPattern pattern = LiquidPattern.Solid;
        public Color color2 = Color.black;
        [ColorUsage(false, true)] public Color glow2 = Color.black;
        [Tooltip("Size of the bands / swirls (1 = about a tile).")]
        public float patternScale = 1f;
        [Tooltip("How fast the pattern flows / churns.")]
        public float patternSpeed = 1f;
    }

    /// <summary>How a liquid's color moves. Solid is plain; the rest use the UnderPressure/LiquidPattern shader.</summary>
    public enum LiquidPattern { Solid, Bands, Marble }

    /// <summary>
    /// Every tunable number in one place. Create one via Assets > Create > Under Pressure > Game Config
    /// and drag it onto the GameBootstrap, or leave it empty to use these defaults.
    /// </summary>
    [CreateAssetMenu(menuName = "Under Pressure/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("Board")]
        public int columns = 9;
        public int rows = 7;
        public float tileSize = 1.6f;
        public Vector2Int spoutCell = new Vector2Int(1, 3);
        public Dir spoutDirection = Dir.E;
        [Tooltip("Walkable gap between the belt and the board.")]
        public float laneWidth = 2.6f;

        [Header("Round & flow")]
        [Tooltip("Pipes the liquid must fill (not counting the spout) for the level to count as complete.")]
        public int goalDistance = 12;
        [Tooltip("Seconds before the liquid starts flowing.")]
        public float countdownSeconds = 30f;
        [Tooltip("Seconds for liquid to travel through one pipe channel.")]
        public float secondsPerPiece = 2.6f;
        public float spoutFillSeconds = 1.6f;
        [Tooltip("Flow speeds you can dial in by holding Use on the dispenser and pressing rotate. Index 'flowSpeedDefault' is where it starts.")]
        public float[] flowSpeedSteps = { 0.5f, 0.75f, 1f, 1.5f, 2f, 3f };
        public int flowSpeedDefault = 2;
        [Tooltip("The leak warning (klaxon, LEAK IN n! on the spout, the alarm music if the riser is off) kicks in when the liquid is less than this many seconds from spilling, at the current flow speed. (The tension riser starts earlier, see musicRiserSeconds.)")]
        public float panicSeconds = 8f;

        [Header("Money & bonuses")]
        public int moneyPerPiece = 10;
        [Tooltip("Money per filled pipe is multiplied by the flow speed: risk faster flow, earn more.")]
        public bool moneyScalesWithFlowSpeed = true;
        [Tooltip("Every completed loop pays this. A loop with N already-finished loops nested inside it pays " +
                 "loopBonus * (N + 1) * nestedLoopMultiplier.")]
        public int loopBonus = 100;
        public int nestedLoopMultiplier = 2;
        [Tooltip("Money lost for every piece that falls into the pit unused.")]
        public int wastedPiecePenalty = 5;
        [Tooltip("End-of-level bonus per +1x of AVERAGE belt speed over the round (0.5 = +50% of your earnings at an average of 2x).")]
        public float beltBonusPerExtraSpeed = 0.5f;
        [Tooltip("If on, running the belt slower than 1x on average costs the same percentage.")]
        public bool beltSlowPenalty = false;
        [Tooltip("End-of-level bonus per +1x of AVERAGE flow speed while the liquid ran (smaller than the belt's: faster flow already pays more per pipe).")]
        public float flowBonusPerExtraSpeed = 0.2f;

        [Header("Conveyor belt")]
        public float beltSpeed = 1.15f;
        [Tooltip("Distance between pieces on the belt.")]
        public float beltSpacing = 2.2f;
        [Range(0f, 1f)] [Tooltip("How much of the belt is pre-filled at the start (from the entrance onward).")]
        public float beltPrefill = 0.3f;
        [Tooltip("Pieces are dealt from a shuffled bag of this list, so long droughts can't happen.")]
        public PipeType[] spawnBag =
        {
            PipeType.Straight, PipeType.Straight, PipeType.Straight,
            PipeType.Corner, PipeType.Corner, PipeType.Corner, PipeType.Corner,
            PipeType.Cross
        };
        [Tooltip("Belt speeds the lever can select (multiplies beltSpeed). 'beltSpeedDefault' is the starting index.")]
        public float[] beltSpeedSteps = { 0.5f, 0.75f, 1f, 1.5f, 2f, 3f };
        public int beltSpeedDefault = 2;
        [Range(0f, 1f)] [Tooltip("Belt speed while off the clock between chambers, so there's always something to play with.")]
        public float freePlayBeltSpeed = 0.35f;

        [Header("Player movement")]
        public float walkSpeed = 5.2f;
        public float acceleration = 45f;
        public float turnSpeedDegrees = 1080f;
        [Tooltip("Not used right now: the sprint button is a dash (below). Kept for a future sprint item.")]
        public float sprintMultiplier = 1.5f;
        [Tooltip("How far one dash carries you, in metres.")]
        public float dashDistance = 2.2f;
        [Tooltip("How fast you move during a dash (metres per second). Distance / speed = how long the dash lasts.")]
        public float dashSpeed = 15f;
        [Tooltip("Seconds before you can dash again.")]
        public float dashCooldown = 0.35f;
        public float jumpHeight = 0.7f;
        public float jumpSeconds = 0.45f;
        [Tooltip("Speed multiplier while crawling on top of placed pipes.")]
        public float crawlSpeedMultiplier = 0.4f;
        public float vaultSeconds = 0.55f;
        public float climbUpSeconds = 0.32f;
        [Tooltip("Speed lost per loose piece underfoot.")]
        public float loosePieceSlowdown = 0.16f;
        public float minLooseSpeedMultiplier = 0.55f;

        [Header("Ghost (mouse player)")]
        [Tooltip("Clicking the mouse joins a ghost: a wisp that floats after the cursor. Left click = Use, scroll = rotate, right click = drop / let go. It floats over pipes, the belt and the pit, and doesn't use items or lockers.")]
        public bool enableGhost = true;
        [Tooltip("The ghost's top speed, as a multiple of walkSpeed. It chases the cursor at up to this speed (it doesn't teleport to it).")]
        public float ghostSpeedMultiplier = 1.6f;
        [Tooltip("How snappily the ghost settles on the cursor (higher = snappier, lower = floatier). It always eases in and stops on the spot, so it never overshoots.")]
        public float ghostFollow = 6f;
        [Tooltip("How high the ghost floats above the floor.")]
        public float ghostHoverHeight = 0.45f;

        [Header("Interaction")]
        public float pickupRadius = 1.15f;
        [Tooltip("The place/unscrew target is the tile under a point this far (in tiles) in front of you. " +
                 "0.35 = place on your own tile until you're past its centre, then the next one. You hop off a piece placed under you.")]
        public float placeProbeTiles = 0.35f;
        [Range(0.1f, 1f)] [Tooltip("How solid the hologram of the carried piece on the target tile is (1 = opaque).")]
        public float previewOpacity = 0.7f;
        [Range(0f, 2f)] [Tooltip("How much the hologram glows in its color. Pale colors (white = placed but not connected) glow less, so they keep their shading on the white floor.")]
        public float previewGlow = 0.6f;
        [Tooltip("Hold time to unbolt a piece that is NOT attached to the spout network.")]
        public float unboltSeconds = 0.35f;
        [Tooltip("Hold time to unscrew a piece latched to the spout network (the 'bad piece' penalty).")]
        public float unscrewSeconds = 1.4f;
        [Tooltip("After a steal, the piece can't be stolen back for this long.")]
        public float stealCooldown = 0.75f;

        [Header("Items & vending machine")]
        [Tooltip("The water bottle vending machine, against the back wall on the right (in the base and every chamber).")]
        public bool enableVendingMachine = true;
        [Tooltip("Price of water bottles, paid once from the shared bank (everyone gets one).")]
        public int waterBottlePrice = 750;
        [Tooltip("Sips in each bottle.")]
        public int waterBottleCharges = 10;
        [Tooltip("Seconds one sip lasts.")]
        public float waterBottleBuffSeconds = 45f;
        [Tooltip("Walking speed while the sip lasts (1 = normal).")]
        public float waterBottleSpeedMultiplier = 1.2f;
        [Tooltip("Dashes in a row while the sip lasts (normally 1).")]
        public int waterBottleDashes = 2;
        [Tooltip("Price of the first backpack (at the base). Each upgrade costs backpackPriceGrowth times the one before.")]
        public int backpackPrice = 500;
        [Tooltip("Each backpack upgrade costs this many times the previous one.")]
        public float backpackPriceGrowth = 5f;
        [Tooltip("Backpack upgrades available (the first purchase counts as 1).")]
        public int backpackMaxLevel = 10;
        [Tooltip("Spare pieces each backpack level holds.")]
        public int backpackPiecesPerLevel = 1;
        [Tooltip("A Count Ant (base): price, and how much more money everything pays (0.1 = +10%).")]
        public int countAntPrice = 1500;
        public float countAntBonus = 0.1f;
        [Tooltip("Lucky Pirate's Foot (base): price, and extra luck (0.5 = every lucky chance is 1.5x as likely).")]
        public int pirateFootPrice = 2000;
        public float pirateFootLuck = 0.5f;
        [Range(0f, 1f)] [Tooltip("Lucky break: chance a filled pipe pays double (BONUS!). Luck raises it.")]
        public float bonusPipeChance = 0.05f;
        [Tooltip("Funnel (chamber 2, needs the water bottle): price.")]
        public int funnelPrice = 2500;
        [Tooltip("Crossing Sign (chamber 3): price, and how many more crosses come down the belt (0.25 = +25%). Luck raises it.")]
        public int crossingSignPrice = 3000;
        public float crossingSignBoost = 0.25f;
        [Tooltip("Filter piece: price of one (it goes straight into the buyer's hands), and the chamber its machine first appears in (then every chamber after).")]
        public int filterPrice = 400;
        [Tooltip("Price of the Spider Monkey Serum (base), paid once from the shared bank (everyone gets one).")]
        public int serumPrice = 1800;
        [Tooltip("Doses per serum. Each dose lasts a whole chamber.")]
        public int serumCharges = 4;
        [Tooltip("Once owned, the serum machine sells refills: everyone back to full doses for this much.")]
        public int serumRefillPrice = 500;
        [Tooltip("Price of the Spoon (base): dashes go through pipes.")]
        public int spoonPrice = 2500;
        public int filterFromChamber = 4;
        [Tooltip("Every filter a stream has been through makes its pipes pay this much more (0.5 = +50% per filter), whichever way the filter took it: filtering is a risk, so it always pays.")]
        public float filterGradeBonus = 0.5f;
        [Tooltip("On top of that, a filtered stream pays this much more per step it sits away from water in the liquid links (each '+' is a step). Unfiltered spout liquid pays the plain rate.")]
        public float filterDistanceBonus = 0.25f;
        [Tooltip("Inventory panel width, in HUD units (the HUD is 1920 wide at 1080p).")]
        public float inventoryWidth = 860f;
        [Tooltip("Height of each item row in the inventory.")]
        public float inventoryRowHeight = 84f;
        [Range(0.6f, 2f)] [Tooltip("Scales all inventory text (1 = title 42, rows 34, details 32).")]
        public float inventoryTextScale = 1.1f;
        [Tooltip("Space between the inventory's edge and its contents.")]
        public float inventoryPadding = 28f;
        [Range(0.2f, 1f)] [Tooltip("Inventory background opacity (lower = more see-through).")]
        public float inventoryOpacity = 0.74f;

        [Header("Pit")]
        [Tooltip("What it costs (from the bank) to reprint a critter that fell into the belt's exit pit.")]
        public int pitRespawnCost = 50;
        [Tooltip("Seconds before a critter that fell in the pit comes back up on an arrival pad.")]
        public float pitRespawnDelay = 1.6f;

        [Header("Base")]
        [Tooltip("Start the game in the base (lockers, vending machine, practice board, no clock) instead of straight in a chamber.")]
        public bool startInBase = true;
        [Tooltip("Falling in the pit at the base is free.")]
        public bool freePitInBase = true;
        [Tooltip("Vending machines in a row on the base's back wall: the first sells water bottles, the rest are OUT OF ORDER until there's something to put in them.")]
        public int baseVendingMachines = 6;

        [Header("Chambers & liquids")]
        [Tooltip("Grime added each time you teleport to the next chamber (the lab slowly falls apart).")]
        public float grimePerChamber = 0.2f;
        [Tooltip("Seconds everyone must stay on teleporters before one is picked.")]
        public float readyCountdownSeconds = 3f;
        [Tooltip("Every liquid. The lab's gets worse as you go deeper: each chamber uses the last entry whose 'fromChamber' it has reached. Entries with fromChamber 0 are only made by filters (see purifiesTo / concentratesTo).")]
        public LiquidType[] liquids =
        {
            new LiquidType { name = "CLEAN WATER",  fromChamber = 1, color = new Color(0.55f, 0.85f, 1f),  glow = new Color(0.25f, 0.6f, 1.3f), purifiesTo = "FILTERED WATER" },
            new LiquidType { name = "TAP WATER?",   fromChamber = 3, color = new Color(0.5f, 0.8f, 0.72f), glow = new Color(0.25f, 0.7f, 0.6f), purifiesTo = "FILTERED WATER" },
            new LiquidType { name = "TOXIC SLIME",  fromChamber = 4, color = new Color(0.25f, 1f, 0.45f),  glow = new Color(0.3f, 2.2f, 0.7f), purifiesTo = "FILTERED WATER" },
            new LiquidType { name = "IRRADIATED",   fromChamber = 6, color = new Color(0.75f, 1f, 0.15f),  glow = new Color(1.8f, 3.2f, 0.3f), purifiesTo = "FILTERED WATER" },
            new LiquidType { name = "VOID",         fromChamber = 8, color = new Color(0.03f, 0.02f, 0.05f), glow = new Color(0.55f, 0.15f, 1.1f), purifiesTo = "FILTERED WATER" },
            new LiquidType { name = "FILTERED WATER",  fromChamber = 0, color = new Color(0.58f, 0.82f, 1f), glow = new Color(0.52f, 0.74f, 0.9f), purifiesTo = "PURE WATER" },
            new LiquidType { name = "PURE WATER",      fromChamber = 0, color = new Color(0.72f, 0.9f, 1f),  glow = new Color(1f, 1.26f, 1.4f),    purifiesTo = "DEIONIZED WATER" },
            new LiquidType { name = "DEIONIZED WATER", fromChamber = 0, color = new Color(0.85f, 0.97f, 1f), glow = new Color(1.62f, 1.84f, 1.9f) },
        };

        [Header("Holograms")]
        [Range(0.5f, 2f)] [Tooltip("Size of the hologram over the spout (countdown, liquid name, flow and pay).")]
        public float spoutHologramScale = 1.15f;
        [Range(0.5f, 2f)] [Tooltip("Size of the labels over a filter's outlets.")]
        public float filterLabelScale = 1f;
        [Tooltip("Show the pay multiplier (x1.5) after the liquid's name on the filter's outlet labels.")]
        public bool filterLabelShowsMultiplier = false;

        [Header("Camera")]
        [Range(20f, 89f)] [Tooltip("Camera tilt during a round. Lower = more horizontal, shows more of the back wall.")]
        public float gameplayCameraPitch = 48f;
        [Range(0.5f, 2f)] [Tooltip("1 = auto-fit the whole room. Higher = closer.")]
        public float gameplayCameraZoom = 1f;
        [Range(10f, 89f)] [Tooltip("Follow-cam tilt between rounds when the players are near the BACK wall (lower = more horizontal, shows the wall).")]
        public float intermissionCameraPitch = 30f;
        [Range(10f, 89f)] [Tooltip("Follow-cam tilt when the players are near the FRONT of the room (steeper so nobody is hidden).")]
        public float intermissionCameraPitchFront = 58f;
        [Range(0.3f, 3f)] [Tooltip("Follow-cam zoom between rounds. Higher = closer.")]
        public float intermissionCameraZoom = 1f;

        [Header("Lockers")]
        [Tooltip("One locker per player on the far left of the back wall, in the base and every chamber: player color, species, size, body color and clothes in one. Players appear in front of their own.")]
        public bool enableLockers = true;
        [Tooltip("How far the locker strip behind the top belt is (metres from the belt's outer edge to the back wall).")]
        public float closetStripDepth = 1.7f;

        [Header("Unlocks")]
        [Tooltip("Times you've stood in the spray when clean water leaks out, to unlock the shower cap.")]
        public int unlockShowerCapSprays = 1;
        [Tooltip("...and the swim trunks.")]
        public int unlockSwimTrunksSprays = 10;
        [Tooltip("Hops over pipes full of clean water (the same pipe counts every time) to unlock arm floaties.")]
        public int unlockFloatiesWaterHops = 10;
        [Tooltip("Pipes the whole team has ever filled with clean water, to unlock SHARC in the Bio-Reactor.")]
        public int unlockSharcCleanPipes = 1000;
        [Tooltip("How far (metres) the leaking stream reaches when deciding who got sprayed.")]
        public float sprayReach = 2.2f;
        [Tooltip("Testing: everything in the lockers is available.")]
        public bool unlockAllCosmetics = false;
        [Tooltip("Testing: the team owns every shop item (at least level 1) without buying. Untick for real play.")]
        public bool testOwnAllItems = false;
        [Tooltip("Testing: wipe all stats and unlocks every time you press Play. Untick to keep progress.")]
        public bool resetUnlocksOnPlay = false;

        [Header("Music")]
        [Range(0f, 1f)] public float musicVolume = 0.55f;
        [Range(0f, 1f)] [Tooltip("The melody that comes in when the liquid starts flowing.")]
        public float musicLeadVolume = 0.7f;
        [Range(0f, 1f)] [Tooltip("The harmony line that joins the melody on its second time round.")]
        public float musicHarmonyVolume = 0.3f;
        [Range(0f, 1f)] [Tooltip("How much worn-VHS damage (wow/flutter, hiss, skips, dropouts) grime adds to the music.")]
        public float musicVhsAmount = 1f;
        [Range(0f, 1f)] [Tooltip("How much the flow valve speeds up/slows down the music. 1 = exactly the flow speed (3x = chipmunks), 0 = off.")]
        public float musicTempoFollowsFlow = 0.3f;
        [Range(-12f, 12f)] [Tooltip("Pitch of the normal music (not the panic loop), in semitones. Independent of speed; drums aren't pitched.")]
        public float musicPitchSemitones = -3f;
        [Range(0.5f, 1.5f)] [Tooltip("Tempo of the normal music (not the panic loop). 0.84 matches the old tape-style -3 semitones.")]
        public float musicSpeed = 0.84f;
        [Range(0f, 2f)] [Tooltip("Panic loop loudness relative to the rest of the music.")]
        public float musicPanicVolume = 1.4f;
        [Tooltip("On: instead of cutting to the alarm loop, the music keeps going while a tension riser climbs as the liquid gets closer to spilling (topping out at the spill) and drops back as you lay pipe. Off: the old swap to the alarm loop at panic.")]
        public bool musicPanicRiser = true;
        [Tooltip("How many seconds before the spill the riser starts creeping in (a hint long before the full panic).")]
        public float musicRiserSeconds = 20f;
        [Range(0f, 2f)] [Tooltip("Riser loudness relative to the music.")]
        public float musicRiserVolume = 0.6f;
        [Tooltip("The root of the tense minor chord the riser climbs to (MIDI number: 57 = A3). It never resolves: at the spill it cuts into a splash.")]
        public int musicRiserRootNote = 57;
        [Range(0f, 1f)] [Tooltip("Music level at full tension (0.5 = half volume), so the riser sits on top.")]
        public float musicPanicDuck = 0.5f;
        [Range(0f, 1f)] [Tooltip("How muffled the music gets at full tension (a low-pass, like your ears under pressure). 0 = none.")]
        public float musicPanicMuffle = 0.6f;
        [Tooltip("A heartbeat under the riser: one beat per pipe the liquid fills, two when pipes take longer than musicHeartbeatSplitSeconds. Louder as the tension rises.")]
        public bool musicHeartbeat = true;
        [Range(0f, 2f)] public float musicHeartbeatVolume = 0.8f;
        [Tooltip("Pipes slower than this (seconds per pipe at the current flow speed) get two heartbeats each.")]
        public float musicHeartbeatSplitSeconds = 1.5f;
        [Tooltip("Play recorded stems from Resources/Songs/<musicSong> instead of the code-built synth music. Files are named layer_variant (drums_008, bass_005, ...); files without an underscore (full mixes) are ignored. All stems of a song must be the same length. musicPitchSemitones and musicSpeed don't apply; the panic loop is still the synth one.")]
        public bool musicRealAudio = false;
        [Tooltip("Folder under Resources/Songs played when musicRealAudio is on.")]
        public string musicSong = "Overflow";
        [Tooltip("Stem layers that act as the melody (fade in with the flow, silent in the base and off the clock), comma-separated. The other layers always play.")]
        public string musicStemLeadLayers = "vocals";
        [Tooltip("On: the lead stems (vocals) act as the melody, coming in with the flow. Off: they're silent during chamber rounds and play only off the clock and at the base.")]
        public bool musicStemLeadInChambers = false;
        [Tooltip("Off: the lead stems (vocals) never play at all (they aren't even loaded).")]
        public bool musicStemLeadOn = true;
        [Tooltip("Stem layers picked once per song and never swapped, comma-separated. Every other layer re-rolls its variant each time the loop comes round.")]
        public string musicStemFixedLayers = "";
        [Tooltip("Stem layers that build up into the flow (comma-separated, e.g. \"synth\"): silent at the base and off the clock, they creep in over the countdown's last seconds and play in full while the liquid flows. Blank = every non-lead layer plays all the time.")]
        public string musicStemFlowLayers = "synth";
        [Tooltip("How many seconds before the countdown ends the flow layers start creeping in (they reach full volume at zero).")]
        public float musicStemFlowInSeconds = 8f;
        [Tooltip("Music grime gained per chamber (chamber 1 = 0). A song's Song asset gates its variants by this, separate from the lab's look: 0.1 reaches full grime at chamber 11.")]
        public float musicGrimePerChamber = 0.1f;

        [Header("Feel & sound")]
        public float screenShakeScale = 1f;
        public bool hitStop = true;
        [Range(0f, 1f)] public float sfxVolume = 0.8f;
        [Tooltip("The HUD money counter counts up (and down) digit by digit with an NES-style tick.")]
        public bool moneyTickSound = true;
        [Range(0f, 1f)] public float moneyTickVolume = 0.5f;
        [Tooltip("Counter steps (and ticks) per second.")]
        public float moneyTicksPerSecond = 30f;
        [Tooltip("When counting one at a time would take longer than this, each step grows by moneyCountRamp, " +
                 "so big amounts speed up exponentially (and the tick rises in pitch) instead of taking minutes.")]
        public float moneyCountMaxSeconds = 2.5f;
        [Range(1.01f, 2f)] public float moneyCountRamp = 1.18f;
    }
}
