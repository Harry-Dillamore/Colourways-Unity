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
    /// Builds the ColourWays basic level: a pure-white maze you explore in third person as the
    /// stickman from the Unreal project. Footsteps paint the floor; running into a wall splats it.
    /// Everything it creates is a normal asset (materials, prefabs, scene): after running it, edit
    /// the scene and prefabs by hand. No code builds the level at runtime.
    /// </summary>
    public static class ColourWaysSetup
    {
        private const string Root = "Assets/ColourWays";
        internal const string MaterialDir = Root + "/Materials";
        internal const string PrefabDir = Root + "/Prefabs";
        internal const string SettingsDir = Root + "/Settings";
        internal const string ShaderDir = Root + "/Shaders";

        private const string CharacterPath = Root + "/Art/Character/Character_1.fbx";
        private const string BlockPrefabPath = PrefabDir + "/PaintableBlock.prefab";
        private const string PlayerPrefabPath = PrefabDir + "/Player.prefab";
        internal const string ManagerPrefabPath = PrefabDir + "/PaintManager.prefab";
        private const string SpawnPrefabPath = PrefabDir + "/SpawnPoint.prefab";
        private const string FinishPrefabPath = PrefabDir + "/LevelFinish.prefab";
        private const string BlobPrefabPath = PrefabDir + "/PaintBlob.prefab";
        internal const string PalettePath = SettingsDir + "/PaintPalette.asset";
        private const string ControlsPath = Root + "/Input/ColourWaysControls.inputactions";
        private const string BodyFontPath = Root + "/Art/Fonts/Outfit-SemiBold.ttf";

        /// <summary>Height of the stickman in metres.</summary>
        private const float CharacterHeight = 0.7f;

        // Bright paint colours (the Unreal ColourArray), picked at random on every splat.
        private static readonly Color[] Palette =
        {
            new Color(1f, 0.25f, 0.42f), new Color(1f, 0.55f, 0.1f), new Color(1f, 0.82f, 0.1f),
            new Color(0.2f, 0.8f, 0.35f), new Color(0.1f, 0.62f, 1f), new Color(0.55f, 0.3f, 0.95f),
            new Color(0.05f, 0.8f, 0.78f), new Color(1f, 0.4f, 0.85f),
        };

        // ------------------------------------------------------------------ Levels
        // Three short tutorial levels, one idea each. Map legend (rows north -> south):
        //   '#' wall   '.' floor   'S' start   'F' finish   'b' paint blob (checkpoint)
        //   '1'-'9' floor with a tutorial prompt (the matching line in Prompts) shown on entering.

        private sealed class LevelDef
        {
            public string Scene, Kicker, Title, StartPrompt, Next;
            public string[] Map;
            public string[] Prompts = new string[0];
        }

        private static readonly LevelDef[] Levels =
        {
            new LevelDef
            {
                Scene = "Level_1", Kicker = "LEVEL 1", Title = "Find your feet", Next = "Level_2",
                StartPrompt = "WASD to move",
                Prompts = new[]
                {
                    "The walls are invisible, and touching one splats you!",
                    "Move the mouse to turn the camera",
                },
                Map = new[]
                {
                    "#########",
                    "#S....1.#",
                    "#######.#",
                    "#######2#",
                    "#######.#",
                    "#######F#",
                    "#########",
                },
            },
            new LevelDef
            {
                Scene = "Level_2", Kicker = "LEVEL 2", Title = "Splat!", Next = "Level_3",
                StartPrompt = "Hold Shift to run",
                Prompts = new[]
                {
                    "Splatted? Your paint stays, so now you know where that wall is",
                    "R takes you back to your last safe spot",
                },
                Map = new[]
                {
                    "###########",
                    "#S........#",
                    "#########1#",
                    "#.........#",
                    "#2#########",
                    "#.........#",
                    "#########F#",
                    "###########",
                },
            },
            new LevelDef
            {
                Scene = "Level_3", Kicker = "LEVEL 3", Title = "Paint blobs", Next = "",
                StartPrompt = "Touch the paint blobs: they splash the maze and save your spot",
                Prompts = new[] { "Lost? R takes you back to the last blob you touched" },
                Map = new[]
                {
                    "#############",
                    "#S..#...#...#",
                    "###.#.#.#.#.#",
                    "#b..#.#...#.#",
                    "#.###.#####.#",
                    "#...#.....#b#",
                    "###.#####.#.#",
                    "#..1......#F#",
                    "#############",
                },
            },
        };

        private const string MenuScene = "MainMenu";
        private const string SceneDir = "Assets/Scenes";
        private const float Cell = 2f;
        private const float WallHeight = 1.6f; // low enough that the high camera rarely loses the dude behind a wall

        // ------------------------------------------------------------------ Menu

        [MenuItem("ColourWays/Build Levels", priority = -100)]
        public static void RunFullSetup()
        {
            if (!CreateProjectAssets()) return;
            bool exists = false;
            foreach (var l in Levels) exists |= File.Exists($"{SceneDir}/{l.Scene}.unity");
            exists |= File.Exists($"{SceneDir}/{MenuScene}.unity");
            if (exists && !EditorUtility.DisplayDialog("ColourWays", "Rebuild the main menu and the 3 level scenes? (Overwrites them.)", "Rebuild", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildMainMenu();
            for (int i = 0; i < Levels.Length; i++) BuildLevel(Levels[i]);

            // Build list: menu first, then the levels in order.
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene($"{SceneDir}/{MenuScene}.unity", true) };
            foreach (var l in Levels) scenes.Add(new EditorBuildSettingsScene($"{SceneDir}/{l.Scene}.unity", true));
            foreach (var s in EditorBuildSettings.scenes)
                if (File.Exists(s.path) && !scenes.Exists(x => x.path == s.path)) scenes.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = scenes.ToArray();

            // The old single big maze is replaced by the levels.
            if (File.Exists($"{SceneDir}/Level_Basic.unity")) AssetDatabase.DeleteAsset($"{SceneDir}/Level_Basic.unity");

            EditorSceneManager.OpenScene($"{SceneDir}/{MenuScene}.unity");
            Debug.Log("[ColourWays] Built MainMenu + Level_1..3. Press Play in MainMenu (or open a level and play it directly).");
        }
        [MenuItem("GameObject/ColourWays/Paintable Block", false, 10)]
        private static void CreatePaintableBlock(MenuCommand command)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlockPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("ColourWays", "Run ColourWays > Build Basic Level first to create the PaintableBlock prefab.", "OK");
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
            var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsPath);
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);

            if (surfaceShader == null || stampShader == null || controls == null)
            {
                EditorUtility.DisplayDialog("ColourWays", "ColourWays shaders or input actions aren't imported yet. Let Unity finish importing and try again.", "OK");
                return false;
            }
            if (character == null)
            {
                EditorUtility.DisplayDialog("ColourWays", $"The stickman model isn't imported yet ({CharacterPath}). Let Unity finish importing and try again.", "OK");
                return false;
            }

            // Materials. The world is pure white and unshaded until paint lands on it; painted
            // surfaces get smooth shading (darker on the sides away from the light), which is what
            // reveals the shapes. (Screen-space halftone dots crawled as the camera moved.)
            var paintable = SurfaceMaterial(MaterialDir + "/M_PaintableSurface.mat", surfaceShader, Color.white,
                paintable: true, flat: true, halftone: false, outline: 0f, dotShade: 0.74f);
            // The dude: white with soft shading and a thin ink outline so he reads on white.
            var body = SurfaceMaterial(MaterialDir + "/M_Player.mat", surfaceShader, Color.white,
                paintable: false, flat: false, halftone: false, outline: 0.007f, dotShade: 0.8f, shadeAmount: 0.85f);
            // Paint blobs: tinted per blob, soft shading and an ink outline so they pop from afar.
            var blobMat = SurfaceMaterial(MaterialDir + "/M_PaintBlob.mat", surfaceShader, Color.white,
                paintable: false, flat: false, halftone: false, outline: 0.012f, dotShade: 0.72f, shadeAmount: 0.8f);
            var stamp = GetOrCreateMaterial(MaterialDir + "/M_PaintStamp.mat", stampShader);
            var droplet = GetOrCreateMaterial(MaterialDir + "/M_PaintDroplet.mat", particleShader != null ? particleShader : surfaceShader);
            if (droplet.HasProperty("_BaseColor"))
                droplet.SetColor("_BaseColor", Color.white);
            // Droplets: a round soft sprite, alpha-cut to a crisp edge, stretched along their flight.
            var dropTex = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            if (dropTex != null && droplet.HasProperty("_BaseMap")) droplet.SetTexture("_BaseMap", dropTex);
            if (droplet.HasProperty("_AlphaClip"))
            {
                droplet.SetFloat("_AlphaClip", 1f);
                droplet.SetFloat("_Cutoff", 0.45f);
                droplet.EnableKeyword("_ALPHATEST_ON");
            }
            foreach (var m in new[] { stamp, droplet })
                EditorUtility.SetDirty(m);

            // Paint curtain for scene transitions (loaded from Resources at runtime).
            var curtainShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/PaintCurtain.shader");
            if (curtainShader != null)
            {
                EnsureFolder(Root + "/Resources/ColourWays");
                var curtain = GetOrCreateMaterial(Root + "/Resources/ColourWays/M_PaintCurtain.mat", curtainShader);
                EditorUtility.SetDirty(curtain);
            }

            // Palette
            var palette = AssetDatabase.LoadAssetAtPath<PaintPalette>(PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<PaintPalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
            }
            var paletteSo = new SerializedObject(palette);
            var colours = paletteSo.FindProperty("colours");
            if (colours != null)
            {
                colours.arraySize = Palette.Length;
                for (int i = 0; i < Palette.Length; i++) colours.GetArrayElementAtIndex(i).colorValue = Palette[i];
                paletteSo.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(palette);

            ConfigureUrp();

            BuildBlockPrefab(paintable);
            BuildSpawnPrefab();
            BuildManagerPrefab(palette, stamp);
            if (!BuildPlayerPrefab(controls, character, body, droplet, playerLayer, notPlayer))
                return false;
            BuildFinishPrefab();
            BuildBlobPrefab(blobMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ColourWays] Project assets created/updated.");
            return true;
        }

        private static Material SurfaceMaterial(string path, Shader shader, Color colour, bool paintable, bool flat, bool halftone,
            float outline, float dotShade, float shadeAmount = 1f)
        {
            var mat = GetOrCreateMaterial(path, shader);
            mat.SetColor("_BaseColor", colour);
            Toggle(mat, "_Paintable", "_PAINTABLE", paintable);
            Toggle(mat, "_HideUnpainted", "_HIDE_UNPAINTED", false);
            Toggle(mat, "_Halftone", "_HALFTONE", halftone);
            mat.SetFloat("_UnpaintedFlat", flat ? 1f : 0f);
            mat.SetFloat("_UnpaintedShade", 0f);
            mat.SetFloat("_ShadeAmount", shadeAmount);
            mat.SetFloat("_ShadeThreshold", 0.12f);
            mat.SetFloat("_LightMix", 1f);
            mat.SetFloat("_DotShade", dotShade);
            mat.SetColor("_DotColor", new Color(0f, 0f, 0f, 0f));
            mat.SetFloat("_DotScale", 5f);
            mat.SetFloat("_TileLines", 0f);
            mat.SetFloat("_OutlineWidth", outline);
            mat.SetColor("_OutlineColor", new Color(0.1f, 0.1f, 0.12f));
            mat.SetShaderPassEnabled("SRPDefaultUnlit", outline > 0f);
            // Paint look: crisp edges, slight rim, glossy emboss (see ColourWaysSurface.shader).
            mat.SetFloat("_PaintEdge", 0.35f);
            mat.SetFloat("_PaintRim", 0.16f);
            mat.SetFloat("_PaintGloss", 0.55f);
            mat.SetFloat("_PaintEmboss", 1.6f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void Toggle(Material m, string property, string keyword, bool on)
        {
            m.SetFloat(property, on ? 1f : 0f);
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }

        /// <summary>
        /// Pure white, no outlines or effects that would draw the hidden walls: full-screen passes
        /// (edge outline, tilt-shift) and SSAO off, no shadows, no depth/opaque textures.
        /// </summary>
        private static void ConfigureUrp()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature == null) continue;
                    if ((feature is ScreenSpaceAmbientOcclusion || feature is FullScreenPassRendererFeature) && feature.isActive)
                    {
                        feature.SetActive(false);
                        EditorUtility.SetDirty(feature);
                    }
                }
                data.SetDirty();
                EditorUtility.SetDirty(data);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                SetBool(so, "m_MainLightShadowsSupported", false);
                SetBool(so, "m_AdditionalLightShadowsSupported", false);
                SetBool(so, "m_RequireDepthTexture", false);
                SetBool(so, "m_RequireOpaqueTexture", false);
                var msaa = so.FindProperty("m_MSAA");
                if (msaa != null) msaa.intValue = 4;               // smooth edges on the dude, blobs and painted walls
                var renderScale = so.FindProperty("m_RenderScale");
                if (renderScale != null) renderScale.floatValue = 1f;
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
            // Paint stays forever (no trail fade): what you've discovered stays discovered.
            Set(manager, ("palette", palette), ("stampMaterial", stamp), ("atlasSize", 4096), ("texelsPerMetre", 80f),
                ("splatEdgeNoise", 1f), ("splatEdgeSoftness", 0.05f), ("trailFadeSeconds", 0f));
            SavePrefab(go, ManagerPrefabPath);
        }

        private static bool BuildPlayerPrefab(InputActionAsset controls, GameObject character, Material body, Material droplet, int playerLayer, int notPlayer)
        {
            var root = new GameObject("Player");

            // ---- Visual: the stickman, scaled to size, turned to face +Z, feet on the ground.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(character);
            model.name = "Stickman";
            model.transform.SetParent(visual.transform, false);
            if (!FitModel(model.transform, out float height, out float footSpacing, out float legLength))
            {
                Object.DestroyImmediate(root);
                EditorUtility.DisplayDialog("ColourWays", "The stickman model has no mesh renderer. Check the import of " + CharacterPath, "OK");
                return false;
            }
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = body;
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true; // bounds follow the procedural pose
            }
            var animator = visual.AddComponent<StickmanAnimator>();

            // Real animations (Walking / Running / Happy Idle exported from Unreal) if they're in
            // Art/Character/Animations; otherwise StickmanAnimator falls back to its procedural cycle.
            var controller = BuildAnimatorController();
            var modelAnimator = model.GetComponent<Animator>();
            if (modelAnimator == null) modelAnimator = model.AddComponent<Animator>();
            modelAnimator.runtimeAnimatorController = controller;
            modelAnimator.applyRootMotion = false;
            modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // ---- Movement capsule sized to the model.
            var cc = root.AddComponent<CharacterController>();
            cc.height = height;
            cc.radius = Mathf.Clamp(height * 0.2f, 0.18f, 0.4f);
            cc.center = new Vector3(0f, height * 0.5f, 0f);
            cc.stepOffset = Mathf.Min(0.3f, height * 0.3f);
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.03f;
            cc.minMoveDistance = 0f;

            var player = root.AddComponent<PlayerController>();
            var footsteps = root.AddComponent<FootstepPainter>();

            // ---- Paint droplets (Unreal NE_Splat_System)
            var splatGo = new GameObject("PaintSplatEmitter");
            splatGo.transform.SetParent(root.transform, false);
            var particles = splatGo.AddComponent<ParticleSystem>();
            ConfigureDroplets(particles, droplet, notPlayer);
            var splat = splatGo.AddComponent<PaintSplatEmitter>();

            // ---- Camera (detaches from the player at runtime; UE spring arm)
            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, height * 1.6f, -4f);
            camGo.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = false;
            camData.renderShadows = false;
            camData.antialiasing = AntialiasingMode.None; // MSAA (URP asset) smooths the geometry; the shader smooths the paint
            camData.requiresDepthOption = CameraOverrideOption.Off;
            camData.requiresColorOption = CameraOverrideOption.Off;
            var rig = camGo.AddComponent<ThirdPersonCamera>();
            camGo.AddComponent<ThemeApplier>(); // dark mode: background, fog and shader global

            SetLayerRecursively(root, playerLayer);

            float stride = Mathf.Clamp(legLength * 0.75f, 0.25f, 0.8f);
            // Slow and deliberate. Any proper bump into a wall splats (walking or running); scraping along one is fine.
            Set(player, ("controls", controls), ("cameraRig", rig), ("walkSpeed", 2.2f), ("sprintSpeed", 3.8f),
                ("maxAcceleration", 10f), ("brakingDeceleration", 14f), ("bonkSpeed", 0.9f), ("rotationRate", 600f));
            Set(footsteps, ("paintMask", notPlayer), ("footSpacing", footSpacing), ("strideLength", stride),
                ("footprintRadius", Mathf.Clamp(height * 0.08f, 0.07f, 0.14f)), ("footForwardOffset", 0.04f));
            Set(splat, ("player", player), ("paintMask", notPlayer));
            // Locked high angle above and behind (bird's-eye over the shoulder, like the original).
            Set(rig, ("target", root.transform), ("controls", controls), ("collisionMask", notPlayer),
                ("locked", true), ("lockedPitch", 50f), ("lockedYaw", 0f), ("lockedArmLength", 4.5f), ("lockedMouseYaw", true),
                ("mouseSensitivity", 0.12f), ("lagSpeed", 8f),
                ("armLength", 4f), ("startPitch", 18f));
            var rigSo = new SerializedObject(rig);
            rigSo.FindProperty("pivotOffset").vector3Value = new Vector3(0f, height * 0.6f, 0f);
            rigSo.ApplyModifiedPropertiesWithoutUndo();
            Set(animator, ("player", player), ("footsteps", footsteps), ("model", model.transform));

            SavePrefab(root, PlayerPrefabPath);
            return true;
        }

        /// <summary>
        /// Idle / walk / run blend tree on a "Speed" (0..1) parameter from the clips found in
        /// Art/Character/Animations (matched by name). Null when there are no clips yet.
        /// </summary>
        private static RuntimeAnimatorController BuildAnimatorController()
        {
            const string controllerPath = CharacterImport.Folder + "Stickman.controller";
            AnimationClip idle = null, walk = null, run = null;
            if (AssetDatabase.IsValidFolder(CharacterImport.AnimationFolder.TrimEnd('/')))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { CharacterImport.AnimationFolder.TrimEnd('/') }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (!(o is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
                        string n = (file + " " + clip.name).ToLowerInvariant();
                        if (idle == null && n.Contains("idle")) idle = clip;
                        else if (run == null && (n.Contains("run") || n.Contains("jog"))) run = clip;
                        else if (walk == null && n.Contains("walk")) walk = clip;
                    }
                }
            }
            if (idle == null && walk == null && run == null)
            {
                if (File.Exists(controllerPath)) AssetDatabase.DeleteAsset(controllerPath);
                return null;
            }
            walk ??= run ?? idle;
            run ??= walk;
            idle ??= walk;

            if (File.Exists(controllerPath)) AssetDatabase.DeleteAsset(controllerPath);
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.CreateBlendTreeInController("Locomotion", out var tree);
            tree.blendType = UnityEditor.Animations.BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 0.55f);  // walking speed is ~0.58 of running speed
            tree.AddChild(run, 1f);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ColourWays] Stickman animations: idle '{idle.name}', walk '{walk.name}', run '{run.name}'.");
            return controller;
        }

        /// <summary>
        /// Scale the model to <see cref="CharacterHeight"/>, turn it so it faces +Z (worked out from
        /// the toe bones, so it doesn't matter how Unreal oriented the export), and
        /// stand it on y = 0. Also measures foot spacing and leg length for the footprints.
        /// </summary>
        private static bool FitModel(Transform model, out float height, out float footSpacing, out float legLength)
        {
            height = CharacterHeight;
            footSpacing = 0.12f;
            legLength = 0.6f;
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;

            Bounds Measure()
            {
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                return b;
            }

            // Keep the importer's own root rotation (Unreal is Z-up: the FBX root carries the
            // axis conversion). Resetting it to identity is what laid the stickman on his back.
            model.localPosition = Vector3.zero;
            model.localScale = Vector3.one;

            Transform Find(string n)
            {
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
                return null;
            }
            var footL = Find("foot_l");
            var footR = Find("foot_r");
            var thigh = Find("thigh_stretch_l");
            var head = Find("head_x") ?? Find("neck_x");

            // Stand him up: whatever the export axes, the head must end up straight above the feet.
            if (head != null && footL != null && footR != null)
            {
                Vector3 up = head.position - (footL.position + footR.position) * 0.5f;
                if (up.sqrMagnitude > 1e-8f)
                    model.localRotation = Quaternion.FromToRotation(up.normalized, Vector3.up) * model.localRotation;
            }
            // Facing: toes point forward from the feet (doesn't rely on axis/handedness conventions).
            // Falls back to the left/right foot line if there are no toe bones.
            var toesL = Find("toes_01_l");
            var toesR = Find("toes_01_r");
            Vector3 forward = Vector3.zero;
            if (footL != null && toesL != null) forward += toesL.position - footL.position;
            if (footR != null && toesR != null) forward += toesR.position - footR.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-8f && footL != null && footR != null)
            {
                Vector3 right = footR.position - footL.position;
                right.y = 0f;
                if (right.sqrMagnitude > 1e-8f) forward = Vector3.Cross(right.normalized, Vector3.up);
            }
            if (forward.sqrMagnitude > 1e-8f)
                model.localRotation = Quaternion.AngleAxis(Vector3.SignedAngle(forward, Vector3.forward, Vector3.up), Vector3.up) * model.localRotation;

            // Size: measured upright. Skinned bounds can be loose in the editor, so cross-check
            // with the skeleton (feet to head, plus a head's worth) and use the larger.
            var bounds = Measure();
            float tall = bounds.size.y;
            if (head != null && footL != null)
                tall = Mathf.Max(tall, (head.position.y - Mathf.Min(footL.position.y, footR != null ? footR.position.y : footL.position.y)) * 1.15f);
            if (tall > 1e-4f) model.localScale = Vector3.one * (CharacterHeight / tall);

            bounds = Measure();
            model.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            height = Measure().size.y;
            if (footL != null && footR != null)
                footSpacing = Mathf.Clamp(Vector3.Distance(footL.position, footR.position) * 0.5f, 0.05f, 0.3f);
            if (thigh != null && footL != null)
                legLength = Mathf.Clamp(thigh.position.y - footL.position.y, 0.2f, 1.5f);
            return true;
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
            main.startSize = 0.08f;
            main.startColor = Color.white;
            main.gravityModifier = 1.75f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 900; // blob bursts fire a lot of droplets at once

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

            // Stretched along their velocity so fast droplets read as flicks of paint.
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.035f;
            renderer.lengthScale = 1.4f;
            renderer.sharedMaterial = material;

            // A little squash as they fly (size wobble) for a wet, liquid feel.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.08f, 1.1f), new Keyframe(1f, 0.9f)));
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static void BuildFinishPrefab()
        {
            var root = new GameObject("LevelFinish");
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1.5f, 0f);
            box.size = new Vector3(2f, 3f, 2f);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var finish = root.AddComponent<LevelFinish>();

            var camTarget = new GameObject("CameraTarget");
            camTarget.transform.SetParent(root.transform, false);
            camTarget.transform.localPosition = new Vector3(8f, 8f, -10f);
            camTarget.transform.localRotation = Quaternion.LookRotation(-camTarget.transform.localPosition);

            // The "Level complete" card lives in each level's HUD (wired by the level builder).
            Set(finish, ("cameraTarget", camTarget.transform));
            SavePrefab(root, FinishPrefabPath);
        }

        private static void BuildBlobPrefab(Material material)
        {
            var root = new GameObject("PaintBlob");
            var sphere = root.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = 0.8f;
            sphere.center = new Vector3(0f, 0.8f, 0f);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var blob = root.AddComponent<PaintBlob>();

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0f, 0.9f, 0f);
            var main = Part(PrimitiveType.Sphere, "Blob", visual, Vector3.zero, new Vector3(0.75f, 0.68f, 0.75f), material);
            var lump = Part(PrimitiveType.Sphere, "Lump", visual, new Vector3(0.18f, 0.2f, -0.08f), Vector3.one * 0.38f, material);
            var orbit = new GameObject("Orbit").transform;
            orbit.SetParent(visual, false);
            var tinted = new List<Object> { main.GetComponent<Renderer>(), lump.GetComponent<Renderer>() };
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f;
                var drop = Part(PrimitiveType.Sphere, $"Drop{i}", orbit, new Vector3(Mathf.Cos(a) * 0.62f, 0.1f * (i - 1), Mathf.Sin(a) * 0.62f),
                    Vector3.one * (0.14f - i * 0.02f), material);
                tinted.Add(drop.GetComponent<Renderer>());
            }

            Set(blob, ("visual", visual), ("orbit", orbit), ("tinted", tinted.ToArray()));
            SavePrefab(root, BlobPrefabPath);
        }

        private static Font UiFont() =>
            AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ------------------------------------------------------------------ Step 2: scenes

        private static readonly Color Ink = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color Muted = new Color(0.55f, 0.55f, 0.58f);
        private static readonly Color SoftGrey = new Color(0.93f, 0.93f, 0.95f);
        private static readonly Color DarkCard = new Color(0.1f, 0.1f, 0.12f);
        private static readonly Color DarkSoft = new Color(0.2f, 0.2f, 0.24f);
        private static readonly Color DarkInk = new Color(0.96f, 0.96f, 0.97f);
        private const string HeaderFontPath = Root + "/Art/Fonts/PoetsenOne-Regular.ttf";

        private static void BuildLevel(LevelDef def)
        {
            var block = AssetDatabase.LoadAssetAtPath<GameObject>(BlockPrefabPath);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var manager = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
            var spawn = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnPrefabPath);
            var finish = AssetDatabase.LoadAssetAtPath<GameObject>(FinishPrefabPath);
            var blob = AssetDatabase.LoadAssetAtPath<GameObject>(BlobPrefabPath);
            if (block == null || player == null || manager == null || spawn == null || finish == null || blob == null)
            {
                EditorUtility.DisplayDialog("ColourWays", "Prefabs are missing. Run 'ColourWays > Build Levels' again once Unity has finished importing.", "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Pure white (or black in dark mode): no sky, no lights (every surface is unlit), soft fog.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.white;
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 70f;

            var systems = new GameObject("Systems").transform;
            var level = new GameObject("Level").transform;
            var walls = new GameObject("Walls").transform;
            walls.SetParent(level, false);
            var prompts = new GameObject("Tutorial Prompts").transform;
            prompts.SetParent(level, false);
            var blobs = new GameObject("Paint Blobs").transform;
            blobs.SetParent(level, false);

            Spawn(manager, systems, Vector3.zero, Quaternion.identity);

            string[] map = def.Map;
            int rows = map.Length, cols = map[0].Length;
            Vector3 CellCentre(int x, int row) => new Vector3(x * Cell, 0f, (rows - 1 - row) * Cell);
            bool Open(int x, int row) => x >= 0 && row >= 0 && x < cols && row < rows && map[row][x] != '#';

            // Floor: one slab under the whole level plus a margin.
            Vector3 centre = new Vector3((cols - 1) * Cell * 0.5f, 0f, (rows - 1) * Cell * 0.5f);
            Block(block, level, "Floor", centre + Vector3.down * 0.5f, new Vector3((cols + 4) * Cell, 1f, (rows + 4) * Cell));

            // Walls: horizontal runs of '#' become single blocks; everything else per cell.
            Vector2Int start = default, end = default;
            int blobIndex = 0;
            for (int row = 0; row < rows; row++)
            {
                string line = map[row];
                int x = 0;
                while (x < cols)
                {
                    char c = line[x];
                    Vector3 at = CellCentre(x, row);
                    if (c == 'S') start = new Vector2Int(x, row);
                    else if (c == 'F') end = new Vector2Int(x, row);
                    else if (c == 'b')
                    {
                        var go = Spawn(blob, blobs, at, Quaternion.identity);
                        go.name = $"PaintBlob {++blobIndex}";
                        Set(go.GetComponent<PaintBlob>(), ("colour", Palette[(blobIndex * 3) % Palette.Length]));
                    }
                    else if (c >= '1' && c <= '9')
                    {
                        int index = c - '1';
                        if (index < def.Prompts.Length) Prompt(prompts, at, def.Prompts[index]);
                    }

                    if (c != '#') { x++; continue; }
                    int first = x;
                    while (x < cols && line[x] == '#') x++;
                    Vector3 a = CellCentre(first, row), b = CellCentre(x - 1, row);
                    Block(block, walls, $"Wall r{row} {first}-{x - 1}", (a + b) * 0.5f + Vector3.up * (WallHeight * 0.5f),
                        new Vector3((x - first) * Cell, WallHeight, Cell));
                }
            }

            // Start facing down the first corridor; the camera starts looking the same way.
            float yaw = 0f;
            if (Open(start.x + 1, start.y)) yaw = 90f;
            else if (Open(start.x - 1, start.y)) yaw = 270f;
            else if (Open(start.x, start.y + 1)) yaw = 180f;
            var spawnRot = Quaternion.Euler(0f, yaw, 0f);
            Spawn(spawn, systems, CellCentre(start.x, start.y), spawnRot);
            var playerGo = Spawn(player, null, CellCentre(start.x, start.y), spawnRot);
            var rig = playerGo.GetComponentInChildren<ThirdPersonCamera>(true);
            if (rig != null) Set(rig, ("lockedYaw", yaw));

            // Finish + the overhead camera spot for the end-of-level pan over the paint.
            var finishGo = Spawn(finish, level, CellCentre(end.x, end.y), Quaternion.identity);
            var camTarget = finishGo.transform.Find("CameraTarget");
            if (camTarget != null)
            {
                float span = Mathf.Max(cols, rows) * Cell;
                camTarget.position = centre + new Vector3(0f, span * 0.95f, -span * 0.45f);
                camTarget.rotation = Quaternion.LookRotation(centre - camTarget.position);
            }

            var finishUi = BuildLevelHud(def);
            Set(finishGo.GetComponent<LevelFinish>(), ("nextSceneName", def.Next ?? ""), ("menuScene", MenuScene),
                ("completeUI", finishUi.card), ("nextButton", finishUi.next), ("nextLabel", finishUi.nextLabel), ("menuButton", finishUi.menu));
            BuildEventSystem();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{def.Scene}.unity");
        }

        private static void Prompt(Transform parent, Vector3 at, string message)
        {
            var go = new GameObject("Prompt: " + message);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.8f, 0f);
            box.size = new Vector3(Cell, 1.6f, Cell);
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            Set(go.AddComponent<TutorialPrompt>(), ("message", message));
        }

        // UI look: no panels or pills. Everything is white text with a thick black outline (like
        // the logo); menus are a plain list down the left, items fill with paint when hovered.

        /// <summary>Per-level HUD: title, tutorial line, pause menu, level-complete menu.</summary>
        private static (CanvasGroup card, Button next, Text nextLabel, Button menu) BuildLevelHud(LevelDef def)
        {
            var canvas = MakeCanvas("HUD", 10);

            // Level title, top left, fades after a few seconds.
            var title = UI("Title", canvas, typeof(CanvasGroup), typeof(ScreenHint));
            Place(title, new Vector2(0f, 1f), new Vector2(110f, -70f), new Vector2(1200f, 170f), new Vector2(0f, 1f));
            var kicker = Outlined(title, "Kicker", def.Kicker, 34, false, TextAnchor.UpperLeft);
            Place(kicker.rectTransform, new Vector2(0f, 1f), Vector2.zero, new Vector2(1200f, 44f), new Vector2(0f, 1f));
            var name = Outlined(title, "Title", def.Title, 96, true, TextAnchor.UpperLeft);
            Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -44f), new Vector2(1200f, 120f), new Vector2(0f, 1f));
            Set(title.GetComponent<ScreenHint>(), ("holdSeconds", 3.5f), ("fadeSeconds", 1.2f));

            // Tutorial line, bottom centre.
            var prompt = UI("Prompt", canvas, typeof(CanvasGroup), typeof(PromptUI));
            Place(prompt, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(1600f, 80f), new Vector2(0.5f, 0f));
            var promptText = Outlined(prompt, "Text", def.StartPrompt, 50, false, TextAnchor.MiddleCenter);
            Stretch(promptText.rectTransform);
            Set(prompt.GetComponent<PromptUI>(), ("text", promptText));
            var intro = new GameObject("Intro", typeof(LevelIntro));
            Set(intro.GetComponent<LevelIntro>(), ("message", def.StartPrompt), ("delay", 1.2f));

            // Pause menu: dimmed world, title and a list on the left. Dark mode lives here.
            var pause = Overlay(canvas, "Pause", true, out var pauseList);
            Outlined(pauseList, "Title", "Paused", 120, true, TextAnchor.UpperLeft, new Vector2(0f, 0f), new Vector2(900f, 150f));
            var resume = MakeItem(pauseList, "Resume", 0, out _);
            var restart = MakeItem(pauseList, "Restart level", 1, out _);
            var theme = MakeItem(pauseList, "Dark mode", 2, out var themeLabel);
            var toMenu = MakeItem(pauseList, "Main menu", 3, out _);
            var pauseMenu = canvas.gameObject.AddComponent<PauseMenu>();
            Set(pauseMenu, ("sheet", pause), ("resumeButton", resume), ("restartButton", restart), ("themeButton", theme),
                ("themeLabel", themeLabel), ("menuButton", toMenu), ("menuScene", MenuScene));

            // Level complete: no dim (you're looking at your paint), same list style.
            var complete = Overlay(canvas, "Complete", false, out var completeList);
            Outlined(completeList, "Title", "Level complete!", 110, true, TextAnchor.UpperLeft, new Vector2(0f, 0f), new Vector2(1200f, 140f));
            var next = MakeItem(completeList, "Next level", 0, out var nextLabel);
            var menu = MakeItem(completeList, "Main menu", 1, out _);

            return (complete, next, nextLabel, menu);
        }

        private static void BuildMainMenu()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            camGo.AddComponent<ThemeApplier>();

            var canvas = MakeCanvas("Menu", 0);
            var column = UI("Column", canvas);
            Place(column, new Vector2(0f, 1f), new Vector2(120f, -110f), new Vector2(1100f, 900f), new Vector2(0f, 1f));

            // The logo: plain white letters, thick black outline.
            Outlined(column, "Logo", "ColourWays", 190, true, TextAnchor.UpperLeft, Vector2.zero, new Vector2(1100f, 230f), 6f);

            var list = UI("Items", column);
            Place(list, new Vector2(0f, 1f), new Vector2(6f, -330f), new Vector2(700f, 600f), new Vector2(0f, 1f));
            var play = MakeItem(list, "Play", 0, out _);
            var levelButtons = new Object[Levels.Length];
            for (int i = 0; i < Levels.Length; i++)
                levelButtons[i] = MakeItem(list, $"Level {i + 1}", i + 1, out _, 0.78f);
            var quit = MakeItem(list, "Quit", Levels.Length + 1, out _);

            var menu = canvas.gameObject.AddComponent<MainMenu>();
            var so = new SerializedObject(menu);
            var scenes = so.FindProperty("levelScenes");
            scenes.arraySize = Levels.Length;
            for (int i = 0; i < Levels.Length; i++) scenes.GetArrayElementAtIndex(i).stringValue = Levels[i].Scene;
            so.ApplyModifiedPropertiesWithoutUndo();
            Set(menu, ("playButton", play), ("levelButtons", levelButtons), ("quitButton", quit));
            BuildEventSystem();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{MenuScene}.unity");
        }

        private static void BuildEventSystem()
        {
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>().AssignDefaultActions();
        }

        // ------------------------------------------------------------------ UI helpers

        private static RectTransform MakeCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return (RectTransform)go.transform;
        }

        private static RectTransform UI(string name, Transform parent, params System.Type[] components)
        {
            var types = new System.Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            System.Array.Copy(components, 0, types, 1, components.Length);
            var go = new GameObject(name, types);
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Place(RectTransform r, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = position;
            r.sizeDelta = size;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        /// <summary>White text with a thick black outline (two outline passes so it reads chunky, like a sticker).</summary>
        private static Text Outlined(Transform parent, string name, string text, int size, bool header, TextAnchor align,
            Vector2? position = null, Vector2? box = null, float thickness = 0f)
        {
            var t = UI(name, parent, typeof(Text)).GetComponent<Text>();
            t.font = header ? (AssetDatabase.LoadAssetAtPath<Font>(HeaderFontPath) ?? UiFont()) : UiFont();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            float w = thickness > 0f ? thickness : Mathf.Clamp(size * 0.035f, 2f, 5f);
            var outer = t.gameObject.AddComponent<Outline>();
            outer.effectColor = Color.black;
            outer.effectDistance = new Vector2(w, -w);
            var inner = t.gameObject.AddComponent<Outline>();
            inner.effectColor = Color.black;
            inner.effectDistance = new Vector2(w * 0.5f, -w * 0.5f);
            if (position.HasValue || box.HasValue)
                Place(t.rectTransform, new Vector2(0f, 1f), position ?? Vector2.zero, box ?? new Vector2(800f, size * 1.25f), new Vector2(0f, 1f));
            return t;
        }

        /// <summary>A left-aligned menu entry: outlined text with a generous invisible hit area; fills with paint on hover.</summary>
        private static Button MakeItem(Transform list, string label, int index, out Text text, float scale = 1f)
        {
            const float spacing = 92f;
            int size = Mathf.RoundToInt(64 * scale);
            var r = UI(label, list, typeof(Image), typeof(Button), typeof(MenuItemFx));
            Place(r, new Vector2(0f, 1f), new Vector2(0f, -index * spacing - (list.name == "Items" ? 0f : 170f)), new Vector2(620f, spacing - 8f), new Vector2(0f, 1f));
            var hit = r.GetComponent<Image>();
            hit.color = Color.clear; // invisible, just for clicks
            text = Outlined(r, "Label", label, size, false, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform);
            var button = r.GetComponent<Button>();
            button.transition = Selectable.Transition.None; // MenuItemFx does the feedback
            Set(r.GetComponent<MenuItemFx>(), ("label", text));
            return button;
        }

        /// <summary>
        /// Full-screen menu layer: an optional dim of the world (child 0) and a left-hand column
        /// (child 1, animated by the pause menu) that titles and items go into.
        /// </summary>
        private static CanvasGroup Overlay(RectTransform parent, string name, bool dimmed, out RectTransform column)
        {
            var root = UI(name, parent, typeof(CanvasGroup));
            Stretch(root);
            var dim = UI("Dim", root, typeof(Image)).GetComponent<Image>();
            Stretch(dim.rectTransform);
            dim.color = dimmed ? new Color(1f, 1f, 1f, 0.55f) : Color.clear;
            dim.raycastTarget = dimmed;
            if (dimmed)
            {
                var themed = dim.gameObject.AddComponent<Themed>();
                Set(themed, ("lightColour", dim.color), ("darkColour", new Color(0f, 0f, 0f, 0.55f)));
            }
            column = UI("Column", root);
            Place(column, new Vector2(0f, 1f), new Vector2(120f, -110f), new Vector2(1200f, 800f), new Vector2(0f, 1f));
            root.gameObject.SetActive(false);
            return root.GetComponent<CanvasGroup>();
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
                    case int i: prop.intValue = i; break; // also covers LayerMask fields
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

        /// <summary>Put the scene first in the build list and drop entries whose scene files no longer exist.</summary>
        internal static void AddSceneToBuild(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path || !File.Exists(s.path));
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
