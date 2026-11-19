using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// Where the proxy lives and how the lab assistant behaves.
    /// The asset holding these values contains the proxy token, so it is gitignored. It lives at
    /// Assets/ClaudeLab/Settings/Resources/ClaudeProxySettings.asset and is loaded by name at runtime,
    /// so scenes never store a link to it: each computer keeps its own copy without breaking the scene.
    /// </summary>
    [CreateAssetMenu(fileName = ResourceName, menuName = "Claude Lab/Proxy Settings")]
    public class ClaudeProxySettings : ScriptableObject
    {
        public const string ResourceName = "ClaudeProxySettings";

        /// <summary>This computer's settings asset, or null if it hasn't been created yet.</summary>
        public static ClaudeProxySettings Load() => Resources.Load<ClaudeProxySettings>(ResourceName);

        [Tooltip("Base URL of vr-lab-proxy. Use http://localhost:8000 in the editor, " +
                 "or http://<your computer's IP>:8000 on a standalone headset.")]
        public string proxyUrl = "http://localhost:8000";

        [Tooltip("Must match PROXY_TOKEN in vr-lab-proxy/.env.")]
        public string proxyToken = "";

        /// <summary>
        /// The lab's instructions for Claude. Kept here, in the repo, so every computer uses the same wording;
        /// the settings asset is per computer and gitignored.
        /// </summary>
        public const string DefaultSystemPrompt =
            "You are a friendly lab assistant in a VR physics lab for students. " +
            "Use your tools to set up experiments when asked. " +
            "Keep spoken replies short (one to three sentences) because they are shown in a headset.\n\n" +
            "Students can press the panel's Release button, grab, throw or move objects at any time, and you are " +
            "not told when they do. Each student message ends with a <lab_state> snapshot of the lab taken the " +
            "moment they asked. Trust it over anything said earlier in the conversation, and describe objects only " +
            "from that snapshot or from your tools, never from memory. For fall times and impact speeds, use measure_object.";

        [Tooltip("Leave empty to use the lab's built-in instructions (DefaultSystemPrompt in ClaudeProxySettings.cs, " +
                 "the same on every computer). Text here replaces them on this computer only.")]
        [TextArea(3, 10)]
        public string systemPromptOverride = "";

        /// <summary>The instructions sent to Claude: this computer's override if set, else the built-in ones.</summary>
        public string SystemPrompt => string.IsNullOrWhiteSpace(systemPromptOverride) ? DefaultSystemPrompt : systemPromptOverride;

        [Tooltip("Stop the tool loop after this many requests to Claude in one turn.")]
        [Range(1, 16)]
        public int maxRounds = 8;

        [Tooltip("Seconds to wait for each request before giving up.")]
        public int timeoutSeconds = 60;
    }
}
