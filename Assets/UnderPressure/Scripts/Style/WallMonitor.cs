using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A big lab monitor on the back wall (world-space UI). Idles with a "monitoring subjects" screen during play
    /// and becomes the scoreboard at the end of a chamber.
    /// </summary>
    public class WallMonitor : MonoBehaviour
    {
        const float PxW = 1600f, PxH = 820f;

        public struct Results
        {
            public bool won;
            public int chamber, distance, goal, loops, wasted, wastedCost;
            public long earned, bank;
            public string bonusLine;
            public List<(string title, string who, Color color, string value)> awards;
            public List<Progress.Unlocked> unlocks;
        }

        RectTransform canvasRect;
        Text title, left, right, footer, status;
        Image screen, statusDot;
        float blinkT;
        public Vector3 Center => transform.position;

        public static WallMonitor Create(Transform parent, Rect wallRect, float wallZ)
        {
            var go = new GameObject("WallMonitor");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(wallRect.center.x, wallRect.center.y, wallZ - 0.12f);
            var m = go.AddComponent<WallMonitor>();
            m.Build(wallRect.width, wallRect.height);
            return m;
        }

        Transform frame;

        /// <summary>The room grew (the back wall's space for the monitor changed): move and resize it, text and all.</summary>
        public void Relayout(Rect wallRect, float wallZ)
        {
            transform.position = new Vector3(wallRect.center.x, wallRect.center.y, wallZ - 0.12f);
            BuildFrame(wallRect.width, wallRect.height);
            canvasRect.localScale = Vector3.one * Mathf.Min(wallRect.width / PxW, wallRect.height / PxH);
        }

        void BuildFrame(float w, float h)
        {
            if (frame != null) Destroy(frame.gameObject);
            frame = new GameObject("Frame").transform;
            frame.SetParent(transform, false);
            var bezel = Mats.Lit(Theme.WallTrim, 0.6f, 0.5f);
            Prims.Make(PrimitiveType.Cube, frame, new Vector3(0, 0, 0.05f), new Vector3(w + 0.3f, h + 0.3f, 0.2f), bezel, null, "Bezel");
            // little mount arms into the wall
            for (int s = -1; s <= 1; s += 2)
                Prims.Make(PrimitiveType.Cube, frame, new Vector3(s * w * 0.35f, h * 0.5f + 0.25f, 0.1f), new Vector3(0.12f, 0.4f, 0.12f), bezel);
        }

        void Build(float w, float h)
        {
            BuildFrame(w, h);

            var cgo = new GameObject("Screen", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            cgo.transform.localPosition = new Vector3(0, 0, -0.06f);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            cgo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            canvasRect = (RectTransform)cgo.transform;
            canvasRect.sizeDelta = new Vector2(PxW, PxH);
            float scale = Mathf.Min(w / PxW, h / PxH);
            canvasRect.localScale = Vector3.one * scale;

            screen = Rect("BG", canvasRect, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            screen.color = new Color(0.02f, 0.06f, 0.07f, 1f);
            // scanline stripes for a CRT-ish lab look
            for (int i = 0; i < 40; i++)
            {
                var line = Rect("Scan", canvasRect, new Vector2(0, i / 40f), new Vector2(1, i / 40f));
                line.sizeDelta = new Vector2(0, 3);
                line.gameObject.AddComponent<Image>().color = new Color(0.3f, 1f, 0.8f, 0.035f);
            }

            title = Popup.MakeText(Rect("Title", canvasRect, new Vector2(0, 0.82f), new Vector2(1, 0.98f)).gameObject, "", 84, Color.white);
            left = Popup.MakeText(Rect("Left", canvasRect, new Vector2(0.04f, 0.14f), new Vector2(0.5f, 0.8f)).gameObject, "", 46, Color.white, TextAnchor.UpperLeft);
            right = Popup.MakeText(Rect("Right", canvasRect, new Vector2(0.52f, 0.14f), new Vector2(0.97f, 0.8f)).gameObject, "", 42, Color.white, TextAnchor.UpperLeft);
            footer = Popup.MakeText(Rect("Footer", canvasRect, new Vector2(0, 0.01f), new Vector2(1, 0.13f)).gameObject, "", 44, Mats.Money);
            foreach (var t in new[] { title, left, right, footer }) { t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; }

            var st = Rect("Status", canvasRect, new Vector2(0.03f, 0.88f), new Vector2(0.06f, 0.95f));
            statusDot = st.gameObject.AddComponent<Image>();
            statusDot.sprite = Mats.MakeSprite(Mats.CircleTexture);
            statusDot.color = Mats.Danger;
            status = Popup.MakeText(Rect("StatusText", canvasRect, new Vector2(0.07f, 0.86f), new Vector2(0.5f, 0.97f)).gameObject, "REC", 36, Mats.Danger, TextAnchor.MiddleLeft);
            ShowLive();
        }

        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return rt;
        }

        public void ShowLive()
        {
            title.text = $"TEST CHAMBER {Game.Bootstrap.ChamberNumber:00}";
            title.color = new Color(0.6f, 1f, 0.9f);
            left.text = "SUBJECTS: <color=#9EF>MONITORED</color>\nCOMPLIANCE: <color=#9EF>EXPECTED</color>\nESCAPE: <color=#F66>IMPOSSIBLE</color>";
            right.text = "";
            footer.text = $"LAB FUNDING  ${Cash.Hud(Game.Bootstrap.Bank)}";
            status.text = "REC";
        }

        public bool ShowingResults { get; private set; }
        Results lastResults;

        /// <summary>An unlock that happened while the results are up: it goes to the top of the performance notes.</summary>
        public void AddUnlock(Progress.Unlocked u)
        {
            if (!ShowingResults) return;
            if (lastResults.unlocks == null) lastResults.unlocks = new System.Collections.Generic.List<Progress.Unlocked>();
            lastResults.unlocks.Add(u);
            ShowResults(lastResults);
            Sfx.Play(Sfx.I != null ? Sfx.I.coin : null, 0.5f, 1.3f);
        }

        public void ShowResults(Results r)
        {
            ShowingResults = true;
            lastResults = r;
            title.text = r.won ? $"CHAMBER {r.chamber:00}  <color=#8F8>PASSED</color>" : $"CHAMBER {r.chamber:00}  <color=#F55>FAILED</color>";
            title.color = Color.white;
            string waste = r.wasted > 0 ? $"\nWasted pieces  {r.wasted}  <color=#F66>(-${r.wastedCost:N0})</color>" : "";
            left.text =
                $"Distance  {r.distance} / {r.goal}\n" +
                $"Loops  {r.loops}{waste}\n" +
                $"<color=#FD4>Earned  ${r.earned:N0}</color>" +
                (string.IsNullOrEmpty(r.bonusLine) ? "" : $"\n<size=34>{r.bonusLine}</size>");
            var sb = new System.Text.StringBuilder("<size=36><color=#9EF>PERFORMANCE NOTES</color></size>\n");
            int shown = 0;
            if (r.unlocks != null) // unlocks always come first
                foreach (var u in r.unlocks)
                {
                    string who = u.slot >= 0 ? $"<color=#{ColorUtility.ToHtmlStringRGB(u.color)}>P{u.slot + 1}</color>" : "<color=#FFD640>TEAM</color>";
                    sb.Append($"<color=#FFD640>NEW!</color>  {who}  unlocked {u.name}  <size=30>({u.where})</size>\n");
                    shown++;
                }
            if ((r.awards == null || r.awards.Count == 0) && shown == 0) sb.Append("<size=34>No notable behaviour.</size>");
            else if (r.awards != null)
                foreach (var a in r.awards)
                {
                    if (shown >= 6) break;
                    sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(a.color)}>{a.who}</color>  {a.title}  <size=32>{a.value}</size>\n");
                    shown++;
                }
            right.text = sb.ToString();
            footer.text = r.won
                ? $"LAB FUNDING  ${Cash.Hud(r.bank)}   ·   stand on a teleporter"
                : $"LAB FUNDING  ${Cash.Hud(r.bank)}   ·   retry: stand on the teleporter";
            status.text = "REVIEW";
        }

        public void ShowCountdown(int seconds, string detail)
        {
            footer.text = $"TELEPORTING IN {seconds}   ·   {detail}";
        }

        public void ShowFooter(string text) => footer.text = text;

        /// <summary>Live status board during a round (keeps the HUD free).</summary>
        public void SetLive(string chamberLine, string leftText, string rightText, string footerText)
        {
            ShowingResults = false;
            title.text = chamberLine;
            title.color = new Color(0.6f, 1f, 0.9f);
            left.text = leftText;
            right.text = rightText;
            footer.text = footerText;
            status.text = "LIVE";
        }

        void Update()
        {
            blinkT += Time.unscaledDeltaTime;
            statusDot.enabled = (blinkT % 1f) < 0.6f;
            // a subtle flicker that worsens with grime
            float g = Theme.Grime;
            float flicker = g > 0.1f && Random.value < g * 0.02f ? 0.6f : 1f;
            screen.color = new Color(0.02f * flicker, 0.06f * flicker, 0.07f * flicker, 1f);
        }
    }
}
