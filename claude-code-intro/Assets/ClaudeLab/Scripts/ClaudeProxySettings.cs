using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// Where the proxy lives and how the lab assistant behaves.
    /// The asset holding these values contains the proxy token, so it is gitignored.
    /// </summary>
    [CreateAssetMenu(fileName = "ClaudeProxySettings", menuName = "Claude Lab/Proxy Settings")]
    public class ClaudeProxySettings : ScriptableObject
    {
        [Tooltip("Base URL of vr-lab-proxy. Use http://localhost:8000 in the editor, " +
                 "or http://<your computer's IP>:8000 on a standalone headset.")]
        public string proxyUrl = "http://localhost:8000";

        [Tooltip("Must match PROXY_TOKEN in vr-lab-proxy/.env.")]
        public string proxyToken = "";

        [TextArea(3, 10)]
        public string systemPrompt =
            "You are a friendly lab assistant in a VR physics lab for students. " +
            "Use your tools to set up experiments when asked. " +
            "Keep spoken replies short (one to three sentences) because they are shown in a headset.";

        [Tooltip("Stop the tool loop after this many requests to Claude in one turn.")]
        [Range(1, 16)]
        public int maxRounds = 8;

        [Tooltip("Seconds to wait for each request before giving up.")]
        public int timeoutSeconds = 60;
    }
}
