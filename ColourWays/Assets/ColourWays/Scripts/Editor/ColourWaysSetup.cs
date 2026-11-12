using System.Collections.Generic;
using System.IO;
using ColourWays.Level;
using ColourWays.Paint;
using ColourWays.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ColourWays.EditorTools
{
    /// <summary>
    /// One-time scaffolding for the ColourWays port. Creates materials, prefabs, URP settings
    /// and a basic level scene as normal assets. After running it, everything is regular
    /// editor content: edit the scene and prefabs by hand, no code builds the level at runtime.
    /// </summary>
    public static class ColourWaysSetup
    {
        private const string Root = "Assets/ColourWays";
        internal const string MaterialDir = Root + "/Materials";
        internal const string PrefabDir = Root + "/Prefabs";
        internal const string SettingsDir = Root + "/Settings";
        internal const string ShaderDir = Root + "/Shaders";
        private const string ScenePath = "Assets/Scenes/Level_Basic.unity";

        private const string BlockPrefabPath = PrefabDir + "/PaintableBlock.prefab";
        private const string PlayerPrefabPath = PrefabDir + "/Player.prefab";
        internal const string ManagerPrefabPath = PrefabDir + "/PaintManager.prefab";
        private const string SpawnPrefabPath = PrefabDir + "/SpawnPoint.prefab";
        private const string FinishPrefabPath = PrefabDir + "/LevelFinish.prefab";
        internal const string PalettePath = SettingsDir + "/PaintPalette.asset";
        private const string ControlsPath = Root + "/Input/ColourWaysControls.inputactions";

        internal const string OutlineFeatureName = "ColourWays Outline";

        // ------------------------------------------------------------------ Menu

        [MenuItem("ColourWays/Legacy (3rd-person)/Run Full Setup", priority = 200)]
        public static void RunFullSetup()
        {
            if (CreateProjectAssets())
                CreateBasicLevel(true);
        }

        [MenuItem("ColourWays/Legacy (3rd-person)/1. Create Project Assets", priority = 220)]
        public static void CreateProjectAssetsMenu() => CreateProjectAssets();

        [MenuItem("ColourWays/Legacy (3rd-person)/2. Create Basic Level Scene", priority = 221)]
        public static void CreateBasicLevelMenu() => CreateBasicLevel(true);

        [MenuItem("GameObject/ColourWays/Paintable Block", false, 10)]
        private static void CreatePaintableBlock(MenuCommand command)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("ColourWays", "Run ColourWays > Setup first to create the PaintableBlock prefab.", "OK");
                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            if (command.context == null && SceneView.lastActiveSceneView != null)
                go.transform.position = SceneView.lastActiveSceneView.pivot;
            Undo.RegisterCreatedObjectUndo(go, "Create Paintable Block");
            Selection.activeObject = go;
        }

        // ------------------------------------------------------------------ Step 1: assets

        public static bool CreateProjectAssets()
        {
            EnsureFolder(MaterialDir);
            EnsureFolder(PrefabDir);
            EnsureFolder(SettingsDir);
            EnsureFolder("Assets/Scenes");

            int playerLayer = EnsureLayer("Player", 8);
            if (playerLayer < 0)
            {
                EditorUtility.DisplayDialog("ColourWays", "No free user layer for 'Player'. Free one up in Tags & Layers and run again.", "OK");
                return false;
            }
            int notPlayer = ~(1 << playerLayer);

            var surfaceShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/ColourWaysSurface.shader");
            var stampShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/PaintStamp.shader");
            var outlineShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/Outline.shader");
            var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsPath);

            if (surfaceShader == null || stampShader == null || outlineShader == null || controls == null)
            {
                EditorUtility.DisplayDialog("ColourWays", "ColourWays shaders or input actions aren't imported yet. Let Unity finish importing and try again.", "OK");
                return false;
            }

            // Materials
            var paintable = GetOrCreateMaterial(MaterialDir + "/M_PaintableSurface.mat", surfaceShader);
            paintable.SetColor("_BaseColor", Color.white);
            paintable.SetFloat("_Paintable", 1f);
            paintable.EnableKeyword("_PAINTABLE");

            var white = GetOrCreateMaterial(MaterialDir + "/M_White.mat", surfaceShader);
            white.SetColor("_BaseColor", Color.white);
            white.SetFloat("_Paintable", 0f);
            white.DisableKeyword("_PAINTABLE");

            var ink = GetOrCreateMaterial(MaterialDir + "/M_Ink.mat", surfaceShader);
            ink.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.1f));
            ink.SetFloat("_Paintable", 0f);
            ink.DisableKeyword("_PAINTABLE");

            var stamp = GetOrCreateMaterial(MaterialDir + "/M_PaintStamp.mat", stampShader);
            var outline = GetOrCreateMaterial(MaterialDir + "/M_Outline.mat", outlineShader);
            var droplet = GetOrCreateMaterial(MaterialDir + "/M_PaintDroplet.mat", particleShader != null ? particleShader : surfaceShader);
            if (droplet.HasProperty("_BaseColor"))
                droplet.SetColor("_BaseColor", Color.white);

            foreach (var m in new[] { paintable, white, ink, stamp, outline, droplet })
                EditorUtility.SetDirty(m);

            // Palette
            var palette = AssetDatabase.LoadAssetAtPath<PaintPalette>(PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<PaintPalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
            }

            ConfigureUrp(outline);

            BuildBlockPrefab(paintable);
            BuildSpawnPrefab();
            BuildManagerPrefab(palette, stamp);
            BuildPlayerPrefab(controls, white, ink, droplet, playerLayer, notPlayer);
            BuildFinishPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ColourWays] Project assets created/updated.");
            return true;
        }

        private static void ConfigureUrp(Material outline)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;

                FullScreenPassRendererFeature outlineFeature = null;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature == null) continue;
                    // The look is flat white with no shading: AO would add grey smudges.
                    if (feature is ScreenSpaceAmbientOcclusion && feature.isActive)
                    {
                        feature.SetActive(false);
                        EditorUtility.SetDirty(feature);
                    }
                    if (feature is FullScreenPassRendererFeature fs && fs.name == OutlineFeatureName)
                        outlineFeature = fs;
                }

                if (outlineFeature == null)
                {
                    outlineFeature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                    outlineFeature.name = OutlineFeatureName;
                    AssetDatabase.AddObjectToAsset(outlineFeature, data);
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(outlineFeature, out string _, out long localId);

                    var so = new SerializedObject(data);
                    var features = so.FindProperty("m_RendererFeatures");
                    var map = so.FindProperty("m_RendererFeatureMap");
                    int index = features.arraySize;
                    features.arraySize = index + 1;
                    features.GetArrayElementAtIndex(index).objectReferenceValue = outlineFeature;
                    map.arraySize = index + 1;
                    map.GetArrayElementAtIndex(index).longValue = localId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                outlineFeature.passMaterial = outline;
                outlineFeature.passIndex = 0;
                outlineFeature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
                outlineFeature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
                outlineFeature.fetchColorBuffer = true;
                outlineFeature.bindDepthStencilAttachment = false;
                outlineFeature.SetActive(true);
                EditorUtility.SetDirty(outlineFeature);

                data.SetDirty();
                EditorUtility.SetDirty(data);
            }

            // No shadows anywhere (and every ColourWays shader is unlit), so skip shadow maps.
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                SetBool(so, "m_MainLightShadowsSupported", false);
                SetBool(so, "m_AdditionalLightShadowsSupported", false);
                SetBool(so, "m_RequireDepthTexture", true);
                SetBool(so, "m_RequireOpaqueTexture", false);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }

        // ------------------------------------------------------------------ Prefabs

        private static void BuildBlockPrefab(Material paintable)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PaintableBlock";
            ConfigureRenderer(go.GetComponent<MeshRenderer>(), paintable);
            var surface = go.AddComponent<PaintableSurface>();
            Set(surface, ("paintableFaces", (int)(PaintableSurface.Faces.All & ~PaintableSurface.Faces.NegY)));
            SavePrefab(go, BlockPrefabPath);
        }

        private static void BuildSpawnPrefab()
        {
            var go = new GameObject("SpawnPoint");
            go.AddComponent<SpawnPoint>();
            SavePrefab(go, SpawnPrefabPath);
        }

        internal static void BuildManagerPrefab(PaintPalette palette, Material stamp)
        {
            var go = new GameObject("PaintManager");
            var manager = go.AddComponent<PaintManager>();
            Set(manager, ("palette", palette), ("stampMaterial", stamp));
            SavePrefab(go, ManagerPrefabPath);
        }

        private static void BuildPlayerPrefab(InputActionAsset controls, Material white, Material ink, Material droplet, int playerLayer, int notPlayer)
        {
            var root = new GameObject("Player");

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.5f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.03f;
            cc.minMoveDistance = 0f;

            var player = root.AddComponent<PlayerController>();
            var footsteps = root.AddComponent<FootstepPainter>();

            // ---- Visual: little white blob with two eyes and two feet
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var blob = visual.AddComponent<BlobVisual>();

            var body = Part(PrimitiveType.Sphere, "Body", visual.transform, new Vector3(0f, 0.48f, 0f), new Vector3(0.72f, 0.74f, 0.72f), white);
            Part(PrimitiveType.Sphere, "EyeL", body, new Vector3(-0.17f, 0.14f, 0.43f), new Vector3(0.13f, 0.2f, 0.1f), ink);
            Part(PrimitiveType.Sphere, "EyeR", body, new Vector3(0.17f, 0.14f, 0.43f), new Vector3(0.13f, 0.2f, 0.1f), ink);
            var footL = Part(PrimitiveType.Sphere, "FootL", visual.transform, new Vector3(-0.14f, 0.05f, 0.02f), new Vector3(0.17f, 0.1f, 0.24f), white);
            var footR = Part(PrimitiveType.Sphere, "FootR", visual.transform, new Vector3(0.14f, 0.05f, 0.02f), new Vector3(0.17f, 0.1f, 0.24f), white);

            // ---- Paint droplets (Unreal NE_Splat_System)
            var splatGo = new GameObject("PaintSplatEmitter");
            splatGo.transform.SetParent(root.transform, false);
            var particles = splatGo.AddComponent<ParticleSystem>();
            ConfigureDroplets(particles, droplet, notPlayer);
            var splat = splatGo.AddComponent<PaintSplatEmitter>();

            // ---- Camera (detaches from the player at runtime)
            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 2.1f, -4.3f);
            camGo.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = false;
            camData.renderShadows = false;
            camData.antialiasing = AntialiasingMode.None;
            var rig = camGo.AddComponent<ThirdPersonCamera>();

            SetLayerRecursively(root, playerLayer);

            Set(player, ("controls", controls), ("cameraRig", rig));
            Set(footsteps, ("paintMask", notPlayer));
            Set(splat, ("player", player), ("groundMask", notPlayer));
            Set(rig, ("target", root.transform), ("controls", controls), ("collisionMask", notPlayer));
            Set(blob,
                ("player", player),
                ("footsteps", footsteps),
                ("body", body),
                ("footLeft", footL),
                ("footRight", footR),
                ("paintTinted", new Object[] { footL.GetComponent<Renderer>(), footR.GetComponent<Renderer>() }));

            SavePrefab(root, PlayerPrefabPath);
        }

        private static void ConfigureDroplets(ParticleSystem ps, Material material, int collisionMask)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = 3f;
            main.startSpeed = 0f;
            main.startSize = 0.1f;
            main.startColor = Color.white;
            main.gravityModifier = 1.75f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.High;
            collision.collidesWith = collisionMask;
            collision.enableDynamicColliders = false;
            collision.dampen = 0f;
            collision.bounce = 0f;
            collision.lifetimeLoss = 1f;
            collision.radiusScale = 0.5f;
            collision.sendCollisionMessages = true;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void BuildFinishPrefab()
        {
            var root = new GameObject("LevelFinish");
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1.5f, 0f);
            box.size = new Vector3(4f, 3f, 2f);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var finish = root.AddComponent<LevelFinish>();

            var camTarget = new GameObject("CameraTarget");
            camTarget.transform.SetParent(root.transform, false);
            camTarget.transform.localPosition = new Vector3(8f, 8f, -10f);
            camTarget.transform.localRotation = Quaternion.LookRotation(-camTarget.transform.localPosition);

            // "Level Complete" UI (Unreal LevelComplete widget)
            var ui = new GameObject("CompleteUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            ui.transform.SetParent(root.transform, false);
            ui.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = ui.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(ui.transform, false);
            var rect = label.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.62f);
            rect.sizeDelta = new Vector2(1400f, 220f);
            var text = label.GetComponent<Text>();
            text.text = "Level Complete!";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 110;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.12f, 0.12f, 0.14f);
            text.raycastTarget = false;
            ui.SetActive(false);

            Set(finish, ("cameraTarget", camTarget.transform), ("completeUI", ui));
            SavePrefab(root, FinishPrefabPath);
        }

        // ------------------------------------------------------------------ Step 2: level

        public static void CreateBasicLevel(bool askOverwrite)
        {
            var block = AssetDatabase.LoadAssetAtPath<GameObject>(BlockPrefabPath);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var manager = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
            var spawn = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnPrefabPath);
            var finish = AssetDatabase.LoadAssetAtPath<GameObject>(FinishPrefabPath);
            if (block == null || player == null || manager == null || spawn == null || finish == null)
            {
                EditorUtility.DisplayDialog("ColourWays", "Prefabs are missing. Run 'ColourWays > Setup > 1. Create Project Assets' first.", "OK");
                return;
            }

            if (askOverwrite && File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("ColourWays", $"{ScenePath} already exists. Overwrite it with a fresh basic level?", "Overwrite", "Cancel"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // All-white world, no sky, no lights (every surface is unlit). Fog fades into white.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.white;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 200f;

            var systems = new GameObject("Systems").transform;
            var level = new GameObject("Level").transform;
            var signs = new GameObject("Signs").transform;

            Spawn(manager, systems, Vector3.zero, Quaternion.identity);
            Spawn(spawn, systems, new Vector3(0f, 0f, 0f), Quaternion.identity);
            var playerGo = Spawn(player, null, new Vector3(0f, 0f, 0f), Quaternion.identity);

            // --- Start room (Tutorial: "WASD to move")
            Block(block, level, "Floor_Start", new Vector3(0f, -0.5f, 2f), new Vector3(12f, 1f, 14f));
            Block(block, level, "Wall_Left", new Vector3(-6.25f, 1.5f, 2f), new Vector3(0.5f, 4f, 14f));
            Block(block, level, "Wall_Right", new Vector3(6.25f, 1.5f, 2f), new Vector3(0.5f, 4f, 14f));
            Block(block, level, "Wall_Back", new Vector3(0f, 1.5f, -5.25f), new Vector3(13f, 4f, 0.5f));
            Block(block, level, "Pillar", new Vector3(-3f, 1f, 4f), new Vector3(1f, 2f, 1f));
            Block(block, level, "Crate", new Vector3(3f, 0.5f, 5f), new Vector3(1.5f, 1f, 1.5f));
            Sign(signs, "WASD to move", new Vector3(0f, 2.4f, 7.5f));

            // --- First gap (Tutorial: "Space to jump")
            Block(block, level, "Floor_Jump", new Vector3(0f, -0.5f, 15f), new Vector3(8f, 1f, 8f));
            Sign(signs, "Space to jump", new Vector3(0f, 2.4f, 12f));

            // --- Ramp up to the high platform
            const float rampAngle = 18.435f; // atan(2 / 6): rises 2m over 6m
            float rad = rampAngle * Mathf.Deg2Rad;
            Vector3 rampTop = new Vector3(0f, 1f, 22f);
            Vector3 rampUp = new Vector3(0f, Mathf.Cos(rad), -Mathf.Sin(rad));
            Block(block, level, "Ramp", rampTop - rampUp * 0.25f, new Vector3(4f, 0.5f, 6.4f), new Vector3(-rampAngle, 0f, 0f));

            Block(block, level, "Platform_High", new Vector3(0f, 0.75f, 29f), new Vector3(10f, 2.5f, 8f));
            Block(block, level, "Wall_High", new Vector3(-4.75f, 3.25f, 30f), new Vector3(0.5f, 2.5f, 6f));
            Sign(signs, "Hold R to restart", new Vector3(0f, 4f, 30f));

            // --- Stepping stones (fall = respawn)
            Block(block, level, "Stone_1", new Vector3(-2f, 1.75f, 35.5f), new Vector3(2.5f, 0.5f, 2.5f));
            Block(block, level, "Stone_2", new Vector3(1.5f, 2.25f, 38.5f), new Vector3(2.5f, 0.5f, 2.5f));
            Block(block, level, "Stone_3", new Vector3(-1f, 2.75f, 41.5f), new Vector3(2.5f, 0.5f, 2.5f));

            // --- Finish
            Block(block, level, "Platform_Finish", new Vector3(0f, 2.5f, 47f), new Vector3(8f, 1f, 8f));
            Block(block, level, "Gate_Left", new Vector3(-2.25f, 4.5f, 49f), new Vector3(0.5f, 3f, 0.5f));
            Block(block, level, "Gate_Right", new Vector3(2.25f, 4.5f, 49f), new Vector3(0.5f, 3f, 0.5f));
            Block(block, level, "Gate_Top", new Vector3(0f, 6.25f, 49f), new Vector3(5f, 0.5f, 0.5f));
            Sign(signs, "Reach the end!", new Vector3(0f, 7.6f, 49f));

            var finishGo = Spawn(finish, level, new Vector3(0f, 3f, 49f), Quaternion.identity);
            var camTarget = finishGo.transform.Find("CameraTarget");
            if (camTarget != null)
            {
                camTarget.position = new Vector3(16f, 20f, 36f);
                camTarget.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, 22f) - camTarget.position);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            Selection.activeGameObject = playerGo;
            Debug.Log($"[ColourWays] Basic level saved to {ScenePath}. Press Play!");
        }

        // ------------------------------------------------------------------ Helpers

        private static GameObject Spawn(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        private static void Block(GameObject prefab, Transform parent, string name, Vector3 center, Vector3 size, Vector3 euler = default)
        {
            var go = Spawn(prefab, parent, center, Quaternion.Euler(euler));
            go.name = name;
            go.transform.localScale = size;
        }

        private static void Sign(Transform parent, string text, Vector3 position)
        {
            var go = new GameObject("Sign - " + text);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.fontSize = 64;
            mesh.characterSize = 0.08f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = new Color(0.15f, 0.15f, 0.18f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mesh.font.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            ConfigureRenderer(go.GetComponent<MeshRenderer>(), material);
            return go.transform;
        }

        internal static void ConfigureRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        internal static void SavePrefab(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        internal static Material GetOrCreateMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }

        internal static void Set(Object target, params (string name, object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in values)
            {
                var prop = so.FindProperty(name);
                if (prop == null)
                {
                    Debug.LogError($"[ColourWays] {target.GetType().Name} has no serialized field '{name}'.");
                    continue;
                }

                switch (value)
                {
                    case null: prop.objectReferenceValue = null; break;
                    case Object[] array:
                        prop.arraySize = array.Length;
                        for (int i = 0; i < array.Length; i++)
                            prop.GetArrayElementAtIndex(i).objectReferenceValue = array[i];
                        break;
                    case Object obj: prop.objectReferenceValue = obj; break;
                    case int i: prop.intValue = i; break;
                    case float f: prop.floatValue = f; break;
                    case bool b: prop.boolValue = b; break;
                    case string s: prop.stringValue = s; break;
                    case Color c: prop.colorValue = c; break;
                    default: Debug.LogError($"[ColourWays] Unsupported value type for '{name}'."); break;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetBool(SerializedObject so, string name, bool value)
        {
            var prop = so.FindProperty(name);
            if (prop != null) prop.boolValue = value;
        }

        internal static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        internal static int EnsureLayer(string layerName, int preferred)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");

            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                    return i;

            int slot = -1;
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferred).stringValue))
                slot = preferred;
            else
                for (int i = 8; i < layers.arraySize && slot < 0; i++)
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                        slot = i;

            if (slot < 0) return -1;
            layers.GetArrayElementAtIndex(slot).stringValue = layerName;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            return slot;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        internal static void AddSceneToBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
