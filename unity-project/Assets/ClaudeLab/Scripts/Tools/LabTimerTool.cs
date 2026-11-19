using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// lab_timer: a stopwatch students can see. Claude can start, stop, reset or read it.
    /// Shows on a TextMeshPro label if one is assigned (the prompt panel builder does this).
    /// </summary>
    public class LabTimerTool : LabTool
    {
        [Tooltip("Optional label that shows the time, e.g. on the prompt panel.")]
        public TMP_Text display;

        float _elapsed;
        bool _running;

        public float Elapsed => _elapsed;
        public bool IsRunning => _running;

        public override string ToolName => "lab_timer";

        public override string Description =>
            "A stopwatch shown to the student. Actions: start (keeps any time already on it), stop, reset (sets to 0 and stops), read. " +
            "Your replies reach the student a second or more after you decide, so this suits slow things a person times " +
            "(counting pendulum swings, a race the student starts). For drops, measure_object already records exact fall times.";

        public override JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""action"": { ""type"": ""string"", ""enum"": [""start"", ""stop"", ""reset"", ""read""] }
            },
            ""required"": [""action""]
        }");

        void Start() => Refresh();

        void Update()
        {
            if (!_running) return;
            _elapsed += Time.deltaTime;
            Refresh();
        }

        public override ToolResult Execute(JObject input)
        {
            string action = (string)input["action"];
            switch (action)
            {
                case "start":
                    _running = true;
                    Refresh();
                    return ToolResult.Ok($"Timer started at {_elapsed:0.00} s.");
                case "stop":
                    _running = false;
                    Refresh();
                    return ToolResult.Ok($"Timer stopped at {_elapsed:0.00} s.");
                case "reset":
                    _running = false;
                    _elapsed = 0f;
                    Refresh();
                    return ToolResult.Ok("Timer reset to 0.00 s.");
                case "read":
                    return ToolResult.Ok($"Timer shows {_elapsed:0.00} s and is {(_running ? "running" : "stopped")}.");
                default:
                    return ToolResult.Error($"Unknown action '{action}'. Use start, stop, reset or read.");
            }
        }

        void Refresh()
        {
            if (display != null)
                display.text = $"Timer: {_elapsed:0.00} s{(_running ? "" : "  (stopped)")}";
        }
    }
}
