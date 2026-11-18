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
            new Preset { label = "Moon gravity", prompt = "Switch to Moon gravity and drop a ball from 2 meters." },
            new Preset { label = "Back to Earth", prompt = "Set gravity back to Earth's." },
            new Preset { label = "Clear the lab", prompt = "Clear the lab." },
        };

        [Header("Built by Claude Lab → Add Prompt Panel to Scene")]
        public TMP_Text replyText;
        public TMP_Text statusText;
        public RectTransform buttonContainer;
        public Button newConversationButton;

        [Header("Look")]
        public Color buttonColor = new Color(0.20f, 0.36f, 0.62f);
        public Color textColor = Color.white;
        public float buttonHeight = 64f;
        public float buttonFontSize = 26f;

        readonly List<Button> _buttons = new List<Button>();

        void Awake()
        {
            if (agent == null) agent = FindFirstObjectByType<ClaudeAgent>();
            RebuildButtons();
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

        public void Ask(Preset preset)
        {
            if (agent == null)
            {
                ShowError("No Claude Lab Assistant in the scene.");
                return;
            }
            SetStatus($"You asked: {preset.prompt}");
            agent.Ask(preset.prompt);
        }

        public void NewConversation()
        {
            if (agent == null || agent.IsBusy) return;
            agent.ResetConversation();
            if (replyText != null) replyText.text = "";
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
            if (replyText != null) replyText.text = text;
        }

        void ShowToolUsed(string toolName)
        {
            SetStatus($"Claude used {toolName.Replace('_', ' ')}…");
        }

        void ShowError(string message)
        {
            if (replyText != null) replyText.text = $"<color=#ff7a7a>{message}</color>";
        }

        void ShowBusy(bool busy)
        {
            foreach (var b in _buttons)
                if (b != null) b.interactable = !busy;
            if (newConversationButton != null) newConversationButton.interactable = !busy;

            if (busy)
            {
                if (replyText != null) replyText.text = "<i>Thinking…</i>";
            }
            else
            {
                SetStatus("Ready. Press another button.");
            }
        }

        void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }
    }
}
