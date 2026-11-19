using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// paint_object: changes the color of objects already in the lab, so students can tell them apart.
    /// The finished version of the workshop's Part 3. It lives outside unity-project so Unity doesn't compile it
    /// next to a participant's own PaintObjectTool. To use it, copy it into Assets/ClaudeLab/Scripts/Tools.
    /// </summary>
    public class PaintObjectTool : LabTool
    {
        [Tooltip("The spawner whose objects get painted. Defaults to one on this GameObject or its children.")]
        public SpawnObjectTool spawner;

        // 1. The name Claude calls.
        public override string ToolName => "paint_object";

        // 2. What Claude reads to decide when to use it.
        public override string Description =>
            "Change the color of objects already in the lab, so students can tell them apart. " +
            "name: an object like 'ball #1', or a type like 'ball' to paint every ball. " +
            "Use get_scene_state first if you don't know the names.";

        // 3. The input Claude must send, as JSON Schema.
        public override JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"":  { ""type"": ""string"", ""description"": ""An object like 'ball #1', or a type like 'ball'."" },
                ""color"": { ""type"": ""string"", ""enum"": [""red"", ""orange"", ""yellow"", ""green"", ""blue"", ""purple"", ""white"", ""black""] }
            },
            ""required"": [""name"", ""color""]
        }");

        void Awake()
        {
            if (spawner == null) spawner = GetComponentInChildren<SpawnObjectTool>();
        }

        // 4. What happens when Claude calls it. The returned text goes back to Claude.
        public override ToolResult Execute(JObject input)
        {
            string name = (string)input["name"];
            string colorName = ((string)input["color"])?.ToLowerInvariant();

            Color color;
            switch (colorName)
            {
                case "red": color = new Color(0.9f, 0.2f, 0.2f); break;
                case "orange": color = new Color(0.95f, 0.55f, 0.15f); break;
                case "yellow": color = new Color(0.95f, 0.85f, 0.2f); break;
                case "green": color = new Color(0.25f, 0.75f, 0.3f); break;
                case "blue": color = new Color(0.25f, 0.45f, 0.95f); break;
                case "purple": color = new Color(0.6f, 0.3f, 0.85f); break;
                case "white": color = Color.white; break;
                case "black": color = new Color(0.1f, 0.1f, 0.1f); break;
                default:
                    return ToolResult.Error($"Unknown color '{colorName}'. Use red, orange, yellow, green, blue, purple, white or black.");
            }

            var targets = LabQuery.Find(spawner, name);
            if (targets.Count == 0) return ToolResult.Error(LabQuery.NotFound(spawner, name));

            foreach (var t in targets)
            {
                // A pendulum is painted on its bob; balls, blocks and ramps on themselves.
                // MeshRenderer, not Renderer: the bob also has a LineRenderer for its string.
                Rigidbody body = LabQuery.MovingBody(t);
                var renderer = (body != null ? body.transform : t).GetComponent<MeshRenderer>();
                if (renderer != null) renderer.material.color = color;
            }

            return ToolResult.Ok($"Painted {string.Join(", ", targets.Select(t => t.name))} {colorName}.");
        }
    }
}
