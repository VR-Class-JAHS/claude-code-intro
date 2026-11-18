using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace ClaudeLab
{
    /// <summary>
    /// Talks to vr-lab-proxy and runs the tool loop:
    /// send the conversation -> if Claude asks for tools, run them here and send the results back ->
    /// repeat until Claude finishes (stop_reason "end_turn") or maxRounds is reached.
    /// </summary>
    public class ClaudeAgent : MonoBehaviour
    {
        [SerializeField] ClaudeProxySettings settings;

        [Tooltip("Tools Claude may use. Leave empty to use every LabTool found on this GameObject and its children.")]
        [SerializeField] List<LabTool> tools = new List<LabTool>();

        [Header("Events (hook UI or speech here)")]
        public UnityEvent<string> onAssistantText = new UnityEvent<string>();
        public UnityEvent<string> onToolUsed = new UnityEvent<string>();
        public UnityEvent<string> onError = new UnityEvent<string>();
        public UnityEvent<bool> onBusyChanged = new UnityEvent<bool>();

        [SerializeField] bool logToConsole = true;

        readonly JArray _messages = new JArray();
        bool _busy;

        public bool IsBusy => _busy;
        public ClaudeProxySettings Settings => settings;

        [Tooltip("Optional. Finds the proxy on the Wi-Fi; falls back to the settings' proxyUrl. Defaults to one on this GameObject.")]
        [SerializeField] ProxyDiscovery discovery;

        /// <summary>The proxy address in use: the one found on the network, else proxyUrl from settings.</summary>
        public string ProxyUrl => (discovery != null ? discovery.DiscoveredUrl : null) ?? settings.proxyUrl;

        void Awake()
        {
            if (tools.Count == 0)
                tools.AddRange(GetComponentsInChildren<LabTool>());
            if (discovery == null) discovery = GetComponent<ProxyDiscovery>();
        }

        /// <summary>Send a student's message. Ignored (with an error event) while a turn is running.</summary>
        public void Ask(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText)) return;
            if (_busy)
            {
                RaiseError("Still working on the last request.");
                return;
            }
            if (settings == null || string.IsNullOrEmpty(settings.proxyToken))
            {
                RaiseError("ClaudeAgent has no proxy settings or token. Assign a ClaudeProxySettings asset.");
                return;
            }
            StartCoroutine(RunTurn(userText));
        }

        /// <summary>Forget the conversation so far.</summary>
        public void ResetConversation()
        {
            if (_busy) return;
            _messages.Clear();
        }

        IEnumerator RunTurn(string userText)
        {
            SetBusy(true);
            // If anything fails mid-turn, roll back so the history never ends with an unanswered tool_use.
            int checkpoint = _messages.Count;
            _messages.Add(new JObject { ["role"] = "user", ["content"] = userText });
            Log($"You: {userText}");

            bool finished = false;
            for (int round = 0; round < settings.maxRounds && !finished; round++)
            {
                JObject response = null;
                string error = null;
                yield return PostChat(r => response = r, e => error = e);

                if (error != null)
                {
                    Rollback(checkpoint);
                    RaiseError(error);
                    SetBusy(false);
                    yield break;
                }

                var content = (JArray)response["content"] ?? new JArray();
                StripNulls(content);
                _messages.Add(new JObject { ["role"] = "assistant", ["content"] = content });

                string text = string.Join("\n", content
                    .Where(b => (string)b["type"] == "text")
                    .Select(b => (string)b["text"]))
                    .Trim();
                if (text.Length > 0)
                {
                    Log($"Claude: {text}");
                    onAssistantText.Invoke(text);
                }

                string stopReason = (string)response["stop_reason"];
                if (stopReason != "tool_use")
                {
                    finished = true; // end_turn, max_tokens, refusal, etc.
                    break;
                }

                var results = new JArray();
                foreach (var block in content.Where(b => (string)b["type"] == "tool_use"))
                    results.Add(RunTool(block));
                _messages.Add(new JObject { ["role"] = "user", ["content"] = results });
            }

            if (!finished)
            {
                Rollback(checkpoint);
                RaiseError($"Stopped after {settings.maxRounds} rounds without a final answer.");
            }
            SetBusy(false);
        }

        JObject RunTool(JToken block)
        {
            string name = (string)block["name"];
            string id = (string)block["id"];
            var input = block["input"] as JObject ?? new JObject();

            ToolResult result;
            var tool = tools.FirstOrDefault(t => t != null && t.ToolName == name);
            if (tool == null)
            {
                result = ToolResult.Error($"Unknown tool '{name}'.");
            }
            else
            {
                try { result = tool.Execute(input); }
                catch (Exception e) { result = ToolResult.Error(e.Message); }
            }

            Log($"Tool {name} {input.ToString(Formatting.None)} -> {(result.IsError ? "ERROR: " : "")}{result.Content}");
            onToolUsed.Invoke(name);

            var toolResult = new JObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = id,
                ["content"] = result.Content,
            };
            if (result.IsError) toolResult["is_error"] = true;
            return toolResult;
        }

        IEnumerator PostChat(Action<JObject> onSuccess, Action<string> onFailure)
        {
            var body = new JObject
            {
                ["system"] = settings.systemPrompt,
                ["tools"] = new JArray(tools.Where(t => t != null).Select(t => t.Definition)),
                ["messages"] = _messages,
            };

            string url = ProxyUrl.TrimEnd('/') + "/chat";
            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-Proxy-Token", settings.proxyToken);
            req.timeout = settings.timeoutSeconds;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string detail = req.downloadHandler?.text;
                onFailure(req.responseCode == 401
                    ? "Proxy rejected the token. Check proxyToken matches PROXY_TOKEN in .env."
                    : $"Request to {url} failed ({req.responseCode} {req.error}). {detail}");
                yield break;
            }

            try { onSuccess(JObject.Parse(req.downloadHandler.text)); }
            catch (JsonException e) { onFailure($"Could not read proxy response: {e.Message}"); }
        }

        void Rollback(int count)
        {
            while (_messages.Count > count) _messages.RemoveAt(_messages.Count - 1);
        }

        // The proxy returns fields like "citations": null; drop them before echoing content back.
        static void StripNulls(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var p in obj.Properties().Where(p => p.Value.Type == JTokenType.Null).ToList())
                    p.Remove();
                foreach (var p in obj.Properties()) StripNulls(p.Value);
            }
            else if (token is JArray arr)
            {
                foreach (var item in arr) StripNulls(item);
            }
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            onBusyChanged.Invoke(busy);
        }

        void RaiseError(string message)
        {
            Debug.LogWarning($"[ClaudeAgent] {message}", this);
            onError.Invoke(message);
        }

        void Log(string message)
        {
            if (logToConsole) Debug.Log($"[ClaudeAgent] {message}", this);
        }
    }
}
