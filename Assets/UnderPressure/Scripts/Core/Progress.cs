using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Lifetime stats and secret cosmetic unlocks, saved in PlayerPrefs. Personal stats belong to a player slot
    /// (P1..P4); a few are shared by the whole team. Unlocked items appear in the closets; locked ones are skipped.
    /// </summary>
    public static class Progress
    {
        // personal stats
        public const string Sprayed = "sprayed";       // stood in the spray when clean water leaked out
        public const string WaterHops = "waterhops";   // hopped over a pipe full of clean water
        // team stats
        public const string TeamCleanPipes = "cleanpipes"; // pipes filled with clean water, all-time
        public const string TeamPipeBursts = "pipebursts"; // two streams met head-on and burst a pipe (PIPE BURSTER, later)

        struct Lock
        {
            public Part part; public int index; public string stat; public bool team; public string name, where;
            public ClosetKind closet;
            public System.Func<GameConfig, int> need;
        }

        static readonly Lock[] Locks =
        {
            new Lock { part = Part.Hat, index = 4, stat = Sprayed, name = "SHOWER CAP", where = "YOUR LOCKER", closet = ClosetKind.Wardrobe, need = c => c.unlockShowerCapSprays },
            new Lock { part = Part.Legs, index = 1, stat = Sprayed, name = "SWIM TRUNKS", where = "YOUR LOCKER", closet = ClosetKind.Wardrobe, need = c => c.unlockSwimTrunksSprays },
            new Lock { part = Part.Arms, index = 1, stat = WaterHops, name = "ARM FLOATIES", where = "YOUR LOCKER", closet = ClosetKind.Wardrobe, need = c => c.unlockFloatiesWaterHops },
            new Lock { part = Part.Species, index = 3, stat = TeamCleanPipes, team = true, name = "SHARC", where = "YOUR LOCKER", closet = ClosetKind.BioReactor, need = c => c.unlockSharcCleanPipes },
        };

        static GameConfig Cfg => Game.Config != null ? Game.Config : DefaultCfg;
        static GameConfig defaultCfg;
        static GameConfig DefaultCfg => defaultCfg != null ? defaultCfg : (defaultCfg = ScriptableObject.CreateInstance<GameConfig>());

        static string Key(int slot, string stat) => $"up_stat{slot}_{stat}";
        static string NewKey(int slot, int lockIndex) => $"up_new{slot}_{lockIndex}";

        /// <summary>An unlock that happened this round (shown on the wall monitor's performance notes).</summary>
        public struct Unlocked { public int slot; public Color color; public string name, where; }
        static readonly System.Collections.Generic.List<Unlocked> pending = new System.Collections.Generic.List<Unlocked>();

        /// <summary>Unlocks since the last time the results board asked (it lists them first).</summary>
        public static System.Collections.Generic.List<Unlocked> TakePending()
        {
            var l = new System.Collections.Generic.List<Unlocked>(pending);
            pending.Clear();
            return l;
        }

        // ------------------------------------------------------------------ "NEW" (unlocked but not looked at yet)

        public static bool IsNew(int slot, Part part, int index)
        {
            for (int i = 0; i < Locks.Length; i++)
                if (Locks[i].part == part && Locks[i].index == index) return PlayerPrefs.GetInt(NewKey(slot, i), 0) == 1;
            return false;
        }

        /// <summary>Does this row (part) have anything new and unseen for this player? (The locker puts a NEW! sticker on it.)</summary>
        public static bool AnyNew(int slot, Part part)
        {
            for (int i = 0; i < Locks.Length; i++)
                if (Locks[i].part == part && PlayerPrefs.GetInt(NewKey(slot, i), 0) == 1 && Met(Locks[i], slot)) return true;
            return false;
        }

        /// <summary>Names of this player's new, unseen items that live in this kind of closet.</summary>
        public static System.Collections.Generic.List<string> NewIn(int slot, ClosetKind kind)
        {
            var l = new System.Collections.Generic.List<string>();
            for (int i = 0; i < Locks.Length; i++)
                if (Locks[i].closet == kind && PlayerPrefs.GetInt(NewKey(slot, i), 0) == 1 && Met(Locks[i], slot)) l.Add(Locks[i].name);
            return l;
        }

        /// <summary>The player has been inside that closet now, so its new items aren't new any more.</summary>
        public static void MarkSeen(int slot, ClosetKind kind)
        {
            for (int i = 0; i < Locks.Length; i++)
                if (Locks[i].closet == kind) PlayerPrefs.DeleteKey(NewKey(slot, i));
            PlayerPrefs.Save();
        }
        static string TeamKey(string stat) => $"up_team_{stat}";

        public static int Stat(int slot, string stat) => PlayerPrefs.GetInt(Key(slot, stat), 0);
        public static int Team(string stat) => PlayerPrefs.GetInt(TeamKey(stat), 0);

        /// <summary>True for this chamber's liquid if it's water (the early, clean chambers).</summary>
        public static bool LiquidIsWater => Mats.LiquidName != null && Mats.LiquidName.ToUpperInvariant().Contains("WATER");

        static bool Met(Lock l, int slot) => (l.team ? Team(l.stat) : Stat(slot, l.stat)) >= Mathf.Max(0, l.need(Cfg));

        public static bool IsUnlocked(int slot, Part part, int index)
        {
            if (Cfg.unlockAllCosmetics) return true;
            foreach (var l in Locks)
                if (l.part == part && l.index == index) return Met(l, slot);
            return true;
        }

        /// <summary>Does this player have anything besides "NONE" for this part? (hides empty secret rows)</summary>
        public static bool AnyUnlocked(int slot, Part part)
        {
            var names = CritterLook.Names(part);
            for (int i = 1; i < names.Length; i++) if (IsUnlocked(slot, part, i)) return true;
            return false;
        }

        /// <summary>Progress toward every lock for one player (debugging / a future stats screen).</summary>
        public static string Describe(int slot)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var l in Locks)
            {
                int have = l.team ? Team(l.stat) : Stat(slot, l.stat);
                sb.Append($"{l.name}: {Mathf.Min(have, l.need(Cfg))}/{l.need(Cfg)}{(Met(l, slot) ? " (UNLOCKED)" : "")}\n");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ counting

        public static void Add(PlayerController p, string stat, int amount = 1)
        {
            if (p == null) return;
            bool[] before = Snapshot(p.Index);
            PlayerPrefs.SetInt(Key(p.Index, stat), Stat(p.Index, stat) + amount);
            Announce(p, before);
        }

        public static void AddTeam(string stat, int amount = 1)
        {
            bool[][] before = new bool[JoinManager.MaxPlayers][];
            for (int i = 0; i < before.Length; i++) before[i] = Snapshot(i);
            PlayerPrefs.SetInt(TeamKey(stat), Team(stat) + amount);
            bool announced = false;
            foreach (var p in Game.Players)
            {
                if (p == null) continue;
                // team unlocks: one banner for everybody, then a confetti pop on each critter
                for (int i = 0; i < Locks.Length; i++)
                {
                    if (!Locks[i].team || before[p.Index][i] || !Met(Locks[i], p.Index)) continue;
                    PlayerPrefs.SetInt(NewKey(p.Index, i), 1);
                    if (!announced) { announced = true; Celebrate(null, Locks[i]); }
                    Fx.Confetti(p.transform.position + Vector3.up, 24, 5f);
                    p.Cheer();
                }
            }
        }

        static bool[] Snapshot(int slot)
        {
            var b = new bool[Locks.Length];
            for (int i = 0; i < Locks.Length; i++) b[i] = Met(Locks[i], slot);
            return b;
        }

        static void Announce(PlayerController p, bool[] before)
        {
            for (int i = 0; i < Locks.Length; i++)
                if (!before[i] && Met(Locks[i], p.Index))
                {
                    PlayerPrefs.SetInt(NewKey(p.Index, i), 1);
                    Celebrate(p, Locks[i]);
                }
        }

        static void Celebrate(PlayerController p, Lock l)
        {
            PlayerPrefs.Save();
            if (Cfg.unlockAllCosmetics) return;
            Color c = p != null ? p.Color : Mats.Money;
            string who = p != null ? $"P{p.Index + 1} " : "";
            Banner.Show($"{who}UNLOCKED: {l.name}!", c, 84, 3.2f, 60f);
            Juice.Delay(0.35f, () => Banner.Show($"find it in the {l.where}", Color.white, 40, 3f, -10f));
            var u = new Unlocked { slot = p != null ? p.Index : -1, color = c, name = l.name, where = l.where };
            // the results board lists it first; if it's already up, it gets added there right away
            if (Game.Monitor != null && Game.Monitor.ShowingResults) Game.Monitor.AddUnlock(u);
            else pending.Add(u);
            Sfx.Play(Sfx.I != null ? Sfx.I.fanfare : null, 0.8f, 1.1f, 0f);
            if (p != null)
            {
                Fx.Confetti(p.transform.position + Vector3.up, 50, 7f);
                p.Cheer();
            }
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>A pipe filled up. Clean water counts toward the team's SHARC.</summary>
        public static void OnPieceFilled(PipePiece piece)
        {
            if (LiquidIsWater) AddTeam(TeamCleanPipes);
        }

        /// <summary>The leak: anyone standing in the spray of clean water gets drenched (and counted).</summary>
        public static void OnSpill(Vector3 pos, Vector3 dir)
        {
            PlayerPrefs.Save();
            if (!LiquidIsWater) return;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
            foreach (var p in Game.Players)
            {
                if (p == null || !InSpray(p, pos, dir)) continue;
                p.GetSprayed(dir);
                Add(p, Sprayed);
            }
        }

        /// <summary>Is this critter standing in the leak's spray (a cone 'sprayReach' long from pos along dir)?</summary>
        public static bool InSpray(PlayerController p, Vector3 pos, Vector3 dir)
        {
            if (p == null || !p.CanBeSprayed || Cfg == null) return false;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
            Vector3 d = p.transform.position - pos;
            d.y = 0f;
            float along = Vector3.Dot(d, dir);
            if (along < -0.4f || along > Cfg.sprayReach) return false;
            float side = (d - dir * along).magnitude;
            return side <= 0.55f + Mathf.Max(0f, along) * 0.3f; // the spray fans out a little
        }

        /// <summary>Wipe all saved stats and unlocks (looks are kept, minus anything now locked).</summary>
        const string FurthestKey = "up_furthest";

        /// <summary>The deepest chamber the team has reached (the base gets as grimy as this, and you can jump back to it).</summary>
        public static int Furthest => Mathf.Max(1, PlayerPrefs.GetInt(FurthestKey, 1));

        public static void ReachedChamber(int n)
        {
            if (n <= Furthest) return;
            PlayerPrefs.SetInt(FurthestKey, n);
            PlayerPrefs.Save();
        }

        const string BankKey = "up_bank";
        static long? bank;

        /// <summary>
        /// LAB FUNDING, the team's shared bank. Saved between sessions (as a string: PlayerPrefs has no long).
        /// Every change is written straight away, so a crash or quit never loses money.
        /// </summary>
        public static long Bank
        {
            get
            {
                if (bank == null) bank = long.TryParse(PlayerPrefs.GetString(BankKey, "0"), out var v) ? v : 0L;
                return bank.Value;
            }
            set
            {
                if (bank == value) return;
                bank = value;
                PlayerPrefs.SetString(BankKey, value.ToString());
                PlayerPrefs.Save();
            }
        }

        public static void ResetAll()
        {
            for (int slot = 0; slot < JoinManager.MaxPlayers; slot++)
            {
                PlayerPrefs.DeleteKey(Key(slot, Sprayed));
                PlayerPrefs.DeleteKey(Key(slot, WaterHops));
                for (int i = 0; i < Locks.Length; i++) PlayerPrefs.DeleteKey(NewKey(slot, i));
            }
            PlayerPrefs.DeleteKey(TeamKey(TeamCleanPipes));
            PlayerPrefs.DeleteKey(FurthestKey);
            PlayerPrefs.DeleteKey(BankKey);
            bank = null;
            Items.ResetAll(); // bought items too (testing)
            PlayerPrefs.Save();
        }
    }
}
