using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// set_property: changes an object that's already in the lab — mass, height, velocity, friction or bounciness —
    /// so requests like "make the ball twice as heavy and drop it from higher" work without spawning a new one.
    /// Gravity is lab-wide and stays in set_gravity.
    /// </summary>
    public class SetPropertyTool : LabTool
    {
        [Tooltip("The spawner whose objects get changed. Defaults to one on this GameObject or its children.")]
        public SpawnObjectTool spawner;

        public override string ToolName => "set_property";

        public override string Description =>
            "Change one property of an object already in the lab. Use get_scene_state first for names and current values. " +
            "name: an object like 'ball #1', or a type like 'ball' to change every ball. Properties: " +
            "mass (kg, 0.01-100; balls, blocks, pendulum bobs); " +
            "height (m, 0-10; balls and blocks only): moves the object so its bottom is that high and stops it there. " +
            "If new balls and blocks wait for Release, it waits again for the student to press Release; otherwise it falls at once. " +
            "velocity (m/s, 0-30, with direction up/down/forward/back/left/right as the student sees it; balls, blocks, pendulum bobs): " +
            "a waiting object keeps waiting and is launched with that velocity when Release is pressed; a free object is pushed at once. " +
            "friction (0 = ice, 1 = rubber; any object, ramps included); " +
            "bounciness (0 = no bounce, 1 = bounces back to full height; any object). " +
            "To change gravity use set_gravity. For a series of changes, call this once per property.";

        public override JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"":      { ""type"": ""string"", ""description"": ""Object name like 'ball #1', or a type like 'ball' for all of them."" },
                ""property"":  { ""type"": ""string"", ""enum"": [""mass"", ""height"", ""velocity"", ""friction"", ""bounciness""] },
                ""value"":     { ""type"": ""number"" },
                ""direction"": { ""type"": ""string"", ""enum"": [""up"", ""down"", ""forward"", ""back"", ""left"", ""right""], ""description"": ""For velocity only. Default forward (away from the student)."" }
            },
            ""required"": [""name"", ""property"", ""value""]
        }");

        void Awake()
        {
            if (spawner == null) spawner = GetComponentInChildren<SpawnObjectTool>();
        }

        public override ToolResult Execute(JObject input)
        {
            if (spawner == null) return ToolResult.Error("No spawner is connected to set_property.");

            string name = (string)input["name"];
            string property = ((string)input["property"])?.ToLowerInvariant();
            float? value = input.Value<float?>("value");
            if (value == null) return ToolResult.Error("Give a value.");

            List<Transform> targets = LabQuery.Find(spawner, name);
            if (targets.Count == 0) return ToolResult.Error(LabQuery.NotFound(spawner, name));

            var results = new StringBuilder();
            int changed = 0;
            foreach (var t in targets)
            {
                string line = Apply(t, property, value.Value, (string)input["direction"], out bool ok);
                results.AppendLine(line);
                if (ok) changed++;
            }

            string text = results.ToString().TrimEnd();
            return changed > 0 ? ToolResult.Ok(text) : ToolResult.Error(text);
        }

        string Apply(Transform t, string property, float v, string direction, out bool ok)
        {
            ok = false;
            string type = LabQuery.TypeOf(t);
            Rigidbody body = LabQuery.MovingBody(t);
            var recorder = body != null ? body.GetComponent<FallRecorder>() : null;

            switch (property)
            {
                case "mass":
                    if (body == null) return $"{t.name}: a {type} has no mass to change.";
                    if (v < 0.01f || v > 100f) return $"{t.name}: mass must be 0.01 to 100 kg.";
                    float old = body.mass;
                    body.mass = v;
                    ok = true;
                    return $"{t.name}: mass {old:0.##} kg -> {v:0.##} kg.";

                case "height":
                    if (recorder == null) return $"{t.name}: only balls and blocks can be moved to a new height.";
                    if (recorder.IsHeld) return $"{t.name}: a student is holding it; ask them to let go first.";
                    if (v < 0f || v > 10f) return $"{t.name}: height must be 0 to 10 m.";
                    var col = body.GetComponent<Collider>();
                    float bottom = col.bounds.min.y - spawner.SpawnArea.position.y;
                    t.position += Vector3.up * (v - bottom);
                    t.rotation = spawner.SpawnArea.rotation; // stand it upright again
                    Physics.SyncTransforms(); // so the drop record starts from the new height
                    recorder.Reset(wait: spawner.holdUntilRelease);
                    ok = true;
                    return spawner.holdUntilRelease
                        ? $"{t.name}: moved to {v:0.##} m. It is waiting for the student to press Release."
                        : $"{t.name}: moved to {v:0.##} m and is falling now.";

                case "velocity":
                    if (body == null) return $"{t.name}: a {type} can't move.";
                    if (v < 0f || v > 30f) return $"{t.name}: speed must be 0 to 30 m/s.";
                    Vector3 vel = Direction(direction) * v;
                    string dir = string.IsNullOrEmpty(direction) ? "forward" : direction;
                    ok = true;
                    if (recorder != null && recorder.IsWaiting)
                    {
                        recorder.LaunchVelocity = vel;
                        return $"{t.name}: will be launched {dir} at {v:0.##} m/s when the student presses Release.";
                    }
                    if (recorder != null) recorder.Launch(vel);
                    else { body.linearVelocity = vel; body.WakeUp(); }
                    return $"{t.name}: pushed {dir} at {v:0.##} m/s.";

                case "friction":
                case "bounciness":
                    if (v < 0f || v > 1f) return $"{t.name}: {property} must be 0 to 1.";
                    var collider = body != null ? body.GetComponent<Collider>() : t.GetComponent<Collider>();
                    if (collider == null) return $"{t.name}: has no surface to change.";
                    var mat = SurfaceOf(collider);
                    if (property == "friction")
                    {
                        mat.dynamicFriction = v;
                        mat.staticFriction = v;
                    }
                    else
                    {
                        mat.bounciness = v;
                        // Maximum: the floor's zero bounciness would otherwise cancel it out.
                        mat.bounceCombine = PhysicsMaterialCombine.Maximum;
                    }
                    ok = true;
                    return $"{t.name}: {property} set to {v:0.##}.";

                default:
                    return $"Unknown property '{property}'. Use mass, height, velocity, friction or bounciness (gravity: set_gravity).";
            }
        }

        // The object's own surface material, created on first change so objects never share one.
        static PhysicsMaterial SurfaceOf(Collider collider)
        {
            var current = collider.sharedMaterial;
            if (current != null && current.name.EndsWith(" (lab)")) return current;
            var mat = new PhysicsMaterial($"{collider.name} (lab)")
            {
                dynamicFriction = current != null ? current.dynamicFriction : 0.6f,
                staticFriction = current != null ? current.staticFriction : 0.6f,
                bounciness = current != null ? current.bounciness : 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = current != null ? current.bounceCombine : PhysicsMaterialCombine.Average,
            };
            collider.sharedMaterial = mat;
            return mat;
        }

        Vector3 Direction(string direction)
        {
            Transform area = spawner.SpawnArea;
            switch (direction)
            {
                case "up": return Vector3.up;
                case "down": return Vector3.down;
                case "back": return -Vector3.ProjectOnPlane(area.forward, Vector3.up).normalized;
                case "left": return -Vector3.ProjectOnPlane(area.right, Vector3.up).normalized;
                case "right": return Vector3.ProjectOnPlane(area.right, Vector3.up).normalized;
                default: return Vector3.ProjectOnPlane(area.forward, Vector3.up).normalized; // forward
            }
        }
    }
}
