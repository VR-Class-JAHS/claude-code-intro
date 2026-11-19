using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace ClaudeLab
{
    /// <summary>
    /// start_measurement: records a quantity over time for one or more objects (height, speed, vertical velocity,
    /// or energy) and graphs it on the panel. Recording runs here every physics step, because Claude's next
    /// request arrives seconds later. If the objects are waiting for Release, recording starts at Release, so t = 0
    /// is the moment they let go. read_measurement gives Claude the numbers afterwards.
    /// </summary>
    public class StartMeasurementTool : LabTool
    {
        public enum State { Idle, WaitingForRelease, Recording, Done }

        public class Series
        {
            public string label;
            public string unit;
            public Color color;
            public readonly List<Vector2> points = new List<Vector2>(); // (seconds, value)
        }

        [Tooltip("The spawner whose objects get measured. Defaults to one on this GameObject or its children.")]
        public SpawnObjectTool spawner;

        [Tooltip("Readings per second.")]
        [Range(5, 50)] public int samplesPerSecond = 25;

        [Tooltip("Longest recording Claude may ask for, in seconds.")]
        public float maxDuration = 20f;

        /// <summary>Raised whenever new readings arrive or a measurement starts or ends. The panel's graph listens.</summary>
        public UnityEvent onChanged = new UnityEvent();

        public State Status { get; private set; } = State.Idle;
        public string Title { get; private set; } = "";
        public float Duration { get; private set; }
        public float Elapsed { get; private set; }
        public IReadOnlyList<Series> AllSeries => _series;

        static readonly Color[] Palette =
        {
            new Color(0.98f, 0.62f, 0.25f), // orange
            new Color(0.35f, 0.70f, 1.00f), // blue
            new Color(0.55f, 0.85f, 0.45f), // green
            new Color(0.90f, 0.45f, 0.80f), // pink
        };

        readonly List<Series> _series = new List<Series>();
        readonly List<(Rigidbody body, FallRecorder recorder, string quantity, Series series)> _targets =
            new List<(Rigidbody, FallRecorder, string, Series)>();
        float _sampleTimer;

        public override string ToolName => "start_measurement";

        public override string Description =>
            "Record a quantity over time and draw it as a live graph on the student's panel. " +
            "quantity: height (m, the object's bottom above the floor), speed (m/s), vertical_velocity (m/s, negative = falling), " +
            "or energy (kinetic, potential and total, in joules; potential is measured from the floor). " +
            "name: an object like 'ball #1', or a type like 'ball' for all of them (up to 4 lines on one graph; energy is one object only). " +
            "If the objects are waiting for Release, recording starts when the student presses Release, so set up the objects, " +
            "call this, then tell the student to press Release and watch the graph. Otherwise it starts now. " +
            "Starting a new measurement replaces the old graph. The readings aren't available until the recording ends; " +
            "use read_measurement later (for example when the student asks what happened) to get the numbers.";

        public override JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""name"":       { ""type"": ""string"", ""description"": ""Object name like 'ball #1', or a type like 'ball'."" },
                ""quantity"":   { ""type"": ""string"", ""enum"": [""height"", ""speed"", ""vertical_velocity"", ""energy""] },
                ""duration_s"": { ""type"": ""number"", ""description"": ""How long to record, 1 to 20 seconds. Default 3."" }
            },
            ""required"": [""name"", ""quantity""]
        }");

        void Awake()
        {
            if (spawner == null) spawner = GetComponentInChildren<SpawnObjectTool>();
        }

        public override ToolResult Execute(JObject input)
        {
            if (spawner == null) return ToolResult.Error("No spawner is connected to start_measurement.");

            string name = (string)input["name"];
            string quantity = ((string)input["quantity"])?.ToLowerInvariant();
            float duration = Mathf.Clamp(input.Value<float?>("duration_s") ?? 3f, 1f, maxDuration);

            var found = LabQuery.Find(spawner, name)
                .Select(t => (t, body: LabQuery.MovingBody(t)))
                .Where(x => x.body != null)
                .ToList();
            if (found.Count == 0)
            {
                var any = LabQuery.Find(spawner, name);
                return ToolResult.Error(any.Count == 0
                    ? LabQuery.NotFound(spawner, name)
                    : $"{string.Join(", ", any.Select(t => t.name))} can't move, so there's nothing to record.");
            }

            _series.Clear();
            _targets.Clear();
            switch (quantity)
            {
                case "height":
                case "speed":
                case "vertical_velocity":
                    if (found.Count > Palette.Length)
                        return ToolResult.Error($"That's {found.Count} objects; a graph shows at most {Palette.Length}. Name fewer objects.");
                    for (int i = 0; i < found.Count; i++)
                        AddTarget(found[i].body, quantity, $"{found[i].t.name}", Palette[i]);
                    Title = $"{Pretty(quantity)} ({Unit(quantity)})";
                    break;

                case "energy":
                    if (found.Count > 1)
                        return ToolResult.Error($"Energy graphs one object at a time; '{name}' matched {found.Count}. Pick one, like '{found[0].t.name}'.");
                    var b = found[0].body;
                    AddTarget(b, "kinetic", "kinetic", Palette[0]);
                    AddTarget(b, "potential", "potential", Palette[1]);
                    AddTarget(b, "total", "total", Palette[2]);
                    Title = $"{found[0].t.name} energy (J)";
                    break;

                default:
                    return ToolResult.Error($"Unknown quantity '{quantity}'. Use height, speed, vertical_velocity or energy.");
            }

            Duration = duration;
            Elapsed = 0f;
            _sampleTimer = 0f;
            bool anyWaiting = _targets.Any(x => x.recorder != null && x.recorder.IsWaiting);
            Status = anyWaiting ? State.WaitingForRelease : State.Recording;
            if (Status == State.Recording) Sample(); // the reading at t = 0
            onChanged.Invoke();

            string what = string.Join(", ", _series.Select(s => s.label));
            return ToolResult.Ok(anyWaiting
                ? $"Ready to record {Title} for {duration:0.#} s ({what}). Recording starts when the student presses Release; the graph is on their panel."
                : $"Recording {Title} for {duration:0.#} s ({what}) now; the graph is on the student's panel.");
        }

        void AddTarget(Rigidbody body, string quantity, string label, Color color)
        {
            var s = new Series { label = label, unit = Unit(quantity), color = color };
            _series.Add(s);
            _targets.Add((body, body.GetComponent<FallRecorder>(), quantity, s));
        }

        void FixedUpdate()
        {
            if (Status == State.WaitingForRelease)
            {
                // Start the clock the moment any waiting object is let go (Release button, release_objects or a grab).
                if (_targets.Any(x => x.body == null)) { Stop(); return; }
                if (_targets.All(x => x.recorder == null || !x.recorder.IsWaiting))
                {
                    Status = State.Recording;
                    Elapsed = 0f;
                    _sampleTimer = 0f;
                    Sample();
                    onChanged.Invoke();
                }
                return;
            }
            if (Status != State.Recording) return;

            if (_targets.Any(x => x.body == null)) { Stop(); return; } // cleared from the lab

            Elapsed += Time.fixedDeltaTime;
            _sampleTimer += Time.fixedDeltaTime;
            float interval = 1f / samplesPerSecond;
            if (_sampleTimer >= interval)
            {
                _sampleTimer -= interval;
                Sample();
                onChanged.Invoke();
            }
            if (Elapsed >= Duration)
            {
                Sample();
                Stop();
            }
        }

        void Sample()
        {
            float floorY = spawner.SpawnArea.position.y;
            float g = -Physics.gravity.y;
            foreach (var (body, _, quantity, series) in _targets)
            {
                if (body == null) continue;
                var col = body.GetComponent<Collider>();
                float h = (col != null ? col.bounds.min.y : body.position.y) - floorY;
                Vector3 v = body.linearVelocity;
                float value = quantity switch
                {
                    "height" => h,
                    "speed" => v.magnitude,
                    "vertical_velocity" => v.y,
                    "kinetic" => 0.5f * body.mass * v.sqrMagnitude,
                    "potential" => body.mass * g * h,
                    "total" => 0.5f * body.mass * v.sqrMagnitude + body.mass * g * h,
                    _ => 0f,
                };
                series.points.Add(new Vector2(Elapsed, value));
            }
        }

        void Stop()
        {
            Status = State.Done;
            onChanged.Invoke();
        }

        static string Pretty(string q) => q switch
        {
            "vertical_velocity" => "vertical velocity",
            _ => q,
        };

        static string Unit(string q) => q switch
        {
            "height" => "m",
            "speed" => "m/s",
            "vertical_velocity" => "m/s",
            _ => "J",
        };
    }
}
