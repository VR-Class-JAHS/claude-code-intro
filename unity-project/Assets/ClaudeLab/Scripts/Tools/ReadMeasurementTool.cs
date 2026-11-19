using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// read_measurement: the numbers behind the graph that start_measurement drew, summarized so Claude can
    /// explain it: each line's start, end, lowest and highest values (with times), plus evenly spaced readings.
    /// </summary>
    public class ReadMeasurementTool : LabTool
    {
        [Tooltip("Defaults to the StartMeasurementTool on this GameObject or its children.")]
        public StartMeasurementTool measurement;

        [Tooltip("How many evenly spaced readings to include per line.")]
        [Range(4, 30)] public int readings = 12;

        public override string ToolName => "read_measurement";

        public override string Description =>
            "Get the numbers from the latest start_measurement graph: for each line, the values at the start and end, " +
            "the lowest and highest values and when they happened, and evenly spaced readings over time (t in seconds from release). " +
            "Use it to explain what the graph shows, with real numbers, when the student asks about it.";

        public override JObject InputSchema => new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject(),
        };

        void Awake()
        {
            if (measurement == null) measurement = GetComponentInChildren<StartMeasurementTool>();
        }

        public override ToolResult Execute(JObject input)
        {
            if (measurement == null) return ToolResult.Error("No start_measurement tool is connected.");

            switch (measurement.Status)
            {
                case StartMeasurementTool.State.Idle:
                    return ToolResult.Ok("Nothing has been measured yet. Use start_measurement first.");
                case StartMeasurementTool.State.WaitingForRelease:
                    return ToolResult.Ok($"The {measurement.Title} graph is set up but the student hasn't pressed Release yet, so there are no readings.");
            }

            var sb = new StringBuilder();
            sb.AppendLine(measurement.Status == StartMeasurementTool.State.Recording
                ? $"{measurement.Title}: still recording, {measurement.Elapsed:0.0} of {measurement.Duration:0.#} s so far."
                : $"{measurement.Title}: finished, {measurement.Elapsed:0.0} s recorded. t = 0 is the start (Release, if the objects were waiting).");
            sb.AppendLine($"Gravity during the reading: {-Physics.gravity.y:0.##} m/s^2.");

            foreach (var s in measurement.AllSeries)
            {
                var pts = s.points;
                if (pts.Count == 0) { sb.AppendLine($"- {s.label}: no readings."); continue; }
                var min = pts.OrderBy(p => p.y).First();
                var max = pts.OrderByDescending(p => p.y).First();
                sb.AppendLine($"- {s.label} ({s.unit}): start {pts[0].y:0.###} at t=0, end {pts[pts.Count - 1].y:0.###} at t={pts[pts.Count - 1].x:0.00} s, " +
                              $"lowest {min.y:0.###} at t={min.x:0.00} s, highest {max.y:0.###} at t={max.x:0.00} s.");

                int n = Mathf.Min(readings, pts.Count);
                var picks = Enumerable.Range(0, n)
                    .Select(i => pts[Mathf.RoundToInt(i * (pts.Count - 1) / (float)Mathf.Max(1, n - 1))])
                    .Select(p => $"{p.x:0.00} s: {p.y:0.###}");
                sb.AppendLine($"  readings: {string.Join("; ", picks)}");
            }
            return ToolResult.Ok(sb.ToString().TrimEnd());
        }
    }
}
