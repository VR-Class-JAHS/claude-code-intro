using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ClaudeLab
{
    /// <summary>
    /// spawn_object: creates a ball, block, pendulum or ramp in the lab.
    /// Objects are placed in a row along the spawn area and parented under one container
    /// so clear_lab can remove them.
    /// </summary>
    public class SpawnObjectTool : LabTool
    {
        [Tooltip("Where objects appear. Its position is the floor point; its forward/right axes orient the row. " +
                 "Defaults to this GameObject.")]
        public Transform spawnArea;

        [Tooltip("Students can pick up balls and blocks with the XR controllers.")]
        public bool makeGrabbable = true;

        [Tooltip("Balls and blocks wait frozen in mid-air until the student presses Release (or Claude calls " +
                 "release_objects). Off: they fall the moment they appear.")]
        public bool holdUntilRelease = true;

        [Tooltip("Material for spawned objects (Assets/ClaudeLab/Materials/LabObject). Needed in builds: shapes made " +
                 "from code otherwise get a built-in material the URP renderer can't draw, so they show up purple.")]
        public Material objectMaterial;

        [Min(1)] public int maxObjects = 30;
        public float spacing = 0.6f;
        public int perRow = 5;

        public Transform Container
        {
            get
            {
                if (_container == null)
                {
                    _container = new GameObject("Claude Spawned Objects").transform;
                    _container.SetParent(SpawnArea, false);
                }
                return _container;
            }
        }

        Transform _container;
        int _spawnIndex;

        public Transform SpawnArea =>spawnArea != null ? spawnArea : transform;

        public override string ToolName => "spawn_object";

        public override string Description =>
            "Create a physics object in the lab. Objects appear side by side in front of the student. " +
            (holdUntilRelease
                ? "A ball or block appears frozen with its bottom at height_m and waits. It does NOT fall yet: " +
                  "it falls when the student presses the Release button on their panel (all waiting objects drop together). " +
                  "Even if the student says 'drop', set the objects up and tell them to press Release when they're ready, " +
                  "so they can predict and watch. "
                : "A ball or block appears with its bottom at height_m and falls immediately under gravity. ") +
            "A pendulum hangs from a pivot at height_m and starts swinging immediately. " +
            "A ramp is a fixed incline whose top edge is at height_m. " +
            "To roll a ball (or slide a block) down a ramp, spawn the ramp first, then spawn the ball or block with " +
            "on_ramp set to that ramp's name: it appears at the top of the slope instead of in the row, and starts " +
            "down the ramp when released.";

        public override JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""type"":      { ""type"": ""string"", ""enum"": [""ball"", ""block"", ""pendulum"", ""ramp""] },
                ""height_m"":  { ""type"": ""number"", ""description"": ""Height above the floor in meters, 0 to 10. Default 1."" },
                ""mass_kg"":   { ""type"": ""number"", ""description"": ""Mass in kilograms for ball, block or pendulum bob. Default 1."" },
                ""angle_deg"": { ""type"": ""number"", ""description"": ""Ramp incline in degrees, 5 to 60. Default 25. Ramps only."" },
                ""on_ramp"":   { ""type"": ""string"", ""description"": ""Ball or block only: the name of a ramp already in the lab (e.g. 'ramp #2'), or 'newest' for the most recent ramp. The object is placed at the top of that ramp's slope, ignoring height_m."" }
            },
            ""required"": [""type""]
        }");

        public override ToolResult Execute(JObject input)
        {
            if (Container.childCount >= maxObjects)
                return ToolResult.Error($"The lab already has {maxObjects} objects. Use clear_lab first.");

            string type = (string)input["type"];
            float height = Mathf.Clamp(input.Value<float?>("height_m") ?? 1f, 0f, 10f);
            float mass = Mathf.Clamp(input.Value<float?>("mass_kg") ?? 1f, 0.01f, 100f);
            float angle = Mathf.Clamp(input.Value<float?>("angle_deg") ?? 25f, 5f, 60f);

            string onRamp = ((string)input["on_ramp"])?.Trim();
            if (!string.IsNullOrEmpty(onRamp))
            {
                if (type != "ball" && type != "block")
                    return ToolResult.Error("on_ramp only works for a ball or a block.");
                return SpawnOnRamp(type, mass, onRamp);
            }

            Vector3 floorPoint = NextFloorPoint();
            if (type == "ramp")
            {
                string tooLong = CheckRampFits(floorPoint, Mathf.Max(height, 0.1f), angle);
                if (tooLong != null) return ToolResult.Error(tooLong);
            }

            GameObject go;
            switch (type)
            {
                case "ball": go = MakeBall(floorPoint, height, mass); break;
                case "block": go = MakeBlock(floorPoint, height, mass); break;
                case "pendulum": go = MakePendulum(floorPoint, Mathf.Max(height, 0.6f), mass); break;
                case "ramp": go = MakeRamp(floorPoint, Mathf.Max(height, 0.1f), angle); break;
                default: return ToolResult.Error($"Unknown type '{type}'. Use ball, block, pendulum or ramp.");
            }

            _spawnIndex++;
            go.name = $"{type} #{_spawnIndex}";
            string extra = type == "ramp" ? $", angle {angle:0.#} deg" : $", mass {mass:0.##} kg";
            string state = type == "ball" || type == "block"
                ? (holdUntilRelease ? " It is waiting for the student to press Release." : " It is falling now.")
                : "";
            return ToolResult.Ok($"Spawned {go.name} at height {height:0.##} m{extra}.{state}");
        }

        /// <summary>
        /// Places a ball or block near the top of a ramp's slope, just above the surface, so it rolls or slides
        /// down when released. Added 2026-11-20: a student asked to "roll a ball down a ramp" and the ball appeared
        /// in the row beside the ramp, with no way for Claude to move it over.
        /// </summary>
        ToolResult SpawnOnRamp(string type, float mass, string rampName)
        {
            Transform ramp = FindRamp(rampName);
            if (ramp == null)
                return ToolResult.Error(rampName.ToLowerInvariant() == "newest"
                    ? "There is no ramp in the lab. Spawn a ramp first."
                    : $"No ramp named '{rampName}'. Use get_scene_state for names, or 'newest'.");

            // The ramp is a thin box: local +z runs up the slope, local +y is the surface normal (see MakeRamp).
            float length = ramp.localScale.z;
            float thickness = ramp.localScale.y;
            float halfHeight = type == "ball" ? 0.1f : 0.125f;
            const float fromTop = 0.2f; // keep the whole object on the slope, not hanging off the top edge
            const float gap = 0.01f;
            Vector3 pos = ramp.position
                          + ramp.forward * (length / 2f - fromTop)
                          + ramp.up * (thickness / 2f + halfHeight + gap);

            GameObject go = type == "ball"
                ? MakeBall(pos - Vector3.up * 0.1f, 0f, mass)     // MakeBall/MakeBlock add their half height back
                : MakeBlock(pos - Vector3.up * 0.125f, 0f, mass);
            if (type == "block") go.transform.rotation = ramp.rotation; // sit flat on the slope

            _spawnIndex++;
            go.name = $"{type} #{_spawnIndex}";

            float angle = Vector3.Angle(Vector3.ProjectOnPlane(ramp.forward, Vector3.up), ramp.forward);
            float bottom = go.GetComponent<Collider>().bounds.min.y - SpawnArea.position.y;
            string state = holdUntilRelease
                ? " It is waiting for the student to press Release, then it goes down the ramp."
                : " It is going down the ramp now.";
            string slideNote = "";
            const float defaultFriction = 0.6f;
            if (type == "block" && defaultFriction >= Mathf.Tan(angle * Mathf.Deg2Rad))
                slideNote = $" Note: with the default friction ({defaultFriction}) a block will NOT slide on a {angle:0.#} deg ramp " +
                            $"(it needs friction below {Mathf.Tan(angle * Mathf.Deg2Rad):0.##}); lower its friction with set_property " +
                            "or use a steeper ramp.";
            return ToolResult.Ok($"Spawned {go.name} ({mass:0.##} kg) at the top of {ramp.name} ({angle:0.#} deg), " +
                                 $"bottom {bottom:0.00} m above the floor.{state}{slideNote}");
        }

        /// <summary>
        /// A ramp runs forward from its floor point by topHeight / tan(angle). If a wall or other fixed object is
        /// closer than that, the top of the ramp would go through it (2026-11-20: a 2 m, 25 deg ramp poked through
        /// the template room's Wall_Front, and a ball placed at its top was pushed off sideways). Returns an error
        /// message with heights and angles that do fit, or null if the ramp fits.
        /// </summary>
        string CheckRampFits(Vector3 floorPoint, float topHeight, float angleDeg)
        {
            float space = FreeSpaceAhead(floorPoint, topHeight);
            if (Fits(topHeight, angleDeg, space)) return null;

            float run = topHeight / Mathf.Tan(angleDeg * Mathf.Deg2Rad);
            // The space depends on the height (a table can block a tall ramp but not a low one), so search for
            // suggestions that really fit instead of scaling: Claude should need only one retry.
            float maxHeight = 0f;
            for (float h = topHeight - 0.05f; h >= 0.1f; h -= 0.05f)
                if (Fits(h, angleDeg, FreeSpaceAhead(floorPoint, h))) { maxHeight = h; break; }
            float minAngle = 0f;
            for (float a = Mathf.Ceil(angleDeg + 1f); a <= 60f; a += 1f)
                if (Fits(topHeight, a, space)) { minAngle = a; break; }

            string options = maxHeight > 0f
                ? $"Use a top height of at most {Mathf.Floor(maxHeight * 10f) / 10f:0.0} m at {angleDeg:0.#} deg"
                : $"No ramp at {angleDeg:0.#} deg fits here";
            if (minAngle > 0f) options += $", or keep {topHeight:0.##} m and make it {minAngle:0} deg or steeper";
            return $"A {topHeight:0.##} m ramp at {angleDeg:0.#} deg runs {run:0.0} m forward, but there is only " +
                   $"{space:0.0} m of floor before a wall or object. {options}.";
        }

        static bool Fits(float topHeight, float angleDeg, float space)
        {
            const float clearance = 0.3f;
            return topHeight / Mathf.Tan(angleDeg * Mathf.Deg2Rad) + clearance <= space;
        }

        /// <summary>Distance forward from a floor point to the nearest wall or fixed object, ignoring the lab's own objects.</summary>
        float FreeSpaceAhead(Vector3 floorPoint, float topHeight)
        {
            float nearest = 50f;
            // Check low, middle and top of where the ramp would be, so a ledge or table at any height counts.
            foreach (float h in new[] { 0.1f, topHeight / 2f, Mathf.Max(topHeight - 0.05f, 0.1f) })
            {
                var hits = Physics.RaycastAll(floorPoint + Vector3.up * h, SpawnArea.forward, nearest,
                                              Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                foreach (var hit in hits)
                {
                    if (hit.collider.transform.IsChildOf(Container)) continue;           // other lab objects
                    if (hit.rigidbody != null && !hit.rigidbody.isKinematic) continue;   // loose props, players' hands
                    nearest = Mathf.Min(nearest, hit.distance);
                }
            }
            return nearest;
        }

        Transform FindRamp(string name)
        {
            Transform newest = null;
            for (int i = 0; i < Container.childCount; i++)
            {
                var t = Container.GetChild(i);
                if (LabQuery.TypeOf(t) != "ramp") continue;
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase)) return t;
                newest = t; // children are in spawn order, so the last ramp is the newest
            }
            return name.ToLowerInvariant() == "newest" ? newest : null;
        }

        Vector3 NextFloorPoint()
        {
            Transform area = SpawnArea;
            int i = Container.childCount;
            int slot = i % perRow;
            int row = i / perRow;
            // Fill each row from the middle outward, right side first: 0, +1, -1, +2, -2 ...
            // so the first objects land straight ahead, away from the panel on the left.
            int steps = (slot + 1) / 2;
            float x = (slot % 2 == 1 ? steps : -steps) * spacing;
            float z = row * spacing;
            return area.position + area.right * x + area.forward * z;
        }

        GameObject MakeBall(Vector3 floor, float height, float mass)
        {
            var go = Primitive(PrimitiveType.Sphere, floor + Vector3.up * (height + 0.1f), Vector3.one * 0.2f,
                new Color(0.95f, 0.45f, 0.2f));
            AddBody(go, mass, grabbable: true);
            return go;
        }

        GameObject MakeBlock(Vector3 floor, float height, float mass)
        {
            var go = Primitive(PrimitiveType.Cube, floor + Vector3.up * (height + 0.125f), Vector3.one * 0.25f,
                new Color(0.3f, 0.55f, 0.95f));
            AddBody(go, mass, grabbable: true);
            return go;
        }

        GameObject MakePendulum(Vector3 floor, float pivotHeight, float mass)
        {
            const float length = 0.5f;
            const float startAngle = 30f;

            var root = new GameObject("pendulum");
            root.transform.SetParent(Container, true);
            root.transform.position = floor + Vector3.up * pivotHeight;
            root.transform.rotation = SpawnArea.rotation;

            var pivot = Primitive(PrimitiveType.Cube, root.transform.position, Vector3.one * 0.05f, Color.gray, root.transform);
            pivot.name = "pivot";
            var pivotBody = pivot.AddComponent<Rigidbody>();
            pivotBody.isKinematic = true;

            Vector3 down = Quaternion.AngleAxis(startAngle, root.transform.forward) * Vector3.down;
            var bob = Primitive(PrimitiveType.Sphere, pivot.transform.position + down * length, Vector3.one * 0.15f,
                new Color(0.9f, 0.8f, 0.2f), root.transform);
            bob.name = "bob";
            var bobBody = AddBody(bob, mass, grabbable: false);

            var hinge = bob.AddComponent<HingeJoint>();
            hinge.connectedBody = pivotBody;
            hinge.autoConfigureConnectedAnchor = false;
            hinge.anchor = bob.transform.InverseTransformPoint(pivot.transform.position);
            hinge.connectedAnchor = Vector3.zero;
            hinge.axis = bob.transform.InverseTransformDirection(root.transform.forward);
            bobBody.angularDamping = 0.01f;

            var line = bob.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = line.endWidth = 0.01f;
            // Copy the primitive's material so the shader is guaranteed to be in builds.
            line.material = new Material(bob.GetComponent<Renderer>().sharedMaterial) { color = Color.black };
            bob.AddComponent<PendulumString>().pivot = pivot.transform;

            return root;
        }

        GameObject MakeRamp(Vector3 floor, float topHeight, float angleDeg)
        {
            float length = topHeight / Mathf.Sin(angleDeg * Mathf.Deg2Rad);
            Vector3 forward = SpawnArea.forward;
            // Center of the slope, with its low edge at floor point and rising away from the student.
            Vector3 center = floor + forward * (length * Mathf.Cos(angleDeg * Mathf.Deg2Rad) / 2f) + Vector3.up * (topHeight / 2f);
            var go = Primitive(PrimitiveType.Cube, center, new Vector3(0.5f, 0.03f, length), new Color(0.6f, 0.6f, 0.65f));
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(-angleDeg, 0f, 0f);
            return go; // static collider, no rigidbody
        }

        GameObject Primitive(PrimitiveType shape, Vector3 position, Vector3 scale, Color color, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.transform.SetParent(parent != null ? parent : Container, true);
            go.transform.SetPositionAndRotation(position, SpawnArea.rotation);
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            if (objectMaterial != null)
                renderer.sharedMaterial = objectMaterial;
            else
                Debug.LogWarning("[SpawnObjectTool] No Object Material assigned; spawned objects may be purple in builds. " +
                                 "Run Claude Lab → Add Lab Assistant to Scene to assign it.", this);
            renderer.material.color = color; // per-object copy of the material, tinted
            return go;
        }

        Rigidbody AddBody(GameObject go, float mass, bool grabbable)
        {
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (grabbable && makeGrabbable)
                go.AddComponent<XRGrabInteractable>();
            // Balls and blocks record each drop so measure_object can report fall time and impact speed.
            if (grabbable)
            {
                var recorder = go.AddComponent<FallRecorder>();
                recorder.floorY = SpawnArea.position.y;
                recorder.startWaiting = holdUntilRelease;
            }
            return body;
        }
    }
}
