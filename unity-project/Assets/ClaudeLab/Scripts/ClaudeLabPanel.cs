using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ClaudeLab
{
    /// <summary>
    /// World-space panel for the headset: one button per preset prompt, plus Claude's latest reply.
    /// Edit the Presets list in the Inspector, then use the component's ⋮ menu → Rebuild Buttons
    /// (buttons are also rebuilt automatically when Play starts).
    /// Built by the menu item Claude Lab → Add Prompt Panel to Scene.
    /// </summary>
    public class ClaudeLabPanel : MonoBehaviour
    {
        [Serializable]
        public class Preset
        {
            public string label = "Drop a ball";
            [TextArea(2, 4)] public string prompt = "Drop a ball from 2 meters.";
        }

        public ClaudeAgent agent;

        public List<Preset> presets = new List<Preset>
        {
            new Preset { label = "Ball vs. block", prompt = "Drop a ball from 2 meters and a block from 1 meter." },
            new Preset { label = "Heavy vs. light", prompt = "Drop a 1 kg ball and a 5 kg ball from 2 meters." },
            new Preset { label = "Pendulum", prompt = "Set up a pendulum I can watch swing." },
            new Preset { label = "Ramp", prompt = "Set up a 30 degree ramp." },
            new Preset { label = "How fast did it hit?", prompt = "Measure the objects. How long did each one take to fall, and how fast was it going when it hit?" },
            new Preset { label = "Heavier + higher", prompt = "Make the ball twice as heavy and drop it from higher." },
            new Preset { label = "Energy graph", prompt = "Drop a ball from 3 meters and graph its energy as it falls." },
            new Preset { label = "Explain the graph", prompt = "Read the measurement and explain what the graph shows." },
            new Preset { label = "Moon gravity", prompt = "Switch to Moon gravity and drop a ball from 2 meters." },
            new Preset { label = "Back to Earth", prompt = "Set gravity back to Earth's." },
            new Preset { label = "Clear the lab", prompt = "Clear the lab." },
        };

        [Header("Built by Claude Lab → Add Prompt Panel to Scene")]
        public TMP_Text replyText;
        public TMP_Text statusText;
        public RectTransform buttonContainer;
        public Button newConversationButton;

        [Header("Release button")]
        [Tooltip("A button that drops every ball and block waiting in mid-air, all at once.")]
        public bool showReleaseButton = true;
        [Tooltip("Defaults to the ReleaseObjectsTool in the scene.")]
        public ReleaseObjectsTool releaseTool;
        public Color releaseButtonColor = new Color(0.16f, 0.55f, 0.30f);

        [Header("Voice input")]
        [Tooltip("Adds a 'Hold to talk' button. The proxy turns the recording into text on its own computer (Whisper).")]
        public bool showVoiceInput = true;

        [Header("Graph")]
        [Tooltip("Adds a graph under the reply that shows start_measurement recordings as they happen.")]
        public bool showGraph = true;

        [Header("Typed input")]
        [Tooltip("Adds a text box and Send button under the reply, for typing any request.")]
        public bool showTypedInput = true;
        [Tooltip("Hide the text box on headsets and phones, where typing isn't practical. It still shows in the Editor and desktop builds.")]
        public bool typedInputDesktopOnly = true;

        [Header("Exit")]
        [Tooltip("Adds an 'Exit lab' button beside 'New conversation' (press twice to quit).")]
        public bool showExitButton = true;

        [Header("Moving")]
        [Tooltip("Adds a 'Grab here to move' bar at the bottom so students can drag the panel out of the way.")]
        public bool movable = true;

        [Header("Look")]
        public Color buttonColor = new Color(0.20f, 0.36f, 0.62f);
        public Color textColor = Color.white;
        public float buttonHeight = 64f;
        public float buttonFontSize = 26f;

        readonly List<Button> _buttons = new List<Button>();
        ScrollingReply _scrolling;

        void Awake()
        {
            if (agent == null) agent = FindFirstObjectByType<ClaudeAgent>();
            RebuildButtons();
            // Graph first, then the typed-input row: both go right under the reply box, so this order puts
            // the text box directly under the reply and the graph below it.
            if (showGraph && GetComponent<MeasurementGraph>() == null)
                gameObject.AddComponent<MeasurementGraph>();
            if (showVoiceInput && GetComponent<VoiceInput>() == null)
                gameObject.AddComponent<VoiceInput>();
            // Before PanelMover: it sizes its grab bar after the panel's layout, which these rows change.
            bool typingPractical = Application.isEditor || !Application.isMobilePlatform;
            if (showTypedInput && (typingPractical || !typedInputDesktopOnly) && GetComponent<TypedPromptInput>() == null)
                gameObject.AddComponent<TypedPromptInput>();
            // After the rows above: they find their spot next to the reply box through the reply text's parent,
            // which this moves into a scrolling viewport.
            _scrolling = GetComponent<ScrollingReply>();
            if (_scrolling == null) _scrolling = gameObject.AddComponent<ScrollingReply>();
            if (showExitButton && GetComponent<ExitButton>() == null)
                gameObject.AddComponent<ExitButton>();
            if (movable && GetComponent<PanelMover>() == null)
                gameObject.AddComponent<PanelMover>();
            if (newConversationButton != null)
                newConversationButton.onClick.AddListener(NewConversation);
            SetStatus("Press a button to ask Claude.");
        }

        void OnEnable()
        {
            if (agent == null) return;
            agent.onAssistantText.AddListener(ShowReply);
            agent.onToolUsed.AddListener(ShowToolUsed);
            agent.onError.AddListener(ShowError);
            agent.onBusyChanged.AddListener(ShowBusy);
        }

        void OnDisable()
        {
            if (agent == null) return;
            agent.onAssistantText.RemoveListener(ShowReply);
            agent.onToolUsed.RemoveListener(ShowToolUsed);
            agent.onError.RemoveListener(ShowError);
            agent.onBusyChanged.RemoveListener(ShowBusy);
        }

        public void Ask(Preset preset) => AskText(preset.prompt);

        /// <summary>Send any text to Claude, as if it were a preset. Used by the preset buttons and the typed-input box.</summary>
        public void AskText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (agent == null)
            {
                ShowError("No Claude Lab Assistant in the scene.");
                return;
            }
            SetStatus($"You asked: {text}");
            agent.Ask(text);
        }

        public void Release()
        {
            if (releaseTool == null) releaseTool = FindFirstObjectByType<ReleaseObjectsTool>();
            if (releaseTool == null)
            {
                ShowError("No release tool in the scene. Run Claude Lab → Add Lab Assistant to Scene.");
                return;
            }
            int count = releaseTool.ReleaseAll();
            SetStatus(count == 0
                ? "Nothing is waiting to be released."
                : $"Released {count} object{(count == 1 ? "" : "s")}. Watch them fall!");
        }

        public void NewConversation()
        {
            if (agent == null || agent.IsBusy) return;
            agent.ResetConversation();
            SetReplyText("");
            SetStatus("New conversation. Claude has forgotten earlier requests.");
        }

        /// <summary>Delete the old preset buttons and make one per preset. Works in Edit mode too.</summary>
        [ContextMenu("Rebuild Buttons")]
        public void RebuildButtons()
        {
            if (buttonContainer == null) return;

            for (int i = buttonContainer.childCount - 1; i >= 0; i--)
            {
                var child = buttonContainer.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            _buttons.Clear();

            // Release goes first and stays pressable while Claude is working: it acts right away, without Claude.
            if (showReleaseButton)
            {
                var release = CreateButton(buttonContainer, "Release", releaseButtonColor, textColor, buttonHeight, buttonFontSize);
                if (Application.isPlaying) release.onClick.AddListener(Release);
            }

            foreach (var preset in presets)
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.label)) continue;
                var button = CreateButton(buttonContainer, preset.label, buttonColor, textColor, buttonHeight, buttonFontSize);
                var captured = preset;
                // Runtime listener only: in Edit mode it would not be saved, and Awake rebuilds anyway.
                if (Application.isPlaying) button.onClick.AddListener(() => Ask(captured));
                _buttons.Add(button);
            }

#if UNITY_EDITOR
            // So a rebuild from the ⋮ menu is saved with the scene.
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        /// <summary>A plain UI button with a TextMeshPro label. Also used by the editor setup.</summary>
        public static Button CreateButton(Transform parent, string label, Color color, Color labelColor, float height, float fontSize)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            go.GetComponent<LayoutElement>().preferredHeight = height;

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            button.colors = colors;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.layer = go.layer;
            textGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)textGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12, 4);
            rt.offsetMax = new Vector2(-12, -4);
            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.enableAutoSizing = true; // long labels shrink to fit the button
            tmp.fontSizeMax = fontSize;
            tmp.fontSizeMin = 14f;
            tmp.color = labelColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            return button;
        }

        void ShowReply(string text)
        {
            SetReplyText(MarkdownToRichText(text));
        }

        /// <summary>
        /// Claude sometimes answers in Markdown. TextMeshPro shows that literally, so turn the common bits into
        /// rich text: **bold** and *italic*, headings as bold lines, and "- " bullets as "• ".
        /// </summary>
        public static string MarkdownToRichText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?![\*\w])", "<i>$1</i>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^#{1,6}\s+(.+)$", "<b>$1</b>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^(\s*)[-*]\s+", "$1• ");
            return text;
        }

        void ShowToolUsed(string toolName)
        {
            SetStatus($"Claude used {toolName.Replace('_', ' ')}…");
        }

        void ShowError(string message)
        {
            SetReplyText($"<color=#ff7a7a>{message}</color>");
        }

        void ShowBusy(bool busy)
        {
            foreach (var b in _buttons)
                if (b != null) b.interactable = !busy;
            if (newConversationButton != null) newConversationButton.interactable = !busy;

            if (busy)
            {
                SetReplyText("<i>Thinking…</i>");
            }
            else
            {
                if (releaseTool == null) releaseTool = FindFirstObjectByType<ReleaseObjectsTool>();
                int waiting = releaseTool != null ? releaseTool.WaitingCount() : 0;
                SetStatus(waiting > 0
                    ? $"{waiting} object{(waiting == 1 ? " is" : "s are")} waiting. Press Release when you're ready."
                    : "Ready. Press another button.");
            }
        }

        void SetReplyText(string text)
        {
            if (replyText == null) return;
            replyText.text = text;
            if (_scrolling != null) _scrolling.ScrollToTop();
        }

        /// <summary>Show a one-line message on the panel's status line (used by the input helpers).</summary>
        public void ShowStatus(string message) => SetStatus(message);

        void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }
    }
}
