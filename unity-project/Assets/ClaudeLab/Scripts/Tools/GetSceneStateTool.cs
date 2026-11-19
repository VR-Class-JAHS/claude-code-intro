using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// get_scene_state: one line per object in the lab (type, mass, where it is, how it's moving, surface settings),
    /// plus gravity, the timer and whether objects wait for Release. Claude calls this before changing things.
    /// </summary>
    public class GetSceneStateTool : LabTool
    {
        [Tooltip("The spawner whose objects get listed. Defaults to one on this GameObject or its children.")]
        public SpawnObjectTool spawner;

        LabTimerTool _timer;

        public override string ToolName => "get_scene_state";

        public override string Description =>
            "List everything in the lab right now: each object's name, type, mass, where it is " +
            "(height of its bottom above the floor; sideways and forward from the student, in meters), " +
            "speed, whether it is waiting for Release, held by a student, falling, swinging or resting, " +
            "and its friction and bounciness. Also gravity, the timer, and whether new balls and blocks wait for Release. " +
            "Call this before set_property so you use the right object names and current values. " +
            "For a ball's or block's last fall time and impact speed, use measure_object.";

        public override JObject InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject(),
        };

        void Awake()
        {
            if (spawner == null) spawner = GetComponentInChildren<SpawnObjectTool>();
            _timer = GetComponentInChildren<LabTimerTool>();
        }

        public override ToolResult Execute(JObject input)
        {
            if (spawner == null) return ToolResult.Error("No spawner is connected to get_scene_state.");

            var sb = new StringBuilder();
            var all = LabQuery.All(spawner);
            if (all.Count == 0) sb.AppendLine("The lab is empty.");
            else sb.AppendLine($"{all.Count} object{(all.Count == 1 ? "" : "s")} in the lab:");

            Transform area = spawner.SpawnArea;
            foreach (var t in all) sb.AppendLine("- " + Describe(t, area));

            float g = -Physics.gravity.y;
            sb.AppendLine($"Gravity: {g:0.##} m/s^2{GravityName(g)}.");
            if (_timer != null)
                sb.AppendLine($"Timer: {_timer.Elapsed:0.00} s, {(_timer.IsRunning ? "running" : "stopped")}.");
            sb.Append(spawner.holdUntilRelease
                ? "New balls and blocks wait in mid-air until the student presses Release."
                : "New balls and blocks fall as soon as they appear.");
            return ToolResult.Ok(sb.ToString());
        }

        static string Describe(Transform t, Transform area)
        {
            string type = LabQuery.TypeOf(t);
            if (type == "ramp")
            {
                float angle = Vector3.Angle(Vector3.ProjectOnPlane(t.forward, Vector3.up), t.forward);
                return $"{t.name}: ramp, incline {angle:0.#} deg, top edge {t.GetComponent<Collider>().bounds.max.y - area.position.y:0.00} m high, " +
                       $"{Where(t.position, area)}. Fixed in place.";
            }

            var body = LabQuery.MovingBody(t);
            if (body == null) return $"{t.name}: {type}.";

            var col = body.GetComponent<Collider>();
            float bottom = (col != null ? col.bounds.min.y : body.position.y) - area.position.y;
            float speed = body.linearVelocity.magnitude;
            string s = $"{t.name}: {type}, {body.mass:0.##} kg, bottom {bottom:0.00} m above the floor, " +
                       $"{Where(body.position, area)}, speed {speed:0.00} m/s, {Motion(body, speed)}";

            var mat = col != null ? col.sharedMaterial : null;
            if (mat != null)
                s += $", friction {mat.dynamicFriction:0.##}, bounciness {mat.bounciness:0.##}";
            else
                s += ", default surface (friction 0.6, bounciness 0)";
            return s + ".";
        }

        static string Motion(Rigidbody body, float speed)
        {
            var rec = body.GetComponent<FallRecorder>();
            if (rec != null)
            {
                if (rec.IsWaiting)
                    return rec.LaunchVelocity.HasValue
                        ? $"waiting for Release, then launched at {rec.LaunchVelocity.Value.magnitude:0.##} m/s"
                        : "waiting for Release";
                if (rec.IsHeld) return "held by a student";
                if (rec.HasDrop && !rec.HasLanded) return "falling";
            }
            if (body.GetComponent<HingeJoint>() != null) return speed > 0.05f ? "swinging" : "hanging still";
            return speed > 0.05f ? "moving" : "resting";
        }

        // Position relative to the spawn area, as the student sees it.
        static string Where(Vector3 p, Transform area)
        {
            Vector3 d = p - area.position;
            float side = Vector3.Dot(d, area.right);
            float ahead = Vector3.Dot(d, area.forward);
            string sideText = Mathf.Abs(side) < 0.05f ? "centered" : $"{Mathf.Abs(side):0.0} m {(side > 0 ? "right" : "left")}";
            return $"{sideText}, {ahead:0.0} m forward of the spawn row";
        }

        static string GravityName(float g)
        {
            if (Mathf.Abs(g - 9.81f) < 0.05f) return " (Earth)";
            if (Mathf.Abs(g - 1.62f) < 0.05f) return " (Moon)";
            if (Mathf.Abs(g - 3.71f) < 0.05f) return " (Mars)";
            if (Mathf.Abs(g - 24.79f) < 0.05f) return " (Jupiter)";
            if (g < 0.01f) return " (none, objects float)";
            return "";
        }
    }
}
