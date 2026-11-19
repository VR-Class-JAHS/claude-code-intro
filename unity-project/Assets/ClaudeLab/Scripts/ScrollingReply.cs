using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ClaudeLab
{
    /// <summary>
    /// Makes the panel's reply box scroll instead of letting long replies spill out of it: the reply text gets a
    /// fixed size, sits in a masked viewport, and a scrollbar appears on the right when it's longer than the box.
    /// Scroll by dragging the text or the scrollbar handle (controller trigger, pinch or mouse) or with the mouse wheel.
    /// ClaudeLabPanel adds this at Play time and calls ScrollToTop for each new reply.
    /// </summary>
    [RequireComponent(typeof(ClaudeLabPanel))]
    public class ScrollingReply : MonoBehaviour
    {
        public float fontSize = 22f;
        public float scrollbarWidth = 16f;
        public Color trackColor = new Color(1f, 1f, 1f, 0.08f);
        public Color handleColor = new Color(0.72f, 0.76f, 0.82f, 0.85f);

        ScrollRect _scroll;

        void Awake()
        {
            var reply = GetComponent<ClaudeLabPanel>().replyText;
            if (reply == null || reply.transform.parent == null) return;
            Build(reply.transform.parent as RectTransform, reply);
        }

        /// <summary>Show the start of the reply. Call after changing the text.</summary>
        public void ScrollToTop()
        {
            if (_scroll == null) return;
            Canvas.ForceUpdateCanvases(); // let the content resize to the new text first
            _scroll.verticalNormalizedPosition = 1f;
        }

        void Build(RectTransform box, TMP_Text reply)
        {
            // The box takes the drag and wheel events, so it needs to be a raycast target.
            var boxImage = box.GetComponent<Image>();
            if (boxImage != null) boxImage.raycastTarget = true;

            // Viewport: the box's area minus the scrollbar, clipping the text.
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.layer = box.gameObject.layer;
            viewport.transform.SetParent(box, false);
            var vrt = (RectTransform)viewport.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(0f, 6f);
            vrt.offsetMax = new Vector2(-(scrollbarWidth + 6f), -6f);

            // Content: the reply text itself, pinned to the top and as tall as its text.
            var rt = (RectTransform)reply.transform;
            rt.SetParent(vrt, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            reply.enableAutoSizing = false; // scroll instead of shrinking
            reply.fontSize = fontSize;
            reply.margin = new Vector4(16f, 6f, 6f, 6f);
            reply.textWrappingMode = TextWrappingModes.Normal;
            reply.overflowMode = TextOverflowModes.Overflow;
            // Not "??": in the Editor a missing component comes back as a fake null that "??" doesn't catch.
            var fitter = reply.gameObject.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = reply.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Scrollbar on the right edge.
            var bar = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            bar.layer = box.gameObject.layer;
            bar.transform.SetParent(box, false);
            var brt = (RectTransform)bar.transform;
            brt.anchorMin = new Vector2(1f, 0f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.sizeDelta = new Vector2(scrollbarWidth, -12f);
            brt.anchoredPosition = new Vector2(-4f, 0f);
            bar.GetComponent<Image>().color = trackColor;

            var area = new GameObject("Sliding Area", typeof(RectTransform));
            area.layer = bar.layer;
            area.transform.SetParent(brt, false);
            var art = (RectTransform)area.transform;
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = art.offsetMax = Vector2.zero;

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.layer = bar.layer;
            handle.transform.SetParent(art, false);
            var hrt = (RectTransform)handle.transform;
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = handleColor;

            var scrollbar = bar.GetComponent<Scrollbar>();
            scrollbar.handleRect = hrt;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            _scroll = box.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = vrt;
            _scroll.content = rt;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide; // only when the reply is too long
        }
    }
}
