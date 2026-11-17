using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>clear_lab: removes everything spawn_object created.</summary>
    public class ClearLabTool : LabTool
    {
        [Tooltip("The spawner whose objects get cleared. Defaults to one on this GameObject or its children.")]
        public SpawnObjectTool spawner;

        public override string ToolName => "clear_lab";

        public override string Description =>
            "Remove every object that spawn_object created, leaving an empty lab.";

        public override JObject InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject(),
        };

        void Awake()
        {
            if (spawner == null) spawner = GetComponentInChildren<SpawnObjectTool>();
        }

        public override ToolResult Execute(JObject input)
        {
            if (spawner == null) return ToolResult.Error("No spawner is connected to clear_lab.");

            Transform container = spawner.Container;
            int count = container.childCount;
            for (int i = count - 1; i >= 0; i--)
            {
                // Detach first: Destroy waits until end of frame, and a spawn_object call
                // in the same reply should already see an empty lab.
                var child = container.GetChild(i);
                child.SetParent(null, true);
                Destroy(child.gameObject);
            }
            return ToolResult.Ok(count == 0 ? "The lab was already empty." : $"Removed {count} objects.");
        }
    }
}
