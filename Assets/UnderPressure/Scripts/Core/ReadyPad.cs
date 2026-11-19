using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// Government-funded teleporter chamber: riveted steel base, an arch of pipes with a downward emitter,
    /// twin Tesla coils and an amperage meter. Its plaque records the lab's history of rebranding:
    /// INCINERATOR (struck out) → CLONE REINTEGRATION (struck out) → TELEPORTER.
    /// Charge it by everyone standing on teleporters; at full charge, lightning.
    /// </summary>
    public class ReadyPad : MonoBehaviour
    {
        public enum Kind { Retry, Next }
        public Kind Destination { get; private set; }
        public float Radius { get; private set; } = 1.0f;
        public int Occupants { get; set; }

        const float ArchHalf = 1.35f, ArchHeight = 2.9f, CoilX = 2.05f, CoilTop = 1.95f;
        static readonly Color Red = new Color(0.85f, 0.08f, 0.08f);

        Color color;
        Material ringMat, emitterMat, arcMat, boltMat, coilTopMat;
        Renderer[] ampLamps;
        Material lampOff, lampLow, lampMid, lampHigh;
        LineRenderer[] arcs;
        LineRenderer bolt, boltBranch;
        Light flashLight;
        float charge, flash, rise, arcTimer, strikeT = -1f;
        bool spent; // after the strike the coils go dead
        readonly System.Random rng = new System.Random();

        public Vector3 EmitterPoint => transform.position + Vector3.up * (ArchHeight - 0.42f) + Vector3.forward * 0.4f;

        public static ReadyPad Create(Transform parent, Vector3 pos, Kind kind, string destinationLabel)
        {
            var go = new GameObject("Teleporter_" + kind);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var p = go.AddComponent<ReadyPad>();
            p.Destination = kind;
            p.color = kind == Kind.Next ? Mats.Liquid : Theme.AccentOrange;
            p.Build(destinationLabel);
            return p;
        }

        void Build(string destination)
        {
            var steel = Mats.Lit(new Color(0.42f, 0.45f, 0.5f), 0.75f, 0.85f);
            var darkSteel = Mats.Lit(new Color(0.16f, 0.17f, 0.2f), 0.6f, 0.8f);
            var copper = Mats.Lit(new Color(0.78f, 0.42f, 0.2f), 0.8f, 0.9f);
            var chrome = Mats.Lit(new Color(0.85f, 0.87f, 0.9f), 0.95f, 1f);
            var hazardY = Mats.Lit(Theme.Hazard, 0.4f);
            var ink = Mats.Lit(Theme.Ink, 0.5f, 0.4f);

            // ---- base: stepped riveted platform
            Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, 0.05f, 0), new Vector3(2.7f, 0.05f, 2.7f), darkSteel, null, "Step");
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                Prims.Make(PrimitiveType.Cube, transform, new Vector3(Mathf.Cos(a) * 1.3f, 0.1f, Mathf.Sin(a) * 1.3f), new Vector3(0.22f, 0.02f, 0.1f),
                    (i & 1) == 0 ? hazardY : ink, Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), "Hazard");
            }
            Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, 0.12f, 0), new Vector3(2.2f, 0.03f, 2.2f), steel, null, "Deck");
            ringMat = new Material(Mats.Lit(color, 0.6f, 0f, color * 0.6f));
            Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, 0.145f, 0), new Vector3(2.0f, 0.01f, 2.0f), ringMat, null, "Ring");
            Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, 0.15f, 0), new Vector3(1.75f, 0.012f, 1.75f), darkSteel, null, "Grate");
            for (int i = -3; i <= 3; i++) // grate bars
                Prims.Make(PrimitiveType.Cube, transform, new Vector3(i * 0.22f, 0.165f, 0), new Vector3(0.04f, 0.01f, 1.6f * Mathf.Sqrt(1f - (i * 0.22f / 0.88f) * (i * 0.22f / 0.88f))), steel);
            for (int i = 0; i < 8; i++) // rivets
            {
                float a = i / 8f * Mathf.PI * 2f + 0.2f;
                Prims.Make(PrimitiveType.Sphere, transform, new Vector3(Mathf.Cos(a) * 1.06f, 0.15f, Mathf.Sin(a) * 1.06f), Vector3.one * 0.07f, chrome);
            }

            // ---- arch: two pillars wrapped in pipes, a crossbar with the downward emitter
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * ArchHalf;
                Prims.Make(PrimitiveType.Cube, transform, new Vector3(x, 0.2f, 0.25f), new Vector3(0.5f, 0.4f, 0.5f), darkSteel, null, "PillarFoot");
                Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(x, ArchHeight * 0.5f, 0.25f), new Vector3(0.24f, ArchHeight * 0.5f, 0.24f), steel, null, "Pillar");
                // feed pipes running up the pillar
                Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(x + s * 0.2f, ArchHeight * 0.5f, 0.38f), new Vector3(0.1f, ArchHeight * 0.5f, 0.1f), copper, null, "Pipe");
                Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(x + s * 0.17f, ArchHeight * 0.45f, 0.08f), new Vector3(0.07f, ArchHeight * 0.45f, 0.07f), chrome, null, "Pipe");
                for (int k = 1; k <= 3; k++) // clamps
                    Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(x, k * 0.9f, 0.25f), new Vector3(0.3f, 0.04f, 0.3f), darkSteel, null, "Band");
                var col = new GameObject("PillarCollider");
                col.transform.SetParent(transform, false);
                col.transform.localPosition = new Vector3(x, 1f, 0.25f);
                col.AddComponent<BoxCollider>().size = new Vector3(0.55f, 2f, 0.55f);
            }
            Prims.Make(PrimitiveType.Cube, transform, new Vector3(0, ArchHeight, 0.25f), new Vector3(ArchHalf * 2f + 0.5f, 0.36f, 0.5f), darkSteel, null, "Crossbar");
            Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, ArchHeight + 0.2f, 0.4f), new Vector3(0.12f, ArchHalf + 0.2f, 0.12f), copper, Quaternion.Euler(0, 0, 90f), "CrossPipe");
            // emitter stack hanging over the pad centre
            // tucked under the back half of the crossbar so it never covers the plaque's LED strip
            Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, ArchHeight - 0.24f, 0.4f), new Vector3(0.48f, 0.06f, 0.48f), steel, null, "EmitterCollar");
            Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, ArchHeight - 0.33f, 0.4f), new Vector3(0.36f, 0.05f, 0.36f), darkSteel, null, "EmitterRing");
            emitterMat = new Material(Mats.Lit(color, 0.9f, 0f, color * 0.3f));
            Prims.Make(PrimitiveType.Sphere, transform, new Vector3(0, ArchHeight - 0.4f, 0.4f), new Vector3(0.26f, 0.18f, 0.26f), emitterMat, null, "EmitterLens");

            // ---- tesla coils either side
            coilTopMat = new Material(Mats.Lit(new Color(0.85f, 0.87f, 0.9f), 0.95f, 1f, Color.black));
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * CoilX;
                Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(x, 0.2f, 0.1f), new Vector3(0.48f, 0.2f, 0.48f), darkSteel, null, "CoilBase");
                Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(x, 1.0f, 0.1f), new Vector3(0.22f, 0.65f, 0.22f), ink, null, "CoilCore");
                for (int k = 0; k < 9; k++)
                    Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(x, 0.5f + k * 0.13f, 0.1f), new Vector3(0.3f, 0.025f, 0.3f), copper, null, "Winding");
                Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(x, 1.72f, 0.1f), new Vector3(0.06f, 0.1f, 0.06f), chrome, null, "Stem");
                Prims.Make(PrimitiveType.Sphere, transform, new Vector3(x, CoilTop, 0.1f), new Vector3(0.72f, 0.22f, 0.72f), coilTopMat, null, "Toroid");
                var col = new GameObject("CoilCollider");
                col.transform.SetParent(transform, false);
                col.transform.localPosition = new Vector3(x, 1f, 0.1f);
                col.AddComponent<BoxCollider>().size = new Vector3(0.5f, 2f, 0.5f);
            }

            // ---- amperage meter on the left pillar, facing the camera
            lampOff = Mats.Lit(new Color(0.1f, 0.1f, 0.12f), 0.6f);
            lampLow = Mats.Lit(Theme.AccentBlue, 0.8f, 0f, Theme.AccentBlue * 1.5f);
            lampMid = Mats.Lit(Theme.Hazard, 0.8f, 0f, Theme.Hazard * 1.5f);
            lampHigh = Mats.Lit(Mats.Danger, 0.8f, 0f, Mats.Danger * 2f);
            Vector3 meter = new Vector3(-ArchHalf, 1.5f, 0.25f - 0.27f);
            Prims.Make(PrimitiveType.Cube, transform, meter, new Vector3(0.3f, 1.4f, 0.06f), ink, null, "AmpMeter");
            ampLamps = new Renderer[8];
            for (int i = 0; i < ampLamps.Length; i++)
                ampLamps[i] = Prims.Make(PrimitiveType.Cube, transform, meter + new Vector3(0, -0.56f + i * 0.16f, -0.035f), new Vector3(0.2f, 0.1f, 0.02f), lampOff, null, "AmpLamp").GetComponent<Renderer>();

            // ---- official plaque on the crossbar
            BuildPlaque(destination);

            // ---- electricity
            arcMat = Mats.Unlit(new Color(1.6f, 1.9f, 3.2f));
            boltMat = Mats.Unlit(new Color(4f, 4f, 5f));
            arcs = new LineRenderer[3];
            for (int i = 0; i < arcs.Length; i++) arcs[i] = MakeLine("Arc" + i, arcMat, 0.035f);
            bolt = MakeLine("Bolt", boltMat, 0.16f);
            boltBranch = MakeLine("BoltBranch", boltMat, 0.07f);

            var lgo = new GameObject("Flash");
            lgo.transform.SetParent(transform, false);
            lgo.transform.localPosition = new Vector3(0, 2f, -0.5f);
            flashLight = lgo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.range = 9f;
            flashLight.color = new Color(0.8f, 0.9f, 1f);
            flashLight.intensity = 0f;
        }

        void BuildPlaque(string destination)
        {
            var plate = Mats.Lit(new Color(0.55f, 0.57f, 0.6f), 0.7f, 0.9f);
            var bolt = Mats.Lit(new Color(0.85f, 0.87f, 0.9f), 0.95f, 1f);
            Vector3 c = new Vector3(0, ArchHeight + 0.28f, -0.04f); // bolted across the front of (and above) the crossbar
            Prims.Make(PrimitiveType.Cube, transform, c + new Vector3(0, 0, 0.04f), new Vector3(2.9f, 0.95f, 0.06f), plate, null, "Plaque");
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                Prims.Make(PrimitiveType.Sphere, transform, c + new Vector3(sx * 1.36f, sy * 0.38f, 0f), Vector3.one * 0.07f, bolt);

            var cgo = new GameObject("PlaqueText", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            cgo.transform.localPosition = c + new Vector3(0, 0, -0.02f);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            cgo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var rt = (RectTransform)cgo.transform;
            rt.sizeDelta = new Vector2(560, 184);
            rt.localScale = Vector3.one * (2.8f / 560f);

            var engraved = new Color(0.12f, 0.13f, 0.15f);
            Line(rt, "DEPT. OF SUBJECT LOGISTICS  ·  UNIT 7-B", 17, 74f, new Color(0.2f, 0.21f, 0.24f), FontStyle.Normal);
            Struck(rt, "INCINERATOR", 30, 46f, engraved);
            Struck(rt, "CLONE REINTEGRATION", 26, 14f, engraved);
            Line(rt, "TELEPORTER", 44, -24f, Red, FontStyle.Bold);
            // destination on a little LED strip
            var led = new GameObject("LED", typeof(RectTransform));
            var lr = (RectTransform)led.transform;
            lr.SetParent(rt, false);
            lr.anchoredPosition = new Vector2(0, -68f);
            lr.sizeDelta = new Vector2(420, 36);
            led.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.04f);
            Line(rt, destination, 24, -68f, Destination == Kind.Next ? new Color(0.35f, 1f, 0.5f) : new Color(1f, 0.65f, 0.2f), FontStyle.Bold);
        }

        static Text Line(RectTransform parent, string text, int size, float y, Color c, FontStyle style = FontStyle.Bold)
        {
            var go = new GameObject(text, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchoredPosition = new Vector2(0, y);
            r.sizeDelta = new Vector2(560, size + 10);
            var t = go.AddComponent<Text>();
            t.font = Popup.Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Engraved text with a red stripe of paint across it, like someone updated the plaque on the cheap.</summary>
        static void Struck(RectTransform parent, string text, int size, float y, Color c)
        {
            var t = Line(parent, text, size, y, c);
            var strike = new GameObject("Strike", typeof(RectTransform));
            var r = (RectTransform)strike.transform;
            r.SetParent(parent, false);
            r.anchoredPosition = new Vector2(0, y - 1f);
            r.sizeDelta = new Vector2(t.preferredWidth + 16f, Mathf.Max(4f, size * 0.16f));
            r.localRotation = Quaternion.Euler(0, 0, Random.Range(-2.5f, 2.5f));
            var img = strike.AddComponent<Image>();
            img.color = Red;
            img.raycastTarget = false;
        }

        LineRenderer MakeLine(string name, Material mat, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = mat;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.enabled = false;
            return lr;
        }

        /// <summary>Jagged lightning between two points.</summary>
        void Jag(LineRenderer lr, Vector3 a, Vector3 b, int segments, float amplitude)
        {
            lr.positionCount = segments + 1;
            Vector3 d = b - a;
            Vector3 side = Vector3.Cross(d.normalized, Vector3.forward);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            side.Normalize();
            Vector3 side2 = Vector3.Cross(d.normalized, side).normalized;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float env = Mathf.Sin(t * Mathf.PI);
                Vector3 off = (side * ((float)rng.NextDouble() * 2f - 1f) + side2 * ((float)rng.NextDouble() * 2f - 1f) * 0.5f) * amplitude * env;
                lr.SetPosition(i, Vector3.Lerp(a, b, t) + off);
            }
        }

        public bool Contains(Vector3 p)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            return d.magnitude < Radius;
        }

        /// <summary>0..1 charge from everyone being on teleporters (shared by all pads).</summary>
        public void SetCharge(float c) { if (!spent) charge = Mathf.Clamp01(c); }

        public void Flash() { flash = 1f; arcTimer = 0f; Sfx.Play(Sfx.I != null ? Sfx.I.zap : null, 0.6f, 1.2f); }

        /// <summary>The big one: a bolt from the emitter down to the pad.</summary>
        public void Strike()
        {
            strikeT = 0f;
            spent = true;
            flash = 1f;
            Fx.Sparks(transform.position + Vector3.up * 0.3f, 40, 9f);
            // scorch mark
            Prims.Make(PrimitiveType.Cylinder, transform, new Vector3(0, 0.172f, 0), new Vector3(1.4f, 0.003f, 1.4f), Mats.Lit(new Color(0.05f, 0.04f, 0.04f), 0.1f), null, "Scorch");
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            rise = Mathf.MoveTowards(rise, 1f, dt * 2.2f);
            transform.localScale = new Vector3(1f, Juice.OutBack(rise), 1f);
            flash = Mathf.MoveTowards(flash, 0f, dt * 3f);

            if (spent && strikeT > 0.45f) charge = Mathf.MoveTowards(charge, 0f, dt * 3f); // drained
            float occ = spent ? 0f : Mathf.Clamp01(Occupants * 0.6f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (3f + charge * 20f));
            ringMat.SetColor("_EmissionColor", color * (0.4f + occ * 1.2f + pulse * 0.3f + charge * 2f + flash * 3f));
            emitterMat.SetColor("_EmissionColor", color * (0.3f + charge * charge * 6f + flash * 6f));
            coilTopMat.SetColor("_EmissionColor", new Color(0.5f, 0.6f, 1f) * (charge * charge * 2.5f * pulse + flash * 2f));

            // amperage meter climbs with the charge, with a nervous flicker at the top
            float shown = Mathf.Clamp01(charge + (charge > 0.05f ? ((float)rng.NextDouble() - 0.5f) * 0.08f : 0f));
            for (int i = 0; i < ampLamps.Length; i++)
            {
                bool lit = shown * ampLamps.Length > i + 0.2f;
                var m = !lit ? lampOff : i >= 6 ? lampHigh : i >= 3 ? lampMid : lampLow;
                if (ampLamps[i].sharedMaterial != m) ampLamps[i].sharedMaterial = m;
            }

            // arcs crawl between the coils and the arch more often as the charge builds
            arcTimer -= dt;
            if (arcTimer <= 0f)
            {
                float c = spent && strikeT > 0.45f ? 0f : Mathf.Max(charge, flash);
                arcTimer = Mathf.Lerp(0.35f, 0.04f, c);
                Vector3 l = transform.TransformPoint(new Vector3(-CoilX, CoilTop + 0.08f, 0.1f));
                Vector3 r = transform.TransformPoint(new Vector3(CoilX, CoilTop + 0.08f, 0.1f));
                Vector3 em = EmitterPoint;
                for (int i = 0; i < arcs.Length; i++)
                {
                    bool on = c > 0.05f && rng.NextDouble() < c * (i == 2 ? 0.6 : 1.0);
                    arcs[i].enabled = on;
                    if (!on) continue;
                    if (i == 0) Jag(arcs[i], l, Vector3.Lerp(l, em, 0.6f + 0.4f * c), 10, 0.25f);
                    else if (i == 1) Jag(arcs[i], r, Vector3.Lerp(r, em, 0.6f + 0.4f * c), 10, 0.25f);
                    else Jag(arcs[i], l, r, 18, 0.45f);
                    arcs[i].widthMultiplier = 0.02f + c * 0.04f;
                }
                if (c > 0.15f && rng.NextDouble() < c * 0.5) Sfx.Play(Sfx.I != null ? Sfx.I.zap : null, 0.25f + c * 0.35f, 0.8f + c * 0.6f, 0.1f);
            }

            // the strike
            if (strikeT >= 0f)
            {
                strikeT += dt;
                bool show = strikeT < 0.45f && (strikeT < 0.08f || ((int)(strikeT * 30f) & 1) == 0);
                bolt.enabled = show;
                boltBranch.enabled = show && strikeT < 0.25f;
                if (show)
                {
                    Vector3 top = EmitterPoint, ground = transform.position + Vector3.up * 0.2f;
                    Jag(bolt, top, ground, 14, 0.4f);
                    Jag(boltBranch, Vector3.Lerp(top, ground, 0.35f), ground + new Vector3((float)rng.NextDouble() * 2f - 1f, 0f, -0.6f), 8, 0.3f);
                }
                flashLight.intensity = Mathf.Max(0f, 12f * (1f - strikeT * 2.5f));
            }
            else flashLight.intensity = charge * charge * 1.5f * pulse + flash * 4f;
        }
    }
}
