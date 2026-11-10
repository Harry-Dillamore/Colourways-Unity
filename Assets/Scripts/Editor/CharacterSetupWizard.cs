using UnityEditor;
using UnityEngine;

public static class CharacterSetupWizard
{
    [MenuItem("Tools/Character Controller/Setup Complete Demo (Ground + First-Person Player)", false, 1)]
    public static void SetupCompleteFirstPersonScene()
    {
        EnsureGroundPlane();
        EnsureArenaWallsAndObstacles();
        EnsurePaintBatcher();
        CreateFirstPersonPlayer();
        Debug.Log("[CharacterSetup] First-person demo scene configured! Run into any wall to explode into paint.");
    }

    [MenuItem("Tools/Character Controller/Setup Complete Demo (Ground + Third-Person Player)", false, 2)]
    public static void SetupCompleteThirdPersonScene()
    {
        EnsureGroundPlane();
        EnsureArenaWallsAndObstacles();
        EnsurePaintBatcher();
        CreateThirdPersonPlayer();
        Debug.Log("[CharacterSetup] Third-person demo scene configured! Run into any wall to explode into paint.");
    }

    [MenuItem("Tools/Character Controller/Add First-Person Player", false, 20)]
    public static void CreateFirstPersonPlayer()
    {
        EnsurePaintBatcher();
        GameObject player = CreatePlayerBase("FirstPersonPlayer", out CharacterController cc);
        player.transform.position = new Vector3(0f, 1f, 0f);

        // Find or create Camera
        Camera mainCam = Camera.main;
        Transform camTransform;

        if (mainCam != null)
        {
            camTransform = mainCam.transform;
            Undo.SetTransformParent(camTransform, player.transform, "Parent Main Camera to Player");
        }
        else
        {
            GameObject camObj = new GameObject("PlayerCamera", typeof(Camera), typeof(AudioListener));
            Undo.RegisterCreatedObjectUndo(camObj, "Create Player Camera");
            camTransform = camObj.transform;
            camTransform.SetParent(player.transform);
        }

        camTransform.localPosition = new Vector3(0f, 0.6f, 0.15f); // Eye level (~1.6m)
        camTransform.localRotation = Quaternion.identity;

        PlayerController controller = Undo.AddComponent<PlayerController>(player);
        controller.mode = PlayerController.ControlMode.FirstPerson;
        controller.playerCamera = camTransform;

        Undo.AddComponent<PaintExplosionSystem>(player);

        Selection.activeGameObject = player;
    }

    [MenuItem("Tools/Character Controller/Add Third-Person Player", false, 21)]
    public static void CreateThirdPersonPlayer()
    {
        EnsurePaintBatcher();
        GameObject player = CreatePlayerBase("ThirdPersonPlayer", out CharacterController cc);
        player.transform.position = new Vector3(0f, 1f, 0f);

        // Create camera pivot
        GameObject pivotObj = new GameObject("CameraPivot");
        Undo.RegisterCreatedObjectUndo(pivotObj, "Create Camera Pivot");
        Transform pivotTransform = pivotObj.transform;
        pivotTransform.SetParent(player.transform);
        pivotTransform.localPosition = new Vector3(0f, 1.4f, 0f);
        pivotTransform.localRotation = Quaternion.identity;

        // Find or create Camera
        Camera mainCam = Camera.main;
        Transform camTransform;

        if (mainCam != null)
        {
            camTransform = mainCam.transform;
            Undo.SetTransformParent(camTransform, pivotTransform, "Parent Main Camera to Pivot");
        }
        else
        {
            GameObject camObj = new GameObject("PlayerCamera", typeof(Camera), typeof(AudioListener));
            Undo.RegisterCreatedObjectUndo(camObj, "Create Player Camera");
            camTransform = camObj.transform;
            camTransform.SetParent(pivotTransform);
        }

        camTransform.localPosition = new Vector3(0f, 0.6f, -3.8f);
        camTransform.localRotation = Quaternion.Euler(10f, 0f, 0f);

        PlayerController controller = Undo.AddComponent<PlayerController>(player);
        controller.mode = PlayerController.ControlMode.ThirdPerson;
        controller.playerCamera = camTransform;
        controller.cameraPivot = pivotTransform;

        Undo.AddComponent<PaintExplosionSystem>(player);

        Selection.activeGameObject = player;
    }

    [MenuItem("Tools/Character Controller/Add Ground Plane", false, 40)]
    public static void EnsureGroundPlane()
    {
        GameObject existingGround = GameObject.Find("Ground");
        if (existingGround != null)
        {
            return;
        }

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(50f, 1f, 50f);

        Undo.RegisterCreatedObjectUndo(ground, "Create Ground Plane");
    }

    [MenuItem("Tools/Character Controller/Add Arena Walls & Obstacles", false, 41)]
    public static void EnsureArenaWallsAndObstacles()
    {
        GameObject existingObstacles = GameObject.Find("Arena_Environment");
        if (existingObstacles != null)
        {
            return;
        }

        GameObject group = new GameObject("Arena_Environment");
        Undo.RegisterCreatedObjectUndo(group, "Create Arena Group");

        float size = 50f;
        float half = size * 0.5f;
        float wallHeight = 4f;
        float wallThick = 1f;

        // 4 Boundary Walls
        CreateWall(group.transform, "Wall_North", new Vector3(0f, wallHeight * 0.5f, half), new Vector3(size, wallHeight, wallThick));
        CreateWall(group.transform, "Wall_South", new Vector3(0f, wallHeight * 0.5f, -half), new Vector3(size, wallHeight, wallThick));
        CreateWall(group.transform, "Wall_East", new Vector3(half, wallHeight * 0.5f, 0f), new Vector3(wallThick, wallHeight, size));
        CreateWall(group.transform, "Wall_West", new Vector3(-half, wallHeight * 0.5f, 0f), new Vector3(wallThick, wallHeight, size));

        // Center Obstacle Pillars
        CreateWall(group.transform, "Pillar_1", new Vector3(6f, 2f, 6f), new Vector3(2.5f, 4f, 2.5f));
        CreateWall(group.transform, "Pillar_2", new Vector3(-6f, 2f, 6f), new Vector3(2.5f, 4f, 2.5f));
        CreateWall(group.transform, "Pillar_3", new Vector3(0f, 2f, 12f), new Vector3(5f, 4f, 1.5f));

        // Low / medium platforms
        CreateWall(group.transform, "Platform_Step", new Vector3(-8f, 0.4f, -4f), new Vector3(4f, 0.8f, 4f));

        // Ramp for slope testing
        GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "Ramp";
        ramp.transform.SetParent(group.transform);
        ramp.transform.position = new Vector3(10f, 1.0f, -4f);
        ramp.transform.localScale = new Vector3(4f, 0.4f, 6f);
        ramp.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
        Undo.RegisterCreatedObjectUndo(ramp, "Create Ramp");
    }

    [MenuItem("Tools/Character Controller/Add Paint Decal Batcher", false, 50)]
    public static void EnsurePaintBatcher()
    {
        PaintDecalBatcher existing = Object.FindFirstObjectByType<PaintDecalBatcher>();
        if (existing == null)
        {
            GameObject go = new GameObject("[PaintDecalBatcher]");
            Undo.RegisterCreatedObjectUndo(go, "Create Paint Decal Batcher");
            Undo.AddComponent<PaintDecalBatcher>(go);
        }
    }

    private static void CreateWall(Transform parent, string name, Vector3 pos, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent);
        wall.transform.position = pos;
        wall.transform.localScale = scale;
        Undo.RegisterCreatedObjectUndo(wall, "Create Wall");
    }

    private static GameObject CreatePlayerBase(string name, out CharacterController cc)
    {
        GameObject oldPlayer = GameObject.Find(name);
        if (oldPlayer != null)
        {
            Undo.DestroyObjectImmediate(oldPlayer);
        }

        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = name;
        player.tag = "Player";

        Collider col = player.GetComponent<Collider>();
        if (col != null)
        {
            Object.DestroyImmediate(col);
        }

        cc = Undo.AddComponent<CharacterController>(player);
        cc.center = Vector3.zero;
        cc.height = 2.0f;
        cc.radius = 0.5f;
        cc.stepOffset = 0.3f;
        cc.slopeLimit = 45f;

        // Visual visor facing forward (Z+)
        GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visor.name = "Visor_Front";
        visor.transform.SetParent(player.transform);
        visor.transform.localPosition = new Vector3(0f, 0.5f, 0.4f);
        visor.transform.localScale = new Vector3(0.5f, 0.2f, 0.3f);
        Collider visorCol = visor.GetComponent<Collider>();
        if (visorCol != null) Object.DestroyImmediate(visorCol);

        Undo.RegisterCreatedObjectUndo(player, "Create Player Character");
        return player;
    }
}
