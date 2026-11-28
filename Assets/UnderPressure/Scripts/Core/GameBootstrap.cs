using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnderPressure
{
    /// <summary>
    /// Drop this on an empty GameObject in an empty scene and press Play.
    /// Builds the whole level (board, belt, plumber, HUD, lighting, post-processing) from code.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Optional. Leave empty to use the defaults in GameConfig.cs.")]
        public GameConfig config;

        [Header("Style")]
        [Tooltip("0 = pristine test chamber, 1 = rusted, stained, flickering. Later levels will creep toward 1.")]
        [Range(0f, 1f)] public float grime = 0f;
        [Tooltip("Starting chamber number. Teleporting to the next chamber counts up from here.")]
        public int chamberNumber = 1;
        [Tooltip("Same seed = same stains/broken panels every restart.")]
        public int decorSeed = 1234;

        GameObject level;

        // run state (survives restarts and chamber changes)
        public int ChamberNumber { get; private set; }
        /// <summary>In the base (lockers, shop, practice board, no clock) rather than a test chamber.</summary>
        public bool InBase { get; private set; }
        public float CurrentGrime { get; private set; }
        /// <summary>LAB FUNDING. Saved between sessions (see Progress.Bank).</summary>
        public long Bank { get => Progress.Bank; set => Progress.Bank = value; }
        /// <summary>Set by the teleporter: players materialise with sparks when the next level builds.</summary>
        public bool ArrivingByTeleport { get; set; }
        float lastInspectorGrime = -1f;
        int lastInspectorChamber = -1;
        /// <summary>Centre of the top walkway (where the teleporters appear) and how far apart to put them.</summary>
        public Vector3 PadAnchor { get; private set; }
        /// <summary>The top run of the belt: its centreline z (and the belt's half width).</summary>
        public float TopBeltZ { get; private set; }
        public const float BeltHalfWidth = 1.0f;
        /// <summary>The belt lever sits this far right of the pit's centre: clear of its railing with room to walk
        /// between, against the top belt.</summary>
        const float LeverFromPit = 3.45f;
        public float PadSpacing { get; private set; }
        JoinManager joins;
        Light sun;
        VolumeProfile post;
        float playerSpawnX, playerSpawnZ;
        float[] lockerXs = new float[0];
        const float BaseMachineSpacing = 1.8f; // gaps wide enough to see the bottle on the machine's sides
        Rect walkArea;

        // ---------------- the room grows with the players (GameConfig.roomGrowsWithPlayers, experimental)
        /// <summary>How many players the room is sized for: everyone who's joined (at least one, at most 8).</summary>
        public int GrowPlayers => Mathf.Clamp(joins != null ? joins.Slots.Count : 0, 1, JoinManager.Capacity);

        void Awake()
        {
            // Unity 6.6 projects default to "reload scene only" (no domain reload) when entering Play mode,
            // so static state from the previous Play session must be reset explicitly.
            Game.Clear();
            Game.Bootstrap = this;
            ChamberNumber = chamberNumber;
            CurrentGrime = grime;
            lastInspectorGrime = grime;
            lastInspectorChamber = chamberNumber;
            Mats.Init();
            Layers.SetupCollisionMatrix();
            if (GetComponent<JuiceHost>() == null) gameObject.AddComponent<JuiceHost>();
            var cfg = config != null ? config : ScriptableObject.CreateInstance<GameConfig>();
            Game.Config = cfg;
            if (cfg.resetUnlocksOnPlay)
            {
                Progress.ResetAll();
                Debug.Log("[UnderPressure] resetUnlocksOnPlay is on: stats and unlocks wiped. Untick it to keep progress.");
            }
            if (GetComponent<Sfx>() == null) gameObject.AddComponent<Sfx>();
            if (GetComponent<Music>() == null) gameObject.AddComponent<Music>();
            if (GrimeOverlay.I == null) GrimeOverlay.Create(transform);
            joins = GetComponent<JoinManager>();
            if (joins == null) joins = gameObject.AddComponent<JoinManager>();
            GameSettings.Load();
#if !UNITY_EDITOR
            if (PlayerPrefs.HasKey("up_display")) GameSettings.ApplyDisplay();
#endif
            InBase = cfg.startInBase;
            SetupLighting();
            BuildLevel(cfg);
        }

        /// <summary>How grimy chamber n is: the Inspector's starting grime, plus a bit per chamber after the starting one.</summary>
        public float GrimeFor(int n) => Mathf.Clamp01(grime + (n - chamberNumber) * (Game.Config != null ? Game.Config.grimePerChamber : 0.2f));

        /// <summary>
        /// Teleport to a chamber by number. Grime is a running value: each chamber further on adds grimePerChamber to
        /// wherever spills have left it (so cleaning up carries forward). Going back to an earlier chamber starts over
        /// from that chamber's own grime.
        /// </summary>
        public void GoToChamber(int n)
        {
            InBase = false;
            n = Mathf.Max(1, n);
            CurrentGrime = n > ChamberNumber
                ? Mathf.Clamp01(CurrentGrime + (n - ChamberNumber) * (Game.Config != null ? Game.Config.grimePerChamber : 0.2f))
                : n < ChamberNumber ? GrimeFor(n) : CurrentGrime;
            ChamberNumber = n;
            Restart();
        }

        /// <summary>Spills move grime up (filthy liquids) or down (clean ones). Returns how much it actually changed.</summary>
        public float AddGrime(float delta)
        {
            float before = CurrentGrime;
            CurrentGrime = Mathf.Clamp01(CurrentGrime + delta);
            return CurrentGrime - before;
        }

        /// <summary>Teleport back to the base. It's as grimy as the deepest chamber you've reached.</summary>
        public void GoToBase()
        {
            InBase = true;
            Restart();
        }

        public void Restart() => StartCoroutine(RestartCo());

        /// <summary>Teleport to the next chamber: number goes up, and so does the grime.</summary>
        public void NextChamber() => GoToChamber(ChamberNumber + 1);

        IEnumerator RestartCo()
        {
            if (JuiceHost.Instance != null) JuiceHost.Instance.StopAllCoroutines(); // drop pending delayed FX
            Time.timeScale = 1f;
            var cfg = Game.Config;
            if (level != null) Destroy(level);
            Game.Clear();
            yield return null; // let the old level actually go away
            Game.Config = cfg;
            BuildLevel(cfg);
        }

        void BuildLevel(GameConfig cfg)
        {
            // tweaking the Inspector values still takes effect on the next (re)build
            if (!Mathf.Approximately(grime, lastInspectorGrime)) { CurrentGrime = grime; lastInspectorGrime = grime; }
            if (chamberNumber != lastInspectorChamber) { ChamberNumber = chamberNumber; lastInspectorChamber = chamberNumber; }
            if (!InBase) Progress.ReachedChamber(ChamberNumber);
            int furthest = Progress.Furthest;
            Theme.Apply(CurrentGrime); // the base is as grimy as the run has got
            if (GrimeOverlay.I != null) GrimeOverlay.I.Snap(CurrentGrime);
            Theme.ApplyLiquid(InBase ? furthest : ChamberNumber, cfg);
            Mats.Init();
            ApplyLighting();
            level = new GameObject("UnderPressure_Level");
            Game.LevelRoot = level.transform;
            float S = cfg.tileSize;

            // ---------------- layout (board cell (0,0) is at the world origin)
            builtFor = GrowPlayers;
            var L = ComputeLayout(cfg, builtFor);
            layout = L;
            Vector3 origin = Vector3.zero;
            walkArea = L.walk;
            BuildScenery(cfg, L);
            Game.Monitor = WallMonitor.Create(level.transform, ChamberBuilder.MonitorRect, ChamberBuilder.MonitorWallZ);
            for (int i = 0; i < L.closetXs.Length; i++)
                Closet.Create(level.transform, ClosetKind.Locker, new Vector3(L.closetXs[i], 0f, ChamberBuilder.BackWallZ), i);
            lockerXs = L.closetXs;
            machines.Clear();
            if (cfg.enableVendingMachine)
            {
                // machines against the back wall on the right, facing the room. The base has a whole row: its own items,
                // then the water as a placeholder, then out-of-order ones. Each chamber has one: the item whose 'chamber'
                // is this one (water in chamber 1), or an out-of-order machine until that chamber gets an item.
                var stock = new System.Collections.Generic.List<ItemDef>();
                if (InBase)
                {
                    foreach (var d in Items.All) if (d.chamber == 0) stock.Add(d);   // the base's own items (backpack)
                    var water = Items.Get(Items.WaterBottle);                         // placeholder until there's more
                    if (water != null && !stock.Contains(water)) stock.Add(water);
                }
                else stock.AddRange(Items.StockFor(ChamberNumber)); // this chamber's special, plus the filter from chamber 4 on
                int n = MachineCount(cfg); // (one out of order if nothing yet)
                float z = ChamberBuilder.BackWallZ - 0.37f;
                for (int i = 0; i < n; i++)
                {
                    float x = L.roomR - 1.0f - (n - 1 - i) * BaseMachineSpacing;
                    var item = i < stock.Count ? stock[i] : null;
                    machines.Add(VendingMachine.Create(level.transform, new Vector3(x, 0f, z), Quaternion.Euler(0f, 180f, 0f), item, item != null ? 2 : 0, i).transform);
                }
            }
            PadAnchor = new Vector3((L.boardMinX + L.boardMaxX) * 0.5f, 0f, L.padZ);
            PadSpacing = 5.0f; // teleporters are ~4.6 wide including their tesla coils (up to three side by side)

            // ---------------- board
            var boardGo = new GameObject("Board");
            boardGo.transform.SetParent(level.transform, false);
            var board = boardGo.AddComponent<GridBoard>();
            board.Build(cfg, origin, L.cols, L.rows, L.minRow, L.spoutCell);
            Game.Board = board;

            // ---------------- conveyor
            var beltGo = new GameObject("Conveyor");
            beltGo.transform.SetParent(level.transform, false);
            var belt = beltGo.AddComponent<Conveyor>();
            belt.Build(cfg, L.beltPath, L.pitCenter);
            Game.Conveyor = belt;

            // belt speed lever: against the top belt, just right of the pit
            Game.Lever = BeltLever.Create(level.transform, new Vector3(L.xL + LeverFromPit, 0f, L.zT - BeltHalfWidth - 0.12f - BeltLever.HalfDepth - 0.04f), Vector3.forward);

            // ---------------- HUD (before anything that pops text)
            var hudGo = new GameObject("HUD", typeof(RectTransform));
            hudGo.transform.SetParent(level.transform, false);
            var hud = hudGo.AddComponent<Hud>();
            hud.Build();
            Game.Hud = hud;

            playerSpawnX = L.boardMinX - cfg.laneWidth * 0.5f;
            playerSpawnZ = (L.boardMinZ + L.boardMaxZ) * 0.5f;

            // ---------------- flow + rules
            var flowGo = new GameObject("Flow");
            flowGo.transform.SetParent(level.transform, false);
            var flow = flowGo.AddComponent<FlowController>();
            flow.Init(cfg, board);
            Game.Flow = flow;

            var gm = level.AddComponent<GameManager>();
            gm.Init(cfg, board, flow, hud);
            Game.Manager = gm;

            // ---------------- camera
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Theme.Ink;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rig = cam.GetComponent<CameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.Init(cam, L.frame);
            Game.Cam = rig;
            GameSettings.ApplyLight();

            // Fog only eats the far background, however big the board (and camera distance) gets.
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = rig.Distance * 1.05f;
            RenderSettings.fogEndDistance = rig.Distance * Mathf.Lerp(4f, 2.2f, Theme.Grime);

            // players who already joined (e.g. before a restart) come straight back
            foreach (var slot in joins.Slots) SpawnPlayer(slot, false);
            hud.SetRoster(Game.Players);
            if (ArrivingByTeleport)
            {
                ArrivingByTeleport = false;
                for (int i = 0; i < Game.Players.Count; i++) Game.Players[i].ArriveAt(Game.Players[i].transform.position, 0.6f + i * 0.18f, null);
                hud.Flash(new Color(0.8f, 0.9f, 1f), 0.8f);
                Sfx.Play(Sfx.I != null ? Sfx.I.materialize : null, 0.9f, 1f, 0f);
            }
            Music.Instance?.Resume();
        }

        // ================================================================== room layout

        /// <summary>Everything about the room's size and shape, worked out from the config and the player count.</summary>
        class RoomLayout
        {
            public int cols, rows, minRow;
            public Vector2Int spoutCell;
            public float boardMinX, boardMaxX, boardMinZ, boardMaxZ, xL, xR, zB, zT, zBack, roomL, roomR, roomFront, padZ;
            public bool closets;
            public Rect walk;
            public float[] closetXs;
            public Vector3[] beltPath;
            public Vector3 pitCenter;
            public Bounds frame;
        }

        RoomLayout layout;
        int builtFor;          // how many players the room was laid out for
        GameObject scenery;    // walls, floor, colliders, the secret door and its room: rebuilt when the room grows
        readonly System.Collections.Generic.List<Transform> machines = new System.Collections.Generic.List<Transform>();

        RoomLayout ComputeLayout(GameConfig cfg, int players)
        {
            var L = new RoomLayout();
            float S = cfg.tileSize;
            L.cols = cfg.columns; L.rows = cfg.rows; L.minRow = 0;
            L.spoutCell = cfg.spoutCell;
            int lockerCount = JoinManager.MaxPlayers;
            if (cfg.roomGrowsWithPlayers)
            {
                // growBaseSize square for one player, a column and a row more per player. The top-left never moves:
                // columns are added on the right and rows at the bottom (below row 0), and the spout counts from the top-left.
                int baseSize = Mathf.Max(2, cfg.growBaseSize);
                int topRow = baseSize - 1;
                L.cols = L.rows = baseSize + players - 1;
                L.minRow = topRow - (L.rows - 1);
                L.spoutCell = new Vector2Int(cfg.spoutCell.x, topRow - cfg.spoutCell.y);
                lockerCount = players;
            }
            L.boardMinX = -S * 0.5f; L.boardMaxX = (L.cols - 0.5f) * S;
            L.boardMinZ = (L.minRow - 0.5f) * S; L.boardMaxZ = (L.minRow + L.rows - 0.5f) * S;
            const float hw = BeltHalfWidth;
            float lane = cfg.laneWidth;
            L.xL = L.boardMinX - lane - hw; L.xR = L.boardMaxX + lane + hw;
            L.zB = L.boardMinZ - lane - hw; L.zT = L.boardMaxZ + lane + hw;
            // where the teleporters go (in the secret room next door, at this depth): just behind the board's top edge
            L.padZ = L.boardMaxZ + 0.22f + 0.2f + ReadyPad.FootprintRadius;
            // one locker per player (all three closets in one), set into the back wall behind a strip of floor past the top belt
            int lockers = cfg.enableLockers ? lockerCount : 0;
            L.closets = lockers > 0;
            L.zBack = L.zT + hw + (L.closets ? Mathf.Max(1.1f, cfg.closetStripDepth) : 1.1f);
            L.roomL = L.xL - hw - 1.3f; L.roomR = L.xR + hw + 1.3f;
            L.roomFront = L.zB - hw - 3f;
            // every floor tile in the room is walkable: outside the belt loop too, up to the side walls and the front edge
            L.walk = Rect.MinMaxRect(L.roomL + 0.25f, L.roomFront + 0.3f, L.roomR - 0.25f, L.closets ? L.zBack - 0.3f : L.zT + hw);
            L.closetXs = new float[lockers];
            const float lockerSpacing = 2.1f;
            for (int i = 0; i < lockers; i++) L.closetXs[i] = L.roomL + 1.5f + i * lockerSpacing; // on the far left, left to right
            // belt centreline: out of the left wall along the top, clockwise round the corners, ending at the pit
            // (the extra 0.5 keeps the pit's north caution tape clear of the top run of the belt)
            float pitGap = hw * 2f + 1.3f + 0.5f;
            L.beltPath = new[]
            {
                new Vector3(L.xL - hw - 5.5f, 0f, L.zT),
                new Vector3(L.xR, 0f, L.zT),
                new Vector3(L.xR, 0f, L.zB),
                new Vector3(L.xL, 0f, L.zB),
                new Vector3(L.xL, 0f, L.zT - pitGap),
            };
            L.pitCenter = new Vector3(L.xL, 0f, L.zT - pitGap + hw + 0.15f);
            L.frame = new Bounds();
            L.frame.SetMinMax(new Vector3(L.xL - hw - 0.3f, 0f, L.zB - hw - 0.2f), new Vector3(L.xR + hw + 0.3f, 1f, L.closets ? L.zBack + 0.2f : L.zT + hw + 0.8f));
            return L;
        }

        /// <summary>The room's shell for this layout: walls, floor, colliders, decor, and the secret door and its room.</summary>
        void BuildScenery(GameConfig cfg, RoomLayout L)
        {
            scenery = new GameObject("Scenery");
            scenery.transform.SetParent(level.transform, false);
            var chamber = new ChamberBuilder.Layout
            {
                walk = L.walk,
                xLeft = L.roomL,
                xRight = L.roomR,
                zBack = L.zBack,
                closetXs = L.closetXs,
                backClear = cfg.enableVendingMachine ? new Vector2(L.roomR - 0.6f - MachineCount(cfg) * BaseMachineSpacing, L.roomR) : Vector2.zero,
                hasVending = false, // (vending machines stand against the back wall now, none on the right wall)
                vendingZ = (L.zB + L.zT) * 0.5f,
                zFront = L.roomFront,
                slotZ = L.zT,
                slotHalfWidth = BeltHalfWidth,
                secretDoor = true,
            };
            ChamberBuilder.Build(scenery.transform, chamber, InBase ? 0 : ChamberNumber, decorSeed + (InBase ? 0 : ChamberNumber) * 31); // the base is "chamber 00"
            TopBeltZ = L.zT;
            // the secret door in the right wall, onto a second room the same size as this one: the teleporters live in there
            if (ChamberBuilder.HasSecretDoor)
                SecretDoor.Create(scenery.transform, L.roomR, ChamberBuilder.SecretDoorZ0, ChamberBuilder.SecretDoorZ1, L.roomL, L.roomFront, L.zBack); // (GameManager lays it out)
        }

        /// <summary>
        /// The room grows to fit everyone who's joined (GameConfig.roomGrowsWithPlayers), live, mid-flow included.
        /// The top-left never moves, so the board's pipes, the spout, the flow, the pit, the lever and the lockers stay
        /// put. The shell (walls, floor, the secret door and its room) is rebuilt at the new size; the board gains a
        /// column and a row per player; the belt is re-laid with its pieces gliding over; a locker is added per player;
        /// the vending machines slide along; teleporters already called up come up again in the moved room.
        /// </summary>
        public void GrowRoom()
        {
            var cfg = Game.Config;
            if (cfg == null || !cfg.roomGrowsWithPlayers || level == null || layout == null) return;
            int want = GrowPlayers;
            if (want <= builtFor) return; // (it doesn't shrink while you're in it)
            if (Game.Manager != null && Game.Manager.Teleporting) return; // the level is about to be rebuilt anyway
            var old = layout;
            var L = ComputeLayout(cfg, want);
            builtFor = want;
            layout = L;
            float dx = L.roomR - old.roomR;

            // the teleporter room moves right with the wall: anyone in it moves with it
            var door = SecretDoor.Instance;
            bool doorWasOpen = door != null && !door.IsClosed;
            foreach (var p in Game.Players)
                if (p != null && p.transform.position.x > old.roomR) p.ShiftBy(new Vector3(dx, 0f, 0f));

            // the shell, at the new size
            if (scenery != null) Destroy(scenery);
            BuildScenery(cfg, L);
            if (doorWasOpen && SecretDoor.Instance != null) SecretDoor.Instance.OpenInstantly();
            Game.Monitor?.Relayout(ChamberBuilder.MonitorRect, ChamberBuilder.MonitorWallZ);

            // the live parts, patched
            Game.Board?.Grow(L.cols, L.rows, L.minRow);
            Game.Conveyor?.Repath(L.beltPath);
            for (int i = old.closetXs.Length; i < L.closetXs.Length; i++)
                Closet.Create(level.transform, ClosetKind.Locker, new Vector3(L.closetXs[i], 0f, ChamberBuilder.BackWallZ), i);
            lockerXs = L.closetXs;
            foreach (var m in machines) if (m != null) m.position += new Vector3(dx, 0f, 0f);
            PadAnchor = new Vector3((L.boardMinX + L.boardMaxX) * 0.5f, 0f, L.padZ);
            playerSpawnX = L.boardMinX - cfg.laneWidth * 0.5f;
            playerSpawnZ = (L.boardMinZ + L.boardMaxZ) * 0.5f;
            walkArea = L.walk;
            foreach (var p in Game.Players) if (p != null) p.SetWalkArea(L.walk);
            if (Game.Cam != null)
            {
                Game.Cam.SetFrame(L.frame);
                RenderSettings.fogStartDistance = Game.Cam.Distance * 1.05f;
                RenderSettings.fogEndDistance = Game.Cam.Distance * Mathf.Lerp(4f, 2.2f, Theme.Grime);
            }
            Game.Manager?.OnRoomGrown();

            // the show (for now): a rumble, and dust where the walls moved out
            Game.Cam?.AddTrauma(0.45f);
            Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.8f, 0.6f, 0f);
            Sfx.Play(Sfx.I != null ? Sfx.I.creak : null, 0.6f, 0.7f, 0f);
            Sfx.Play(Sfx.I != null ? Sfx.I.thunk : null, 0.9f, 0.55f, 0f);
            var dust = new Color(0.75f, 0.77f, 0.8f);
            for (float z = L.roomFront; z <= L.zBack; z += 2.5f)
                Fx.Burst(new Vector3(L.roomR - 0.3f, 0.2f, z), dust, new Color(0.55f, 0.56f, 0.58f), 6, 1.6f, 0.16f, 0.7f, -0.05f);
            for (float x = L.roomL; x <= L.roomR; x += 2.5f)
                Fx.Burst(new Vector3(x, 0.2f, L.roomFront + 0.3f), dust, new Color(0.55f, 0.56f, 0.58f), 6, 1.6f, 0.16f, 0.7f, -0.05f);
            Debug.Log($"[UnderPressure] Room grew for {want} players: {L.cols}x{L.rows} board.");
        }

        /// <summary>How many vending machines this room has.</summary>
        int MachineCount(GameConfig cfg) => InBase ? Mathf.Max(1, cfg.baseVendingMachines) : Mathf.Max(1, Items.StockFor(ChamberNumber).Count);

        public Vector3 SpawnPoint(int index)
        {
            // everyone turns up in front of their own locker
            if (index >= 0 && index < lockerXs.Length) return new Vector3(lockerXs[index], 0f, ChamberBuilder.BackWallZ - 1.0f);
            return new Vector3(playerSpawnX, 0f, playerSpawnZ + (index - 1.5f) * 1.1f);
        }

        public void SpawnPlayer(JoinManager.Slot slot, bool announce)
        {
            var cfg = Game.Config;
            if (announce && GrowPlayers > builtFor) GrowRoom(); // a new player: the room grows to fit them first
            var previewGo = new GameObject("PlacementPreview_P" + (slot.index + 1));
            previewGo.transform.SetParent(level.transform, false);
            var preview = previewGo.AddComponent<PlacementPreview>();
            preview.Build(cfg.tileSize);

            joins.AssignColor(slot);
            var color = slot.Color;
            var go = new GameObject("Player" + (slot.index + 1));
            go.transform.SetParent(level.transform, false);
            go.transform.position = SpawnPoint(slot.index);
            var player = go.AddComponent<PlayerController>();
            player.Init(cfg, Game.Board, Game.Conveyor, preview, walkArea, slot.index, color, new PlayerInputs(slot.scheme, slot.device), CritterLook.Load(slot.index));
            Game.Players.Add(player);
            Game.Hud.SetRoster(Game.Players);

            if (announce)
            {
                Banner.Show($"P{slot.index + 1} JOINED!", color, 90, 1.0f, -40f);
                player.ArriveAt(go.transform.position, 0f, () =>
                {
                    Fx.Confetti(go.transform.position + Vector3.up, 30, 6f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.pop : null, 0.9f, 1.1f + slot.index * 0.1f);
                });
            }
        }

        void SetupLighting()
        {
            foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
                if (l.gameObject.scene.IsValid() && l.type == LightType.Directional) l.gameObject.SetActive(false);
            foreach (var v in Resources.FindObjectsOfTypeAll<Volume>())
                if (v.gameObject.scene.IsValid() && v.gameObject.name != "PostFX") v.gameObject.SetActive(false);

            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(62f, -25f, 0f);

            var volGo = new GameObject("PostFX");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 50f;
            post = ScriptableObject.CreateInstance<VolumeProfile>();
            post.Add<Bloom>(true);
            post.Add<Vignette>(true);
            post.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            post.Add<ColorAdjustments>(true);
            post.Add<ChromaticAberration>(true);
            post.Add<FilmGrain>(true);
            vol.sharedProfile = post;
        }

        /// <summary>Sterile = bright, cool, crisp. Grimy = dim, warm, foggy, grainy.</summary>
        void ApplyLighting()
        {
            float g = Theme.Grime;
            sun.color = Theme.LightColor;
            sun.intensity = Theme.LightIntensity;
            sun.shadowStrength = Mathf.Lerp(0.45f, 0.8f, g);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Theme.Ambient;
            RenderSettings.ambientEquatorColor = Color.Lerp(Theme.Ambient, Theme.Floor, 0.5f);
            RenderSettings.ambientGroundColor = Theme.Ambient * 0.55f;
            RenderSettings.fog = g > 0.05f;
            RenderSettings.fogColor = Theme.Fog;

            if (post.TryGet(out Bloom bloom)) { bloom.intensity.Override(Mathf.Lerp(0.8f, 1.1f, g)); bloom.threshold.Override(1.0f); bloom.scatter.Override(0.65f); }
            if (post.TryGet(out Vignette vig)) { vig.intensity.Override(Mathf.Lerp(0.18f, 0.32f, g)); vig.smoothness.Override(0.5f); }
            if (post.TryGet(out ColorAdjustments ca)) { ca.saturation.Override(Mathf.Lerp(-5f, -25f, g)); ca.contrast.Override(Mathf.Lerp(12f, 20f, g)); }
            if (post.TryGet(out ChromaticAberration chroma)) chroma.intensity.Override(Mathf.Lerp(0.0f, 0.35f, g));
            if (post.TryGet(out FilmGrain grain)) { grain.type.Override(FilmGrainLookup.Medium3); grain.intensity.Override(Mathf.Lerp(0.0f, 0.55f, g)); }
        }
    }
}
