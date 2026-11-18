using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ClaudeLab.EditorTools
{
    /// <summary>
    /// A small local bridge that lets the Unity MCP server (unity-mcp/server.py) inspect and drive this Editor:
    /// list GameObjects, read and change component properties, enter and exit Play mode, read the Console, run
    /// Claude Lab menu items, and ask the lab panel a question in Play mode.
    ///
    /// Protocol: TCP on 127.0.0.1 only, one JSON request per line, one JSON reply per line:
    ///   {"token": "...", "command": "list_gameobjects", "args": {...}}  ->  {"ok": true, "result": ...}
    /// The token is random per Editor start and written to Library/ClaudeLabBridge/token.txt (Library isn't in git),
    /// so only processes on this computer that can read the project folder can connect.
    /// Commands run on Unity's main thread. The bridge restarts after every script reload (including entering
    /// Play mode); the MCP server retries while it's away.
    /// Menu: Claude Lab → Bridge Status.
    /// </summary>
    [InitializeOnLoad]
    public static class ClaudeLabBridge
    {
        public const int Port = 8765;
        const int MaxLogs = 500;

        static TcpListener _listener;
        static Thread _thread;
        static volatile bool _running;
        static string _token;
        static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();
        static readonly List<JObject> Logs = new List<JObject>();
        static int _logSeq;

        static string TokenPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "ClaudeLabBridge", "token.txt"));

        static ClaudeLabBridge()
        {
            if (Application.isBatchMode) return;
            _token = LoadOrCreateToken();
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            Application.logMessageReceivedThreaded += OnLog;
            Start();
        }

        // Keep the same token across script reloads in one Editor session, so the MCP server keeps working.
        static string LoadOrCreateToken()
        {
            string key = "ClaudeLabBridge.Token";
            string token = SessionState.GetString(key, "");
            if (string.IsNullOrEmpty(token))
            {
                var bytes = new byte[24];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
                token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
                SessionState.SetString(key, token);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath));
            File.WriteAllText(TokenPath, token);
            return token;
        }

        static void Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
            }
            catch (SocketException e)
            {
                Debug.LogWarning($"[Claude Lab Bridge] Couldn't listen on 127.0.0.1:{Port} ({e.Message}). Is another Unity Editor running this project?");
                _listener = null;
                return;
            }
            _running = true;
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "ClaudeLabBridge" };
            _thread.Start();
        }

        static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch (Exception) { }
            _listener = null;
        }

        [MenuItem("Claude Lab/Bridge Status")]
        static void Status()
        {
            Debug.Log(_listener != null
                ? $"[Claude Lab Bridge] Listening on 127.0.0.1:{Port}. Token file: {TokenPath}"
                : "[Claude Lab Bridge] Not listening. See earlier warnings in the Console.");
        }

        // ---- networking (background thread) ----

        static void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (Exception) { return; } // listener stopped
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        static void Serve(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
            {
                try
                {
                    client.ReceiveTimeout = 30000;
                    string line = reader.ReadLine();
                    if (line == null) return;
                    writer.WriteLine(Handle(line).ToString(Formatting.None));
                }
                catch (Exception) { /* client went away */ }
            }
        }

        static JObject Handle(string line)
        {
            JObject req;
            try { req = JObject.Parse(line); }
            catch (JsonException) { return Fail("Request isn't JSON."); }

            if ((string)req["token"] != _token) return Fail("Bad token.");
            string command = (string)req["command"] ?? "";
            var args = req["args"] as JObject ?? new JObject();

            // Run on the main thread and wait for it.
            var done = new TaskCompletionSource<JObject>();
            MainThread.Enqueue(() =>
            {
                try { done.SetResult(new JObject { ["ok"] = true, ["result"] = Run(command, args) }); }
                catch (Exception e) { done.SetResult(Fail(e.Message)); }
            });
            return done.Task.Wait(20000) ? done.Task.Result : Fail("Unity didn't answer in 20 s (busy compiling or importing?).");
        }

        static JObject Fail(string message) => new JObject { ["ok"] = false, ["error"] = message };

        static void Pump()
        {
            int n = 0;
            while (n++ < 20 && MainThread.TryDequeue(out var action)) action();
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            lock (Logs)
            {
                Logs.Add(new JObject
                {
                    ["seq"] = ++_logSeq,
                    ["type"] = type.ToString(),
                    ["message"] = message.Length > 2000 ? message.Substring(0, 2000) + "…" : message,
                });
                if (Logs.Count > MaxLogs) Logs.RemoveAt(0);
            }
        }

        // ---- commands (main thread) ----

        static JToken Run(string command, JObject a)
        {
            switch (command)
            {
                case "status": return StatusInfo();
                case "list_gameobjects": return ListGameObjects((string)a["root"], a.Value<int?>("depth") ?? 2, a.Value<bool?>("components") ?? true);
                case "get_component": return GetComponent((string)a["path"], (string)a["component"]);
                case "set_property": return SetProperty((string)a["path"], (string)a["component"], (string)a["property"], a["value"]);
                case "play_mode": return PlayMode((string)a["action"]);
                case "console_logs": return ConsoleLogs(a.Value<int?>("since") ?? 0, a.Value<int?>("max") ?? 50, (string)a["level"]);
                case "run_menu_item": return RunMenuItem((string)a["item"]);
                case "lab_ask": return LabAsk((string)a["text"]);
                case "lab_release": return LabRelease();
                case "lab_status": return LabStatus();
                default: throw new ArgumentException($"Unknown command '{command}'.");
            }
        }

        static JObject StatusInfo()
        {
            var scene = SceneManager.GetActiveScene();
            return new JObject
            {
                ["unity"] = Application.unityVersion,
                ["project"] = Path.GetFileName(Path.GetDirectoryName(Application.dataPath)),
                ["scene"] = scene.path,
                ["scene_dirty"] = scene.isDirty,
                ["play_mode"] = EditorApplication.isPlaying,
                ["paused"] = EditorApplication.isPaused,
                ["compiling"] = EditorApplication.isCompiling,
                ["platform"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
            };
        }

        // Hierarchy paths like "Claude Prompt Panel/Reply Box". Includes inactive objects.
        static GameObject Find(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Give a GameObject path, like \"Claude Lab Assistant\".");
            string[] parts = path.Trim('/').Split('/');
            var roots = AllRoots();
            var current = roots.FirstOrDefault(g => g.name == parts[0]);
            for (int i = 1; current != null && i < parts.Length; i++)
            {
                var child = current.transform.Cast<Transform>().FirstOrDefault(t => t.name == parts[i]);
                current = child != null ? child.gameObject : null;
            }
            if (current == null)
                throw new ArgumentException($"No GameObject at '{path}'. Roots: {string.Join(", ", roots.Select(g => g.name))}.");
            return current;
        }

        static List<GameObject> AllRoots()
        {
            var roots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded) roots.AddRange(s.GetRootGameObjects());
            }
            return roots;
        }

        static JArray ListGameObjects(string root, int depth, bool components)
        {
            depth = Mathf.Clamp(depth, 0, 6);
            var start = string.IsNullOrEmpty(root) ? AllRoots() : new List<GameObject> { Find(root) };
            var list = new JArray();
            int budget = 400;
            foreach (var go in start) Describe(go, "", depth, components, list, ref budget);
            if (budget <= 0) list.Add("… (stopped at 400 objects; ask for a smaller root or depth)");
            return list;
        }

        static void Describe(GameObject go, string parentPath, int depth, bool components, JArray list, ref int budget)
        {
            if (budget-- <= 0) return;
            string path = string.IsNullOrEmpty(parentPath) ? go.name : parentPath + "/" + go.name;
            var o = new JObject { ["path"] = path };
            if (!go.activeSelf) o["active"] = false;
            if (components)
                o["components"] = new JArray(go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name));
            if (go.transform.childCount > 0) o["children"] = go.transform.childCount;
            list.Add(o);
            if (depth <= 0) return;
            foreach (Transform child in go.transform)
                Describe(child.gameObject, path, depth - 1, components, list, ref budget);
        }

        static Component FindComponent(GameObject go, string type)
        {
            var c = go.GetComponents<Component>().FirstOrDefault(x => x != null &&
                (x.GetType().Name == type || x.GetType().FullName == type));
            if (c == null)
                throw new ArgumentException($"'{go.name}' has no {type}. It has: {string.Join(", ", go.GetComponents<Component>().Where(x => x != null).Select(x => x.GetType().Name))}.");
            return c;
        }

        static JObject GetComponent(string path, string type)
        {
            var c = FindComponent(Find(path), type);
            var props = new JObject();
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool enter = true;
            int count = 0;
            while (it.NextVisible(enter) && count++ < 200)
            {
                enter = false; // top-level fields only; arrays and nested structs come back summarized
                if (it.name == "m_Script") continue;
                props[it.propertyPath] = Value(it);
            }
            return new JObject { ["path"] = path, ["component"] = c.GetType().Name, ["properties"] = props };
        }

        static JToken Value(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.longValue;
                case SerializedPropertyType.Boolean: return p.boolValue;
                case SerializedPropertyType.Float: return Math.Round(p.doubleValue, 5);
                case SerializedPropertyType.String: return p.stringValue;
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length ? p.enumDisplayNames[p.enumValueIndex] : p.intValue.ToString();
                case SerializedPropertyType.Color: return "#" + ColorUtility.ToHtmlStringRGBA(p.colorValue);
                case SerializedPropertyType.Vector2: return $"{p.vector2Value.x:0.###}, {p.vector2Value.y:0.###}";
                case SerializedPropertyType.Vector3: return $"{p.vector3Value.x:0.###}, {p.vector3Value.y:0.###}, {p.vector3Value.z:0.###}";
                case SerializedPropertyType.ObjectReference:
                    return p.objectReferenceValue != null ? $"{p.objectReferenceValue.name} ({p.objectReferenceValue.GetType().Name})" : "None";
                default:
                    return p.isArray ? $"({p.propertyType}, {p.arraySize} items)" : $"({p.propertyType})";
            }
        }

        static JObject SetProperty(string path, string type, string property, JToken value)
        {
            var c = FindComponent(Find(path), type);
            var so = new SerializedObject(c);
            var p = so.FindProperty(property);
            if (p == null) throw new ArgumentException($"{type} has no property '{property}'. Use get_component to see the names.");
            string before = Value(p).ToString();

            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: p.longValue = value.Value<long>(); break;
                case SerializedPropertyType.Boolean: p.boolValue = value.Value<bool>(); break;
                case SerializedPropertyType.Float: p.doubleValue = value.Value<double>(); break;
                case SerializedPropertyType.String: p.stringValue = value.ToString(); break;
                case SerializedPropertyType.Enum:
                    if (value.Type == JTokenType.Integer) p.enumValueIndex = value.Value<int>();
                    else
                    {
                        int i = Array.FindIndex(p.enumDisplayNames, n => string.Equals(n, value.ToString(), StringComparison.OrdinalIgnoreCase));
                        if (i < 0) throw new ArgumentException($"'{value}' isn't one of: {string.Join(", ", p.enumDisplayNames)}.");
                        p.enumValueIndex = i;
                    }
                    break;
                case SerializedPropertyType.Color:
                    if (!ColorUtility.TryParseHtmlString(value.ToString(), out var color))
                        throw new ArgumentException("Give a color like \"#FF8800\".");
                    p.colorValue = color;
                    break;
                case SerializedPropertyType.Vector2: { var f = Floats(value, 2); p.vector2Value = new Vector2(f[0], f[1]); break; }
                case SerializedPropertyType.Vector3: { var f = Floats(value, 3); p.vector3Value = new Vector3(f[0], f[1], f[2]); break; }
                default:
                    throw new ArgumentException($"Setting {p.propertyType} properties isn't supported; change it in the Inspector.");
            }

            so.ApplyModifiedProperties(); // records Undo
            if (!EditorApplication.isPlaying) EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            return new JObject
            {
                ["property"] = property,
                ["before"] = before,
                ["after"] = Value(p),
                ["note"] = EditorApplication.isPlaying
                    ? "Changed in Play mode: Unity discards this when Play mode ends."
                    : "Changed in the scene (undoable, not saved). Save the scene in Unity to keep it.",
            };
        }

        static float[] Floats(JToken value, int n)
        {
            var parts = value.Type == JTokenType.Array
                ? value.Select(v => v.Value<float>()).ToArray()
                : value.ToString().Split(',').Select(s => float.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (parts.Length != n) throw new ArgumentException($"Give {n} numbers, like \"{string.Join(", ", Enumerable.Repeat("0", n))}\".");
            return parts;
        }

        static JObject PlayMode(string action)
        {
            switch (action)
            {
                case "enter":
                    if (SceneManager.GetActiveScene().isDirty)
                        return new JObject { ["started"] = false, ["reason"] = "The scene has unsaved changes. Save it in Unity first, or keep them out of Play mode." };
                    EditorApplication.isPlaying = true;
                    return new JObject { ["started"] = true, ["note"] = "Entering Play mode reloads scripts; the bridge is back in a few seconds." };
                case "exit":
                    EditorApplication.isPlaying = false;
                    return new JObject { ["stopped"] = true };
                default:
                    return new JObject { ["play_mode"] = EditorApplication.isPlaying };
            }
        }

        static JObject ConsoleLogs(int since, int max, string level)
        {
            JObject[] items;
            lock (Logs) items = Logs.Where(l => (int)l["seq"] > since).ToArray();
            if (!string.IsNullOrEmpty(level) && level != "all")
            {
                bool errorsOnly = level == "error";
                items = items.Where(l =>
                {
                    string t = (string)l["type"];
                    return errorsOnly ? t == "Error" || t == "Exception" || t == "Assert" : t != "Log";
                }).ToArray();
            }
            max = Mathf.Clamp(max, 1, 200);
            var last = items.Skip(Math.Max(0, items.Length - max));
            return new JObject { ["latest_seq"] = _logSeq, ["logs"] = new JArray(last) };
        }

        static JObject RunMenuItem(string item)
        {
            // Only the project's own menu: running arbitrary editor menu items (Build, File/Quit...) is out of scope.
            if (string.IsNullOrEmpty(item) || !item.StartsWith("Claude Lab/"))
                throw new ArgumentException("Only items under the 'Claude Lab/' menu can be run, e.g. \"Claude Lab/Add Lab Assistant to Scene\".");
            bool ran = EditorApplication.ExecuteMenuItem(item);
            return new JObject { ["ran"] = ran, ["note"] = ran ? "Check console_logs for what it did." : "No such menu item." };
        }

        // ---- the lab in Play mode ----

        static ClaudeLabPanel Panel()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("The lab only runs in Play mode. Use play_mode with action \"enter\" first.");
            var panel = UnityEngine.Object.FindFirstObjectByType<ClaudeLabPanel>();
            if (panel == null) throw new InvalidOperationException("No Claude Prompt Panel in the scene.");
            return panel;
        }

        static JObject LabAsk(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Give the question text.");
            var panel = Panel();
            if (panel.agent != null && panel.agent.IsBusy) throw new InvalidOperationException("The lab assistant is still answering the last question.");
            panel.AskText(text);
            return new JObject { ["sent"] = text, ["note"] = "Use lab_status to see Claude's reply and the objects once it's done." };
        }

        static JObject LabRelease()
        {
            Panel().Release();
            return LabStatus();
        }

        static JObject LabStatus()
        {
            var panel = Panel();
            var state = UnityEngine.Object.FindFirstObjectByType<GetSceneStateTool>();
            return new JObject
            {
                ["busy"] = panel.agent != null && panel.agent.IsBusy,
                ["status_line"] = panel.statusText != null ? panel.statusText.text : null,
                ["reply"] = panel.replyText != null ? panel.replyText.text : null,
                ["scene_state"] = state != null ? state.Execute(new JObject()).Content : "(no get_scene_state tool in the scene)",
            };
        }
    }
}
