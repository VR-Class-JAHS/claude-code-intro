using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ClaudeLab.EditorTools
{
    /// <summary>
    /// Menu: Claude Lab > Add Prompt Panel to Scene.
    /// Builds a world-space canvas with preset prompt buttons and a reply box, makes it clickable by XR
    /// controllers, hands and the mouse, and places it to the left of the spawn area facing the camera.
    /// </summary>
    public static class ClaudeLabPanelSetup
    {
        const float PixelsPerMeter = 1000f; // canvas units are pixels; 520 px wide = 0.52 m
        const float Width = 520f;

        static readonly Color PanelColor = new Color(0.08f, 0.10f, 0.14f, 0.92f);
        static readonly Color ReplyBoxColor = new Color(0.15f, 0.18f, 0.24f, 1f);
        static readonly Color MutedText = new Color(0.72f, 0.76f, 0.82f);
        static readonly Color SecondaryButton = new Color(0.30f, 0.32f, 0.36f);

        [MenuItem("Claude Lab/Add Prompt Panel to Scene")]
        static void AddPanel()
        {
            var existing = Object.FindFirstObjectByType<ClaudeLabPanel>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[Claude Lab] This scene already has a prompt panel; selected it.", existing);
                return;
            }

            var agent = Object.FindFirstObjectByType<ClaudeAgent>();
            if (agent == null)
                Debug.LogWarning("[Claude Lab] No Claude Lab Assistant in this scene yet. Use Claude Lab → Add Lab Assistant to Scene, then drag it into the panel's Agent field.");

            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0) uiLayer = 0;

            // Canvas root
            var root = new GameObject("Claude Prompt Panel",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster),
                typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(ClaudeLabPanel));
            root.layer = uiLayer;
            Undo.RegisterCreatedObjectUndo(root, "Add Claude Prompt Panel");

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f; // sharper text up close

            var rootRt = (RectTransform)root.transform;
            rootRt.sizeDelta = new Vector2(Width, 700f);
            rootRt.localScale = Vector3.one / PixelsPerMeter;
            root.GetComponent<Image>().color = PanelColor;

            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            root.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Title and status line
            var title = AddText(root.transform, "Title", "Claude Lab Assistant", 34f, Color.white, 46f);
            title.fontStyle = FontStyles.Bold;
            var status = AddText(root.transform, "Status", "Press a button to ask Claude.", 20f, MutedText, 54f);

            // Reply box
            var replyBox = new GameObject("Reply Box", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            replyBox.layer = uiLayer;
            replyBox.transform.SetParent(root.transform, false);
            replyBox.GetComponent<Image>().color = ReplyBoxColor;
            replyBox.GetComponent<Image>().raycastTarget = false;
            replyBox.GetComponent<LayoutElement>().preferredHeight = 230f;

            var reply = AddText(replyBox.transform, "Reply", "", 26f, Color.white, 0f);
            var replyRt = (RectTransform)reply.transform;
            replyRt.anchorMin = Vector2.zero;
            replyRt.anchorMax = Vector2.one;
            replyRt.offsetMin = new Vector2(16, 12);
            replyRt.offsetMax = new Vector2(-16, -12);
            reply.alignment = TextAlignmentOptions.TopLeft;
            reply.enableAutoSizing = true; // long replies shrink instead of spilling out
            reply.fontSizeMin = 16f;
            reply.fontSizeMax = 26f;
            Object.DestroyImmediate(reply.GetComponent<LayoutElement>());

            // Stopwatch readout for the lab_timer tool
            var timer = AddText(root.transform, "Timer", "Timer: 0.00 s  (stopped)", 22f, MutedText, 30f);
            var timerTool = Object.FindFirstObjectByType<LabTimerTool>();
            if (timerTool != null)
            {
                Undo.RecordObject(timerTool, "Connect timer display");
                timerTool.display = timer;
            }
            else
            {
                timer.gameObject.SetActive(false);
                Debug.Log("[Claude Lab] No lab_timer tool found, so the panel's timer line is hidden. " +
                          "Run Claude Lab → Add Lab Assistant to Scene to add it, then drag the Timer text into its Display field.");
            }

            // Preset buttons go here, two per row (rebuilt from the Presets list)
            var buttons = new GameObject("Preset Buttons", typeof(RectTransform), typeof(GridLayoutGroup));
            buttons.layer = uiLayer;
            buttons.transform.SetParent(root.transform, false);
            var grid = buttons.GetComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(10f, 10f);
            grid.cellSize = new Vector2((Width - 48f - 10f) / 2f, 64f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            var panel = root.GetComponent<ClaudeLabPanel>();
            var newConversation = ClaudeLabPanel.CreateButton(root.transform, "New conversation",
                SecondaryButton, Color.white, 48f, 22f);

            panel.agent = agent;
            panel.replyText = reply;
            panel.statusText = status;
            panel.buttonContainer = (RectTransform)buttons.transform;
            panel.newConversationButton = newConversation;
            panel.RebuildButtons();

            Place(rootRt, agent);
            EnsureXREventSystem();

            Selection.activeGameObject = root;
            Debug.Log("[Claude Lab] Added Claude Prompt Panel. Edit its Presets list in the Inspector, then ⋮ → Rebuild Buttons.", root);
        }

        [MenuItem("Claude Lab/Reset Prompt Panel Position")]
        static void ResetPanelPosition()
        {
            var panel = Object.FindFirstObjectByType<ClaudeLabPanel>();
            if (panel == null)
            {
                Debug.LogWarning("[Claude Lab] No prompt panel in this scene. Use Claude Lab → Add Prompt Panel to Scene.");
                return;
            }
            var rt = (RectTransform)panel.transform;
            Undo.RecordObject(rt, "Reset Prompt Panel Position");
            Place(rt, Object.FindFirstObjectByType<ClaudeAgent>());
            Selection.activeGameObject = panel.gameObject;
            Debug.Log("[Claude Lab] Moved the prompt panel to its default spot, left of the spawn area. Save the scene.", panel);
        }

        static TextMeshProUGUI AddText(Transform parent, string name, string text, float size, Color color, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            if (height > 0f) go.GetComponent<LayoutElement>().preferredHeight = height;
            return tmp;
        }

        // To the left of the spawn area, about 1 m from the camera, at a comfortable reading height, facing the camera.
        static void Place(RectTransform panel, ClaudeAgent agent)
        {
            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : new Vector3(0f, 1.4f, 0f);
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd == Vector3.zero) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);

            // About 45 degrees to the left, 1.1 m away: clear of the spawn row, which fills from straight ahead outward.
            Vector3 pos = camPos + fwd * 0.8f - right * 0.8f;
            float floor = agent != null ? agent.transform.position.y : 0f;
            pos.y = floor + 1.25f;

            Vector3 facing = Vector3.ProjectOnPlane(pos - camPos, Vector3.up);
            if (facing == Vector3.zero) facing = fwd;
            // A world-space canvas is read from behind its +Z, so point +Z away from the viewer.
            panel.SetPositionAndRotation(pos, Quaternion.LookRotation(facing.normalized, Vector3.up));
        }

        // XR pointers only click UI through an XRUIInputModule on the EventSystem.
        static void EnsureXREventSystem()
        {
            var es = Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
                Undo.RegisterCreatedObjectUndo(go, "Add XR EventSystem");
                Debug.Log("[Claude Lab] Added an EventSystem with XRUIInputModule.", go);
                return;
            }
            if (es.GetComponent<XRUIInputModule>() == null)
            {
                Debug.LogWarning("[Claude Lab] The scene's EventSystem has no XRUIInputModule, so controllers and hands can't press the panel. " +
                                 "Remove its other input module and add XRUIInputModule.", es);
            }
        }
    }
}
