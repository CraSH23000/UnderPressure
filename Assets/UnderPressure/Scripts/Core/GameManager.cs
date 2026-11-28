using UnityEngine;
using UnityEngine.InputSystem;

namespace UnderPressure
{
    /// <summary>Countdown → flow → spill. Money, distance goal, loop multiplier, and the big celebratory moments.</summary>
    public class GameManager : MonoBehaviour
    {
        public enum State { Countdown, Flowing, Won, Lost, Lobby }

        public State Current { get; private set; } = State.Countdown;
        /// <summary>Off the clock: after a round, or at the base (free play, belt idling).</summary>
        public bool IsOver => Current == State.Won || Current == State.Lost || Current == State.Lobby;
        /// <summary>At the base: no round at all.</summary>
        public bool InLobby => Current == State.Lobby;
        /// <summary>This round's earnings (paid into LAB FUNDING on a win). The HUD counts up to it.</summary>
        public long Money { get; private set; }
        public int Distance { get; private set; }
        public int Loops { get; private set; }
        public float CountdownRemaining => countdown;

        /// <summary>How close the liquid is to spilling, for the music's riser: 0 until it's within musicRiserSeconds,
        /// 1 at the spill. Only while it flows.</summary>
        public float SpillTension => Current == State.Flowing && flow != null && flow.Running
            ? Mathf.Clamp01(1f - flow.SecondsToSpill / Mathf.Max(0.5f, cfg.musicRiserSeconds)) : 0f;
        /// <summary>The liquid is about to leak: panic music, warning HUD.</summary>
        public bool Panic { get; private set; }

        GameConfig cfg;
        GridBoard board;
        FlowController flow;
        Hud hud;
        float countdown, endTime;
        int lastWholeSecond = -1;
        bool goalHit;
        float beltSpeedIntegral, beltTime; // time-weighted belt speed, for the end bonus
        float flowSpeedIntegral, flowTime; // time-weighted flow speed while the liquid runs
        public int WastedPieces { get; private set; }
        int wastedCost;

        // intermission (between chambers)
        bool intermission, teleporting;
        /// <summary>The lightning has struck: the level is about to be rebuilt somewhere else.</summary>
        public bool Teleporting => teleporting;
        readonly System.Collections.Generic.List<ReadyPad> pads = new System.Collections.Generic.List<ReadyPad>();
        float charge;           // teleporter charge 0..1 (builds while everyone stands on one)
        AudioSource hum;
        InputAction restart, pause, pauseRestart;
        public bool Paused => JuiceHost.Instance != null && JuiceHost.Instance.Paused;

        public void Init(GameConfig config, GridBoard b, FlowController f, Hud h)
        {
            cfg = config; board = b; flow = f; hud = h;
            countdown = cfg.countdownSeconds;
            flow.PieceFilled += OnPieceFilled;
            flow.LoopCompleted += OnLoop;
            flow.Spilled += OnSpill;

            restart = new InputAction("Restart", InputActionType.Button);
            restart.AddBinding("<Keyboard>/space");
            restart.AddBinding("<Keyboard>/e");
            restart.AddBinding("<Keyboard>/enter");
            restart.AddBinding("<Gamepad>/buttonSouth");
            restart.AddBinding("<Gamepad>/buttonWest");
            restart.AddBinding("<Gamepad>/start");
            restart.Enable();

            pause = new InputAction("Pause", InputActionType.Button);
            pause.AddBinding("<Keyboard>/escape");
            pause.AddBinding("<Gamepad>/start");
            pause.Enable();
            pauseRestart = new InputAction("PauseRestart", InputActionType.Button);
            pauseRestart.AddBinding("<Keyboard>/r");
            pauseRestart.AddBinding("<Gamepad>/select");
            pauseRestart.Enable();

            hud.SetDistance(0, cfg.goalDistance);
            if (Game.Bootstrap != null && Game.Bootstrap.InBase) { Current = State.Lobby; intermission = true; }
        }

        void OnDestroy()
        {
            if (hum != null) Destroy(hum.gameObject);
            restart?.Dispose();
            pause?.Dispose();
            pauseRestart?.Dispose();
            if (JuiceHost.Instance != null) JuiceHost.Instance.Paused = false;
        }

        void HandlePause()
        {
            if (IsOver || JuiceHost.Instance == null) return;
            if (pause.WasPressedThisFrame())
            {
                bool p = !JuiceHost.Instance.Paused;
                JuiceHost.Instance.Paused = p;
                hud.ShowPause(p);
                AudioListener.pause = false;
                Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.6f, p ? 0.7f : 1.1f, 0f);
            }
            else if (JuiceHost.Instance.Paused && pauseRestart.WasPressedThisFrame())
            {
                JuiceHost.Instance.Paused = false;
                Game.Bootstrap.Restart();
            }
        }

        /// <summary>A critter fell in the pit: reprinting one costs money (from the round, or the bank when off the clock).</summary>
        public void OnCritterLost(Vector3 pitPos)
        {
            int cost = cfg.pitRespawnCost;
            if (cost <= 0 || (InLobby && cfg.freePitInBase)) return; // falling in at the base is free
            if (IsOver) Game.Bootstrap.Bank -= cost;
            else { Money -= cost; hud.PunchMoney(0.8f); }
            Popup.Spawn(pitPos + Vector3.up * 1.0f, $"-${cost}", Mats.Danger, 44, 1.2f, 110f);
            Sfx.Play(Sfx.I != null ? Sfx.I.waste : null, 0.8f, 0.8f, 0.03f);
        }

        /// <summary>A piece fell into the pit unused.</summary>
        public void OnPieceWasted(Vector3 pitPos)
        {
            if (IsOver) return;
            int loss = cfg.wastedPiecePenalty;
            Money -= loss;
            WastedPieces++;
            wastedCost += loss;
            Popup.Spawn(pitPos + Vector3.up * 0.8f, $"-${loss}", Mats.Danger, 42, 1.1f, 110f);
            Sfx.Play(Sfx.I != null ? Sfx.I.waste : null, 0.7f, 1f, 0.03f);
            Fx.Burst(pitPos + Vector3.up * 0.2f, new Color(0.05f, 0.05f, 0.06f), new Color(0.3f, 0.05f, 0.04f), 10, 2.5f, 0.22f, 0.6f, -0.2f);
            Juice.Shake(0.08f);
            hud.PunchMoney(0.6f);
        }

        void Update()
        {
            if (Paused) return; // the pause menu (PauseMenu, on the HUD) owns pausing now

            int joined = Game.Players.Count;
            hud.SetJoinPrompt(joined > 0 && joined < JoinManager.MaxPlayers);
            hud.ShowDistance(!InLobby);

            if (Current == State.Flowing)
            {
                flowSpeedIntegral += flow.SpeedMultiplier * Time.deltaTime;
                flowTime += Time.deltaTime;
            }
            if (!IsOver && joined > 0 && Game.Conveyor != null)
            {
                beltSpeedIntegral += Game.Conveyor.SpeedMultiplier * Time.deltaTime;
                beltTime += Time.deltaTime;
            }

            switch (Current)
            {
                case State.Countdown:
                    if (joined == 0)
                    {
                        // nobody here yet: hold the clock until the first critter joins
                        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
                        hud.SetTimer("PRESS (A) / SPACE / ENTER", $"to join · up to {JoinManager.MaxPlayers} players", new Color(1f, 1f, 1f, pulse));
                        board.Spout.Pressure = 0f;
                        break;
                    }
                    UpdateCountdown();
                    break;
                case State.Flowing:
                    UpdatePanic();
                    hud.SetTimer("", "", Color.white);
                    var holo = board.Spout.Hologram;
                    if (Panic)
                    {
                        int left = Mathf.CeilToInt(flow.SecondsToSpill); // seconds, at the current flow speed
                        holo.Set(left <= 0 ? "LEAK!" : $"LEAK IN {left}!", "lay pipe ahead!", true);
                    }
                    else
                        holo.Set("", SpoutHologram.LiquidName, false, SpoutHologram.LiquidTextColor, FlowInfo());
                    UpdateLiveMonitor();
                    break;
                case State.Lobby:
                    UpdateLobby();
                    break;
                default:
                    if (intermission) UpdateTeleporters();
                    break;
            }
        }

        void UpdatePanic()
        {
            bool panic = flow.SecondsToSpill < Mathf.Max(0.5f, cfg.panicSeconds);
            if (panic && !Panic)
            {
                Banner.Show("LEAK WARNING!", Mats.Danger, 100, 1.0f, -60f);
                Sfx.Play(Sfx.I != null ? Sfx.I.alarm : null, 0.8f, 1f, 0f);
                Juice.Shake(0.15f);
                hud.PunchTimer(1f);
            }
            else if (!panic && Panic)
            {
                Popup.Spawn(board.Spout.CenterTop + Vector3.up * 0.5f, "PHEW", Mats.Liquid, 36, 0.8f);
            }
            Panic = panic;
        }

        void UpdateCountdown()
        {
            countdown -= Time.deltaTime * Mathf.Max(1f, flow.SpeedMultiplier); // opening the valve early shortens the wait
            float pressureWindow = Mathf.Min(10f, cfg.countdownSeconds);
            board.Spout.Pressure = Mathf.Clamp01(1f - countdown / pressureWindow);

            int whole = Mathf.CeilToInt(Mathf.Max(0f, countdown));
            bool urgent = whole <= 5;
            hud.SetTimer("", "", Color.white);
            board.Spout.Hologram.Set(whole.ToString(), SpoutHologram.LiquidName, urgent, SpoutHologram.LiquidTextColor, FlowInfo());
            UpdateLiveMonitor();
            if (whole != lastWholeSecond)
            {
                lastWholeSecond = whole;
                if (urgent && whole > 0)
                {
                    Sfx.Play(Sfx.I != null ? Sfx.I.tick : null, 0.8f, 1f + (5 - whole) * 0.08f, 0f);
                    hud.PunchTimer();
                    board.Spout.Tick();
                }
            }
            if (countdown <= 0f) StartFlow();
        }

        float liveTimer;

        /// <summary>The back-wall monitor doubles as the live status board during a round.</summary>
        void UpdateLiveMonitor()
        {
            if (Game.Monitor == null) return;
            liveTimer -= Time.unscaledDeltaTime;
            if (liveTimer > 0f) return;
            liveTimer = 0.25f;
            string state = Current == State.Countdown ? $"FLOW IN {Mathf.CeilToInt(Mathf.Max(0f, countdown))}" : Panic ? "<color=#F55>LEAK IMMINENT</color>" : "FLOWING";
            string left =
                $"STATUS  {state}\n" +
                $"DISTANCE  {Distance} / {cfg.goalDistance}{(Distance >= cfg.goalDistance ? "  <color=#FD4>GOAL</color>" : "")}\n" +
                $"LOOPS  {Loops}\n" +
                $"EARNED  <color=#FD4>${Money:N0}</color>";
            string right =
                $"FLOW  x{flow.SpeedMultiplier:0.##}\n" +
                $"BELT  x{(Game.Conveyor != null ? Game.Conveyor.SpeedMultiplier : 1f):0.##}\n" +
                (WastedPieces > 0 ? $"WASTED  <color=#F66>{WastedPieces}</color>\n" : "") +
                $"SUBJECTS  {Game.Players.Count}/{JoinManager.MaxPlayers}\n" +
                $"MEDIUM  <color=#{ColorUtility.ToHtmlStringRGB(Mats.Liquid)}>{Mats.LiquidName}</color>";
            string footer = Game.Players.Count < JoinManager.MaxPlayers
                ? $"LAB FUNDING  ${Cash.Hud(Game.Bootstrap.Bank)}   ·   new subjects: press (A) / Space / Enter"
                : $"LAB FUNDING  ${Cash.Hud(Game.Bootstrap.Bank)}";
            Game.Monitor.SetLive($"TEST CHAMBER {Game.Bootstrap.ChamberNumber:00}", left, right, footer);
        }

        void StartFlow()
        {
            Current = State.Flowing;
            board.Spout.Pressure = 0.25f;
            board.Spout.Burst();
            flow.Begin();
            Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.9f, 1f, 0f);
            Juice.Shake(0.4f);
            Juice.HitStop(0.06f);
            Game.Cam?.FovPunch(-2f);
            Banner.Show("FLOW!", Mats.Liquid, 110, 0.9f);
        }

        void OnPieceFilled(PipePiece p)
        {
            Distance++;
            Music.Instance?.PipeFilled(); // the heartbeat
            Progress.OnPieceFilled(p);
            // the Count Ant, and filtered streams (purer water / stronger concentrate) are worth more
            int value = Mathf.RoundToInt(PieceValue() * Items.MoneyMultiplier * flow.LastGrade.ValueMultiplier);
            Vector3 top = p.transform.position + Vector3.up * (p.TopHeight + 0.3f);
            // lucky break: this pipe pays double
            if (Items.Roll(cfg.bonusPipeChance, out bool lucky))
            {
                value *= 2;
                Popup.Spawn(top + Vector3.up * 0.5f, "BONUS x2", new Color(1f, 0.85f, 0.25f), 34, 1f);
                Sfx.Play(Sfx.I != null ? Sfx.I.coin : null, 0.7f, 1.3f);
                if (lucky) Juice.Delay(0.15f, () => Items.ShowLucky(top + Vector3.up * 1.0f));
            }
            Money += value;
            Popup.Spawn(top, $"+${value}", Mats.Money, 36, 0.8f);
            Sfx.Play(Sfx.I != null ? Sfx.I.bloop : null, 0.7f, 1f + Mathf.Min(Distance, 24) * 0.035f, 0.01f);
            p.Spring.Squash(0.3f);
            Fx.Burst(top - Vector3.up * 0.2f, Mats.Liquid, Color.white, 6, 2.5f, 0.14f, 0.4f, 0.8f, true);
            hud.SetDistance(Distance, cfg.goalDistance);
            hud.PunchMoney(0.5f);

            if (!goalHit && Distance >= cfg.goalDistance)
            {
                goalHit = true;
                Banner.Show("GOAL REACHED!", Mats.Money, 120, 1.6f);
                Sfx.Play(Sfx.I != null ? Sfx.I.fanfare : null, 0.9f, 1f, 0f);
                Fx.Confetti(p.transform.position + Vector3.up, 60, 9f);
                Juice.HitStop(0.1f);
                Juice.Shake(0.5f);
                Game.Cam?.FovPunch(-3f);
                foreach (var pl in Game.Players) if (pl != null) pl.Cheer();
            }
        }

        int PieceValue() => cfg.moneyScalesWithFlowSpeed
            ? Mathf.Max(1, Mathf.RoundToInt(cfg.moneyPerPiece * flow.SpeedMultiplier))
            : cfg.moneyPerPiece;

        /// <summary>
        /// A plain loop pays loopBonus. A loop with N finished loops nested inside it pays
        /// loopBonus * (N + 1) * nestedLoopMultiplier — the nested loops get paid again, doubled.
        /// </summary>
        /// <summary>
        /// End-of-level speed bonuses, both based on time-weighted AVERAGE speed (no last-second cranking):
        ///   belt: (avg - 1) * beltBonusPerExtraSpeed   (negative only if beltSlowPenalty)
        ///   flow: (avg - 1) * flowBonusPerExtraSpeed   (bonus only; faster flow already pays per pipe)
        /// Both percentages apply to the earnings before bonuses.
        /// </summary>
        string ApplySpeedBonuses()
        {
            if (Money <= 0) return null;
            long baseMoney = Money;
            var lines = new System.Collections.Generic.List<string>();
            long total = 0;

            if (beltTime > 0.5f)
            {
                float avg = beltSpeedIntegral / beltTime;
                float pct = (avg - 1f) * cfg.beltBonusPerExtraSpeed;
                if (pct < 0f && !cfg.beltSlowPenalty) pct = 0f;
                long bonus = (long)System.Math.Round(baseMoney * (double)pct);
                total += bonus;
                lines.Add(bonus == 0 ? $"Belt avg x{avg:0.##}" : $"Belt avg x{avg:0.##}  {(bonus > 0 ? "+" : "-")}{Mathf.Abs(pct) * 100f:0}%  ({(bonus > 0 ? "+" : "-")}${System.Math.Abs(bonus):N0})");
            }
            if (flowTime > 0.5f)
            {
                float avg = flowSpeedIntegral / flowTime;
                float pct = Mathf.Max(0f, (avg - 1f) * cfg.flowBonusPerExtraSpeed);
                long bonus = (long)System.Math.Round(baseMoney * (double)pct);
                total += bonus;
                lines.Add(bonus == 0 ? $"Flow avg x{avg:0.##}" : $"Flow avg x{avg:0.##}  +{pct * 100f:0}%  (+${bonus:N0})");
            }

            if (total != 0)
            {
                Money += total;
                hud.PunchMoney(1f);
                if (total > 0)
                {
                    Popup.Spawn(board.Spout.CenterTop + Vector3.up * 0.6f, $"SPEED BONUS +${total:N0}", Mats.Money, 46, 1.6f, 120f);
                    Sfx.Play(Sfx.I != null ? Sfx.I.coin : null, 0.9f, 1.1f);
                }
            }
            return lines.Count > 0 ? string.Join("     ", lines) : null;
        }

        void OnLoop(PipePiece p, int nested)
        {
            Loops++;
            int count = nested + 1;
            int amount = Mathf.RoundToInt(cfg.loopBonus * count * (nested > 0 ? Mathf.Max(1, cfg.nestedLoopMultiplier) : 1) * Items.MoneyMultiplier);
            Money += amount;
            Vector3 c = p.transform.position + Vector3.up * (p.TopHeight + 0.2f);
            int heat = Mathf.Min(count, 6); // how big the celebration gets

            string title = nested == 0 ? "LOOP!" : $"NESTED x{count}!";
            Banner.Show(title, Color.Lerp(Mats.Liquid, Mats.Money, Mathf.Min(1f, (heat - 1) * 0.3f)), 120 + heat * 8, 1.4f);
            string detail = nested == 0 ? $"+${amount}" : $"+${amount}  ({count} loops x{cfg.nestedLoopMultiplier})";
            Popup.Spawn(c + Vector3.up * 0.4f, detail, Mats.Money, 52 + heat * 4, 1.4f, 120f);
            Sfx.Play(Sfx.I != null ? Sfx.I.loop : null, 1f, 1f + (heat - 1) * 0.1f, 0f);
            Juice.Delay(0.15f, () => Sfx.Play(Sfx.I != null ? Sfx.I.coin : null, 0.8f, 1f + (heat - 1) * 0.06f));
            if (nested > 0) Juice.Delay(0.3f, () => Sfx.Play(Sfx.I != null ? Sfx.I.fanfare : null, 0.6f, 1f + (heat - 2) * 0.05f, 0f));
            Fx.Fountain(c, Mats.Liquid, Color.white, 30 + heat * 10, 7f, 0.2f, 0.9f, 1.2f, true);
            Fx.Confetti(c, 30 + heat * 15, 7f + heat);
            p.Spring.Squash(0.6f);
            Juice.HitStop(0.09f + heat * 0.02f);
            Juice.Shake(0.4f + heat * 0.07f);
            Game.Cam?.FovPunch(-2.5f - heat * 0.5f);
            hud.PunchMoney(1f);
            foreach (var pl in Game.Players) if (pl != null) pl.Cheer();
        }

        void OnSpill(Vector3 pos, Vector3 dir)
        {
            Panic = false;
            Current = Distance >= cfg.goalDistance ? State.Won : State.Lost;
            Game.Conveyor?.ResetSpeed(); // off the clock: idle speed, lever back to default (still usable)
            Progress.OnSpill(pos, dir);
            FunnelCatch(pos, dir);
            if (Game.Bootstrap != null && !Game.Bootstrap.InBase)
            {
                // what spilled moves the lab's grime, shown as a ring rolling out from here
                float want = flow.SpillGrade.SpillGrime;
                float applied = Game.Bootstrap.AddGrime(want);
                if (GrimeOverlay.I != null) GrimeOverlay.I.Ring(pos, applied, want);
            }
            board.Spout.Hologram.Set(Current == State.Won ? "PASSED" : "SPILLED", "", Current != State.Won);
            Juice.SlowMo(0.25f, 0.9f);
            if (flow.LastSpillBurst)
            {
                // two streams met head-on: the pipe blew open, spraying out of both torn ends
                Juice.Shake(0.8f);
                Game.Cam?.FovPunch(-4f);
                var axis = flow.BurstAxis;
                for (int s = -1; s <= 1; s += 2)
                    Fx.Burst(pos, Mats.Liquid, new Color(0.7f, 1f, 0.75f), 35, 6f, 0.24f, 0.9f, 2.2f, true,
                        ParticleSystemShapeType.Cone, 0.1f, Quaternion.LookRotation(axis * s + Vector3.up * 0.8f));
                Fx.Sparks(pos, 20, 7f);
                Banner.Show(flow.LastBurstFilter ? "FILTER SHATTERED!" : "PIPE BURST!", Mats.Danger, 120, 1.4f);
                Progress.AddTeam(Progress.TeamPipeBursts);
            }
            else
            {
                Juice.Shake(0.55f);
                Fx.Burst(pos, Mats.Liquid, new Color(0.7f, 1f, 0.75f), 45, 5.5f, 0.24f, 0.9f, 2.2f, true,
                    ParticleSystemShapeType.Cone, 0.1f, Quaternion.LookRotation(dir + Vector3.up * 0.6f));
            }
            Sfx.Play(Sfx.I != null ? Sfx.I.spill : null, 1f, 1f, 0f);
            Sfx.Play(Sfx.I != null ? Sfx.I.splat : null, 0.8f);

            // a wobbly blob of whatever was in the pipes (a burst puddles right under the tear)
            float off = flow.LastSpillBurst ? 0f : 0.7f;
            SlimePuddle.Spawn(new Vector3(pos.x + dir.x * off, 0f, pos.z + dir.z * off), flow.LastSpillBurst ? flow.BurstAxis : dir);

            Juice.Delay(0.6f, BeginIntermission);
        }

        /// <summary>The funnel: anyone with a bottle standing in the leak catches some. Water refills it; anything else
        /// contaminates it (side effects when they drink).</summary>
        void FunnelCatch(Vector3 pos, Vector3 dir)
        {
            if (!Items.TeamOwns(Items.Funnel) || !Items.TeamOwns(Items.WaterBottle)) return;
            var grade = flow.SpillGrade;       // whichever stream leaked (filters can change it)
            bool water = grade.water && grade.level == 0;
            string name = grade.Name;
            foreach (var pl in Game.Players)
            {
                if (pl == null || !Progress.InSpray(pl, pos, dir)) continue;
                int slot = pl.Index;
                Items.SetCharges(slot, Items.WaterBottle, Items.MaxCharges(Items.WaterBottle));
                Items.SetBottleMix(slot, water ? "" : name);
                Vector3 at = pl.transform.position + Vector3.up * 2.1f;
                if (water) Popup.Spawn(at, "CAUGHT IT!  BOTTLE REFILLED", new Color(0.5f, 0.85f, 1f), 34, 1.6f);
                else Popup.Spawn(at, $"CAUGHT IT!  ...{name}?", grade.Color, 34, 1.8f);
                Fx.Burst(pl.transform.position + Vector3.up * 1.2f, Mats.Liquid, Color.white, 20, 2.5f, 0.1f, 0.6f, 1f, true);
                Sfx.Play(Sfx.I != null ? Sfx.I.bloop : null, 0.8f, 0.8f);
                if (!grade.water) pl.GetSprayed(dir); // you still get soaked in it
            }
        }

        // =================================================================== base

        bool lobbyCam;
        float baseMonitorT;

        /// <summary>The spout hologram's small line: flow speed and (if it scales with speed) the pay per pipe.</summary>
        string FlowInfo() => $"flow x{flow.SpeedMultiplier:0.##}" + (cfg.moneyScalesWithFlowSpeed ? $"  ·  ${PieceValue()}/pipe" : "");

        void UpdateLobby()
        {
            board.Spout.Pressure = 0f;
            if (Game.Players.Count == 0)
            {
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
                hud.SetTimer("PRESS (A) / SPACE / ENTER", $"to join · up to {JoinManager.MaxPlayers} players", new Color(1f, 1f, 1f, pulse));
                board.Spout.Hologram.Set("PRACTICE", "no clock at the base", false);
                UpdateBaseMonitor();
                return;
            }
            if (!lobbyCam && Game.Cam != null && Game.Monitor != null) { lobbyCam = true; Game.Cam.BeginFollow(Game.Monitor.Center); }
            if (pads.Count == 0 && pendingPads == null) CreateBasePads();
            board.Spout.Hologram.Set("PRACTICE", "no clock at the base", false);
            UpdateBaseMonitor();
            UpdateTeleporters();
        }

        // =================================================================== teleporters (in the secret room)

        /// <summary>Teleporters waiting for someone to walk into the secret room.</summary>
        System.Action pendingPads;
        /// <summary>How the teleporters were last called up (to call them up again if the room grows around them).</summary>
        System.Action lastPads;
        readonly System.Collections.Generic.List<GameObject> lockedHatches = new System.Collections.Generic.List<GameObject>();

        /// <summary>
        /// The room grew (someone joined): the secret room was rebuilt bigger and further over, so any teleporters
        /// already called up go and come up again in their new spots (straight away if someone's in there).
        /// </summary>
        public void OnRoomGrown()
        {
            if (lastPads == null) return; // nobody has called them up yet
            foreach (var p in pads) if (p != null) Destroy(p.gameObject);
            pads.Clear();
            foreach (var h in lockedHatches) if (h != null) Destroy(h);
            lockedHatches.Clear();
            charge = 0f;
            teleporterBelt = teleporterBeltLeft = null; // (they went with the old room)
            QueuePads(lastPads);
        }

        /// <summary>
        /// The teleporters live behind the secret door: open it now, and bring them up out of the floor once someone
        /// walks in. (No secret door: straight away, in the chamber.)
        /// </summary>
        void QueuePads(System.Action make)
        {
            var door = SecretDoor.Instance;
            lastPads = make;
            if (door == null) { make(); return; }
            pendingPads = make;
            BuildTeleporterBelt();
            door.Open();
        }

        DecorBelt teleporterBelt, teleporterBeltLeft;

        /// <summary>
        /// The secret room's express belts (32x), both ending lined up with the boss floor's teleporter, a strip of
        /// floor short of it. Right: up out of a chute just past the doorway, along the back at the same depth as the
        /// chamber's top belt, south down the gap between the teleporters and the far wall, then west. Left: up out of
        /// a chute just south of the doorway (which stays clear), south down the gap by the near wall, then east.
        /// You can ride them.
        /// </summary>
        void BuildTeleporterBelt()
        {
            var door = SecretDoor.Instance;
            if (door == null || teleporterBelt != null) return;
            // a narrow room (it grows with the players) has no space for them beside the teleporters
            if (door.Room.width * 0.5f < 9.5f) return;
            float hw = GameBootstrap.BeltHalfWidth, backZ = Game.Bootstrap.TopBeltZ, speed = cfg.beltSpeed * 32f;
            var boss = FloorSpot(BossFloor - 1, out _);
            float endGap = ReadyPad.HatchHalfWidth + 1.0f; // stops short: riders land on a strip of floor first

            float eastX = door.Room.xMax - hw - 0.15f;
            teleporterBelt = DecorBelt.Create(door.RoomRoot, new[]
            {
                new Vector3(door.Room.xMin + 3f, 0f, backZ), new Vector3(eastX, 0f, backZ),
                new Vector3(eastX, 0f, boss.z), new Vector3(boss.x + endGap, 0f, boss.z),
            }, hw, speed);

            float westX = door.Room.xMin + hw + 0.15f;
            float startZ = door.DoorZ0 - 0.7f; // its chute sits just south of the doorway
            teleporterBeltLeft = DecorBelt.Create(door.RoomRoot, new[]
            {
                new Vector3(westX, 0f, startZ), new Vector3(westX, 0f, boss.z), new Vector3(boss.x - endGap, 0f, boss.z),
            }, hw, speed);
        }

        void UpdatePendingPads()
        {
            if (pendingPads == null) return;
            var door = SecretDoor.Instance;
            if (door != null && !door.AnyoneInside) return;
            var make = pendingPads;
            pendingPads = null;
            make();
        }

        /// <summary>
        /// Floors with a teleporter (or a locked hatch) of their own in the secret room: three to a row, and the last
        /// one (the boss floor) on its own row at the front. Deeper floors will get a room of their own.
        /// </summary>
        const int FloorCount = 10, FloorCols = 3;
        public const int BossFloor = FloorCount;

        /// <summary>
        /// The front row of the secret room (retry / base / next): dx teleporter-spacings right of its centre, just in
        /// front of the fast belt (which runs at the same depth as the chamber's top belt).
        /// (No secret door: the old spot in the chamber's top lane.)
        /// </summary>
        Vector3 PadSpot(float dx)
        {
            var bs = Game.Bootstrap;
            var door = SecretDoor.Instance;
            if (door == null) return bs.PadAnchor + Vector3.right * dx * bs.PadSpacing;
            float z = bs.TopBeltZ - GameBootstrap.BeltHalfWidth - 0.12f - 0.25f - ReadyPad.FootprintRadius;
            return new Vector3(door.Room.center.x + dx * bs.PadSpacing, 0f, z);
        }

        /// <summary>
        /// Floor i (0 = chamber 01): rows of three below the front row, left to right; the boss floor alone and centred
        /// in front. Row spacing shrinks to fit the room (but keeps a walkway between rows).
        /// </summary>
        Vector3 FloorSpot(int i, out float delay)
        {
            var top = PadSpot(0f);
            int rows = (FloorCount - 2) / FloorCols + 2; // full rows of three, plus the boss row
            float room = top.z - ReadyPad.FootprintRadius - (SecretDoor.Instance.Room.yMin + 0.2f);
            float pitch = Mathf.Clamp(room / rows, 3.1f, 4.6f); // (packs tighter in a small room: it grows with the players)
            bool boss = i == BossFloor - 1;
            int row = boss ? rows - 1 : i / FloorCols, col = boss ? 1 : i % FloorCols;
            delay = 0.6f + i * 0.22f + (boss ? 0.5f : 0f); // up one after the other after the front row, the boss last
            return new Vector3(top.x + (col - (FloorCols - 1) * 0.5f) * Game.Bootstrap.PadSpacing, 0f, top.z - pitch * (row + 1));
        }

        /// <summary>A teleporter for every floor reached so far, and a shut hatch for each one still locked.</summary>
        void AddFloorGrid()
        {
            if (SecretDoor.Instance == null) return;
            int furthest = Progress.Furthest;
            for (int c = 1; c <= FloorCount; c++)
            {
                var at = FloorSpot(c - 1, out float delay);
                if (c <= furthest) pads.Add(ReadyPad.Create(Game.LevelRoot, at, ReadyPad.Kind.Chamber, $"CHAMBER {c:00}", delay, c));
                else lockedHatches.Add(ReadyPad.CreateLockedHatch(Game.LevelRoot, at, c));
            }
        }

        /// <summary>The way out: chamber 01, plus "continue" to the deepest chamber you've reached.</summary>
        void CreateBasePads() => QueuePads(() =>
        {
            if (SecretDoor.Instance != null) AddFloorGrid(); // every floor reached so far
            else
            {
                int furthest = Progress.Furthest;
                pads.Add(ReadyPad.Create(Game.LevelRoot, PadSpot(furthest <= 1 ? 0f : -0.5f), ReadyPad.Kind.Chamber, "CHAMBER 01", 0.2f, 1));
                if (furthest > 1) pads.Add(ReadyPad.Create(Game.LevelRoot, PadSpot(0.5f), ReadyPad.Kind.Chamber, $"CHAMBER {furthest:00}", 0.5f, furthest));
            }
            Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.6f, 1.3f, 0f);
        });

        void UpdateBaseMonitor()
        {
            if (Game.Monitor == null) return;
            baseMonitorT -= Time.unscaledDeltaTime;
            if (baseMonitorT > 0f) return;
            baseMonitorT = 0.5f;
            int furthest = Progress.Furthest;
            bool bottles = Items.TeamOwns(Items.WaterBottle);
            string left =
                $"DEEPEST CHAMBER  {furthest:00}\n" +
                $"LAB FUNDING  <color=#FD4>${Cash.Hud(Game.Bootstrap.Bank)}</color>\n" +
                $"WATER BOTTLES  {(bottles ? "<color=#9EF>ISSUED</color>" : $"${cfg.waterBottlePrice:N0}")}";
            string right =
                $"SUBJECTS  {Game.Players.Count}/{JoinManager.MaxPlayers}\n" +
                "CLOCK  <color=#9EF>OFF</color>\n" +
                $"MEDIUM  <color=#{ColorUtility.ToHtmlStringRGB(Mats.Liquid)}>{Mats.LiquidName}</color>";
            string footer = Game.Players.Count < JoinManager.MaxPlayers
                ? "your locker is at the back  ·  new subjects: press (A) / Space / Enter"
                : "your locker is at the back  ·  stand on a teleporter together to start";
            Game.Monitor.SetLive("LAB BASE", left, right, footer);
        }

        // =================================================================== intermission

        /// <summary>
        /// No lockdown: results go up on the wall monitor, the camera drops in to follow everyone,
        /// and teleporter pads appear. Once every player stands on one, a short countdown runs and
        /// one pad is picked at random, weighted by how many players chose it.
        /// </summary>
        void BeginIntermission()
        {
            endTime = Time.unscaledTime;
            bool won = Current == State.Won;
            string bonusLine = won ? ApplySpeedBonuses() : null;
            if (won) Game.Bootstrap.Bank += Money;
            if (won) Progress.ReachedChamber(Game.Bootstrap.ChamberNumber + 1); // passing a chamber unlocks the next one (CONTINUE at the base)

            Banner.Show(won ? "PLUMBING COMPLETE!" : "SPILLED!", won ? Mats.Money : Mats.Danger, 120, 2.2f, 120f);
            if (won)
            {
                Sfx.Play(Sfx.I != null ? Sfx.I.fanfare : null, 0.9f, 1.12f, 0f);
                Fx.Confetti(board.Spout.transform.position + Vector3.up * 2f, 80, 10f);
            }
            else Sfx.Play(Sfx.I != null ? Sfx.I.nope : null, 0.5f, 0.6f, 0f);
            foreach (var pl in Game.Players)
                if (pl != null) { if (won) pl.Cheer(); else pl.Sad(); }

            Game.Monitor?.ShowResults(new WallMonitor.Results
            {
                won = won, chamber = Game.Bootstrap.ChamberNumber, distance = Distance, goal = cfg.goalDistance,
                loops = Loops, earned = Money, wasted = WastedPieces, wastedCost = wastedCost,
                bank = Game.Bootstrap.Bank, bonusLine = bonusLine, awards = BuildAwards(), unlocks = Progress.TakePending(),
            });
            if (Game.Monitor != null) Game.Cam?.BeginFollow(Game.Monitor.Center);

            var bs = Game.Bootstrap;
            // retry on the left, the base in the middle, the next chamber on the right (if you passed)
            QueuePads(() =>
            {
                if (won)
                {
                    pads.Add(ReadyPad.Create(Game.LevelRoot, PadSpot(-1f), ReadyPad.Kind.Retry, $"CHAMBER {bs.ChamberNumber:00}"));
                    pads.Add(ReadyPad.Create(Game.LevelRoot, PadSpot(0f), ReadyPad.Kind.Base, "BASE", 0.2f));
                    pads.Add(ReadyPad.Create(Game.LevelRoot, PadSpot(1f), ReadyPad.Kind.Next, $"CHAMBER {bs.ChamberNumber + 1:00}", 0.4f));
                }
                else
                {
                    pads.Add(ReadyPad.Create(Game.LevelRoot, PadSpot(-0.5f), ReadyPad.Kind.Retry, $"CHAMBER {bs.ChamberNumber:00}"));
                    pads.Add(ReadyPad.Create(Game.LevelRoot, PadSpot(0.5f), ReadyPad.Kind.Base, "BASE", 0.2f));
                }
                AddFloorGrid();
                Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.6f, 1.3f, 0f);
            });
            intermission = true;
        }

        void UpdateTeleporters()
        {
            UpdatePendingPads();
            if (teleporting) return;
            foreach (var p in pads) p.Occupants = 0;
            int ready = 0, total = 0;
            foreach (var pl in Game.Players)
            {
                if (pl == null) continue;
                total++;
                foreach (var p in pads)
                    if (p.Contains(pl.transform.position)) { p.Occupants++; ready++; break; }
            }

            bool allReady = total > 0 && ready == total;
            string title = InLobby ? "LAB BASE" : Current == State.Won ? "CHAMBER PASSED" : "TEST FAILED";

            // charge up while everyone's on a teleporter; it bleeds off fast if anyone steps away
            float dt = Time.unscaledDeltaTime;
            charge = allReady ? charge + dt / Mathf.Max(0.5f, cfg.readyCountdownSeconds) : Mathf.Max(0f, charge - dt * 1.5f);
            foreach (var p in pads) p.SetCharge(charge);
            if (hum == null) hum = Sfx.Loop(Sfx.I != null ? Sfx.I.hum : null);
            if (hum != null)
            {
                hum.volume = Mathf.Clamp01(charge * 1.2f) * 0.55f * (Game.Config != null ? Game.Config.sfxVolume : 0.8f);
                hum.pitch = 0.6f + charge * 1.1f;
            }

            if (!allReady && charge <= 0f)
            {
                hud.SetTimer(title, pendingPads != null ? "The teleporters are through the door on the right" : $"Everyone stand on a teleporter  ·  ready {ready}/{total}", InLobby ? new Color(0.75f, 0.88f, 1f) : Current == State.Won ? Mats.Liquid : Mats.Danger);
                if (!InLobby && Time.unscaledTime - endTime > 2.5f) Game.Monitor?.ShowFooter($"OFF THE CLOCK   ·   LAB FUNDING  ${Cash.Hud(Game.Bootstrap.Bank)}   ·   ready {ready}/{total}");
                return;
            }

            var oddsParts = new System.Collections.Generic.List<string>();
            foreach (var p in pads) if (p.Occupants > 0) oddsParts.Add($"{p.ShortName} {p.Occupants}");
            string odds = string.Join(" : ", oddsParts);
            int amps = Mathf.RoundToInt(charge * 9000f);
            hud.SetTimer(allReady ? $"CHARGING  {charge * 100f:0}%" : "DISCHARGING...", $"{amps:N0} AMPS  ·  {odds}", Color.Lerp(Color.white, new Color(0.6f, 0.8f, 1f), charge));
            if (!InLobby) Game.Monitor?.ShowFooter($"CAPACITOR {charge * 100f:0}%   ·   {amps:N0} A   ·   {odds}");
            if (allReady) Juice.Shake(charge * charge * 0.02f);
            if (charge >= 1f) StartCoroutine(Teleport());
        }

        System.Collections.IEnumerator Teleport()
        {
            teleporting = true;
            // weighted pick: choose a random ready player, go where they're standing
            var choices = new System.Collections.Generic.List<ReadyPad>();
            foreach (var p in pads) for (int i = 0; i < p.Occupants; i++) choices.Add(p);
            var chosen = choices[Random.Range(0, choices.Count)];

            // roulette between the occupied pads: the current jumps between them before settling
            var occupied = new System.Collections.Generic.List<ReadyPad>();
            foreach (var p in pads) if (p.Occupants > 0) occupied.Add(p);
            if (occupied.Count > 1)
            {
                int flips = 7 + occupied.Count;
                int at = 0;
                for (int i = 0; i < flips; i++)
                {
                    at = i % occupied.Count;
                    // land on the chosen one
                    if (i == flips - 1) at = occupied.IndexOf(chosen);
                    var p = occupied[at];
                    p.Flash();
                    hud.SetTimer(p.Question, "", Color.white);
                    yield return new WaitForSecondsRealtime(0.09f + i * 0.035f);
                }
            }
            chosen.Flash();
            hud.SetTimer(chosen.Answer, "", chosen.Color);
            yield return new WaitForSecondsRealtime(0.35f);

            // STRIKE. Every occupied teleporter fires; destination is the vote winner.
            foreach (var p in pads) if (p.Occupants > 0) p.Strike();
            if (hum != null) hum.Stop();
            Music.Instance?.Cut();
            Sfx.Play(Sfx.I != null ? Sfx.I.thunder : null, 1f, 1f, 0.05f);
            hud.Flash(Color.white, 0.6f); // short, so the electrocution reads
            Juice.Shake(0.9f);
            Game.Cam?.FovPunch(-4f);
            Juice.HitStop(0.08f);
            foreach (var pl in Game.Players) if (pl != null) pl.Vaporize();
            hud.SetTimer("", "", Color.white);

            // a couple of seconds of silence while the smoke clears
            yield return new WaitForSecondsRealtime(2.2f);
            Game.Bootstrap.ArrivingByTeleport = true;
            switch (chosen.Destination)
            {
                case ReadyPad.Kind.Next: Game.Bootstrap.NextChamber(); break;
                case ReadyPad.Kind.Base: Game.Bootstrap.GoToBase(); break;
                case ReadyPad.Kind.Chamber: Game.Bootstrap.GoToChamber(chosen.TargetChamber); break;
                default: Game.Bootstrap.Restart(); break;
            }
        }

        /// <summary>Fun per-player notes for the scoreboard, Overcooked-style.</summary>
        System.Collections.Generic.List<(string, string, Color, string)> BuildAwards()
        {
            var list = new System.Collections.Generic.List<(string, string, Color, string)>();
            void Award(string title, System.Func<PlayerController, int> stat, string unit)
            {
                PlayerController best = null;
                int bestV = 0;
                foreach (var p in Game.Players)
                    if (p != null && stat(p) > bestV) { bestV = stat(p); best = p; }
                if (best != null) list.Add(($"{title}", $"P{best.Index + 1}", best.Color, $"({bestV} {unit})"));
            }
            Award("MOST PIPES LAID", p => p.Stats.pipesPlaced, "pipes");
            Award("MASTER THIEF", p => p.Stats.steals, "steals");
            Award("DEMOLITION EXPERT", p => p.Stats.unscrewed, "removed");
            Award("BUTTERFINGERS", p => p.Stats.drops, "dropped");
            Award("BUNNY HOPPER", p => p.Stats.jumps, "jumps");
            if (list.Count > 5) list.RemoveRange(5, list.Count - 5);
            return list;
        }
    }
}
