using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure
{
    /// <summary>
    /// A little icon that rides above a pipe piece on the belt (the Crossing Sign held over an extra cross), with an
    /// optional LUCKY! under it. It bobs, faces the camera, and fades once the piece leaves the belt or after a while.
    /// </summary>
    public class FloatingTag : MonoBehaviour
    {
        PipePiece target;
        Image img;
        Text lucky;
        float t, fade = 1f;
        const float Size = 0.85f;

        public static FloatingTag Create(PipePiece piece, string icon, bool isLucky)
        {
            if (piece == null) return null;
            var go = new GameObject("FloatingTag", typeof(RectTransform));
            go.transform.SetParent(Game.LevelRoot, false);
            var tag = go.AddComponent<FloatingTag>();
            tag.target = piece;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(256, 256);
            rt.localScale = Vector3.one * (Size / 256f);

            var igo = new GameObject("Icon", typeof(RectTransform));
            var irt = (RectTransform)igo.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = irt.offsetMax = Vector2.zero;
            tag.img = igo.AddComponent<Image>();
            tag.img.sprite = Glyphs.Icon(icon);
            tag.img.preserveAspect = true;
            tag.img.raycastTarget = false;
            if (tag.img.sprite == null) tag.img.enabled = false;

            if (isLucky)
            {
                var lgo = new GameObject("Lucky", typeof(RectTransform));
                var lrt = (RectTransform)lgo.transform;
                lrt.SetParent(rt, false);
                lrt.anchorMin = new Vector2(0.5f, 0f); lrt.anchorMax = new Vector2(0.5f, 0f);
                lrt.sizeDelta = new Vector2(420, 90);
                lrt.anchoredPosition = new Vector2(0, -40);
                tag.lucky = Popup.MakeText(lgo, "LUCKY!", 64, new Color(0.35f, 1f, 0.4f));
            }
            return tag;
        }

        void LateUpdate()
        {
            t += Time.deltaTime;
            bool gone = target == null || !target.gameObject.activeInHierarchy || target.State != PieceState.OnBelt;
            if (gone) fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime * 2.5f); // stays up the whole ride (it starts inside the wall)
            if (fade <= 0f) { Destroy(gameObject); return; }
            if (target != null)
                transform.position = target.transform.position + Vector3.up * (1.1f + Mathf.Sin(t * 4f) * 0.06f);
            var cam = Game.Cam != null ? Game.Cam.Cam : Camera.main;
            if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
            // pop in, wobble like it's being waved, shrink away
            float pop = Juice.OutBack(Mathf.Clamp01(t / 0.3f));
            transform.localScale = Vector3.one * (Size / 256f) * pop * Mathf.Max(0.01f, fade);
            if (img != null) { img.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 6f) * 8f); img.color = new Color(1, 1, 1, fade); }
            if (lucky != null) lucky.color = new Color(0.35f, 1f, 0.4f, fade);
        }
    }
}
