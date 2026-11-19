using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// measure_object: reports height, speed and the last recorded drop for objects spawn_object made.
    /// </summary>
    public class MeasureObjectTool : LabTool
    {
        [Tooltip("The spawner whose objects get measured. Defaults to one on this GameObject or its children.")]
        public SpawnObjectTool spawner;

        public override string ToolName => "measure_object";

        public override string Description =>
            "Measure objects in the lab right now: height of the object's bottom above the floor (m), speed (m/s) and mass. " +
            "For balls and blocks it also reports their last drop, recorded automatically when they were spawned " +
            "or when a student let go of them: release height, fall time until the first impact (s) and impact speed (m/s). " +
            "Use this to answer questions about what just happened, and give students real measured numbers. " +
            "Leave name empty to measure everything.";

        public override JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"", ""description"": ""Object name from spawn_object, e.g. 'ball #1', or a type like 'ball' for all balls. Empty for every object."" }
            }
        }");

        void Awake()
        {
            if (spawner == null) spawner = GetComponentInChildren<SpawnObjectTool>();
        }

        public override ToolResult Execute(JObject input)
        {
            if (spawner == null) return ToolResult.Error("No spawner is connected to measure_object.");

            if (spawner.Container.childCount == 0) return ToolResult.Ok("The lab is empty. Nothing to measure.");

            string name = (string)input["name"];
            var targets = LabQuery.Find(spawner, name);
            if (targets.Count == 0) return ToolResult.Error(LabQuery.NotFound(spawner, name));

            float floorY = spawner.SpawnArea.position.y;
            var sb = new StringBuilder();
            foreach (var t in targets) sb.AppendLine(Describe(t, floorY));
            sb.Append($"Gravity is {-Physics.gravity.y:0.##} m/s^2.");
            return ToolResult.Ok(sb.ToString());
        }

        static string Describe(Transform t, float floorY)
        {
            var recorder = t.GetComponent<FallRecorder>();
            if (recorder != null)
            {
                var body = recorder.Body;
                string s = $"{t.name} ({body.mass:0.##} kg): now {recorder.Height:0.00} m high, speed {body.linearVelocity.magnitude:0.00} m/s";
                if (recorder.IsWaiting) return s + ", frozen in place waiting for the student to press Release.";
                if (recorder.IsHeld) return s + ", a student is holding it.";
                if (!recorder.HasDrop) return s + ".";
                if (!recorder.HasLanded)
                    return s + $". Still falling, released from {recorder.ReleaseHeight:0.00} m.";
                return s + $". Last drop: released at {recorder.ReleaseHeight:0.00} m (highest point {recorder.PeakHeight:0.00} m), " +
                           $"fell for {recorder.FallTime:0.00} s, hit at {recorder.ImpactSpeed:0.00} m/s.";
            }

            var bob = t.Find("bob");
            if (bob != null && bob.TryGetComponent<Rigidbody>(out var bobBody))
            {
                return $"{t.name} (bob {bobBody.mass:0.##} kg): bob is {bob.position.y - floorY:0.00} m high, " +
                       $"moving at {bobBody.linearVelocity.magnitude:0.00} m/s.";
            }

            if (t.TryGetComponent<Rigidbody>(out var other))
                return $"{t.name}: {t.position.y - floorY:0.00} m high, speed {other.linearVelocity.magnitude:0.00} m/s.";

            float angle = Vector3.Angle(Vector3.ProjectOnPlane(t.forward, Vector3.up), t.forward);
            return $"{t.name}: fixed in place, incline {angle:0.#} deg.";
        }
    }
}
