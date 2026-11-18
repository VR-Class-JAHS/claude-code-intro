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
            "A ball or block is released at height_m and falls under gravity. " +
            "A pendulum hangs from a pivot at height_m and starts swinging. " +
            "A ramp is a fixed incline whose top edge is at height_m.";

        public override JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""type"":      { ""type"": ""string"", ""enum"": [""ball"", ""block"", ""pendulum"", ""ramp""] },
                ""height_m"":  { ""type"": ""number"", ""description"": ""Height above the floor in meters, 0 to 10. Default 1."" },
                ""mass_kg"":   { ""type"": ""number"", ""description"": ""Mass in kilograms for ball, block or pendulum bob. Default 1."" },
                ""angle_deg"": { ""type"": ""number"", ""description"": ""Ramp incline in degrees, 5 to 60. Default 25. Ramps only."" }
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

            Vector3 floorPoint = NextFloorPoint();
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
            return ToolResult.Ok($"Spawned {go.name} at height {height:0.##} m{extra}.");
        }

        Vector3 NextFloorPoint()
        {
            Transform area = SpawnArea;
            int i = Container.childCount;
            int col = i % perRow;
            int row = i / perRow;
            float x = (col - (perRow - 1) / 2f) * spacing;
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
            go.GetComponent<Renderer>().material.color = color;
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
                go.AddComponent<FallRecorder>().floorY = SpawnArea.position.y;
            return body;
        }
    }
}
