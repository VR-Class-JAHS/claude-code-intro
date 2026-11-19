using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ClaudeLab
{
    /// <summary>
    /// Quick way to try the agent before voice or keyboard UI exists:
    /// press the key in Play mode (or use the component's ⋮ menu) to send the prompt.
    /// Replies go to the Console, and to a TextMeshPro label if one is assigned.
    /// </summary>
    public class ClaudeTestPrompt : MonoBehaviour
    {
        public ClaudeAgent agent;

        [TextArea(2, 5)]
        public string prompt = "Drop a ball from 2 meters and a block from 1 meter.";

        public Key sendKey = Key.T;
        public Key clearKey = Key.R;

        [Tooltip("Optional label (e.g. on a world-space canvas) that shows the latest reply or error.")]
        public TMP_Text output;

        void Awake()
        {
            if (agent == null) agent = GetComponent<ClaudeAgent>();
        }

        void OnEnable()
        {
            if (agent == null) return;
            agent.onAssistantText.AddListener(Show);
            agent.onError.AddListener(ShowError);
            agent.onBusyChanged.AddListener(ShowBusy);
        }

        void OnDisable()
        {
            if (agent == null) return;
            agent.onAssistantText.RemoveListener(Show);
            agent.onError.RemoveListener(ShowError);
            agent.onBusyChanged.RemoveListener(ShowBusy);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb[sendKey].wasPressedThisFrame) Send();
            if (kb[clearKey].wasPressedThisFrame) agent.Ask("Clear the lab.");
        }

        [ContextMenu("Send Prompt")]
        public void Send() => agent.Ask(prompt);

        [ContextMenu("Reset Conversation")]
        public void ResetConversation() => agent.ResetConversation();

        void Show(string text)
        {
            if (output != null) output.text = text;
        }

        void ShowError(string message)
        {
            if (output != null) output.text = $"<color=#d55>{message}</color>";
        }

        void ShowBusy(bool busy)
        {
            if (busy && output != null) output.text = "Thinking…";
        }
    }
}
