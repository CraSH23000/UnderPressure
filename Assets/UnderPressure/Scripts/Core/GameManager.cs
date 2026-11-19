using UnityEngine;
using UnityEngine.InputSystem;

namespace UnderPressure
{
    /// <summary>Countdown → flow → spill. Money, distance goal, loop multiplier, and the big celebratory moments.</summary>
    public class GameManager : MonoBehaviour
    {
        public enum State { Countdown, Flowing, Won, Lost }

        public State Current { get; private set; } = State.Countdown;
        public bool IsOver => Current == State.Won || Current == State.Lost;
        public int Money { get; private set; }
        public int Distance { get; private set; }
        public int Loops { get; private set; }
        public float CountdownRemaining => countdown;
        /// <summary>The liquid is about to leak: panic music, warning HUD.</summary>
        public bool Panic { get; private set; }

        GameConfig cfg;
        GridBoard board;
        FlowController flow;
        Hud hud;
        float countdown, displayMoney, endTime;
        int lastWholeSecond = -1;
        bool goalHit;
        float beltSpeedIntegral, beltTime; // time-weighted belt speed, for the end bonus
        float flowSpeedIntegral, flowTime; // time-weighted flow speed while the liquid runs
        public int WastedPieces { get; private set; }
        int wastedCost;

        // intermission (between chambers)
        bool intermission, teleporting;
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

            hud.SetMoney(0);
            hud.SetDistance(0, cfg.goalDistance);
        }

        void OnDestroy()
        {
            if (hum != null) Destroy(hum);
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
            if (cost <= 0) return;
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
            float udt = Time.unscaledDeltaTime;
            if (Paused) return; // the pause menu (PauseMenu, on the HUD) owns pausing now
            // rolling money counter
            float diff = Money - displayMoney;
            if (Mathf.Abs(diff) > 0.01f)
            {
                displayMoney = Mathf.MoveTowards(displayMoney, Money, Mathf.Max(40f, Mathf.Abs(diff) * 5f) * udt);
                hud.SetMoney(Mathf.RoundToInt(displayMoney));
            }

            int joined = Game.Players.Count;
            hud.SetJoinPrompt(joined >= JoinManager.MaxPlayers || joined == 0 ? "" : $"P{joined + 1} join: (A) · Space · Enter");

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
                        int left = flow.PiecesAhead;
                        holo.Set(left == 0 ? "LEAK!" : $"LEAK IN {left}!", "lay pipe ahead!", Distance, cfg.goalDistance, true);
                    }
                    else
                        holo.Set("FLOWING", $"flow x{flow.SpeedMultiplier:0.##}" + (cfg.moneyScalesWithFlowSpeed ? $"  ·  ${PieceValue()}/pipe" : ""),
                            Distance, cfg.goalDistance, false);
                    UpdateLiveMonitor();
                    break;
                default:
                    if (intermission) UpdateTeleporters();
                    break;
            }
        }

        void UpdatePanic()
        {
            bool panic = flow.PiecesAhead < Mathf.Max(1, cfg.panicPiecesAhead);
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
            board.Spout.Hologram.Set($"FLOW IN {whole}", $"flow x{flow.SpeedMultiplier:0.##}  ·  [Use] valve", Distance, cfg.goalDistance, urgent);
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
                ? $"LAB FUNDING  ${Game.Bootstrap.Bank:N0}   ·   new subjects: press (A) / Space / Enter"
                : $"LAB FUNDING  ${Game.Bootstrap.Bank:N0}";
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
            Progress.OnPieceFilled(p);
            int value = PieceValue();
            Money += value;
            Vector3 top = p.transform.position + Vector3.up * (p.TopHeight + 0.3f);
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
            int baseMoney = Money;
            var lines = new System.Collections.Generic.List<string>();
            int total = 0;

            if (beltTime > 0.5f)
            {
                float avg = beltSpeedIntegral / beltTime;
                float pct = (avg - 1f) * cfg.beltBonusPerExtraSpeed;
                if (pct < 0f && !cfg.beltSlowPenalty) pct = 0f;
                int bonus = Mathf.RoundToInt(baseMoney * pct);
                total += bonus;
                lines.Add(bonus == 0 ? $"Belt avg x{avg:0.##}" : $"Belt avg x{avg:0.##}  {(bonus > 0 ? "+" : "-")}{Mathf.Abs(pct) * 100f:0}%  ({(bonus > 0 ? "+" : "-")}${Mathf.Abs(bonus):N0})");
            }
            if (flowTime > 0.5f)
            {
                float avg = flowSpeedIntegral / flowTime;
                float pct = Mathf.Max(0f, (avg - 1f) * cfg.flowBonusPerExtraSpeed);
                int bonus = Mathf.RoundToInt(baseMoney * pct);
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
            int amount = cfg.loopBonus * count * (nested > 0 ? Mathf.Max(1, cfg.nestedLoopMultiplier) : 1);
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
            board.Spout.Hologram.Set(Current == State.Won ? "PASSED" : "SPILLED", "", Distance, cfg.goalDistance, Current != State.Won);
            Juice.SlowMo(0.25f, 0.9f);
            Juice.Shake(0.55f);
            Fx.Burst(pos, Mats.Liquid, new Color(0.7f, 1f, 0.75f), 45, 5.5f, 0.24f, 0.9f, 2.2f, true,
                ParticleSystemShapeType.Cone, 0.1f, Quaternion.LookRotation(dir + Vector3.up * 0.6f));
            Sfx.Play(Sfx.I != null ? Sfx.I.spill : null, 1f, 1f, 0f);
            Sfx.Play(Sfx.I != null ? Sfx.I.splat : null, 0.8f);

            // a wobbly blob of whatever was in the pipes
            SlimePuddle.Spawn(new Vector3(pos.x + dir.x * 0.7f, 0f, pos.z + dir.z * 0.7f), dir);

            Juice.Delay(0.6f, BeginIntermission);
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
            if (won)
            {
                pads.Add(ReadyPad.Create(Game.LevelRoot, bs.PadAnchor - Vector3.right * bs.PadSpacing, ReadyPad.Kind.Retry, $"RETRY  CHAMBER {bs.ChamberNumber:00}"));
                pads.Add(ReadyPad.Create(Game.LevelRoot, bs.PadAnchor + Vector3.right * bs.PadSpacing, ReadyPad.Kind.Next, $"NEXT:  CHAMBER {bs.ChamberNumber + 1:00}"));
            }
            else pads.Add(ReadyPad.Create(Game.LevelRoot, bs.PadAnchor, ReadyPad.Kind.Retry, $"RETRY  CHAMBER {bs.ChamberNumber:00}"));
            Sfx.Play(Sfx.I != null ? Sfx.I.whoosh : null, 0.6f, 1.3f, 0f);
            intermission = true;
        }

        void UpdateTeleporters()
        {
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
            string title = Current == State.Won ? "CHAMBER PASSED" : "TEST FAILED";

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
                hud.SetTimer(title, $"Everyone stand on a teleporter  ·  ready {ready}/{total}", Current == State.Won ? Mats.Liquid : Mats.Danger);
                if (Time.unscaledTime - endTime > 2.5f) Game.Monitor?.ShowFooter($"OFF THE CLOCK   ·   LAB FUNDING  ${Game.Bootstrap.Bank:N0}   ·   ready {ready}/{total}");
                return;
            }

            string odds = pads.Count > 1 ? $"next {pads[1].Occupants} : retry {pads[0].Occupants}" : "retry";
            int amps = Mathf.RoundToInt(charge * 9000f);
            hud.SetTimer(allReady ? $"CHARGING  {charge * 100f:0}%" : "DISCHARGING...", $"{amps:N0} AMPS  ·  {odds}", Color.Lerp(Color.white, new Color(0.6f, 0.8f, 1f), charge));
            Game.Monitor?.ShowFooter($"CAPACITOR {charge * 100f:0}%   ·   {amps:N0} A   ·   {odds}");
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
            bool next = chosen.Destination == ReadyPad.Kind.Next;

            // roulette between the occupied pads: the current jumps back and forth
            if (pads.Count > 1 && pads[0].Occupants > 0 && pads[1].Occupants > 0)
            {
                int flips = 7 + (chosen == pads[1] ? 1 : 0);
                for (int i = 0; i < flips; i++)
                {
                    var p = pads[i % 2];
                    p.Flash();
                    hud.SetTimer(p.Destination == ReadyPad.Kind.Next ? "NEXT CHAMBER?" : "RETRY?", "", Color.white);
                    yield return new WaitForSecondsRealtime(0.09f + i * 0.035f);
                }
            }
            chosen.Flash();
            hud.SetTimer(next ? "NEXT CHAMBER" : "RETRY", "", next ? Mats.Liquid : Theme.AccentOrange);
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
            if (next) Game.Bootstrap.NextChamber();
            else Game.Bootstrap.Restart();
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
