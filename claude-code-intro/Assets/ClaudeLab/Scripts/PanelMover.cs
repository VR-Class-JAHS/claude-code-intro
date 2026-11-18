using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ClaudeLab
{
    /// <summary>
    /// Adds a "Grab here to move" bar along the bottom of a world-space panel. Grabbing the bar with a
    /// controller ray, a hand, or the simulator moves the whole panel; it stays upright and turns to face
    /// the viewer. Only the bar has a collider, so pointing at buttons still presses them.
    /// ClaudeLabPanel adds this at Play time when its Movable box is ticked.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class PanelMover : MonoBehaviour
    {
        public float handleHeight = 40f;
        public Color handleColor = new Color(0.30f, 0.34f, 0.42f);
        public Color handleTextColor = new Color(0.85f, 0.88f, 0.92f);

        [Tooltip("While moving, keep the panel upright and turned toward the viewer.")]
        public bool faceViewer = true;

        XRGrabInteractable _grab;
        BoxCollider _collider;
        RectTransform _handle;

        void Awake()
        {
            _handle = CreateHandle();

            // The collider must exist before XRGrabInteractable wakes up: it collects its colliders then.
            _collider = _handle.gameObject.AddComponent<BoxCollider>();

            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            _grab = gameObject.AddComponent<XRGrabInteractable>();
            _grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            _grab.trackRotation = false; // we turn it to face the viewer instead of copying the controller's tilt
            _grab.trackScale = false;
            _grab.throwOnDetach = false;
            _grab.useDynamicAttach = true; // keeps the grab point where the ray hit, so the panel doesn't jump
        }

        void Start()
        {
            // Size the collider to the bar once the panel's layout has placed it.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            Rect r = _handle.rect;
            _collider.center = new Vector3(r.center.x, r.center.y, 0f);
            _collider.size = new Vector3(r.width, r.height, 20f); // canvas units; 20 = 2 cm deep
        }

        void LateUpdate()
        {
            if (!faceViewer || _grab == null || !_grab.isSelected) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - cam.transform.position, Vector3.up);
            if (away.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up); // canvas front faces the camera
        }

        RectTransform CreateHandle()
        {
            var go = new GameObject("Move Handle", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            // Default layer, not UI: the grab ray's physics raycast may skip the UI layer.
            go.layer = 0;
            go.transform.SetParent(transform, false);
            go.transform.SetAsLastSibling();
            var image = go.GetComponent<Image>();
            image.color = handleColor;
            image.raycastTarget = false; // UI clicks pass through; grabbing uses the collider
            go.GetComponent<LayoutElement>().preferredHeight = handleHeight;

            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.layer = go.layer;
            label.transform.SetParent(go.transform, false);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var tmp = label.GetComponent<TextMeshProUGUI>();
            tmp.text = "= Grab here to move =";
            tmp.fontSize = 20f;
            tmp.color = handleTextColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return (RectTransform)go.transform;
        }
    }
}
