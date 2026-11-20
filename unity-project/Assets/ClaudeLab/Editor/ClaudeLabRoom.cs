using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ClaudeLab.EditorTools
{
    /// <summary>
    /// Menu: Claude Lab > Build Lab Room. Builds a floor and four walls around the lab, lined up with the
    /// spawn area's direction (the lab faces 30 deg off the template room's walls), under one "Lab Room" object.
    /// Running it again replaces that object. Save the scene afterwards.
    ///
    /// Sizes (2026-11-20), measured along the spawn area's forward axis from the spawn row:
    ///   back wall 3.5 m behind it (the student stands 1.5 m behind the row, plus 2 m behind them),
    ///   front wall 5 m ahead (room for a 2 m, 25 deg ramp: 4.3 m run plus spawn_object's 0.3 m clearance, with 0.4 m spare),
    ///   side walls 3 m either side (the 2.4 m spawn row and the panel on the student's left).
    /// Walls are 3 m high and start at the floor, with closed corners, so balls can't roll under or out.
    /// No ceiling: spawn_object drops from up to 10 m.
    /// </summary>
    public static class ClaudeLabRoom
    {
        const float Behind = 3.5f, Ahead = 5f, HalfWidth = 3f;
        const float WallHeight = 3f, WallThickness = 0.2f, FloorThickness = 0.1f;
        const string RoomName = "Lab Room";
        const string MaterialFolder = "Assets/ClaudeLab/Materials";

        [MenuItem("Claude Lab/Build Lab Room")]
        static void Build()
        {
            var spawner = Object.FindFirstObjectByType<SpawnObjectTool>();
            if (spawner == null)
            {
                Debug.LogWarning("[Claude Lab] No Claude Lab Assistant in the scene. Run Claude Lab > Add Lab Assistant to Scene first.");
                return;
            }

            var old = GameObject.Find(RoomName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            // Level the spawn area's direction, so a tilted spawn area can't tilt the room.
            Transform area = spawner.SpawnArea;
            Vector3 forward = Vector3.ProjectOnPlane(area.forward, Vector3.up).normalized;
            if (forward == Vector3.zero) forward = Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 floor = area.position;

            var room = new GameObject(RoomName);
            Undo.RegisterCreatedObjectUndo(room, "Build Lab Room");
            // Room origin: on the floor, halfway between the back and front walls, centered side to side.
            room.transform.SetPositionAndRotation(floor + forward * ((Ahead - Behind) / 2f), rotation);

            float depth = Behind + Ahead;
            float width = HalfWidth * 2f;
            var floorMat = GetMaterial("LabRoomFloor", new Color(0.78f, 0.78f, 0.8f));
            var wallMat = GetMaterial("LabRoomWall", new Color(0.92f, 0.92f, 0.9f));

            // Floor: top surface exactly at the spawn area's floor height.
            Part(room, "Floor", new Vector3(0f, -FloorThickness / 2f, 0f),
                 new Vector3(width + 2f * WallThickness, FloorThickness, depth + 2f * WallThickness), floorMat);

            float y = WallHeight / 2f;
            float outerDepth = depth + 2f * WallThickness;
            Part(room, "Wall_Front", new Vector3(0f, y, depth / 2f + WallThickness / 2f),
                 new Vector3(width + 2f * WallThickness, WallHeight, WallThickness), wallMat);
            Part(room, "Wall_Back", new Vector3(0f, y, -depth / 2f - WallThickness / 2f),
                 new Vector3(width + 2f * WallThickness, WallHeight, WallThickness), wallMat);
            Part(room, "Wall_Right", new Vector3(HalfWidth + WallThickness / 2f, y, 0f),
                 new Vector3(WallThickness, WallHeight, outerDepth), wallMat);
            Part(room, "Wall_Left", new Vector3(-HalfWidth - WallThickness / 2f, y, 0f),
                 new Vector3(WallThickness, WallHeight, outerDepth), wallMat);

            EditorSceneManager.MarkSceneDirty(room.scene);
            Selection.activeGameObject = room;
            Debug.Log($"[Claude Lab] Built '{RoomName}': {depth:0.#} m deep x {width:0.#} m wide, walls {WallHeight:0.#} m, " +
                      "lined up with the lab. Remove the template room (Environment) and save the scene.");
        }

        static void Part(GameObject room, string name, Vector3 localPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(room.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI |
                                                       StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }

        // A saved URP Lit material, so the room isn't purple in builds (same reason as LabObject.mat).
        static Material GetMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[Claude Lab] URP Lit shader not found; using the lab object material for the room.");
                return AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/LabObject.mat");
            }
            mat = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
