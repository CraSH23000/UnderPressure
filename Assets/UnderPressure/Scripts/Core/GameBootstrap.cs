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
        public int Bank { get; set; }
        /// <summary>Set by the teleporter: players materialise with sparks when the next level builds.</summary>
        public bool ArrivingByTeleport { get; set; }
        float lastInspectorGrime = -1f;
        int lastInspectorChamber = -1;
        /// <summary>Centre of the top walkway (where the teleporters appear) and how far apart to put them.</summary>
        public Vector3 PadAnchor { get; private set; }
        public float PadSpacing { get; private set; }
        JoinManager joins;
        Light sun;
        VolumeProfile post;
        float playerSpawnX, playerSpawnZ;
        float[] lockerXs = new float[0];
        const float BaseMachineSpacing = 1.8f; // gaps wide enough to see the bottle on the machine's sides
        Rect walkArea;

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

        /// <summary>Teleport to a chamber by number.</summary>
        public void GoToChamber(int n)
        {
            InBase = false;
            ChamberNumber = Mathf.Max(1, n);
            CurrentGrime = GrimeFor(ChamberNumber);
            Restart();
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
            Theme.Apply(InBase ? GrimeFor(furthest) : CurrentGrime);
            Theme.ApplyLiquid(InBase ? furthest : ChamberNumber, cfg);
            Mats.Init();
            ApplyLighting();
            level = new GameObject("UnderPressure_Level");
            Game.LevelRoot = level.transform;
            float S = cfg.tileSize;

            // ---------------- layout (board cell (0,0) is at the world origin)
            // The belt loops around the board with a walkable lane (laneWidth) on every side.
            Vector3 origin = Vector3.zero;
            float boardMinX = -S * 0.5f, boardMaxX = (cfg.columns - 0.5f) * S;
            float boardMinZ = -S * 0.5f, boardMaxZ = (cfg.rows - 0.5f) * S;
            const float beltHalfWidth = 1.0f;
            float lane = cfg.laneWidth;
            float xL = boardMinX - lane - beltHalfWidth, xR = boardMaxX + lane + beltHalfWidth;
            float zB = boardMinZ - lane - beltHalfWidth, zT = boardMaxZ + lane + beltHalfWidth;
            // the belt is floor-level, so the walkable area runs right out to its outer edge
            // one locker per player (all three closets in one), set into the back wall behind a strip of floor past the top belt
            var kinds = new System.Collections.Generic.List<ClosetKind>();
            if (cfg.enableLockers) for (int i = 0; i < JoinManager.MaxPlayers; i++) kinds.Add(ClosetKind.Locker);
            bool closets = kinds.Count > 0;
            float zBack = zT + beltHalfWidth + (closets ? Mathf.Max(1.1f, cfg.closetStripDepth) : 1.1f);
            // vending machines stand against the back wall on the right (none on the right wall any more)
            const bool vending = false;
            var walk = Rect.MinMaxRect(xL - beltHalfWidth, zB - beltHalfWidth, xR + beltHalfWidth + (vending ? 1.0f : 0f), closets ? zBack - 0.3f : zT + beltHalfWidth);
            walkArea = walk;
            float roomL = xL - beltHalfWidth - 1.3f, roomR = xR + beltHalfWidth + 1.3f + (vending ? 0.7f : 0f);
            var closetXs = new float[kinds.Count];
            {
                const float spacing = 2.1f;
                float cx = roomL + 1.5f + (kinds.Count - 1) * spacing * 0.5f; // lockers on the far left
                for (int i = 0; i < kinds.Count; i++) closetXs[i] = cx + (i - (kinds.Count - 1) * 0.5f) * spacing;
            }

            // belt centreline: out of the left wall along the top, clockwise round the corners, ending at the pit
            float pitGap = beltHalfWidth * 2f + 1.3f;
            var beltPath = new[]
            {
                new Vector3(xL - beltHalfWidth - 5.5f, 0f, zT),
                new Vector3(xR, 0f, zT),
                new Vector3(xR, 0f, zB),
                new Vector3(xL, 0f, zB),
                new Vector3(xL, 0f, zT - pitGap),
            };
            var pitCenter = new Vector3(xL, 0f, zT - pitGap + beltHalfWidth + 0.15f);

            var layout = new ChamberBuilder.Layout
            {
                walk = walk,
                xLeft = roomL,
                xRight = roomR,
                zBack = zBack,
                closetXs = closetXs,
                backClear = cfg.enableVendingMachine ? new Vector2(roomR - 0.6f - MachineCount(cfg) * BaseMachineSpacing, roomR) : Vector2.zero,
                hasVending = vending,
                vendingZ = (zB + zT) * 0.5f,
                zFront = zB - beltHalfWidth - 3f,
                slotZ = zT,
                slotHalfWidth = beltHalfWidth,
            };
            ChamberBuilder.Build(level.transform, layout, InBase ? 0 : ChamberNumber, decorSeed + (InBase ? 0 : ChamberNumber) * 31); // the base is "chamber 00"
            Game.Monitor = WallMonitor.Create(level.transform, ChamberBuilder.MonitorRect, ChamberBuilder.BackWallZ);
            for (int i = 0; i < kinds.Count; i++)
                Closet.Create(level.transform, kinds[i], new Vector3(closetXs[i], 0f, ChamberBuilder.BackWallZ), kinds[i] == ClosetKind.Locker ? i : -1);
            lockerXs = closetXs;
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
                int n = InBase ? Mathf.Max(1, cfg.baseVendingMachines) : Mathf.Max(1, stock.Count); // (one out of order if nothing yet)
                float z = ChamberBuilder.BackWallZ - 0.37f;
                for (int i = 0; i < n; i++)
                {
                    float x = roomR - 1.0f - (n - 1 - i) * BaseMachineSpacing;
                    var item = i < stock.Count ? stock[i] : null;
                    VendingMachine.Create(level.transform, new Vector3(x, 0f, z), Quaternion.Euler(0f, 180f, 0f), item, item != null ? 2 : 0, i);
                }
            }
            PadAnchor = new Vector3((boardMinX + boardMaxX) * 0.5f, 0f, boardMaxZ + lane * 0.5f);
            PadSpacing = 5.0f; // teleporters are ~4.6 wide including their tesla coils (up to three side by side)

            // ---------------- board
            var boardGo = new GameObject("Board");
            boardGo.transform.SetParent(level.transform, false);
            var board = boardGo.AddComponent<GridBoard>();
            board.Build(cfg, origin);
            Game.Board = board;

            // ---------------- conveyor
            var beltGo = new GameObject("Conveyor");
            beltGo.transform.SetParent(level.transform, false);
            var belt = beltGo.AddComponent<Conveyor>();
            belt.Build(cfg, beltPath, pitCenter);
            Game.Conveyor = belt;

            // belt speed lever: in the walkway corner by the belt entrance (top-left)
            Game.Lever = BeltLever.Create(level.transform, new Vector3(boardMinX - lane * 0.5f, 0f, boardMaxZ + lane * 0.5f), Vector3.forward);

            // ---------------- HUD (before anything that pops text)
            var hudGo = new GameObject("HUD", typeof(RectTransform));
            hudGo.transform.SetParent(level.transform, false);
            var hud = hudGo.AddComponent<Hud>();
            hud.Build();
            Game.Hud = hud;

            playerSpawnX = boardMinX - lane * 0.5f;
            playerSpawnZ = (boardMinZ + boardMaxZ) * 0.5f;

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
            var frame = new Bounds();
            frame.SetMinMax(new Vector3(xL - beltHalfWidth - 0.3f, 0f, zB - beltHalfWidth - 0.2f), new Vector3(xR + beltHalfWidth + (vending ? 1.2f : 0.3f), 1f, closets ? zBack + 0.2f : zT + beltHalfWidth + 0.8f));
            rig.Init(cam, frame);
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

        /// <summary>Where a player's arrival pad comes up (their spot in the left walkway).</summary>
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
