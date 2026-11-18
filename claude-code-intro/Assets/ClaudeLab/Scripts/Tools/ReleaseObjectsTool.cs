using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// release_objects: drops every ball and block that is waiting frozen in mid-air, all at the same moment.
    /// The panel's Release button calls ReleaseAll() directly, without going through Claude, so the drop is instant.
    /// </summary>
    public class ReleaseObjectsTool : LabTool
    {
        [Tooltip("The spawner whose objects get released. Defaults to one on this GameObject or its children.")]
        public SpawnObjectTool spawner;

        public override string ToolName => "release_objects";

        public override string Description =>
            "Drop every ball and block that is waiting in mid-air, all at the same moment. " +
            "Students normally do this themselves with the Release button so they can watch. " +
            "Only call it when the student explicitly asks you to release or let go of the objects.";

        public override JObject InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject(),
        };

        void Awake()
        {
            if (spawner == null) spawner = GetComponentInChildren<SpawnObjectTool>();
        }

        /// <summary>Release all waiting objects. Returns how many were released.</summary>
        public int ReleaseAll()
        {
            if (spawner == null) return 0;
            int count = 0;
            foreach (var recorder in spawner.Container.GetComponentsInChildren<FallRecorder>())
                if (recorder.Release()) count++;
            return count;
        }

        /// <summary>How many objects are waiting to be released.</summary>
        public int WaitingCount()
        {
            if (spawner == null) return 0;
            int count = 0;
            foreach (var recorder in spawner.Container.GetComponentsInChildren<FallRecorder>())
                if (recorder.IsWaiting) count++;
            return count;
        }

        public override ToolResult Execute(JObject input)
        {
            if (spawner == null) return ToolResult.Error("No spawner is connected to release_objects.");
            int count = ReleaseAll();
            return ToolResult.Ok(count == 0
                ? "Nothing was waiting to be released."
                : $"Released {count} object{(count == 1 ? "" : "s")}. They are falling now.");
        }
    }
}
