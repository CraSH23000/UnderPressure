namespace UnderPressure
{
    /// <summary>
    /// Money formatting, shared by the HUD, the wall monitor and the vending machines.
    /// Money is a long (up to $9.2 quintillion), so short forms run K, M, B, T, Qa, Qi.
    /// </summary>
    public static class Cash
    {
        static readonly string[] Suffix = { "K", "M", "B", "T", "Qa", "Qi" };

        /// <summary>$500, $12.5K, $1.6M, $977M, $4.9B: fits small displays like the vending machine price.</summary>
        public static string Short(long v)
        {
            if (v < 0) return "-" + Short(-v);
            if (v < 10000) return v.ToString("N0");
            return Abbrev(v, "0.#");
        }

        /// <summary>
        /// For the HUD counter: every digit up to $999,999,999 (so the count-up reads digit by digit),
        /// then $1.25B, $3.40T … so it never outgrows its panel.
        /// </summary>
        public static string Hud(long v)
        {
            if (v < 0) return "-" + Hud(-v);
            if (v < 1000000000L) return v.ToString("N0");
            return Abbrev(v, "0.00");
        }

        static string Abbrev(long v, string fmt)
        {
            double d = v;
            int i = -1;
            while (d >= 1000.0 && i < Suffix.Length - 1) { d /= 1000.0; i++; }
            return d.ToString(fmt) + Suffix[i];
        }
    }
}
