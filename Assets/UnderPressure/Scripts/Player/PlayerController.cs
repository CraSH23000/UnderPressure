using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// The plumber. Overcooked-style context interaction: one button grabs / places / (hold) unscrews,
    /// depending on what's in your hands and what you're facing.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        enum Mode { Walk, Vault, ClimbUp, Crawl, ClimbDown }
        enum Act { None, TakeFromBelt, PickLoose, Steal, Place, Unscrew, Swap, Drop, PutOnBelt, Valve, Lever, Blocked, Closet, Vending }

        public PipePiece Held { get; private set; }

        public class PlayerStats { public int pipesPlaced, unscrewed, steals, robbed, drops, jumps; }
        public readonly PlayerStats Stats = new PlayerStats();
        public int Index { get; private set; }
        public Color Color { get; private set; }
        public CritterLook Look { get; private set; }
        public bool InCloset => closet != null;
        public ControlScheme Scheme => input.Scheme;

        GameConfig cfg;
        GridBoard board;
        Conveyor belt;
        PlayerInputs input;
        CharacterController cc;
        PlayerRig rig;
        PlacementPreview preview;
        Rect walkArea;
        float S;

        Mode mode = Mode.Walk;
        Vector3 vel;
        Vector3 facing = Vector3.right;
        float height;

        // scripted moves (vault / climb up / climb down)
        float modeT, modeDur, hFrom, hTo, arc;
        Vector3 moveFrom, moveTo;

        // current interaction target
        Act act;
        Vector2Int actCell;
        PipePiece actPiece, highlighted;
        PlayerController actVictim;

        // jumping / sprinting
        float jumpT = -1f, jumpH;
        bool sprinting; // the old hold-to-sprint (kept, but the button now dashes)
        float sprintDustTimer;
        // dash (Overcooked style): a quick burst in the direction you're going
        float dashT, dashCooldownT;
        Vector3 dashDir;
        public bool Dashing => dashT > 0f;
        public bool Airborne => jumpT >= 0f;
        bool valveActive;   // on a speed control (flow valve or belt lever)
        bool onLever;       // ...and it's the belt lever

        // hold-to-unscrew
        PipePiece holdPiece;
        Act holdAct;
        float holdProgress, holdDur, ratchetTimer, creakTimer;
        int ratchetCount;

        public void Init(GameConfig config, GridBoard b, Conveyor c, PlacementPreview p, Rect walkable,
            int index, Color color, PlayerInputs inputs, CritterLook look = null)
        {
            Look = (look ?? CritterLook.Default(index)).Clone();
            cfg = config; board = b; belt = c; preview = p; walkArea = walkable;
            Index = index; Color = color; input = inputs;
            S = cfg.tileSize;
            gameObject.layer = Layers.Player;
            cc = gameObject.AddComponent<CharacterController>();
            cc.height = 1.0f;
            cc.radius = 0.32f;
            cc.center = new Vector3(0, 0.52f, 0);
            cc.stepOffset = 0.1f;
            cc.skinWidth = 0.03f;
            cc.minMoveDistance = 0f;
            var rigGo = Prims.Empty("Rig", transform, Vector3.zero);
            rig = rigGo.AddComponent<PlayerRig>();
            rig.Build(Look);
            pack = rigGo.AddComponent<PlayerBackpack>(); // turns with the critter
            pack.Init(rig);
            Items.EnsureBound(index); // anything owned but not on a button yet gets one

            // floor ring in the player's color so you can find yourself in a crowd
            ringMat = new Material(Mats.Lit(Color.Lerp(color, Color.white, 0.2f), 0.2f, 0f, color * 0.6f)); // its own copy: the color can change
            ring = Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, 0.015f, 0), new Vector3(0.95f, 0.006f, 0.95f), ringMat, null, "FloorRing").transform;
            ringInner = Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, 0.02f, 0), new Vector3(0.8f, 0.006f, 0.8f),
                Mats.Lit(Theme.Floor, 0.3f), null, "FloorRingInner").transform;
            FitToBody();
        }

        Transform ring, ringInner;
        PlayerBackpack pack;
        float refillT;

        /// <summary>Spare pieces in this critter's backpack.</summary>
        public int PackCount => pack != null ? pack.Count : 0;

        /// <summary>The team bought something at a vending machine.</summary>
        public void OnItemBought(ItemDef item)
        {
            if (item != null && item.id == Items.Backpack && pack != null) { pack.Rebuild(); rig.Squash(0.3f); }
        }

        /// <summary>The backpack's button: stow the piece in your hands, or (hands empty) take the top one out.</summary>
        void PackButton()
        {
            if (pack == null) return;
            if (Held != null)
            {
                if (!pack.HasRoom)
                {
                    Nope();
                    Popup.Spawn(Pos + Vector3.up * (rig.Height + 0.3f), $"PACK FULL  {pack.Count}/{Backpacks.Capacity}", Color.white, 28, 0.8f);
                    return;
                }
                CancelHold();
                pack.Stash(Held);
                Held = null;
                rig.Squash(0.2f);
                return;
            }
            if (pack.Empty)
            {
                Nope();
                Popup.Spawn(Pos + Vector3.up * (rig.Height + 0.3f), "PACK'S EMPTY", Color.white, 28, 0.8f);
                return;
            }
            TakeFromPack();
        }

        /// <summary>A piece handed to you (a filter from a vending machine): whatever you're holding goes in your
        /// backpack if there's room, otherwise it's dropped.</summary>
        public void ReceivePiece(PipePiece p)
        {
            if (p == null) return;
            CancelHold();
            if (Held != null)
            {
                if (pack != null && pack.HasRoom) { pack.Stash(Held); Held = null; }
                else DropHeld();
            }
            PickUp(p);
        }

        /// <summary>Pull the top spare piece out of the backpack into your hands.</summary>
        void TakeFromPack()
        {
            if (Held != null || pack == null) return;
            var p = pack.Pop();
            if (p == null) return;
            p.SetState(PieceState.Held);
            p.Spring.SetBase(0.58f);
            Held = p;
            p.HoldTarget = HoldPoint();
            rig.Squash(0.15f);
        }
        Material ringMat;

        /// <summary>Change this player's color (picked at their locker): floor ring, HUD, panels, popups.</summary>
        public void SetPlayerColor(Color c)
        {
            Color = c;
            if (ringMat != null) { Mats.SetBase(ringMat, Color.Lerp(c, Color.white, 0.2f)); Mats.SetEmission(ringMat, c * 0.6f); }
            if (invPanel != null) { invPanel.SetColor(c); }
            Game.Hud?.SetRoster(Game.Players);
        }

        /// <summary>Floor ring and collider follow the critter's size (small / medium / large).</summary>
        void FitToBody()
        {
            float f = Mathf.Clamp(rig.Footprint, 0.6f, 1.8f);
            if (ring != null) ring.localScale = new Vector3(0.95f * f, 0.006f, 0.95f * f);
            if (ringInner != null) ringInner.localScale = new Vector3(0.95f * f - 0.15f, 0.006f, 0.95f * f - 0.15f);
            cc.radius = Mathf.Clamp(0.32f * f, 0.26f, 0.45f);
            cc.height = Mathf.Clamp(rig.Height * 0.85f, 0.8f, 1.6f);
            cc.center = new Vector3(0, cc.height * 0.5f + 0.02f, 0);
        }

        public void Cheer() => rig.Cheer();
        public void Sad() => rig.Sad();

        void OnDestroy()
        {
            if (closet != null) closet.Release(this, false);
            if (invPanel != null) Destroy(invPanel.gameObject);
            input?.Dispose();
            if (highlighted != null) highlighted.SetHighlight(this, false);
        }

        Vector3 Pos => transform.position;

        // ================================================================== frame

        bool vaporized;

        /// <summary>Teleporter strike: the critter is gone, leaving smoke and a charred little skeleton.</summary>
        public void Vaporize()
        {
            if (vaporized) return;
            vaporized = true;
            if (closet != null) { closet.Release(this, false); closet = null; }
            CancelHold();
            UpdateHighlight(null);
            preview.Hide();
            if (Held != null) { Held.gameObject.SetActive(false); Held = null; }
            pack?.Clear();
            CharredRemains.Spawn(Pos, Color);
            foreach (Transform child in transform) child.gameObject.SetActive(false);
            cc.enabled = false;
        }

        /// <summary>Arriving in a new chamber: a crackle of sparks and a pop.</summary>
        public void Materialize()
        {
            rig.Squash(-0.5f);
            Fx.Sparks(Pos + Vector3.up * 0.5f, 24, 5f);
            Fx.Burst(Pos + Vector3.up * 0.5f, Color.white, new Color(0.7f, 0.85f, 1f), 14, 3f, 0.25f, 0.4f, -0.2f, true);
            Fx.Dust(Pos, 0.5f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || vaporized) { if (invOpen) CloseInventory(); return; } // hit-stop / gone
            if (invOpen && (arriving || falling || closet != null)) CloseInventory();
            UpdateBuff(dt);
            if (arriving) { ArrivalUpdate(dt); return; }
            if (falling) { FallUpdate(dt); return; }
            if (closet != null) { ClosetUpdate(dt); return; }
            CheckPit();

            bool over = Game.Manager != null && Game.Manager.IsOver;
            Vector2 mv = input.MoveValue; // between chambers you can roam freely
            Vector3 wish = new Vector3(mv.x, 0f, mv.y);
            if (valveActive && mv.sqrMagnitude > 0.35f) ExitValve(); // walking away lets go of the valve
            if (holdPiece != null || valveActive) wish = Vector3.zero; // committed while unscrewing / on the valve

            // inventory: toggled with its own button; while it's open the critter stands still and the controls drive the list
            bool menuFrame = invOpen;
            if (!invOpen && input.Inventory.WasPressedThisFrame() && mode == Mode.Walk && holdPiece == null && !valveActive) OpenInventory();
            if (invOpen) { InventoryUpdate(); wish = Vector3.zero; menuFrame = true; }
            else for (int i = 0; i < Items.Slots; i++)
                    if (input.ItemButtons[i].WasPressedThisFrame()) UseItemButton(i);

            sprinting = false; // hold-to-sprint is disconnected; the same button dashes
            dashCooldownT -= dt;
            if (dashCooldownT <= 0f) dashChargesLeft = MaxDashes;
            if (!menuFrame && input.Sprint.WasPressedThisFrame() && mode == Mode.Walk && holdPiece == null && !valveActive && dashChargesLeft > 0 && dashT <= 0f)
                StartDash(wish);
            if (mode == Mode.Walk || mode == Mode.Crawl) Locomotion(wish, dt);
            else ScriptedMotion(dt);
            UpdateJump(dt);

            // between chambers it's "off the clock": everything still works (nothing scores), except the speed controls
            if (over && valveActive && !onLever) ExitValve();
            if (menuFrame)
            {
                act = Act.None;
                UpdateHighlight(null);
                preview.Hide();
            }
            else
            {
                UpdateTarget();
                if (over && act == Act.Valve)
                {
                    act = Held != null ? Act.Drop : Act.None;
                    preview.Hide();
                }
                HandleInteract(dt);
                HandleRotate();
                HandleJump();
            }

            if (Held != null) Held.HoldTarget = HoldPoint();

            // valve guide: full guide while on the valve, a small prompt when just facing it
            if (Game.Hud != null)
            {
                Vector3 above = board.Spout.CenterTop + Vector3.up * 0.9f;
                if (valveActive && onLever && Game.Lever != null)
                    Game.Hud.ShowValveGuide(Game.Lever.GuideAnchor, Game.Conveyor.SpeedMultiplier, Game.Conveyor.SpeedIndex, Game.Conveyor.SpeedCount,
                        RotateCCWLabel, RotateCWLabel, UseLabel, Color, "BELT", "  ·  faster belt = end bonus");
                else if (valveActive)
                    Game.Hud.ShowValveGuide(above, Game.Flow.SpeedMultiplier, Game.Flow.SpeedIndex, Game.Flow.SpeedCount,
                        RotateCCWLabel, RotateCWLabel, UseLabel, Color, "FLOW");
                else if (act == Act.Valve)
                    Game.Hud.ShowValvePrompt(above, UseLabel, Color);
                else if (act == Act.Lever && Game.Lever != null)
                    Game.Hud.ShowValvePrompt(Game.Lever.GuideAnchor, UseLabel, Color, "BELT LEVER");
            }

            if (act == Act.Vending && actVending != null && Game.Hud != null)
                Game.Hud.ShowValvePrompt(actVending.PromptAnchor, UseLabel, Color, actVending.PromptText());
            if (act == Act.Closet && actCloset != null && Game.Hud != null)
                Game.Hud.ShowValvePrompt(actCloset.PromptAnchor, UseLabel, Color, actCloset.Occupant != null ? "OCCUPIED" : actCloset.Title);

            Vector3 look = act == Act.None ? Pos + facing * 3f :
                act == Act.Closet && actCloset != null ? actCloset.Inside + Vector3.up :
                act == Act.Lever && Game.Lever != null ? Game.Lever.transform.position + Vector3.up :
                (actPiece != null ? actPiece.transform.position : board.CellCenter(actCell));
            rig.LookAt(look);
            Vector3 flat = vel; flat.y = 0f;
            if ((sprinting || Dashing) && !Airborne)
            {
                sprintDustTimer -= dt;
                if (sprintDustTimer <= 0f) { sprintDustTimer = 0.12f; Fx.Puff(Pos - facing * 0.3f + Vector3.up * 0.08f, Color.Lerp(Theme.Floor, Color.white, 0.4f), 0.14f); }
            }
            rig.Tick(dt, facing, flat.magnitude / cfg.walkSpeed, height + jumpH,
                mode == Mode.Crawl || mode == Mode.ClimbUp, Held != null, holdPiece != null || valveActive,
                mode == Mode.Vault ? modeT : -1f);
        }

        void StartDash(Vector3 wish)
        {
            Vector3 dir = wish.sqrMagnitude > 0.04f ? wish.normalized : facing;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            dashDir = dir.normalized;
            dashT = cfg.dashDistance * (buffT > 0f ? buffDashDist : 1f) / Mathf.Max(0.1f, cfg.dashSpeed);
            dashCooldownT = cfg.dashCooldown + dashT;
            dashChargesLeft--; // with a sip of water you get a second one before the cooldown
            sprintDustTimer = 0f;
            rig.Squash(-0.3f);
            Fx.Puff(Pos - dashDir * 0.25f + Vector3.up * 0.1f, Color.Lerp(Theme.Floor, Color.white, 0.5f), 0.3f);
            Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.55f, 1.35f, 0.08f);
        }

        Vector3 HoldPoint() => Pos + facing * rig.HoldReach + Vector3.up * (rig.HoldHeight + height + jumpH);

        // ================================================================== movement

        void Locomotion(Vector3 wish, float dt)
        {
            float mult = mode == Mode.Crawl ? cfg.crawlSpeedMultiplier : (Airborne ? 1f : LooseMultiplier(dt));
            if (sprinting) mult *= cfg.sprintMultiplier;
            if (buffT > 0f) mult *= buffSpeed; // a sip from the bottle
            Vector3 target = wish * cfg.walkSpeed * mult;
            if (dashT > 0f)
            {
                // dashing: locked to the dash direction at dash speed, then back to normal (the usual deceleration brakes us)
                dashT -= dt;
                vel = dashDir * cfg.dashSpeed;
                facing = dashDir;
                wish = Vector3.zero;
            }
            else vel = Vector3.MoveTowards(vel, target, cfg.acceleration * dt);

            if (wish.sqrMagnitude > 0.01f)
            {
                float cur = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                float want = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
                float yaw = Mathf.MoveTowardsAngle(cur, want, cfg.turnSpeedDegrees * dt) * Mathf.Deg2Rad;
                facing = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            }

            // riding the floor-level belt
            Vector3 carry = mode == Mode.Walk && !Airborne ? belt.SurfaceVelocity(Pos) : Vector3.zero;
            cc.Move((vel + carry + Vector3.down * 4f) * dt);

            if (mode == Mode.Walk)
            {
                height = Mathf.Lerp(height, Airborne ? 0f : CrossHeight(Pos), 1f - Mathf.Exp(-16f * dt));
                if (!Airborne && wish.sqrMagnitude > 0.04f) TryRampOntoPipes(wish);
            }

            if (mode == Mode.Crawl)
            {
                var underCell = board.WorldToCell(Pos);
                var under = board.Get(underCell);
                if (under == null || under.State != PieceState.Placed) StartClimbDown();
                else if (under.Type == PipeType.Cross && Mathf.Max(Mathf.Abs(Pos.x - board.CellCenter(underCell).x), Mathf.Abs(Pos.z - board.CellCenter(underCell).z)) < S * 0.2f)
                {
                    // crawled back onto a cross: it's a ramp again, walk down it
                    mode = Mode.Walk;
                    gameObject.layer = Layers.Player;
                }
                else height = Mathf.Lerp(height, under.TopHeight, 1f - Mathf.Exp(-12f * dt));
            }
        }

        /// <summary>Installed crosses are a gentle hump you walk straight over: how high you are on one.</summary>
        float CrossHeight(Vector3 p)
        {
            var cell = board.WorldToCell(p);
            var pc = board.Get(cell);
            if (pc == null || pc.State != PieceState.Placed || pc.Type != PipeType.Cross) return 0f;
            Vector3 c = board.CellCenter(cell);
            float edge = Mathf.Max(Mathf.Abs(p.x - c.x), Mathf.Abs(p.z - c.z)) / (S * 0.5f);
            float t = Mathf.Clamp01((1f - edge) / 0.8f);
            return pc.TopHeight * t * t * (3f - 2f * t);
        }

        /// <summary>
        /// A cross is a ramp: from on top of one, walking toward a pipe it's joined to takes you straight up onto
        /// that run of pipes (crawling along the top, as if you'd climbed on) with no jump needed.
        /// </summary>
        void TryRampOntoPipes(Vector3 wish)
        {
            var here = board.WorldToCell(Pos);
            var cross = board.Get(here);
            if (cross == null || cross.State != PieceState.Placed || cross.Type != PipeType.Cross) return;
            if (height < cross.TopHeight * 0.3f) return;
            Dir d = DirUtil.FromVector(wish);
            Vector3 c = board.CellCenter(here), dv = DirUtil.ToVector3(d);
            if (Vector3.Dot(Pos - c, dv) < S * 0.3f) return; // only at the edge, heading off that side
            var next = board.Get(here + DirUtil.Offset(d));
            if (next == null || next.State != PieceState.Placed || next.Type == PipeType.Cross) return;
            if (!cross.HasOpening(d) || !next.HasOpening(DirUtil.Opposite(d))) return; // must be joined to it
            mode = Mode.Crawl;
            gameObject.layer = Layers.PlayerClimbing;
            rig.Squash(0.15f);
            Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.25f, 1.8f);
        }

        /// <summary>Loose pieces underfoot slow you down and get shoved around.</summary>
        float LooseMultiplier(float dt)
        {
            int n = 0;
            Vector3 p = Pos;
            foreach (var lp in PipePiece.Loose)
            {
                if (lp == null) continue;
                Vector3 d = lp.transform.position - p;
                d.y = 0f;
                if (d.sqrMagnitude > 0.8f * 0.8f) continue;
                n++;
                if (vel.sqrMagnitude > 0.5f)
                    lp.Body.AddForce(d.normalized * 6f + vel * 1.2f, ForceMode.Acceleration);
            }
            return Mathf.Max(cfg.minLooseSpeedMultiplier, 1f - n * cfg.loosePieceSlowdown);
        }

        void StartScripted(Mode m, Vector3 to, float duration, float targetHeight, float arcHeight)
        {
            mode = m;
            moveFrom = Pos;
            moveTo = new Vector3(to.x, Pos.y, to.z);
            modeDur = Mathf.Max(0.05f, duration);
            modeT = 0f;
            hFrom = height; hTo = targetHeight; arc = arcHeight;
            gameObject.layer = Layers.PlayerClimbing;
            cc.enabled = false;
            vel = Vector3.zero;
        }

        void ScriptedMotion(float dt)
        {
            modeT = Mathf.Min(1f, modeT + dt / modeDur);
            float e = mode == Mode.Vault ? Mathf.SmoothStep(0f, 1f, modeT) : Juice.OutCubic(modeT);
            transform.position = Vector3.Lerp(moveFrom, moveTo, e);
            height = Mathf.Lerp(hFrom, hTo, e) + Mathf.Sin(Mathf.PI * modeT) * arc;
            if (modeT < 1f) return;

            switch (mode)
            {
                case Mode.Vault:
                case Mode.ClimbDown:
                    mode = Mode.Walk;
                    height = 0f;
                    gameObject.layer = Layers.Player;
                    cc.enabled = true;
                    rig.Squash(0.4f);
                    Fx.Dust(Pos, 0.35f, 8);
                    Sfx.Play(Sfx.I != null ? Sfx.I.land : null, 0.5f, 1.1f);
                    break;
                case Mode.ClimbUp:
                    mode = Mode.Crawl;
                    cc.enabled = true;
                    rig.Squash(0.25f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.25f, 1.8f);
                    break;
            }
        }

        float GruntPitch(float baseline) => baseline * (1f + (Index - 1.5f) * 0.08f); // each critter has its own voice

        /// <summary>Jump: over a single pipe = vault; onto a run of pipes = climb on and crawl; otherwise a real hop.</summary>
        void HandleJump()
        {
            if (!input.Jump.WasPressedThisFrame() || holdPiece != null || Airborne) return;
            if (valveActive) { ExitValve(); return; }
            if (mode == Mode.Walk)
            {
                if (TryClimbOrVault()) return;
                StartJump();
            }
            else if (mode == Mode.Crawl)
            {
                // leap off the pipes if there's open floor ahead, else hop along them
                Dir d = DirUtil.FromVector(facing);
                var front = board.WorldToCell(Pos) + DirUtil.Offset(d);
                var fp = board.Get(front);
                if (fp == null || fp.State != PieceState.Placed)
                {
                    Vector3 to = Pos + DirUtil.ToVector3(d) * 1.1f;
                    to.x = Mathf.Clamp(to.x, walkArea.xMin + 0.4f, walkArea.xMax - 0.4f);
                    to.z = Mathf.Clamp(to.z, walkArea.yMin + 0.4f, walkArea.yMax - 0.4f);
                    StartScripted(Mode.ClimbDown, to, 0.4f, 0f, 0.6f);
                    Sfx.Play(Sfx.RandomGrunt(), 0.7f, GruntPitch(1.05f));
                    rig.Squash(-0.3f);
                }
                else { CountWaterHop(fp); StartJump(); }
            }
        }

        void StartJump()
        {
            Stats.jumps++;
            jumpT = 0f;
            rig.Squash(-0.35f);
            Sfx.Play(Sfx.RandomGrunt(), 0.7f, GruntPitch(1.1f));
        }

        void UpdateJump(float dt)
        {
            if (jumpT < 0f) { jumpH = 0f; return; }
            jumpT += dt / Mathf.Max(0.1f, cfg.jumpSeconds);
            jumpH = Mathf.Sin(Mathf.PI * Mathf.Clamp01(jumpT)) * cfg.jumpHeight;
            if (jumpT >= 1f)
            {
                jumpT = -1f;
                jumpH = 0f;
                rig.Squash(0.35f);
                Fx.Dust(Pos, 0.3f, 6);
                Sfx.Play(Sfx.I != null ? Sfx.I.land : null, 0.45f, 1f);
            }
        }

        bool TryClimbOrVault()
        {
            Dir d = DirUtil.FromVector(facing);
            Vector3 dv = DirUtil.ToVector3(d);
            Vector2Int front = board.WorldToCell(Pos + dv * S * 0.62f);
            var p = board.Get(front);
            if (p == null || p.State != PieceState.Placed || p.Type == PipeType.Cross) return false; // crosses: just walk over

            Vector3 c = board.CellCenter(front);
            Vector3 lateral = Pos - c;
            lateral -= dv * Vector3.Dot(lateral, dv);
            lateral.y = 0f;
            lateral = Vector3.ClampMagnitude(lateral, S * 0.28f);

            Vector2Int beyond = front + DirUtil.Offset(d);
            Vector3 landing = c + dv * (S * 0.5f + 0.42f) + lateral;
            bool landingOk = board.InBounds(beyond) ? board.IsFree(beyond) : InWalkArea(landing);

            if (landingOk)
            {
                // single pipe: quick vault over the top
                StartScripted(Mode.Vault, landing, cfg.vaultSeconds, 0f, p.TopHeight + 0.4f);
                CountWaterHop(p);
                rig.Squash(-0.35f);
                Sfx.Play(Sfx.RandomGrunt(), 0.7f, GruntPitch(0.95f));
            }
            else
            {
                // a run of pipes: clamber on top and crawl ("difficult terrain")
                StartScripted(Mode.ClimbUp, c + lateral - dv * S * 0.18f, cfg.climbUpSeconds, p.TopHeight, 0.25f);
                rig.Squash(-0.25f);
                Sfx.Play(Sfx.RandomGrunt(), 0.7f, GruntPitch(0.85f));
            }
            return true;
        }

        void StartClimbDown()
        {
            Vector3 to = Pos + facing * 0.35f;
            to.x = Mathf.Clamp(to.x, walkArea.xMin + 0.4f, walkArea.xMax - 0.4f);
            to.z = Mathf.Clamp(to.z, walkArea.yMin + 0.4f, walkArea.yMax - 0.4f);
            StartScripted(Mode.ClimbDown, to, 0.22f, 0f, 0.18f);
        }

        bool InWalkArea(Vector3 p) =>
            p.x > walkArea.xMin + 0.35f && p.x < walkArea.xMax - 0.35f && p.z > walkArea.yMin + 0.35f && p.z < walkArea.yMax - 0.35f;

        // ================================================================== targeting

        Vector2Int FrontCell()
        {
            var own = board.WorldToCell(Pos);
            var c = board.WorldToCell(Pos + facing * S * cfg.placeProbeTiles);
            // on top of the pipes you can't target the one you're crawling on
            if (mode == Mode.Crawl && c == own) c = board.WorldToCell(Pos + facing * S * (cfg.placeProbeTiles + 0.5f));
            return c;
        }

        void UpdateTarget()
        {
            if (mode != Mode.Walk && mode != Mode.Crawl) { act = Act.None; UpdateHighlight(null); preview.Hide(); return; }
            // just put a piece down: the next one comes out of the backpack
            if (refillT > 0f) { refillT -= Time.deltaTime; if (refillT <= 0f && Held == null && holdPiece == null) TakeFromPack(); }

            Vector3 hand = Pos + facing * 0.8f;
            Vector2Int cell = FrontCell();
            bool inBounds = board.InBounds(cell);
            act = Act.None;
            actPiece = null;
            actCell = cell;

            if (holdPiece != null) { act = holdAct; actPiece = holdPiece; actCell = holdPiece.Cell; }
            else if (valveActive) { act = onLever ? Act.Lever : Act.Valve; actCell = board.Spout.Cell; }
            else if (NearestCloset() != null) act = Act.Closet;
            else if (NearestVending() != null) act = Act.Vending;
            else if (Game.Lever != null && FlatDist(Game.Lever.transform.position, hand) < 1.0f) act = Act.Lever;
            else if (Held == null)
            {
                // whichever grabbable thing is closest to our hands: belt piece, floor piece, or someone else's piece
                float best = float.MaxValue;
                var bp = belt.FindNearest(hand, cfg.pickupRadius);
                if (bp != null) { best = FlatDist(bp.transform.position, hand); act = Act.TakeFromBelt; actPiece = bp; }
                var lp = NearestLoose(hand, cfg.pickupRadius * 0.85f);
                if (lp != null && FlatDist(lp.transform.position, hand) < best) { best = FlatDist(lp.transform.position, hand); act = Act.PickLoose; actPiece = lp; }
                foreach (var other in Game.Players)
                {
                    if (other == null || other == this || other.Held == null) continue;
                    if (Time.time < other.Held.StealProtectedUntil) continue;
                    float d = FlatDist(other.Held.transform.position, hand);
                    if (d < cfg.pickupRadius && d < best) { best = d; act = Act.Steal; actPiece = other.Held; actVictim = other; }
                }
                if (act == Act.None && inBounds)
                {
                    if (board.IsSpout(cell)) act = Act.Valve;
                    else
                    {
                        var p = board.Get(cell);
                        if (p != null) { actPiece = p; act = Locked(p) ? Act.Blocked : Act.Unscrew; }
                    }
                }
            }
            else
            {
                if (inBounds)
                {
                    if (board.IsSpout(cell)) act = Act.Valve;
                    else
                    {
                        var p = board.Get(cell);
                        if (p == null) act = Act.Place;
                        else { actPiece = p; act = Locked(p) ? Act.Blocked : Act.Swap; }
                    }
                }
                else if (belt.IsOverBelt(hand)) act = Act.PutOnBelt;
                else act = Act.Drop;
                // backpack with room: grabbing another piece stows the one in your hands
                if ((act == Act.PutOnBelt || act == Act.Drop) && pack != null && pack.HasRoom)
                {
                    float best = float.MaxValue;
                    var bp = belt.FindNearest(hand, cfg.pickupRadius);
                    if (bp != null) { best = FlatDist(bp.transform.position, hand); act = Act.TakeFromBelt; actPiece = bp; }
                    var lp = NearestLoose(hand, cfg.pickupRadius * 0.85f);
                    if (lp != null && FlatDist(lp.transform.position, hand) < best) { act = Act.PickLoose; actPiece = lp; }
                }
            }

            // visuals
            UpdateHighlight(act == Act.TakeFromBelt || act == Act.PickLoose || act == Act.Steal ? actPiece : null);
            switch (act)
            {
                case Act.Place:
                    preview.Show(board.CellCenter(actCell), WouldConnect(Held, actCell) ? PlacementPreview.Good : PlacementPreview.Neutral, true, Held.Type, Held.Rotation);
                    break;
                case Act.Swap:
                    preview.Show(board.CellCenter(actCell), PlacementPreview.Swap, true, Held.Type, Held.Rotation);
                    break;
                case Act.Unscrew:
                    preview.Show(board.CellCenter(actCell), PlacementPreview.Swap, false, PipeType.Straight, 0);
                    break;
                case Act.Lever:
                case Act.Closet:
                case Act.Vending:
                    preview.Hide();
                    break;
                case Act.Valve:
                    preview.Show(board.CellCenter(actCell), valveActive ? Theme.AccentBlue * 1.4f : Theme.AccentBlue, false, PipeType.Straight, 0);
                    break;
                case Act.Blocked:
                    if (board.InBounds(actCell)) preview.Show(board.CellCenter(actCell), PlacementPreview.Bad, false, PipeType.Straight, 0);
                    else preview.Hide();
                    break;
                default:
                    preview.Hide();
                    break;
            }
        }

        static bool Locked(PipePiece p) => p.FlowLocked || p.AnyLiquid();

        static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        PipePiece NearestLoose(Vector3 point, float radius)
        {
            PipePiece best = null;
            float bestD = radius;
            foreach (var lp in PipePiece.Loose)
            {
                if (lp == null) continue;
                Vector3 d = lp.transform.position - point;
                d.y = 0f;
                float m = d.magnitude;
                if (m < bestD) { bestD = m; best = lp; }
            }
            return best;
        }

        bool WouldConnect(PipePiece piece, Vector2Int cell)
        {
            foreach (var o in piece.WorldOpenings())
            {
                var n = cell + DirUtil.Offset(o);
                if (board.IsSpout(n) && board.Spout.Out == DirUtil.Opposite(o)) return true;
                var np = board.Get(n);
                if (np != null && np.Connected && np.HasOpening(DirUtil.Opposite(o))) return true;
            }
            return false;
        }

        void UpdateHighlight(PipePiece p)
        {
            if (highlighted == p) return;
            if (highlighted != null) highlighted.SetHighlight(this, false);
            highlighted = p;
            if (p != null) p.SetHighlight(this, true);
        }

        // ================================================================== interaction

        void HandleInteract(float dt)
        {
            if (valveActive)
            {
                if (input.Use.WasPressedThisFrame()) ExitValve(); // Use toggles the valve off again
                return;
            }
            if (holdPiece != null)
            {
                if (!input.Use.IsPressed() || holdPiece.State != PieceState.Placed || Locked(holdPiece))
                {
                    CancelHold();
                    return;
                }
                holdProgress += dt / holdDur;
                // a groaning, creaking pipe that sags in pitch: this is the penalty, it should sound like one
                creakTimer -= dt;
                if (creakTimer <= 0f)
                {
                    creakTimer = holdDur > 0.6f ? 0.42f : 0.3f;
                    Sfx.Play(Sfx.I != null ? Sfx.I.creak : null, holdPiece.Connected ? 0.75f : 0.45f, Mathf.Lerp(1.05f, 0.72f, holdProgress), 0.06f);
                }
                ratchetTimer -= dt;
                if (ratchetTimer <= 0f)
                {
                    ratchetTimer = holdDur > 0.6f ? 0.1f : 0.06f;
                    ratchetCount++;
                    rig.WorkPulse();
                    holdPiece.Spring.Squash(0.04f);
                    if (holdPiece.Connected && ratchetCount % 3 == 0)
                        Fx.Sparks(holdPiece.transform.position + Vector3.up * holdPiece.TopHeight, 3, 2.5f);
                }
                holdPiece.SetWiggle(0.25f + holdProgress * 0.9f);
                Game.Hud.ShowProgress(Index, Pos + Vector3.up * (1.9f + height), Mathf.Clamp01(holdProgress));
                if (holdProgress >= 1f) CompleteHold();
                return;
            }

            if (!input.Use.WasPressedThisFrame()) return;

            switch (act)
            {
                case Act.None:
                    // empty hands, nothing in reach: take a spare out of the backpack
                    if (Held == null) TakeFromPack();
                    break;
                case Act.TakeFromBelt:
                    if (Held != null) { pack.Stash(Held); Held = null; }
                    belt.Take(actPiece);
                    PickUp(actPiece);
                    break;
                case Act.PickLoose:
                    if (Held != null) { pack.Stash(Held); Held = null; }
                    actPiece.SnapRotationToYaw();
                    PickUp(actPiece);
                    break;
                case Act.Steal:
                    StealFrom(actVictim);
                    break;
                case Act.Place:
                    PlaceHeld(actCell);
                    break;
                case Act.Unscrew:
                case Act.Swap:
                    holdPiece = actPiece;
                    holdAct = act;
                    holdDur = holdPiece.Connected ? cfg.unscrewSeconds : cfg.unboltSeconds;
                    holdProgress = 0f;
                    ratchetTimer = 0f;
                    creakTimer = 0f;
                    ratchetCount = 0;
                    break;
                case Act.Drop:
                    DropHeld();
                    break;
                case Act.PutOnBelt:
                    belt.Insert(Held, Pos + facing * 0.8f);
                    Held = null;
                    rig.Squash(0.2f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.pickup : null, 0.5f, 0.8f);
                    break;
                case Act.Lever:
                    valveActive = true;
                    onLever = true;
                    Game.Hud?.PunchValve(0);
                    Game.Lever.Pull(0);
                    Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.4f, 0.6f);
                    break;
                case Act.Valve:
                    valveActive = true;
                    onLever = false;
                    Game.Hud?.PunchValve(0);
                    rig.Squash(0.15f);
                    board.Spout.TurnValve(0);
                    Popup.Spawn(board.Spout.CenterTop + Vector3.up * 0.4f, $"FLOW x{Game.Flow.SpeedMultiplier:0.##}", Theme.AccentBlue, 34, 0.9f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.4f, 0.7f);
                    break;
                case Act.Blocked:
                    Nope();
                    break;
                case Act.Closet:
                    TryEnterCloset(actCloset);
                    break;
                case Act.Vending:
                    actVending.TryBuy(this);
                    break;
            }
        }

        void HandleRotate()
        {
            int d = 0;
            if (input.RotateCW.WasPressedThisFrame()) d += 1;
            if (input.RotateCCW.WasPressedThisFrame()) d -= 1;
            if (d == 0) return;
            if (valveActive && onLever)
            {
                bool moved = Game.Conveyor.AdjustSpeed(d);
                Game.Lever.Pull(moved ? d : 0);
                Game.Hud?.PunchValve(moved ? d : 0);
                rig.WorkPulse();
                if (moved)
                {
                    float m = Game.Conveyor.SpeedMultiplier;
                    Popup.Spawn(Game.Lever.GuideAnchor, $"BELT x{m:0.##}", d > 0 ? Mats.Danger : Theme.AccentBlue, 38, 0.9f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.6f, d > 0 ? 1.15f : 0.85f, 0.03f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.5f, 0.7f + m * 0.2f, 0f);
                    Juice.Shake(0.05f);
                }
                else Nope();
                return;
            }
            if (valveActive)
            {
                // on the dispenser's valve: clockwise = faster flow, counter-clockwise = slower
                bool changed = Game.Flow.AdjustSpeed(d);
                board.Spout.TurnValve(changed ? d : 0);
                Game.Hud?.PunchValve(changed ? d : 0);
                rig.WorkPulse();
                if (changed)
                {
                    float m = Game.Flow.SpeedMultiplier;
                    Popup.Spawn(board.Spout.CenterTop + Vector3.up * 0.4f, $"FLOW x{m:0.##}", d > 0 ? Mats.Danger : Theme.AccentBlue, 40, 0.9f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.creak : null, 0.6f, d > 0 ? 1.25f : 0.8f, 0.03f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.6f, 0.7f + m * 0.25f, 0f);
                    Juice.Shake(0.06f);
                }
                else Nope();
                return;
            }
            if (Held == null) return;
            Held.Rotate(d);
            rig.Squash(0.08f);
            Sfx.Play(Sfx.I != null ? Sfx.I.rotate : null, 0.6f, d > 0 ? 1.1f : 0.95f);
        }

        void PickUp(PipePiece p)
        {
            p.SetState(PieceState.Held);
            Held = p;
            p.HoldTarget = HoldPoint();
            p.Spring.Squash(-0.35f);
            rig.Squash(0.22f);
            Sfx.Play(Sfx.I != null ? Sfx.I.pickup : null, 0.7f);
            Fx.Puff(p.transform.position + Vector3.up * 0.3f, Color.white, 0.2f);
        }

        void PlaceHeld(Vector2Int cell)
        {
            var p = Held;
            Held = null;
            PlacePiece(p, cell);
            if (pack != null && !pack.Empty) refillT = 0.18f; // the next spare pops out of the backpack
        }

        void PlacePiece(PipePiece p, Vector2Int cell)
        {
            Stats.pipesPlaced++;
            const float flight = 0.12f;
            p.SetState(PieceState.Placed);
            board.Place(p, cell);
            p.FlyTo(board.CellCenter(cell), p.TargetRotation, flight, 0.45f, () => OnLanded(p));
            foreach (var pl in Game.Players)
            {
                if (pl == null) continue;
                if (pl == this) HopOffPlaced(cell);
                else pl.NudgeOutOf(cell);
            }
            rig.Squash(-0.2f);
            var delta = board.Recompute(true, flight);
            Juice.Delay(flight, () => LatchFeedback(delta));
        }

        void OnLanded(PipePiece p)
        {
            if (p == null) return;
            p.Spring.Squash(0.5f);
            Fx.Dust(p.transform.position, S * 0.42f);
            Sfx.Play(Sfx.I != null ? Sfx.I.thunk : null, 0.9f);
            Juice.Shake(0.18f);
            if (!p.Connected) Sfx.Play(Sfx.I != null ? Sfx.I.ratchet : null, 0.5f, 0.7f); // brackets bolt down
        }

        /// <summary>The dopamine bit: newly-latched pieces clamp on one after another with sparks and rising clanks.</summary>
        void LatchFeedback(GridBoard.Delta delta)
        {
            int n = delta.newlyConnected.Count;
            for (int i = 0; i < n; i++)
            {
                var p = delta.newlyConnected[i];
                int idx = i;
                Juice.Delay(idx * 0.08f, () =>
                {
                    if (p == null || !p.Connected) return;
                    foreach (var o in p.WorldOpenings())
                    {
                        var nc = p.Cell + DirUtil.Offset(o);
                        var np = board.Get(nc);
                        bool joined = (board.IsSpout(nc) && board.Spout.Out == DirUtil.Opposite(o)) ||
                                      (np != null && np.Connected && np.HasOpening(DirUtil.Opposite(o)));
                        if (joined) Fx.Sparks(p.JointPosition(o), 14, 5.5f);
                    }
                    p.Spring.Squash(0.35f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.8f, 1f + idx * 0.07f, 0.02f);
                    Juice.Shake(0.12f + Mathf.Min(idx, 6) * 0.03f);
                    if (idx == 0) Juice.HitStop(0.05f);
                    if (idx == n - 1 && n >= 2)
                    {
                        Popup.Spawn(p.transform.position + Vector3.up * 1.2f, $"LINKED x{n}!", Mats.Clamp, 44 + Mathf.Min(n, 8) * 3);
                        Juice.HitStop(0.07f);
                        Game.Cam?.FovPunch(-1.2f);
                    }
                });
            }
            foreach (var p in delta.newlyDisconnected)
            {
                if (p == null) continue;
                Fx.Burst(p.transform.position + Vector3.up * 0.35f, Mats.Clamp, new Color(0.8f, 0.55f, 0.1f), 8, 3.5f, 0.14f, 0.5f, 1.5f);
                p.Spring.Squash(-0.2f);
            }
            if (delta.newlyDisconnected.Count > 0) Sfx.Play(Sfx.I != null ? Sfx.I.pop : null, 0.5f, 0.7f);
        }

        void CompleteHold()
        {
            var p = holdPiece;
            var action = holdAct;
            var cell = p.Cell;
            bool wasConnected = p.Connected;
            CancelHold();

            board.Remove(cell);
            Stats.unscrewed++;
            Sfx.Play(Sfx.I != null ? Sfx.I.clunk : null, 0.9f, wasConnected ? 0.85f : 1.1f);
            Juice.Shake(wasConnected ? 0.25f : 0.12f);
            if (wasConnected)
            {
                Fx.Burst(p.transform.position + Vector3.up * 0.35f, Mats.Clamp, Color.white, 14, 5f, 0.15f, 0.6f, 1.8f);
                Juice.HitStop(0.04f);
            }

            if (action == Act.Unscrew)
            {
                PickUp(p);
                var delta = board.Recompute(true);
                LatchFeedback(delta);
            }
            else
            {
                // swap: old piece pops out onto the floor, the carried piece takes its place
                Eject(p);
                var mine = Held;
                Held = null;
                PlacePiece(mine, cell);
            }
        }

        /// <summary>Snatch the piece out of another critter's hands.</summary>
        void StealFrom(PlayerController victim)
        {
            if (victim == null) return;
            var p = victim.YieldHeld();
            if (p == null) return;
            PickUp(p);
            Stats.steals++;
            victim.Stats.robbed++;
            p.StealProtectedUntil = Time.time + cfg.stealCooldown;
            Popup.Spawn(p.transform.position + Vector3.up * 0.9f, "YOINK!", Color, 40, 0.8f);
            Sfx.Play(Sfx.I != null ? Sfx.I.yoink : null, 0.8f, 1f);
            Juice.Delay(0.08f, () => Sfx.Play(Sfx.RandomGrunt(), 0.6f, victim != null ? victim.GruntPitch(0.8f) : 0.8f));
            Juice.Shake(0.1f);
        }

        /// <summary>Called on the victim of a steal.</summary>
        public PipePiece YieldHeld()
        {
            CancelHold();
            var p = Held;
            Held = null;
            rig.HeadShake();
            rig.Squash(0.3f);
            return p;
        }

        // ================================================================== arriving / falling in the pit

        bool arriving, falling, shown = true;
        ArrivalPad arrivalPad;
        float fallT, respawnT;
        Vector3 fallFrom;

        void SetShown(bool on)
        {
            shown = on;
            foreach (Transform child in transform) child.gameObject.SetActive(on);
            pack?.SetShown(on); // the stowed pieces live in the level, not under the critter
        }

        /// <summary>Come up through the floor on a little elevator pad (joining, a new chamber, after the pit).</summary>
        public void ArriveAt(Vector3 pos, float delay, System.Action onMaterialize)
        {
            arriving = true;
            falling = false;
            CancelHold();
            UpdateHighlight(null);
            preview.Hide();
            if (Held != null) DropHeld();
            cc.enabled = false;
            gameObject.layer = Layers.PlayerClimbing;
            transform.position = new Vector3(pos.x, 0f, pos.z);
            vel = Vector3.zero;
            mode = Mode.Walk;
            height = 0f;
            facing = Vector3.right;
            SetShown(false);
            System.Action go = () =>
            {
                if (this == null) return;
                arrivalPad = ArrivalPad.Spawn(transform.position, Color,
                    () => { if (this == null) return; SetShown(true); Materialize(); rig.Cheer(); onMaterialize?.Invoke(); },
                    () =>
                    {
                        if (this == null) return;
                        arriving = false;
                        arrivalPad = null;
                        cc.enabled = true;
                        gameObject.layer = Layers.Player;
                    });
            };
            if (delay > 0f) Juice.Delay(delay, go); else go();
        }

        void ArrivalUpdate(float dt)
        {
            if (!shown) return;
            height = arrivalPad != null ? arrivalPad.Top : Mathf.Lerp(height, 0f, 1f - Mathf.Exp(-12f * dt));
            rig.LookAt(transform.position + facing * 3f);
            rig.Tick(dt, facing, 0f, height, false, false, false, -1f);
        }

        void CheckPit()
        {
            if (mode != Mode.Walk || Airborne || belt == null) return;
            Vector3 c = belt.PitCenter;
            float half = belt.PitHalfSize - 0.22f;
            if (Mathf.Abs(Pos.x - c.x) < half && Mathf.Abs(Pos.z - c.z) < half) FallInPit();
        }

        /// <summary>Drank something lethal (filter grade 3): keel over, char, and get reprinted at your locker. Free.</summary>
        void DropDead()
        {
            if (falling || vaporized || !shown) return;
            CancelHold();
            if (valveActive) ExitValve();
            UpdateHighlight(null);
            preview.Hide();
            if (Held != null) DropHeld();
            rig.Sad();
            Sfx.Play(Sfx.RandomGrunt(), 0.9f, GruntPitch(0.7f), 0.05f);
            Sfx.Play(Sfx.I != null ? Sfx.I.nope : null, 0.6f, 0.5f);
            CharredRemains.Spawn(Pos, Color);
            Popup.Spawn(Pos + Vector3.up * (rig.Height + 0.5f), "DON'T DRINK THAT", Mats.Danger, 36, 1.6f);
            falling = true;
            fallT = 1f;              // skip the pit animation: straight to "gone, come back soon"
            fallFrom = Pos;
            cc.enabled = false;
            gameObject.layer = Layers.PlayerClimbing;
            SetShown(false);
            respawnT = cfg.pitRespawnDelay + 0.6f;
        }

        void FallInPit()
        {
            falling = true;
            fallT = 0f;
            fallFrom = Pos;
            CancelHold();
            if (valveActive) ExitValve();
            UpdateHighlight(null);
            preview.Hide();
            if (Held != null) DropHeld();
            cc.enabled = false;
            gameObject.layer = Layers.PlayerClimbing;
            rig.Sad();
            rig.Squash(-0.4f);
            Sfx.Play(Sfx.RandomGrunt(), 0.9f, GruntPitch(1.6f), 0.05f);
            Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.6f, 0.6f);
        }

        void FallUpdate(float dt)
        {
            if (fallT < 1f)
            {
                fallT = Mathf.Min(1f, fallT + dt / 0.55f);
                Vector3 c = belt.PitCenter;
                transform.position = Vector3.Lerp(fallFrom, new Vector3(c.x, 0f, c.z), Juice.OutCubic(Mathf.Min(1f, fallT * 2f)));
                height = -fallT * fallT * 2.2f;
                facing = Quaternion.Euler(0f, 900f * dt * fallT, 0f) * facing; // spinning down the hole
                rig.Tick(dt, facing, 0.5f, height, false, false, false, -1f);
                if (fallT >= 1f)
                {
                    SetShown(false);
                    Fx.Burst(c + Vector3.up * 0.2f, new Color(0.05f, 0.05f, 0.06f), new Color(0.4f, 0.05f, 0.04f), 16, 3f, 0.25f, 0.7f, -0.3f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.splat : null, 0.7f, 0.6f);
                    Juice.Shake(0.15f);
                    Game.Manager?.OnCritterLost(c);
                    respawnT = cfg.pitRespawnDelay;
                }
                return;
            }
            respawnT -= dt;
            if (respawnT <= 0f)
            {
                falling = false;
                height = 0f;
                var at = Game.Bootstrap != null ? Game.Bootstrap.SpawnPoint(Index) : fallFrom;
                ArriveAt(at, 0f, null);
            }
        }

        // ================================================================== items: inventory, item buttons, water bottle

        InventoryPanel invPanel;
        InventoryPanel.View invView;
        bool invOpen, invDetails;
        int invSel, invOpenedFrame, invLastNy, dashChargesLeft;
        float invRepeat, buffT, buffFxT;
        VendingMachine actVending;

        public string InventoryLabel => input.InventoryLabel;
        int MaxDashes => buffT > 0f ? Mathf.Max(1, buffDashes) : 1;

        // what the current sip does (clean water, or whatever the funnel caught)
        float buffSpeed = 1f, buffDashDist = 1f, hiccupT;
        int buffDashes = 1;
        bool buffHiccups;
        Color buffColor = new Color(0.5f, 0.85f, 1f);

        /// <summary>Set up the sip's effects from what's in the bottle. Returns the line to pop up.</summary>
        bool sipLethal;

        string SetSipEffects(string mix)
        {
            sipLethal = false;
            float sp = cfg.waterBottleSpeedMultiplier;
            int dashes = Mathf.Max(1, cfg.waterBottleDashes);
            buffSpeed = sp; buffDashes = dashes; buffDashDist = 1f; buffHiccups = false;
            buffColor = new Color(0.5f, 0.85f, 1f);
            string m = (mix ?? "").ToUpperInvariant();
            if (m.Length == 0) return null;
            // filter grade 3 of anything is lethal
            if (m.Contains("DEIONIZED") || m.Contains("HYPER-CONCENTRATED")) { sipLethal = true; return m.Contains("DEIONIZED") ? "TOO... PURE..." : "TOO... STRONG..."; }
            // filtered water: the purer, the better
            if (m.Contains("PURE WATER")) { buffSpeed = sp + 0.3f; buffDashes = dashes + 1; buffDashDist = 1.3f; buffColor = new Color(0.85f, 0.97f, 1f); return "CRISP!"; }
            if (m.Contains("FILTERED WATER")) { buffSpeed = sp + 0.15f; buffDashes = dashes + 1; buffColor = new Color(0.55f, 0.85f, 1f); return "REFRESHING!"; }
            // concentrates hit harder: x2, x3
            int k = m.Contains("DOUBLE-CONCENTRATED") ? 3 : m.Contains("CONCENTRATED") ? 2 : 1;
            if (m.Contains("TOXIC"))
            {
                // zippy and jittery, with the hiccups
                buffSpeed = sp + 0.25f * k; buffDashes = dashes + k; buffHiccups = true; hiccupT = 1f / k;
                buffColor = new Color(0.35f, 1f, 0.3f);
                return k > 1 ? "BLEEEGH!!  ...ZOOOM!" : "BLEGH!  ...ZOOM!";
            }
            if (m.Contains("IRRADIATED"))
            {
                // you glow, and your dashes go much further
                buffDashDist = 1f + 0.7f * k;
                buffColor = new Color(0.8f, 1f, 0.2f);
                return k > 1 ? "VERY TINGLY..." : "TINGLY...";
            }
            if (m.Contains("VOID"))
            {
                // long blinking dashes, lots of them, but no extra walking speed
                buffSpeed = 1f; buffDashes = dashes + 2 * k; buffDashDist = 1f + k;
                buffColor = new Color(0.55f, 0.2f, 1f);
                return "WHERE AM I?";
            }
            buffColor = Mats.Liquid;
            return "TASTES FUNNY";
        }

        VendingMachine NearestVending()
        {
            actVending = null;
            if (mode != Mode.Walk || Airborne || holdPiece != null || valveActive) return null;
            float best = 1.0f;
            foreach (var v in VendingMachine.All)
            {
                if (v == null) continue;
                float d = FlatDist(v.Front, Pos);
                if (d < best) { best = d; actVending = v; }
            }
            return actVending;
        }

        void OpenInventory()
        {
            invOpen = true;
            invDetails = false;
            invOpenedFrame = Time.frameCount;
            invLastNy = 0;
            CancelHold();
            if (invPanel == null) invPanel = InventoryPanel.Create(Color);
            rig.Squash(0.15f);
        }

        void CloseInventory()
        {
            invOpen = false;
            if (invPanel != null) invPanel.Hide();
            Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.4f, 1.1f);
        }

        void InventoryUpdate()
        {
            if (Time.frameCount == invOpenedFrame) Items.EnsureBound(Index);
            var owned = Items.Owned(Index);
            invSel = Mathf.Clamp(invSel, 0, Mathf.Max(0, owned.Count - 1));
            // up/down through the list (repeats while held)
            Vector2 mv = input.MoveValue;
            int ny = mv.y > 0.55f ? 1 : mv.y < -0.55f ? -1 : 0;
            invRepeat -= Time.deltaTime;
            if (ny != 0 && (ny != invLastNy || invRepeat <= 0f) && owned.Count > 1)
            {
                invRepeat = ny != invLastNy ? 0.35f : 0.15f;
                int was = invSel;
                invSel = Mathf.Clamp(invSel - ny, 0, owned.Count - 1);
                if (invSel != was) Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.5f, 1.3f);
            }
            invLastNy = ny;

            var sel = owned.Count > 0 ? owned[invSel] : null;
            // press an item button to put the highlighted item on it (swaps if it was on another button)
            for (int i = 0; i < Items.Slots; i++)
                if (input.ItemButtons[i].WasPressedThisFrame() && sel != null && sel.active)
                {
                    Items.Bind(Index, sel.id, i);
                    Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.5f, 1.5f);
                }
            // Use flips the panel to the highlighted item's details page and back (items are used with their buttons)
            if (input.Use.WasPressedThisFrame() && sel != null) invDetails = !invDetails;
            if (sel == null) invDetails = false;
            if ((input.Inventory.WasPressedThisFrame() && Time.frameCount != invOpenedFrame) || input.Jump.WasPressedThisFrame())
            {
                if (invDetails && input.Jump.WasPressedThisFrame()) { invDetails = false; }  // Jump backs out of details first
                else { CloseInventory(); return; }
            }

            var binds = Items.Bindings(Index);
            var v = invView ?? (invView = new InventoryPanel.View());
            v.title = $"P{Index + 1}  ITEMS";
            v.pad = input.Scheme == ControlScheme.Gamepad;
            v.closeKey = input.InventoryLabel;
            v.detailsKey = UseLabel;
            if (v.rows.Length != owned.Count) v.rows = new InventoryPanel.Row[owned.Count];
            for (int i = 0; i < owned.Count; i++)
            {
                var d = owned[i];
                string right = d.hasCharges ? $"{Items.Charges(Index, d.id)}/{Items.MaxCharges(d.id)}"
                    : d.id == Items.Backpack ? $"{PackCount}/{Backpacks.Capacity}  <color=#FD6>LV {Items.Level(d.id)}</color>"
                    : (d.active ? "" : $"<color=#FD6>{d.passiveLabel}</color>");
                v.rows[i] = new InventoryPanel.Row { name = d.name, right = right, icon = d.icon, selected = i == invSel };
            }
            for (int i = 0; i < Items.Slots; i++)
            {
                var bd = binds[i] != null ? Items.Get(binds[i]) : null;
                v.slots[i] = new InventoryPanel.Slot { key = input.ItemLabel(i), icon = bd != null ? bd.icon : null, highlight = sel != null && binds[i] == sel.id };
            }
            v.assignHint = sel != null && sel.active && System.Array.IndexOf(binds, sel.id) < 0;
            v.detailName = null;
            if (invDetails && sel != null)
            {
                int b = System.Array.IndexOf(binds, sel.id);
                v.detailName = sel.name;
                v.detailIcon = sel.icon;
                v.detailBody = sel.description;
                if (sel.hasCharges) v.detailBody += $" <color=#9EF>{Items.Charges(Index, sel.id)}/{Items.MaxCharges(sel.id)} left</color>";
                if (sel.id == Items.Backpack)
                    v.detailBody += $"\n<color=#9EF>{PackCount}/{Backpacks.Capacity} carried</color>  <color=#FD6>LV {Items.Level(sel.id)}: {Backpacks.StageName(Items.Level(sel.id))}</color>";
                v.detailUseKey = sel.active && b >= 0 ? input.ItemLabel(b) : null;
                v.detailNote = !sel.active ? sel.passiveLabel : b < 0 ? "Not on a button yet" : null;
                if (sel.id == Items.WaterBottle && Items.BottleMix(Index).Length > 0) v.detailNote = $"Contaminated: {Items.BottleMix(Index)}";
            }
            invPanel.Show(Pos + Vector3.up * (rig.Height + 0.25f), v);
        }

        void UseItemButton(int i)
        {
            Items.EnsureBound(Index);
            string id = Items.Bindings(Index)[i];
            var d = string.IsNullOrEmpty(id) ? null : Items.Get(id);
            if (d == null || !Items.Has(Index, id))
            {
                Nope();
                Popup.Spawn(Pos + Vector3.up * (rig.Height + 0.3f), $"NOTHING ON [{input.ItemLabel(i)}]", Color.white, 28, 0.8f);
                return;
            }
            UseItem(d);
        }

        void UseItem(ItemDef d)
        {
            if (d.id == Items.Backpack) { PackButton(); return; }
            if (d.id != Items.WaterBottle) return;
            if (rig.Sipping) return; // one sip at a time
            int c = Items.Charges(Index, d.id);
            if (c <= 0)
            {
                Nope();
                Popup.Spawn(Pos + Vector3.up * (rig.Height + 0.3f), "BOTTLE'S EMPTY", Mats.Danger, 32, 0.9f);
                return;
            }
            Items.SetCharges(Index, d.id, c - 1);
            buffT = cfg.waterBottleBuffSeconds;
            string side = SetSipEffects(Items.BottleMix(Index));
            dashChargesLeft = MaxDashes;
            if (side != null) Juice.Delay(0.9f, () => { if (this != null) Popup.Spawn(Pos + Vector3.up * (rig.Height + 0.6f), side, sipLethal ? Mats.Danger : buffColor, 34, 1.4f); });
            if (sipLethal) { buffT = 0f; Juice.Delay(1.7f, () => { if (this != null) DropDead(); }); }
            rig.Sip((grip, scale) => VendingMachine.MakeBottle(grip, new Vector3(0f, -0.095f * scale, 0f), scale));
            Popup.Spawn(Pos + Vector3.up * (rig.Height + 0.35f), $"SIP!  {c - 1} LEFT", new Color(0.5f, 0.85f, 1f), 36, 1.1f);
            Sfx.Play(Sfx.I != null ? Sfx.I.bloop : null, 0.6f, 1.3f);
        }

        void UpdateBuff(float dt)
        {
            if (buffT <= 0f) return;
            buffT -= dt;
            buffFxT -= dt;
            if (buffFxT <= 0f && !Airborne)
            {
                buffFxT = 0.3f;
                Fx.Burst(Pos + Vector3.up * 0.15f, buffColor, Color.white, 3, 1f, 0.05f, 0.35f, 0.5f);
            }
            if (buffHiccups)
            {
                hiccupT -= dt;
                if (hiccupT <= 0f && !Airborne)
                {
                    hiccupT = Random.Range(1.2f, 2.6f);
                    rig.Hop();
                    Popup.Spawn(Pos + Vector3.up * (rig.Height + 0.25f), "HIC!", buffColor, 26, 0.6f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.hop : null, 0.5f, 1.8f);
                }
            }
            if (buffT <= 0f)
                Popup.Spawn(Pos + Vector3.up * (rig.Height + 0.3f), "THIRSTY AGAIN", new Color(0.6f, 0.75f, 0.85f), 26, 0.9f);
        }

        // ================================================================== closets

        Closet closet, actCloset;
        int closetPhase; // 0 walking in, 1 inside, 2 walking out
        int closetRow, lastNavX, lastNavY;
        float closetT, navRepeat;
        Vector3 closetFrom;

        Closet NearestCloset()
        {
            actCloset = null;
            if (mode != Mode.Walk || Airborne || holdPiece != null || valveActive) return null;
            float best = 0.85f;
            foreach (var c in Closet.All)
            {
                if (c == null || !c.UsableBy(this)) continue; // other players' lockers
                float d = FlatDist(c.Entrance, Pos);
                if (d < best) { best = d; actCloset = c; }
            }
            return actCloset;
        }

        void TryEnterCloset(Closet c)
        {
            if (c == null) return;
            if (!c.TryOccupy(this))
            {
                Nope();
                Popup.Spawn(c.PromptAnchor, "OCCUPIED", Mats.Danger, 32, 0.8f);
                return;
            }
            if (Held != null) DropHeld();
            CancelHold();
            UpdateHighlight(null);
            preview.Hide();
            closet = c;
            closetPhase = 0;
            closetT = 0f;
            closetRow = 0;
            closetFrom = Pos;
            lastNavX = lastNavY = 0;
            cc.enabled = false;
            gameObject.layer = Layers.PlayerClimbing;
            vel = Vector3.zero;
            act = Act.None;
            rig.Hop();
            Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.4f, 1.1f);
        }

        /// <summary>Not hidden away somewhere (closet, pit, mid-arrival) so the leak can drench you.</summary>
        public bool CanBeSprayed => !vaporized && !arriving && !falling && closet == null;

        /// <summary>Caught in the spray from the leak.</summary>
        public void GetSprayed(Vector3 dir)
        {
            rig.Squash(0.45f);
            rig.HeadShake();
            Fx.Burst(Pos + Vector3.up * 0.8f, Mats.Liquid, Color.white, 30, 3f, 0.14f, 0.7f, 1.5f, true);
            Popup.Spawn(Pos + Vector3.up * 1.6f, "SPLASH!", Mats.Liquid, 40, 1f);
            Sfx.Play(Sfx.I != null ? Sfx.I.splat : null, 0.8f, 1.3f);
        }

        /// <summary>Hopping over a pipe that's full of clean water counts toward the arm floaties.</summary>
        void CountWaterHop(PipePiece over)
        {
            if (over != null && over.AnyLiquid() && Progress.LiquidIsWater) Progress.Add(this, Progress.WaterHops);
        }

        /// <summary>Little celebratory squash when something changes in a closet.</summary>
        public void ClosetPop()
        {
            rig.Squash(-0.35f);
            rig.Cheer();
        }

        /// <summary>Swap the critter's look (rebuilds the body, remembers it for this player slot).</summary>
        public void ApplyLook(CritterLook l)
        {
            Look = l.Clone();
            Look.Save(Index);
            rig.Build(Look);
            bool was = cc.enabled;
            cc.enabled = false; // resizing an enabled controller can push it around
            FitToBody();
            cc.enabled = was;
        }

        void ClosetUpdate(float dt)
        {
            Vector3 dir = Vector3.zero;
            if (closetPhase == 0 || closetPhase == 2)
            {
                closetT = Mathf.Min(1f, closetT + dt / 0.45f);
                Vector3 a = closetPhase == 0 ? closetFrom : closet.Inside, b = closetPhase == 0 ? closet.Inside : closet.Entrance;
                transform.position = Vector3.Lerp(a, b, Juice.OutCubic(closetT));
                dir = b - a; dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f && closetT < 0.7f) facing = Vector3.Slerp(facing, dir.normalized, 1f - Mathf.Exp(-14f * dt));
                else facing = Vector3.Slerp(facing, closet.Facing, 1f - Mathf.Exp(-10f * dt));
                if (closetT >= 1f)
                {
                    if (closetPhase == 0) { closetPhase = 1; closet.Settled(this); rig.Squash(0.3f); }
                    else
                    {
                        closet.Release(this);
                        closet = null;
                        cc.enabled = true;
                        gameObject.layer = Layers.Player;
                        rig.Squash(0.25f);
                        Fx.Dust(Pos, 0.3f, 6);
                        rig.Tick(dt, facing, 0f, 0f, false, false, false, -1f);
                        return;
                    }
                }
            }
            else
            {
                facing = Vector3.Slerp(facing, closet.Facing, 1f - Mathf.Exp(-10f * dt));
                var rows = closet.RowsFor(this);
                closetRow = Mathf.Clamp(closetRow, 0, rows.Length - 1);
                Vector2 mv = input.MoveValue;
                int nx = mv.x > 0.55f ? 1 : mv.x < -0.55f ? -1 : 0;
                int ny = mv.y > 0.55f ? 1 : mv.y < -0.55f ? -1 : 0;
                bool fresh = nx != lastNavX || ny != lastNavY;
                navRepeat -= dt;
                if ((nx != 0 || ny != 0) && (fresh || navRepeat <= 0f))
                {
                    navRepeat = fresh ? 0.4f : 0.16f;
                    if (ny != 0 && rows.Length > 1)
                    {
                        int r = Mathf.Clamp(closetRow - ny, 0, rows.Length - 1);
                        if (r != closetRow) { closetRow = r; Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.5f, 1.2f); }
                    }
                    else if (nx != 0) { closet.Change(this, rows[closetRow], nx); Game.Hud?.PunchCloset(Index, nx); }
                }
                lastNavX = nx; lastNavY = ny;
                int d = 0;
                if (input.RotateCW.WasPressedThisFrame()) d += 1;
                if (input.RotateCCW.WasPressedThisFrame()) d -= 1;
                if (d != 0) { closet.Change(this, rows[closetRow], d); Game.Hud?.PunchCloset(Index, d); }

                if (input.Use.WasPressedThisFrame() || input.Jump.WasPressedThisFrame())
                {
                    closet.Leaving(this);
                    closetPhase = 2;
                    closetT = 0f;
                    rig.Hop();
                }
                else if (Game.Hud != null)
                {
                    var labels = new string[rows.Length];
                    var values = new string[rows.Length];
                    var swatches = new Color[rows.Length];
                    var rowNew = new bool[rows.Length];
                    var valueNew = new bool[rows.Length];
                    for (int i = 0; i < rows.Length; i++)
                    {
                        labels[i] = Closet.RowLabel(rows[i]);
                        values[i] = closet.Value(this, rows[i], out swatches[i]);
                        rowNew[i] = closet.RowNew(this, rows[i]);
                        valueNew[i] = rowNew[i] && closet.ValueNew(this, rows[i]);
                    }
                    string move = input.Scheme == ControlScheme.Gamepad ? "STICK" : input.Scheme == ControlScheme.KeyboardWASD ? "WASD" : "ARROWS";
                    Game.Hud.ShowCloset(Index, Color, closet.Kind == ClosetKind.Locker ? closet.Title : $"P{Index + 1}  {closet.Title}", labels, values, swatches, closetRow,
                        $"{move} / [{RotateCCWLabel}][{RotateCWLabel}] change  ·  [{UseLabel}] done", rowNew, valueNew);
                }
            }
            rig.LookAt(transform.position + facing * 3f + Vector3.up * 0.5f);
            rig.Tick(dt, facing, closetPhase == 1 ? 0f : 0.6f, 0f, false, false, false, -1f);
        }

        void ExitValve()
        {
            if (!valveActive) return;
            valveActive = false;
            rig.Squash(-0.1f);
            Sfx.Play(Sfx.I != null ? Sfx.I.clank : null, 0.3f, 1.2f);
        }

        // button names for on-screen guides, matching this player's controls
        public string UseLabel => input.Scheme == ControlScheme.Gamepad ? "X" : input.Scheme == ControlScheme.KeyboardWASD ? "E" : "Enter";
        public string JumpLabel => input.Scheme == ControlScheme.Gamepad ? "A" : input.Scheme == ControlScheme.KeyboardWASD ? "Space" : "R-Ctrl";
        public string RotateCCWLabel => input.Scheme == ControlScheme.Gamepad ? "Y" : input.Scheme == ControlScheme.KeyboardWASD ? "F" : ";";
        public string RotateCWLabel => input.Scheme == ControlScheme.Gamepad ? "B" : input.Scheme == ControlScheme.KeyboardWASD ? "G" : "'";

        void CancelHold()
        {
            if (holdPiece != null) holdPiece.SetWiggle(0f);
            holdPiece = null;
            holdProgress = 0f;
            Game.Hud?.HideProgress(Index);
        }

        void DropHeld()
        {
            Stats.drops++;
            var p = Held;
            Held = null;
            p.transform.position = HoldPoint();
            p.SetState(PieceState.Loose);
            p.Body.linearVelocity = facing * 3f + Vector3.up * 2.5f;
            p.Body.angularVelocity = Random.insideUnitSphere * 5f;
            rig.Squash(-0.2f);
            Sfx.Play(Sfx.I != null ? Sfx.I.hop : null, 0.5f, 0.7f);
        }

        void Eject(PipePiece p)
        {
            p.SetState(PieceState.Loose);
            p.transform.position += Vector3.up * 0.4f;
            Vector3 side = Vector3.Cross(Vector3.up, facing) * (Random.value < 0.5f ? -1f : 1f);
            p.Body.linearVelocity = side * 3.5f + Vector3.up * 5f - facing * 0.5f;
            p.Body.angularVelocity = Random.insideUnitSphere * 9f;
        }

        void Nope()
        {
            rig.HeadShake();
            Sfx.Play(Sfx.I != null ? Sfx.I.nope : null, 0.6f);
            if (actPiece != null) actPiece.Spring.Squash(0.12f);
        }

        /// <summary>If a freshly placed pipe overlaps the player, step the player out of its footprint.</summary>
        /// <summary>
        /// NES-style placing on the tile you stand on: the critter hops off backwards
        /// (or sideways if a pipe is behind it) instead of ending up inside the pipe.
        /// </summary>
        void HopOffPlaced(Vector2Int cell)
        {
            if (mode != Mode.Walk) return;
            var r = board.CellRect(cell);
            float pad = cc.radius + 0.47f * S - 0.5f * S + 0.04f;
            Vector3 p = Pos;
            bool overlapping = p.x > r.xMin - pad && p.x < r.xMax + pad && p.z > r.yMin - pad && p.z < r.yMax + pad;
            if (!overlapping) return;

            Dir f = DirUtil.FromVector(facing);
            Dir[] order = { DirUtil.Opposite(f), DirUtil.Rotate(f, 1), DirUtil.Rotate(f, -1), f };
            Vector3 center = board.CellCenter(cell);
            foreach (var d in order)
            {
                Vector3 dv = DirUtil.ToVector3(d);
                // keep the sideways offset, land just outside this edge of the tile
                Vector3 lateral = p - center; lateral -= dv * Vector3.Dot(lateral, dv); lateral.y = 0f;
                lateral = Vector3.ClampMagnitude(lateral, S * 0.3f);
                Vector3 target = center + dv * (S * 0.5f + cc.radius + 0.06f) + lateral;
                var n = cell + DirUtil.Offset(d);
                bool ok = board.InBounds(n) ? board.IsFree(n) : InWalkArea(target);
                if (!ok) continue;
                StartScripted(Mode.ClimbDown, target, 0.2f, 0f, 0.35f);
                return;
            }
            NudgeOutOf(cell); // boxed in on all sides: fall back to the nearest edge
        }

        public void NudgeOutOf(Vector2Int cell)
        {
            if (mode != Mode.Walk) return;
            var r = board.CellRect(cell);
            float pad = cc.radius + 0.47f * S - 0.5f * S + 0.02f; // collider is 0.94 of the tile
            float xMin = r.xMin - pad, xMax = r.xMax + pad, zMin = r.yMin - pad, zMax = r.yMax + pad;
            Vector3 p = Pos;
            if (p.x <= xMin || p.x >= xMax || p.z <= zMin || p.z >= zMax) return;
            float dl = p.x - xMin, dr = xMax - p.x, db = p.z - zMin, dt = zMax - p.z;
            float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
            if (m == dl) p.x = xMin; else if (m == dr) p.x = xMax; else if (m == db) p.z = zMin; else p.z = zMax;
            cc.enabled = false;
            transform.position = p;
            cc.enabled = true;
        }
    }
}
