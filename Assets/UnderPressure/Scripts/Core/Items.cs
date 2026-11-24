using UnityEngine;

namespace UnderPressure
{
    /// <summary>An item a player can carry in their inventory.</summary>
    public class ItemDef
    {
        public string id, name, description;
        public string effect;        // what it does, in plain words (shown under the flavour text in the details)
        public bool active;          // used with an item button (otherwise passive)
        public string passiveLabel;  // shown instead of a button for passive items, e.g. "ALWAYS ON", "ON DASH"
        public bool hasCharges;
        public string icon;          // Resources/Icons/<icon>.png
        // ---- the shop
        public int chamber;          // where its vending machine stands: 0 = the base, n = chamber n
        public string signText;      // short name on the machine's lit sign
        public Color signColor = new Color(0.25f, 0.6f, 1f);
        public System.Func<GameConfig, int> basePrice;   // price of the first one
        public System.Func<GameConfig, float> priceGrowth; // each upgrade costs this many times the last
        public System.Func<GameConfig, int> maxLevel;    // 1 = buy once; more = upgradeable
        public string requires;      // another item the team must own first (the funnel needs the bottle)
        public bool consumable;      // bought one at a time by whoever presses the button (the filter piece goes into their hands)
        public System.Func<GameConfig, int> everyChamberFrom; // sold in every chamber from this one on (besides its own)
        public System.Func<GameConfig, int> refillPrice; // once owned, its machine sells refills (everyone back to full charges)
    }

    /// <summary>
    /// Items and who has them, saved in PlayerPrefs. Shop items are bought once for the whole team (everyone gets one),
    /// then each player slot keeps its own charges and its own button bindings (3 item buttons).
    /// </summary>
    public static class Items
    {
        public const string WaterBottle = "bottle";
        public const string Backpack = "backpack";
        public const string CountAnt = "ant";
        public const string PirateFoot = "pegleg";
        public const string CrossingSign = "crossing";
        public const string Funnel = "funnel";
        public const string FilterPiece = "filter";
        public const string SpiderSerum = "serum";
        public const string Spoon = "spoon";
        public const int Slots = 3;

        public static readonly ItemDef[] All =
        {
            new ItemDef
            {
                id = Backpack, name = "BACKPACK", active = true, icon = "item_backpack",
                description = "Carry spare pieces on your back. Its button stows the piece in your hands, or takes one out. Grabbing a piece with full hands stows yours too, and placing one pops out the next.",
                chamber = 0, signText = "PACKS", signColor = new Color(0.95f, 0.55f, 0.15f),
                basePrice = c => c.backpackPrice, priceGrowth = c => c.backpackPriceGrowth, maxLevel = c => c.backpackMaxLevel,
            },
            new ItemDef
            {
                id = CountAnt, name = "A COUNT ANT", active = false, passiveLabel = "ALWAYS ON", icon = "item_ant",
                description = "A tiny accountant. Every dollar the team earns comes in a little higher.",
                chamber = 0, signText = "ANT", signColor = new Color(0.6f, 0.4f, 0.95f),
                basePrice = c => c.countAntPrice, priceGrowth = c => 1f, maxLevel = c => 1,
            },
            new ItemDef
            {
                id = PirateFoot, name = "LUCKY PIRATE'S FOOT", active = false, passiveLabel = "ALWAYS ON", icon = "item_pegleg",
                description = "It's just a peg leg. Raises the team's luck: every lucky break is a little more likely, and you'll see LUCKY! when it was the foot.",
                chamber = 0, signText = "LUCK", signColor = new Color(0.3f, 0.85f, 0.35f),
                basePrice = c => c.pirateFootPrice, priceGrowth = c => 1f, maxLevel = c => 1,
            },
            new ItemDef
            {
                id = SpiderSerum, name = "SPIDER MONKEY SERUM", active = true, hasCharges = true, icon = "item_serum",
                description = "Lab-grade monkey business. One dose lasts the whole chamber.",
                effect = "Climb and vault over pipes just by walking into them, and move along the top at full walking speed.",
                chamber = 0, signText = "SERUM", signColor = new Color(0.9f, 0.3f, 0.75f),
                basePrice = c => c.serumPrice, priceGrowth = c => 1f, maxLevel = c => 1, refillPrice = c => c.serumRefillPrice,
            },
            new ItemDef
            {
                id = Spoon, name = "SPOON", active = false, passiveLabel = "ON DASH", icon = "item_spoon",
                description = "Pipes aren't real, they can't hurt you.",
                effect = "Dash through pipes.",
                chamber = 0, signText = "SPOON", signColor = new Color(0.75f, 0.8f, 0.88f),
                basePrice = c => c.spoonPrice, priceGrowth = c => 1f, maxLevel = c => 1,
            },
            new ItemDef
            {
                id = WaterBottle, name = "WATER BOTTLE", active = true, hasCharges = true, icon = "item_bottle",
                description = "Walk faster and dash twice.",
                chamber = 1, signText = "H2O", signColor = new Color(0.25f, 0.6f, 1f),
                basePrice = c => c.waterBottlePrice, priceGrowth = c => 1f, maxLevel = c => 1,
            },
            new ItemDef
            {
                id = Funnel, name = "FUNNEL", active = false, passiveLabel = "ON SPILL", icon = "item_funnel", requires = WaterBottle,
                description = "Fits your water bottle. Stand in the leak at the end of a chamber to catch it: water refills your bottle; anything else contaminates it, with side effects when you drink.",
                chamber = 2, signText = "FUNNEL", signColor = new Color(0.95f, 0.75f, 0.2f),
                basePrice = c => c.funnelPrice, priceGrowth = c => 1f, maxLevel = c => 1,
            },
            new ItemDef
            {
                id = FilterPiece, name = "FILTER PIECE", active = false, consumable = true, icon = "item_filter",
                description = "A T-shaped pipe that splits whatever goes in: purified water out one side, the concentrate out the other.",
                chamber = -1, signText = "FILTER", signColor = new Color(0.3f, 0.85f, 0.95f),
                basePrice = c => c.filterPrice, priceGrowth = c => 1f, maxLevel = c => 1, everyChamberFrom = c => c.filterFromChamber,
            },
            new ItemDef
            {
                id = CrossingSign, name = "CROSSING SIGN", active = false, passiveLabel = "ALWAYS ON", icon = "item_crossing",
                description = "A crossing guard's sign. More crosses come down the belt, and each extra one arrives with the sign held over it.",
                chamber = 3, signText = "STOP", signColor = new Color(0.95f, 0.2f, 0.2f),
                basePrice = c => c.crossingSignPrice, priceGrowth = c => 1f, maxLevel = c => 1,
            },
        };

        // ------------------------------------------------------------------ team stats from passives

        /// <summary>Every dollar earned is multiplied by this (the Count Ant).</summary>
        public static float MoneyMultiplier => 1f + (TeamOwns(CountAnt) && Cfg != null ? Cfg.countAntBonus : 0f);

        /// <summary>Extra luck: every lucky-break chance is multiplied by (1 + Luck). The pirate's foot.</summary>
        public static float Luck => TeamOwns(PirateFoot) && Cfg != null ? Cfg.pirateFootLuck : 0f;

        /// <summary>Roll a lucky break with base chance 'chance' (boosted by Luck). 'lucky' = it only happened thanks to luck.</summary>
        public static bool Roll(float chance, out bool lucky)
        {
            float r = Random.value;
            bool ok = r < chance * (1f + Luck);
            lucky = ok && r >= chance;
            return ok;
        }

        /// <summary>The LUCKY! notification over something that only happened thanks to luck.</summary>
        public static void ShowLucky(Vector3 world)
        {
            Popup.Spawn(world, "LUCKY!", new Color(0.35f, 1f, 0.4f), 40, 1.2f);
            Fx.Burst(world, new Color(0.35f, 1f, 0.4f), new Color(1f, 0.9f, 0.3f), 14, 2.5f, 0.1f, 0.6f, -0.3f);
            Sfx.Play(Sfx.I != null ? Sfx.I.coin : null, 0.6f, 1.6f);
        }

        /// <summary>The item whose vending machine stands in this chamber (0 = the base), if any.</summary>
        public static ItemDef ForChamber(int chamber)
        {
            foreach (var d in All) if (d.chamber == chamber) return d;
            return null;
        }

        /// <summary>Every machine a chamber gets: its own item, then anything sold in every chamber from here on (the filter).</summary>
        public static System.Collections.Generic.List<ItemDef> StockFor(int chamber)
        {
            var l = new System.Collections.Generic.List<ItemDef>();
            var own = ForChamber(chamber);
            if (own != null) l.Add(own);
            foreach (var d in All)
                if (d.everyChamberFrom != null && Cfg != null && chamber >= d.everyChamberFrom(Cfg) && !l.Contains(d)) l.Add(d);
            return l;
        }

        public static ItemDef Get(string id)
        {
            foreach (var d in All) if (d.id == id) return d;
            return null;
        }

        static GameConfig Cfg => Game.Config;

        // ------------------------------------------------------------------ ownership (team) and charges (per player)

        /// <summary>How many the team has bought: 0 = none, 1 = owned, more = upgraded (backpack).</summary>
        public static int Level(string id)
        {
            int lvl = PlayerPrefs.GetInt("up_own_" + id, 0);
            if (Cfg != null && Cfg.testOwnAllItems)
            {
                var d = Get(id);
                if (d != null && !d.consumable) lvl = Mathf.Max(1, lvl); // testing: own everything
            }
            return lvl;
        }

        public static bool TeamOwns(string id) => Level(id) >= 1;

        public static int MaxLevel(string id)
        {
            var d = Get(id);
            return d != null && d.maxLevel != null && Cfg != null ? Mathf.Max(1, d.maxLevel(Cfg)) : 1;
        }

        public static bool Maxed(string id) => Level(id) >= MaxLevel(id);

        /// <summary>What the next one costs: the base price, times the growth for every one already bought.</summary>
        public static long NextPrice(string id)
        {
            var d = Get(id);
            if (d == null || Cfg == null || d.basePrice == null) return 0;
            double p = d.basePrice(Cfg) * System.Math.Pow(d.priceGrowth != null ? d.priceGrowth(Cfg) : 1f, Level(id));
            return (long)System.Math.Min(p, long.MaxValue / 2);
        }

        public static bool Has(int slot, string id) { var d = Get(id); return d != null && !d.consumable && TeamOwns(id); }

        public static int Charges(int slot, string id) => PlayerPrefs.GetInt($"up_charges{slot}_{id}", 0);
        public static void SetCharges(int slot, string id, int n) { PlayerPrefs.SetInt($"up_charges{slot}_{id}", Mathf.Max(0, n)); PlayerPrefs.Save(); }

        /// <summary>What's in this slot's bottle: "" = clean water, else the name of the liquid the funnel caught.</summary>
        public static string BottleMix(int slot) => PlayerPrefs.GetString($"up_bottlemix{slot}", "");
        public static void SetBottleMix(int slot, string mix) { PlayerPrefs.SetString($"up_bottlemix{slot}", mix ?? ""); PlayerPrefs.Save(); }

        public static int MaxCharges(string id)
        {
            if (Cfg == null) return 1;
            if (id == WaterBottle) return Mathf.Max(1, Cfg.waterBottleCharges);
            if (id == SpiderSerum) return Mathf.Max(1, Cfg.serumCharges);
            return 1;
        }

        /// <summary>Would a refill do anything? (Someone in the room is short of full charges.)</summary>
        public static bool NeedsRefill(string id)
        {
            foreach (var pl in Game.Players)
                if (pl != null && Charges(pl.Index, id) < MaxCharges(id)) return true;
            return false;
        }

        /// <summary>A refill from the machine: every player slot back to full charges.</summary>
        public static void RefillForTeam(string id)
        {
            for (int s = 0; s < JoinManager.MaxPlayers; s++) SetCharges(s, id, MaxCharges(id));
        }

        /// <summary>The team bought it: every player slot gets a full one, bound to their first free item button.</summary>
        public static void BuyForTeam(string id)
        {
            int lvl = Level(id) + 1;
            PlayerPrefs.SetInt("up_own_" + id, lvl);
            var def = Get(id);
            if (lvl > 1 || def == null || !def.active) { PlayerPrefs.Save(); return; } // upgrades and passives: nothing to hand out
            for (int s = 0; s < JoinManager.MaxPlayers; s++)
            {
                SetCharges(s, id, MaxCharges(id));
                var b = Bindings(s);
                if (System.Array.IndexOf(b, id) < 0)
                    for (int i = 0; i < Slots; i++)
                        if (string.IsNullOrEmpty(b[i])) { b[i] = id; SaveBindings(s, b); break; }
            }
            PlayerPrefs.Save();
        }

        /// <summary>Any active item this slot has but hasn't got on a button yet goes on the first free one
        /// (items bought before they had a button, or a button freed up later).</summary>
        public static void EnsureBound(int slot)
        {
            var b = Bindings(slot);
            bool changed = false;
            foreach (var d in All)
            {
                if (!d.active || !Has(slot, d.id) || System.Array.IndexOf(b, d.id) >= 0) continue;
                for (int i = 0; i < Slots; i++)
                    if (string.IsNullOrEmpty(b[i])) { b[i] = d.id; changed = true; break; }
            }
            if (changed) { SaveBindings(slot, b); PlayerPrefs.Save(); }
        }

        /// <summary>Items this player has, in catalogue order.</summary>
        public static System.Collections.Generic.List<ItemDef> Owned(int slot)
        {
            var l = new System.Collections.Generic.List<ItemDef>();
            foreach (var d in All) if (Has(slot, d.id)) l.Add(d);
            return l;
        }

        // ------------------------------------------------------------------ button bindings (per player)

        public static string[] Bindings(int slot)
        {
            var raw = PlayerPrefs.GetString("up_bind" + slot, "");
            var parts = raw.Split(',');
            var b = new string[Slots];
            for (int i = 0; i < Slots; i++) b[i] = i < parts.Length ? parts[i] : "";
            return b;
        }

        static void SaveBindings(int slot, string[] b)
        {
            PlayerPrefs.SetString("up_bind" + slot, string.Join(",", b));
        }

        /// <summary>Put an item on a button. If it was on another button, the two swap.</summary>
        public static void Bind(int slot, string id, int button)
        {
            var b = Bindings(slot);
            int was = System.Array.IndexOf(b, id);
            if (was == button) return;
            if (was >= 0) b[was] = b[button];
            b[button] = id;
            SaveBindings(slot, b);
            PlayerPrefs.Save();
        }

        public static int ButtonOf(int slot, string id) => System.Array.IndexOf(Bindings(slot), id);

        /// <summary>Testing: forget everything bought.</summary>
        public static void ResetAll()
        {
            foreach (var d in All)
            {
                PlayerPrefs.DeleteKey("up_own_" + d.id);
                for (int s = 0; s < JoinManager.MaxPlayers; s++) PlayerPrefs.DeleteKey($"up_charges{s}_{d.id}");
                for (int s = 0; s < JoinManager.MaxPlayers; s++) PlayerPrefs.DeleteKey($"up_bottlemix{s}");
            }
            for (int s = 0; s < JoinManager.MaxPlayers; s++) PlayerPrefs.DeleteKey("up_bind" + s);
            PlayerPrefs.Save();
        }
    }
}
