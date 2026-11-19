using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// set_gravity: changes the strength of gravity for the whole lab (straight down).
    /// The original value is restored when Play mode ends, because in the Editor a runtime change
    /// to Physics.gravity would otherwise be saved into the project's physics settings.
    /// </summary>
    public class SetGravityTool : LabTool
    {
        [Tooltip("Largest gravity Claude may set, in m/s^2.")]
        public float maxGravity = 30f;

        Vector3 _original;

        public override string ToolName => "set_gravity";

        public override string Description =>
            "Change how strong gravity is everywhere in the lab, in m/s^2 (Earth 9.81, Moon 1.62, Mars 3.71, Jupiter 24.79, 0 = floating). " +
            "Gravity always points down. Objects already in the lab are affected immediately. " +
            "Pass a place name or a number.";

        public override JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""place"": { ""type"": ""string"", ""enum"": [""earth"", ""moon"", ""mars"", ""jupiter"", ""zero""] },
                ""strength"": { ""type"": ""number"", ""description"": ""Gravity in m/s^2. Used when place is not given."" }
            }
        }");

        void Awake() => _original = Physics.gravity;

        void OnDestroy() => Physics.gravity = _original;

        public override ToolResult Execute(JObject input)
        {
            string place = ((string)input["place"])?.ToLowerInvariant();
            float g;
            switch (place)
            {
                case "earth": g = 9.81f; break;
                case "moon": g = 1.62f; break;
                case "mars": g = 3.71f; break;
                case "jupiter": g = 24.79f; break;
                case "zero": g = 0f; break;
                case null:
                case "":
                    float? strength = input.Value<float?>("strength");
                    if (strength == null) return ToolResult.Error("Give a place or a strength in m/s^2.");
                    g = strength.Value;
                    break;
                default:
                    return ToolResult.Error($"Unknown place '{place}'. Use earth, moon, mars, jupiter or zero, or give a strength.");
            }

            if (g < 0f || g > maxGravity)
                return ToolResult.Error($"Gravity must be between 0 and {maxGravity} m/s^2.");

            Physics.gravity = Vector3.down * g;
            // Resting bodies sleep and would ignore the change until something touches them.
            foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                body.WakeUp();

            return ToolResult.Ok($"Gravity is now {g:0.##} m/s^2{(place != null ? $" ({place})" : "")}.");
        }
    }
}
