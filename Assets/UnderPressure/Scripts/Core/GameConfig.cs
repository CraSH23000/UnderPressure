using UnityEngine;

namespace UnderPressure
{
    public enum PipeType { Straight, Corner, Cross }

    /// <summary>What's flowing through the pipes. Chosen per chamber: the last entry whose fromChamber has been reached.</summary>
    [System.Serializable]
    public class LiquidType
    {
        public string name = "CLEAN WATER";
        public int fromChamber = 1;
        public Color color = new Color(0.55f, 0.85f, 1f);
        [ColorUsage(false, true)] public Color glow = new Color(0.25f, 0.6f, 1.2f);
    }

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
        [Tooltip("The leak warning (and panic music) kicks in when fewer than this many connected pipes are left ahead of the liquid.")]
        public int panicPiecesAhead = 4;

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
        public float sprintMultiplier = 1.5f;
        public float jumpHeight = 0.7f;
        public float jumpSeconds = 0.45f;
        [Tooltip("Speed multiplier while crawling on top of placed pipes.")]
        public float crawlSpeedMultiplier = 0.4f;
        public float vaultSeconds = 0.55f;
        public float climbUpSeconds = 0.32f;
        [Tooltip("Speed lost per loose piece underfoot.")]
        public float loosePieceSlowdown = 0.16f;
        public float minLooseSpeedMultiplier = 0.55f;

        [Header("Interaction")]
        public float pickupRadius = 1.15f;
        [Tooltip("The place/unscrew target is the tile under a point this far (in tiles) in front of you. " +
                 "0.35 = place on your own tile until you're past its centre, then the next one. You hop off a piece placed under you.")]
        public float placeProbeTiles = 0.35f;
        [Tooltip("Hold time to unbolt a piece that is NOT attached to the spout network.")]
        public float unboltSeconds = 0.35f;
        [Tooltip("Hold time to unscrew a piece latched to the spout network (the 'bad piece' penalty).")]
        public float unscrewSeconds = 1.4f;
        [Tooltip("After a steal, the piece can't be stolen back for this long.")]
        public float stealCooldown = 0.75f;

        [Header("Pit")]
        [Tooltip("What it costs (from the bank) to reprint a critter that fell into the belt's exit pit.")]
        public int pitRespawnCost = 50;
        [Tooltip("Seconds before a critter that fell in the pit comes back up on an arrival pad.")]
        public float pitRespawnDelay = 1.6f;

        [Header("Chambers & liquids")]
        [Tooltip("Grime added each time you teleport to the next chamber (the lab slowly falls apart).")]
        public float grimePerChamber = 0.2f;
        [Tooltip("Seconds everyone must stay on teleporters before one is picked.")]
        public float readyCountdownSeconds = 3f;
        [Tooltip("The lab's liquid gets worse as you go deeper. Each chamber uses the last entry whose 'fromChamber' it has reached.")]
        public LiquidType[] liquids =
        {
            new LiquidType { name = "CLEAN WATER",  fromChamber = 1, color = new Color(0.55f, 0.85f, 1f),  glow = new Color(0.25f, 0.6f, 1.3f) },
            new LiquidType { name = "TAP WATER?",   fromChamber = 3, color = new Color(0.5f, 0.8f, 0.72f), glow = new Color(0.25f, 0.7f, 0.6f) },
            new LiquidType { name = "TOXIC SLIME",  fromChamber = 4, color = new Color(0.25f, 1f, 0.45f),  glow = new Color(0.3f, 2.2f, 0.7f) },
            new LiquidType { name = "IRRADIATED",   fromChamber = 6, color = new Color(0.75f, 1f, 0.15f),  glow = new Color(1.8f, 3.2f, 0.3f) },
            new LiquidType { name = "VOID",         fromChamber = 8, color = new Color(0.03f, 0.02f, 0.05f), glow = new Color(0.55f, 0.15f, 1.1f) },
        };

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

        [Header("Cosmetic closets")]
        [Tooltip("Bio-Reactor: swap your critter's species.")]
        public bool enableBioReactor = true;
        [Tooltip("Spray Booth: repaint your critter (the floor ring keeps your player colour).")]
        public bool enableSprayBooth = true;
        [Tooltip("Wardrobe: hats, torsos and shoes.")]
        public bool enableWardrobe = true;
        [Tooltip("How far the closet strip behind the top belt is (metres from the belt's outer edge to the back wall).")]
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
        [Tooltip("Testing: everything in the closets is available.")]
        public bool unlockAllCosmetics = false;
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

        [Header("Feel & sound")]
        public float screenShakeScale = 1f;
        public bool hitStop = true;
        [Range(0f, 1f)] public float sfxVolume = 0.8f;
    }
}
