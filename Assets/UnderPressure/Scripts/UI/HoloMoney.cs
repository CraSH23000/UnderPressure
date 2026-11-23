using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// The top-left money readout, styled like the spout's hologram: EARNINGS (this round) and LAB FUNDING (the bank).
    ///
    /// Both count digit by digit with an NES-style score tick. When counting one at a time would take longer than
    /// moneyCountMaxSeconds, each step grows by moneyCountRamp, so big amounts speed up exponentially and the tick
    /// rises in pitch; the step resets once the counter catches up. On a win the earnings drain into LAB FUNDING.
    /// At the base there's no round, so only LAB FUNDING shows.
    /// </summary>
    public class HoloMoney : MonoBehaviour
    {
        static readonly Color Holo = new Color(0.45f, 1f, 0.95f);
        static readonly Color Gain = new Color(1f, 0.93f, 0.55f);
        static readonly Color Loss = new Color(1f, 0.38f, 0.32f);
        const float ColW = 340f, PanelH = 104f, Pad = 20f;
        const int MaxTicksPerFrame = 6;

        class Column
        {
            public RectTransform root, valueRect;
            public Text label, value;
            public long shown;
            public double step = 1;
            public float acc, flash, punch;
            public int dir = 1;
        }

        Column earn, fund;
        Column[] columns;
        RectTransform panel;
        Image bg, edge;
        Image[] scans;
        float flicker = 1f, glitch;
        GameManager lastManager;
        bool fundSnapped;
        readonly System.Random rng = new System.Random(23);

        public static HoloMoney Create(RectTransform parent)
        {
            var go = new GameObject("HoloMoney", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(40, -24);
            rt.sizeDelta = new Vector2(ColW * 2 + Pad, PanelH);
            var h = go.AddComponent<HoloMoney>();
            h.Build(rt);
            return h;
        }

        void Build(RectTransform rt)
        {
            panel = rt;
            bg = Child("BG", rt, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            var e = Child("Edge", rt, Vector2.zero, new Vector2(0, 1));
            e.sizeDelta = new Vector2(4, 0);
            edge = e.gameObject.AddComponent<Image>();
            scans = new Image[9];
            for (int i = 0; i < scans.Length; i++)
            {
                var line = Child("Scan", rt, new Vector2(0, (i + 0.5f) / scans.Length), new Vector2(1, (i + 0.5f) / scans.Length));
                line.sizeDelta = new Vector2(0, 2);
                scans[i] = line.gameObject.AddComponent<Image>();
            }
            earn = MakeColumn("Earnings", rt, "EARNINGS", 54);
            fund = MakeColumn("Funding", rt, "LAB FUNDING", 46);
            columns = new[] { earn, fund };
        }

        Column MakeColumn(string name, RectTransform parent, string label, int size)
        {
            var c = new Column();
            c.root = Child(name, parent, new Vector2(0, 0), new Vector2(0, 1));
            c.root.pivot = new Vector2(0, 0.5f);
            c.root.sizeDelta = new Vector2(ColW, 0);
            var l = Child("Label", c.root, new Vector2(0, 0.68f), new Vector2(1, 0.94f));
            c.label = Popup.MakeText(l.gameObject, label, 20, Holo, TextAnchor.MiddleLeft);
            c.valueRect = Child("Value", c.root, new Vector2(0, 0.04f), new Vector2(1, 0.7f));
            c.valueRect.pivot = new Vector2(0, 0.5f); // punches grow to the right, not off the left edge
            c.value = Popup.MakeText(c.valueRect.gameObject, "$0", size, Holo, TextAnchor.MiddleLeft);
            c.value.horizontalOverflow = HorizontalWrapMode.Overflow;
            return c;
        }

        static RectTransform Child(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = aMin; r.anchorMax = aMax;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            return r;
        }

        /// <summary>A money event: a quick glitch and bump on whichever column is showing the round's money.</summary>
        public void Punch(float amount)
        {
            glitch = Mathf.Max(glitch, amount);
            var c = earn.root.gameObject.activeSelf ? earn : fund;
            c.punch = Mathf.Max(c.punch, amount * 0.6f);
        }

        void Update()
        {
            var bs = Game.Bootstrap;
            var cfg = Game.Config;
            if (bs == null || cfg == null) return;
            bool paused = JuiceHost.Instance != null && JuiceHost.Instance.Paused;
            float dt = paused ? 0f : Time.unscaledDeltaTime;

            var gm = Game.Manager;
            bool inBase = bs.InBase || gm == null || gm.InLobby;
            bool won = gm != null && gm.Current == GameManager.State.Won;
            bool lost = gm != null && gm.Current == GameManager.State.Lost;
            long earnTarget = gm == null || won ? 0 : gm.Money; // on a win it drains into LAB FUNDING
            long fundTarget = bs.Bank;

            // a new room: snap instead of counting the last round's number away (and don't count the saved bank up from 0)
            if (gm != lastManager) { lastManager = gm; Snap(earn, earnTarget); }
            if (!fundSnapped) { fundSnapped = true; Snap(fund, fundTarget); }

            // layout: no EARNINGS at the base
            earn.root.gameObject.SetActive(!inBase);
            fund.root.anchoredPosition = new Vector2(inBase ? Pad : Pad + ColW, 0);
            panel.sizeDelta = new Vector2((inBase ? ColW : ColW * 2) + Pad, PanelH);
            earn.label.text = lost ? "FORFEITED" : "EARNINGS";

            int te = inBase ? 0 : Advance(earn, earnTarget, dt, cfg);
            int tf = Advance(fund, fundTarget, dt, cfg);
            // at most one tick per frame; during the win transfer the funding counter does the talking
            var speaker = tf > 0 ? fund : te > 0 ? earn : null;
            if (speaker != null && cfg.moneyTickSound)
            {
                float pitch = 1f + Mathf.Min(1.2f, 0.2f * Mathf.Log10((float)speaker.step));
                if (speaker.dir < 0) pitch *= 0.75f;
                Sfx.PlayTick(cfg.moneyTickVolume, pitch);
            }

            Animate(Time.unscaledDeltaTime, lost);
        }

        void Snap(Column c, long v)
        {
            c.shown = v;
            c.step = 1;
            c.acc = 0f;
            c.value.text = "$" + Cash.Hud(v);
        }

        int Advance(Column c, long target, float dt, GameConfig cfg)
        {
            if (c.shown == target) { c.step = 1; c.acc = 0f; return 0; }
            float tps = Mathf.Max(1f, cfg.moneyTicksPerSecond);
            c.acc += dt * tps;
            int ticks = 0;
            while (c.acc >= 1f && c.shown != target && ticks < MaxTicksPerFrame)
            {
                c.acc -= 1f;
                long rem = target - c.shown;
                int sign = rem > 0 ? 1 : -1;
                long abs = rem > 0 ? rem : -rem;
                // too far to count one at a time in time? speed up (exponentially, while it stays too far)
                if (abs / c.step > tps * Mathf.Max(0.1f, cfg.moneyCountMaxSeconds))
                    c.step = System.Math.Min(c.step * Mathf.Max(1.01f, cfg.moneyCountRamp), 1e18);
                long s = (long)System.Math.Max(1.0, c.step);
                if (s > abs) s = abs;
                c.shown += sign * s;
                c.dir = sign;
                ticks++;
            }
            if (ticks >= MaxTicksPerFrame) c.acc = 0f; // after a hitch, don't build a backlog
            if (ticks > 0)
            {
                c.value.text = "$" + Cash.Hud(c.shown);
                c.flash = 1f;
                c.punch = Mathf.Max(c.punch, 0.1f);
            }
            if (c.shown == target) { c.step = 1; c.acc = 0f; }
            return ticks;
        }

        void Animate(float dt, bool lost)
        {
            // the same instability as the spout's hologram: clean lab, clean projection
            float g = Theme.Grime;
            flicker = g > 0.05f && rng.NextDouble() < g * 0.03 ? Mathf.Lerp(0.85f, 0.4f, g) : Mathf.MoveTowards(flicker, 1f, dt * 6f);
            glitch = Mathf.MoveTowards(glitch, 0f, dt * 4f);
            float shimmer = Mathf.Lerp(0.03f, 0.12f, g);
            float a = (1f - shimmer + shimmer * Mathf.Sin(Time.unscaledTime * 20f)) * flicker;

            bg.color = new Color(Holo.r * 0.12f, Holo.g * 0.2f, Holo.b * 0.22f, 0.45f * flicker);
            edge.color = new Color(Holo.r, Holo.g, Holo.b, 0.8f * a);
            float scroll = Time.unscaledTime * 0.25f % 1f;
            for (int i = 0; i < scans.Length; i++)
            {
                float y = ((i + 0.5f) / scans.Length + scroll) % 1f;
                var r = scans[i].rectTransform;
                r.anchorMin = new Vector2(0, y); r.anchorMax = new Vector2(1, y);
                scans[i].color = new Color(Holo.r, Holo.g, Holo.b, 0.07f * flicker);
            }

            foreach (var c in columns)
            {
                c.flash = Mathf.MoveTowards(c.flash, 0f, dt * 3f);
                c.punch = Mathf.MoveTowards(c.punch, 0f, dt * 5f);
                Color baseCol = c == earn && lost ? Color.Lerp(Holo, Loss, 0.7f) : Holo;
                Color v = Color.Lerp(baseCol, c.dir > 0 ? Gain : Loss, c.flash * 0.8f);
                c.value.color = new Color(v.r, v.g, v.b, a * (c == earn && lost ? 0.6f : 1f));
                c.label.color = new Color(baseCol.r, baseCol.g, baseCol.b, a * 0.7f);
                c.valueRect.localScale = Vector3.one * (1f + c.punch * 0.25f);
                // a money hit makes the projection tear sideways for a moment
                float jitter = glitch > 0.05f ? (float)(rng.NextDouble() - 0.5) * glitch * 14f : 0f;
                c.valueRect.anchoredPosition = new Vector2(jitter, 0f);
            }
        }
    }
}
