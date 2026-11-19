using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ClaudeLab.EditorTools
{
    /// <summary>
    /// Menu: Claude Lab > Add Lab Assistant to Scene.
    /// Creates (or finds) the proxy settings asset in Assets/ClaudeLab/Settings/Resources, fills in the token
    /// from ../vr-lab-proxy/.env if present, and adds a ready-wired "Claude Lab Assistant" object in front of the camera.
    /// </summary>
    [InitializeOnLoad]
    public static class ClaudeLabSetup
    {
        // Inside a Resources folder so ClaudeProxySettings.Load() finds it by name at runtime.
        // Scenes never link to it, so each computer's own copy (gitignored, holds the token) just works.
        const string SettingsParent = "Assets/ClaudeLab/Settings";
        const string SettingsFolder = SettingsParent + "/Resources";
        const string SettingsPath = SettingsFolder + "/" + ClaudeProxySettings.ResourceName + ".asset";
        const string ObjectMaterialPath = "Assets/ClaudeLab/Materials/LabObject.mat";

        // When the Editor opens or scripts reload, move a settings asset made before 2026-11-18
        // (it lived in Assets/ClaudeLab/Settings) into the Resources folder.
        static ClaudeLabSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) MoveSettingsIntoResources();
            };
        }

        [MenuItem("Claude Lab/Add Lab Assistant to Scene")]
        static void AddToScene()
        {
            FindOrCreateSettings();

            var existing = Object.FindFirstObjectByType<ClaudeAgent>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                int added = AddMissingTools(existing.gameObject);
                Debug.Log(added > 0
                    ? $"[Claude Lab] This scene already has a ClaudeAgent; added {added} new tool(s) to it."
                    : "[Claude Lab] This scene already has a ClaudeAgent with every tool; selected it.", existing);
                return;
            }

            var go = new GameObject("Claude Lab Assistant");
            Undo.RegisterCreatedObjectUndo(go, "Add Claude Lab Assistant");

            // Place the spawn area on the floor about 1.5 m in front of the camera.
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                if (fwd == Vector3.zero) fwd = Vector3.forward;
                Vector3 pos = cam.transform.position + fwd * 1.5f;
                pos.y = FloorHeight(pos);
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
            }

            var agent = go.AddComponent<ClaudeAgent>();
            AddMissingTools(go);
            go.AddComponent<ClaudeTestPrompt>().agent = agent;

            Selection.activeGameObject = go;
            Debug.Log("[Claude Lab] Added Claude Lab Assistant. Start vr-lab-proxy, press Play, then press T.", go);
        }

        [MenuItem("Claude Lab/Select Proxy Settings")]
        static void SelectSettings() => Selection.activeObject = FindOrCreateSettings();

        // Every lab tool the assistant should have. New tools go here.
        static readonly System.Type[] ToolTypes =
        {
            typeof(SpawnObjectTool), typeof(ClearLabTool), typeof(MeasureObjectTool),
            typeof(LabTimerTool), typeof(SetGravityTool), typeof(ReleaseObjectsTool),
            typeof(GetSceneStateTool), typeof(SetPropertyTool),
            typeof(StartMeasurementTool), typeof(ReadMeasurementTool),
        };

        static int AddMissingTools(GameObject go)
        {
            int added = 0;
            foreach (var type in ToolTypes)
            {
                if (go.GetComponentInChildren(type) != null) continue;
                Undo.AddComponent(go, type);
                added++;
            }
            // Spawned shapes need a real URP material, or they render purple in builds.
            var spawner = go.GetComponentInChildren<SpawnObjectTool>();
            if (spawner != null && spawner.objectMaterial == null)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(ObjectMaterialPath);
                if (mat == null)
                {
                    Debug.LogWarning($"[Claude Lab] {ObjectMaterialPath} is missing; spawned objects may be purple in builds.");
                }
                else
                {
                    Undo.RecordObject(spawner, "Assign lab object material");
                    spawner.objectMaterial = mat;
                    EditorUtility.SetDirty(spawner);
                    added++;
                }
            }

            // Not a tool, but every assistant should have it: lets the headset find the proxy on the Wi-Fi.
            if (go.GetComponent<ProxyDiscovery>() == null)
            {
                Undo.AddComponent<ProxyDiscovery>(go);
                added++;
            }
            return added;
        }

        static ClaudeProxySettings FindOrCreateSettings()
        {
            var existing = MoveSettingsIntoResources();
            if (existing != null) return existing;

            EnsureSettingsFolder();
            var settings = ScriptableObject.CreateInstance<ClaudeProxySettings>();
            settings.proxyToken = ReadTokenFromProxyEnv() ?? "";
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();

            if (string.IsNullOrEmpty(settings.proxyToken))
                Debug.LogWarning($"[Claude Lab] Created {SettingsPath}. Paste PROXY_TOKEN from vr-lab-proxy/.env into it.", settings);
            else
                Debug.Log($"[Claude Lab] Created {SettingsPath} with the token from vr-lab-proxy/.env.", settings);
            return settings;
        }

        /// <summary>
        /// Returns this computer's settings asset, moving it into the Resources folder first if it's elsewhere.
        /// Null if there is none yet. Moving keeps its contents (token included).
        /// </summary>
        static ClaudeProxySettings MoveSettingsIntoResources()
        {
            var atPath = AssetDatabase.LoadAssetAtPath<ClaudeProxySettings>(SettingsPath);
            if (atPath != null) return atPath;

            string oldPath = AssetDatabase.FindAssets("t:ClaudeProxySettings")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith(".asset"));
            if (oldPath == null) return null;

            EnsureSettingsFolder();
            string error = AssetDatabase.MoveAsset(oldPath, SettingsPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[Claude Lab] Couldn't move {oldPath} to {SettingsPath}: {error}. Move it there by hand " +
                                 "so the assistant can find it.");
                return AssetDatabase.LoadAssetAtPath<ClaudeProxySettings>(oldPath);
            }
            Debug.Log($"[Claude Lab] Moved the proxy settings to {SettingsPath}. Scenes no longer link to it, " +
                      "so it works on every computer without reconnecting.");
            return AssetDatabase.LoadAssetAtPath<ClaudeProxySettings>(SettingsPath);
        }

        static void EnsureSettingsFolder()
        {
            if (!AssetDatabase.IsValidFolder(SettingsParent))
                AssetDatabase.CreateFolder("Assets/ClaudeLab", "Settings");
            if (!AssetDatabase.IsValidFolder(SettingsFolder))
                AssetDatabase.CreateFolder(SettingsParent, "Resources");
        }

        // The Unity project and vr-lab-proxy sit side by side in this repo.
        static string ReadTokenFromProxyEnv()
        {
            string envPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "vr-lab-proxy", ".env"));
            if (!File.Exists(envPath)) return null;

            foreach (var raw in File.ReadAllLines(envPath))
            {
                string line = raw.Trim();
                if (!line.StartsWith("PROXY_TOKEN")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                return line.Substring(eq + 1).Trim().Trim('"', '\'');
            }
            return null;
        }

        static float FloorHeight(Vector3 above)
        {
            return Physics.Raycast(above + Vector3.up * 2f, Vector3.down, out var hit, 10f) ? hit.point.y : 0f;
        }
    }
}
