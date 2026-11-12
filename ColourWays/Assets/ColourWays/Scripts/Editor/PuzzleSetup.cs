using System;
using ColourWays.Paint;
using ColourWays.Puzzle;
using ColourWays.Puzzle.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static ColourWays.EditorTools.ColourWaysSetup;

namespace ColourWays.EditorTools
{
    /// <summary>
    /// Scaffolds the instruction-maze version of ColourWays: sprites, materials, prefabs, the
    /// instruction set and a portrait/mobile "Puzzle" scene with an NYT-Games-style UI.
    /// Everything it makes is a normal asset; re-running rebuilds prefabs and (after asking) the scene.
    /// </summary>
    public static class PuzzleSetup
    {
        private const string Root = "Assets/ColourWays";
        private const string IconDir = Root + "/Art/Icons";
        private const string PuzzlePrefabDir = Root + "/Prefabs/Puzzle";
        private const string PuzzleMaterialDir = Root + "/Materials/Puzzle";
        private const string ScenePath = "Assets/Scenes/Puzzle.unity";
        private const string InstructionSetPath = Root + "/Settings/InstructionSet.asset";
        private const string StampMaterialPath = Root + "/Materials/M_PaintStamp.mat";

        // ---- Design system (Mini Metro feel): warm cream map, soft charcoal UI, round comfy
        // shapes, and bold transit-line colours on the map.
        private static readonly Color Ink = new Color(0.17f, 0.17f, 0.19f);       // soft charcoal
        private static readonly Color Muted = new Color(0.55f, 0.54f, 0.52f);
        private static readonly Color Rule = new Color(0f, 0f, 0f, 0.08f);
        private static readonly Color KeyGrey = new Color(0.91f, 0.9f, 0.87f);    // comfy warm-grey keys / secondary buttons
        // The map is one flat warm cream: board, ground and background match exactly so nothing
        // gives away the hidden level. Only paint (and soft decor far away) shows up.
        private static readonly Color Cream = new Color(0.96f, 0.95f, 0.92f);
        private static readonly Color Paper = Cream;
        private static readonly Color Ground = Cream;
        private static readonly Color DecorDots = new Color(0.82f, 0.8f, 0.76f, 0.9f);
        private static readonly Color Letterbox = new Color(0.9f, 0.89f, 0.86f);

        // Transit-line colours (vibrant, flat).
        private static readonly Color MetroRed = new Color(0.93f, 0.22f, 0.24f);
        private static readonly Color MetroBlue = new Color(0.13f, 0.47f, 0.89f);
        private static readonly Color MetroYellow = new Color(0.99f, 0.76f, 0.08f);
        private static readonly Color MetroGreen = new Color(0.16f, 0.7f, 0.36f);
        private static readonly Color MetroOrange = new Color(0.98f, 0.5f, 0.1f);
        private static readonly Color MetroPurple = new Color(0.56f, 0.3f, 0.78f);
        private static readonly Color MetroCyan = new Color(0.08f, 0.72f, 0.84f);
        private static readonly Color MetroPink = new Color(0.95f, 0.36f, 0.62f);

        private static readonly Color AccentBlue = MetroBlue;
        private static readonly Color AccentOrange = MetroOrange;
        private static readonly Color AccentGreen = MetroGreen;
        private static readonly Color AccentPink = MetroPink;
        private static readonly Color AccentViolet = MetroPurple;

        private static readonly (InstructionType type, string name, int cost, Color colour, string help)[] Instructions =
        {
            (InstructionType.TurnLeft, "Left", 1, AccentBlue, "turns the dude left"),
            (InstructionType.TurnRight, "Right", 1, AccentOrange, "turns the dude right"),
            (InstructionType.Arrow, "Arrow", 2, AccentGreen, "always face this way (tap to rotate)"),
            (InstructionType.Reverse, "U-Turn", 2, AccentPink, "turn around"),
            (InstructionType.Jump, "Jump", 3, AccentViolet, "hop two cells, over a pit or wall"),
        };

        // Each dude is a line colour and paints its route in it.
        private static readonly Color[] DudeColours =
        {
            MetroRed, MetroBlue, MetroYellow, MetroGreen, MetroOrange, MetroPurple, MetroCyan, MetroPink,
        };

        // ---- Fonts: Poetsen One for headers, Outfit for body (both SIL OFL, in Art/Fonts).
        private const string FontDir = Root + "/Art/Fonts";
        private const string HeaderFontPath = FontDir + "/PoetsenOne-Regular.ttf";
        private const string BodyFontPath = FontDir + "/Outfit-Regular.ttf";
        private const string BodyStrongFontPath = FontDir + "/Outfit-SemiBold.ttf";

        private enum FontRole { Header, Body, BodyStrong }

        /// <summary>Text objects with these names are headers (Poetsen One).</summary>
        private static readonly System.Collections.Generic.HashSet<string> HeaderNames =
            new System.Collections.Generic.HashSet<string> { "Title", "Headline", "Score", "Q" };

        /// <summary>Text objects with these names are emphasised body text (Outfit SemiBold).</summary>
        private static readonly System.Collections.Generic.HashSet<string> StrongNames =
            new System.Collections.Generic.HashSet<string> { "Value", "Label", "Name", "Kicker", "Text" };

        private static Font LoadFont(string path) =>
            AssetDatabase.LoadAssetAtPath<Font>(path) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        private static bool HasCustomFonts => AssetDatabase.LoadAssetAtPath<Font>(BodyFontPath) != null;

        private static FontRole RoleFor(string objectName, bool bold)
        {
            if (HeaderNames.Contains(objectName)) return FontRole.Header;
            if (bold || StrongNames.Contains(objectName)) return FontRole.BodyStrong;
            return FontRole.Body;
        }

        /// <summary>
        /// Re-applies header/body fonts to every Text in the open scene and in the puzzle prefabs,
        /// so an already-built scene picks up font changes without rebuilding.
        /// </summary>
        [MenuItem("ColourWays/Apply UI Fonts", priority = -10)]
        public static void ApplyFont()
        {
            int count = 0;
            foreach (var text in Object.FindObjectsByType<Text>(FindObjectsInactive.Include))
            {
                Undo.RecordObject(text, "Apply UI Fonts");
                StyleText(text, RoleFor(text.name, false));
                EditorUtility.SetDirty(text);
                count++;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PuzzlePrefabDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                var texts = root.GetComponentsInChildren<Text>(true);
                foreach (var text in texts) StyleText(text, RoleFor(text.name, false));
                if (texts.Length > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                count += texts.Length;
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[ColourWays] Applied header/body fonts to {count} text elements. Save the scene to keep it.");
        }

        private static void StyleText(Text t, FontRole role)
        {
            switch (role)
            {
                case FontRole.Header: t.font = LoadFont(HeaderFontPath); break;
                case FontRole.BodyStrong: t.font = LoadFont(BodyStrongFontPath); break;
                default: t.font = LoadFont(BodyFontPath); break;
            }
            // Real weights come from the font files; Unity's faux bold only for the built-in fallback.
            t.fontStyle = !HasCustomFonts && role != FontRole.Body ? FontStyle.Bold : FontStyle.Normal;
        }

        private sealed class Sprites
        {
            public Sprite Rounded, Pill, PillOutline, Shadow, Disc, Crosshair, Ring, RotateLeft, RotateRight;
            public Sprite Arrow, TurnLeft, TurnRight, Reverse, Jump;

            public Sprite For(InstructionType t) => t switch
            {
                InstructionType.TurnLeft => TurnLeft,
                InstructionType.TurnRight => TurnRight,
                InstructionType.Arrow => Arrow,
                InstructionType.Reverse => Reverse,
                _ => Jump,
            };
        }

        private sealed class Mats
        {
            public Material Board, Rim, WallHidden, WallRevealed, GroundMat;
            public Material[] Scenery;
            public Material DudeBody, DudeEyes, DudeFeet, StartPad, ExitPad, ExitFlag, Pole;
            public Material Grid, MarkerShadow, MarkerBg, MarkerIcon, StartArrow, Stamp;
        }

        // ------------------------------------------------------------------ Menu

        [MenuItem("ColourWays/Open Puzzle Scene", priority = -30)]
        public static void OpenPuzzleScene()
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                if (EditorUtility.DisplayDialog("ColourWays", "The Puzzle scene doesn't exist yet. Build it now?", "Build", "Cancel"))
                    Run();
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("ColourWays/Setup Puzzle Game (Instruction Maze)", priority = -20)]
        public static void Run()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            foreach (var dir in new[] { IconDir, PuzzlePrefabDir, PuzzleMaterialDir, SettingsDir, "Assets/Scenes" })
                EnsureFolder(dir);

            var sprites = BuildSprites();
            var mats = BuildMaterials(sprites);
            if (mats == null) return;

            BuildInstructionSet(sprites);
            var palette = AssetDatabase.LoadAssetAtPath<PaintPalette>(PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<PaintPalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
            }
            ApplyPalette(palette);
            BuildManagerPrefab(palette, mats.Stamp);

            ConfigureUrpForHiddenLevel();
            ConfigurePlayerForMobile();

            const PaintableSurface.Faces visibleFaces = PaintableSurface.Faces.All & ~PaintableSurface.Faces.NegY;
            BuildBox("Board", mats.Board, visibleFaces, 1f, true);
            BuildBox("WallBlock", mats.WallHidden, visibleFaces, 1f, true);
            BuildBox("Rim", mats.Rim, visibleFaces, 0.6f, true);
            BuildBox("Ground", mats.GroundMat, 0, 1f, false);
            BuildBox("SceneryBlock", mats.Scenery[0], 0, 1f, false);
            BuildDudePrefab(mats);
            BuildStartPrefab(mats);
            BuildExitPrefab(mats);
            BuildMarkerPrefab(mats);
            BuildCardPrefab(sprites);

            AssetDatabase.SaveAssets();

            if (System.IO.File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("ColourWays", $"Rebuild {ScenePath}? (Prefabs, materials and icons were already updated.)", "Rebuild scene", "Keep scene"))
            {
                Debug.Log("[ColourWays] Puzzle assets updated; scene kept.");
                return;
            }

            BuildScene();
        }

        // ------------------------------------------------------------------ Sprites

        private static Sprites BuildSprites()
        {
            return new Sprites
            {
                // Soft, generous radii (Mini Metro comfy): big-radius panels/keys, true pills, soft shadow.
                Rounded = IconFactory.Save(IconDir + "/ui_rounded.png", 96, c => IconFactory.DrawRoundedPanel(c, 30f), new Vector4(34, 34, 34, 34), false),
                Pill = IconFactory.Save(IconDir + "/ui_pill.png", 128, 64, c => IconFactory.DrawRoundedPanel(c, 31f), new Vector4(31, 31, 31, 31), false),
                PillOutline = IconFactory.Save(IconDir + "/ui_pill_stroke.png", 128, 64, c => IconFactory.DrawRoundedOutline(c, 31f, 1.2f), new Vector4(31, 31, 31, 31), false),
                Shadow = IconFactory.Save(IconDir + "/ui_shadow.png", 160, c => IconFactory.DrawSoftShadow(c, 34f, 26f), new Vector4(64, 64, 64, 64), false),
                Disc = IconFactory.Save(IconDir + "/ui_disc.png", 64, IconFactory.DrawDisc, default, false),
                Crosshair = IconFactory.Save(IconDir + "/ui_recenter.png", 96, IconFactory.DrawCrosshair, default, false),
                Ring = IconFactory.Save(IconDir + "/ui_ring.png", 96, c => IconFactory.DrawRing(c, 3.5f), default, false),
                RotateLeft = IconFactory.Save(IconDir + "/ui_rotate_left.png", 96, c => IconFactory.DrawRotate(c, false), default, false),
                RotateRight = IconFactory.Save(IconDir + "/ui_rotate_right.png", 96, c => IconFactory.DrawRotate(c, true), default, false),
                Arrow = IconFactory.Save(IconDir + "/icon_arrow.png", 128, IconFactory.DrawArrow),
                TurnLeft = IconFactory.Save(IconDir + "/icon_turn_left.png", 128, c => IconFactory.DrawTurn(c, false)),
                TurnRight = IconFactory.Save(IconDir + "/icon_turn_right.png", 128, c => IconFactory.DrawTurn(c, true)),
                Reverse = IconFactory.Save(IconDir + "/icon_reverse.png", 128, IconFactory.DrawReverse),
                Jump = IconFactory.Save(IconDir + "/icon_jump.png", 128, IconFactory.DrawJump),
            };
        }

        // ------------------------------------------------------------------ Materials

        private static Mats BuildMaterials(Sprites s)
        {
            var surface = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/ColourWaysSurface.shader");
            var stamp = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/PaintStamp.shader");
            var grid = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/Grid.shader");
            var icon = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + "/Icon.shader");
            if (surface == null || stamp == null || grid == null || icon == null)
            {
                EditorUtility.DisplayDialog("ColourWays", "ColourWays shaders aren't imported yet. Let Unity finish importing and run again.", "OK");
                return null;
            }

            string d = PuzzleMaterialDir;
            var m = new Mats
            {
                // Paint: full-strength line colours on lit faces, soft same-hue dots in shade.
                Board = Surf(d + "/M_Board.mat", surface, Paper, paintable: true, flat: true, dotShade: 0.82f),
                Rim = Surf(d + "/M_Rim.mat", surface, Ink, paintable: true, outline: 0.012f, dotShade: 0.55f),
                WallHidden = Surf(d + "/M_WallHidden.mat", surface, Paper, paintable: true, hideUnpainted: true, dotShade: 0.78f),
                WallRevealed = Surf(d + "/M_WallRevealed.mat", surface, KeyGrey, paintable: true, outline: 0.012f, dotColor: DecorDots),
                GroundMat = Surf(d + "/M_Ground.mat", surface, Ground, halftone: false, shadeAmount: 0f),
                // Decor around the map: soft cream forms plus bold transit-colour blocks (no outlines).
                Scenery = new[]
                {
                    Surf(d + "/M_SceneryLight.mat", surface, new Color(0.98f, 0.97f, 0.95f), dotColor: DecorDots, shadeAmount: 0.9f),
                    Surf(d + "/M_SceneryDark.mat", surface, new Color(0.92f, 0.91f, 0.88f), dotColor: DecorDots, shadeAmount: 1.1f),
                    Surf(d + "/M_AccentBlue.mat", surface, MetroBlue, dotShade: 0.8f, shadeAmount: 1f),
                    Surf(d + "/M_AccentOchre.mat", surface, MetroYellow, dotShade: 0.85f, shadeAmount: 1f),
                    Surf(d + "/M_AccentRose.mat", surface, MetroRed, dotShade: 0.8f, shadeAmount: 1f),
                    Surf(d + "/M_AccentGreen.mat", surface, MetroGreen, dotShade: 0.8f, shadeAmount: 1f),
                },
                // Dudes are tinted their line colour at runtime; smooth flat shading, charcoal outline.
                DudeBody = Surf(d + "/M_DudeBody.mat", surface, Color.white, outline: 0.018f, halftone: false, dotShade: 0.78f, shadeAmount: 0.8f, queue: 3100),
                DudeFeet = Surf(d + "/M_DudeFeet.mat", surface, Color.white, outline: 0.014f, halftone: false, dotShade: 0.7f, queue: 3100),
                DudeEyes = Surf(d + "/M_DudeEyes.mat", surface, Ink, halftone: false, shadeAmount: 0f, queue: 3100),
                // Stations: white disc with a thick charcoal ring (start), plus a red flag at the exit.
                StartPad = Surf(d + "/M_StartPad.mat", surface, Color.white, outline: 0.05f, halftone: false, shadeAmount: 0f),
                ExitPad = Surf(d + "/M_ExitPad.mat", surface, Color.white, outline: 0.05f, halftone: false, shadeAmount: 0f),
                ExitFlag = Surf(d + "/M_ExitFlag.mat", surface, MetroRed, outline: 0.014f, halftone: false, shadeAmount: 0.6f),
                Pole = Surf(d + "/M_Pole.mat", surface, Ink, halftone: false),
                Grid = GetOrCreateMaterial(d + "/M_Grid.mat", grid),
                MarkerShadow = IconMat(d + "/M_MarkerShadow.mat", icon, s.Rounded.texture, 8f, 3001, new Color(0f, 0f, 0f, 0f)),
                MarkerBg = IconMat(d + "/M_MarkerBackground.mat", icon, s.Rounded.texture, 8f, 3002, Color.white),
                MarkerIcon = IconMat(d + "/M_MarkerIcon.mat", icon, s.Arrow.texture, 8f, 3003, Color.white),
                StartArrow = IconMat(d + "/M_StartArrow.mat", icon, s.Arrow.texture, 4f, 3004, Ink),
                Stamp = GetOrCreateMaterial(StampMaterialPath, stamp),
            };

            m.Grid.SetColor("_LineColor", new Color(Ink.r, Ink.g, Ink.b, 0.22f));
            m.Grid.SetColor("_BorderColor", new Color(0f, 0f, 0f, 0f));
            m.Grid.SetFloat("_LineWidth", 0.012f);
            m.Grid.SetColor("_ValidColor", new Color(Ink.r, Ink.g, Ink.b, 0.08f));
            m.Grid.SetColor("_InvalidColor", new Color(MetroRed.r, MetroRed.g, MetroRed.b, 0.2f));
            m.Grid.renderQueue = 3000;
            EditorUtility.SetDirty(m.Grid);
            return m;
        }

        private static Material Surf(string path, Shader shader, Color colour, bool paintable = false, bool hideUnpainted = false,
            float outline = 0f, bool halftone = true, float dotShade = 0.7f, Color dotColor = default, float tileLines = 0f, int queue = -1,
            bool flat = false, float shadeAmount = 1f)
        {
            var mat = GetOrCreateMaterial(path, shader);
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_UnpaintedFlat", flat ? 1f : 0f);
            mat.SetFloat("_ShadeAmount", shadeAmount);
            Toggle(mat, "_Paintable", "_PAINTABLE", paintable);
            Toggle(mat, "_HideUnpainted", "_HIDE_UNPAINTED", hideUnpainted);
            Toggle(mat, "_Halftone", "_HALFTONE", halftone);
            mat.SetFloat("_LightMix", 1f);
            mat.SetFloat("_DotShade", dotShade);
            mat.SetColor("_DotColor", dotColor);
            mat.SetFloat("_DotScale", 5f);
            mat.SetFloat("_ShadeThreshold", 0.12f);
            mat.SetFloat("_TileLines", tileLines);
            mat.SetFloat("_UnpaintedShade", 0f);
            mat.SetFloat("_OutlineWidth", outline);
            mat.SetColor("_OutlineColor", Ink);
            mat.SetShaderPassEnabled("SRPDefaultUnlit", outline > 0f);
            mat.renderQueue = hideUnpainted ? (int)RenderQueue.AlphaTest : queue;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void Toggle(Material m, string property, string keyword, bool on)
        {
            m.SetFloat(property, on ? 1f : 0f);
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }

        private static Material IconMat(string path, Shader shader, Texture texture, float zTest, int queue, Color colour)
        {
            var mat = GetOrCreateMaterial(path, shader);
            mat.SetTexture("_MainTex", texture);
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_ZTest", zTest);
            mat.renderQueue = queue;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ Data assets

        private static void BuildInstructionSet(Sprites s)
        {
            var set = AssetDatabase.LoadAssetAtPath<InstructionSet>(InstructionSetPath);
            bool created = set == null;
            if (created)
            {
                set = ScriptableObject.CreateInstance<InstructionSet>();
                AssetDatabase.CreateAsset(set, InstructionSetPath);
            }

            var so = new SerializedObject(set);
            var entries = so.FindProperty("entries");
            if (entries.arraySize == 0)
            {
                entries.arraySize = Instructions.Length;
                for (int i = 0; i < Instructions.Length; i++)
                {
                    var e = entries.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("type").enumValueIndex = (int)Instructions[i].type;
                    e.FindPropertyRelative("displayName").stringValue = Instructions[i].name;
                    e.FindPropertyRelative("cost").intValue = Instructions[i].cost;
                    e.FindPropertyRelative("enabled").boolValue = true;
                }
            }

            // Re-theme: muted colours + icons (costs and names you've edited are kept).
            for (int i = 0; i < entries.arraySize; i++)
            {
                var e = entries.GetArrayElementAtIndex(i);
                var type = (InstructionType)e.FindPropertyRelative("type").enumValueIndex;
                foreach (var def in Instructions)
                    if (def.type == type) e.FindPropertyRelative("colour").colorValue = def.colour;
                e.FindPropertyRelative("icon").objectReferenceValue = s.For(type);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
        }

        private static void ApplyPalette(PaintPalette palette)
        {
            var so = new SerializedObject(palette);
            var list = so.FindProperty("colours");
            list.arraySize = DudeColours.Length;
            for (int i = 0; i < DudeColours.Length; i++)
                list.GetArrayElementAtIndex(i).colorValue = DudeColours[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(palette);
        }

        // ------------------------------------------------------------------ URP

        /// <summary>
        /// The full-screen edge outline would draw the hidden walls, and a depth prepass would let
        /// invisible walls occlude things, so both are off (also cheaper on web).
        /// </summary>
        private static void ConfigureUrpForHiddenLevel()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature == null) continue;
                    if ((feature is FullScreenPassRendererFeature && feature.name == OutlineFeatureName) || feature is ScreenSpaceAmbientOcclusion)
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
                SetBool(so, "m_RequireOpaqueTexture", false);
                SetBool(so, "m_RequireDepthTexture", false);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }

        /// <summary>Portrait everywhere: phone orientation lock plus a phone-shaped window/canvas on desktop and web.</summary>
        private static void ConfigurePlayerForMobile()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.defaultWebScreenWidth = 450;
            PlayerSettings.defaultWebScreenHeight = 900;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
        }

        // ------------------------------------------------------------------ Prefabs

        private static void BuildBox(string name, Material material, PaintableSurface.Faces faces, float density, bool paintable)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // grid-simulated: no physics needed
            ConfigureRenderer(go.GetComponent<MeshRenderer>(), material);
            if (paintable)
            {
                var surface = go.AddComponent<PaintableSurface>();
                Set(surface, ("paintableFaces", (int)faces), ("texelDensityScale", density));
            }
            Save(go, name);
        }

        private static void BuildDudePrefab(Mats m)
        {
            var root = new GameObject("Dude");
            var view = root.AddComponent<DudeView>();
            var body = Part(PrimitiveType.Sphere, "Body", root.transform, new Vector3(0f, 0.22f, 0f), new Vector3(0.38f, 0.4f, 0.38f), m.DudeBody);
            Part(PrimitiveType.Sphere, "EyeL", body, new Vector3(-0.17f, 0.14f, 0.42f), new Vector3(0.13f, 0.22f, 0.1f), m.DudeEyes);
            Part(PrimitiveType.Sphere, "EyeR", body, new Vector3(0.17f, 0.14f, 0.42f), new Vector3(0.13f, 0.22f, 0.1f), m.DudeEyes);
            var footL = Part(PrimitiveType.Sphere, "FootL", root.transform, new Vector3(-0.08f, 0.03f, 0.03f), new Vector3(0.1f, 0.06f, 0.14f), m.DudeFeet);
            var footR = Part(PrimitiveType.Sphere, "FootR", root.transform, new Vector3(0.08f, 0.03f, 0.03f), new Vector3(0.1f, 0.06f, 0.14f), m.DudeFeet);
            // The whole dude wears its line colour (body + feet).
            Set(view, ("body", body), ("tinted", new Object[] { body.GetComponent<Renderer>(), footL.GetComponent<Renderer>(), footR.GetComponent<Renderer>() }));
            Save(root, "Dude");
        }

        private static void BuildStartPrefab(Mats m)
        {
            var root = new GameObject("Start");
            Part(PrimitiveType.Cylinder, "Pad", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.8f, 0.04f, 0.8f), m.StartPad);
            var arrow = Part(PrimitiveType.Quad, "Arrow", root.transform, new Vector3(0f, 0.09f, 0f), Vector3.one * 0.48f, m.StartArrow);
            arrow.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Save(root, "Start");
        }

        private static void BuildExitPrefab(Mats m)
        {
            var root = new GameObject("Exit");
            Part(PrimitiveType.Cylinder, "Pad", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.8f, 0.05f, 0.8f), m.ExitPad);
            Part(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(0.24f, 0.5f, 0.24f), new Vector3(0.05f, 0.45f, 0.05f), m.Pole);
            Part(PrimitiveType.Cube, "Flag", root.transform, new Vector3(0.4f, 0.82f, 0.24f), new Vector3(0.3f, 0.2f, 0.03f), m.ExitFlag);
            Save(root, "Exit");
        }

        private static void BuildMarkerPrefab(Mats m)
        {
            var root = new GameObject("InstructionMarker");
            var marker = root.AddComponent<InstructionMarker>();
            root.transform.localScale = Vector3.one * 0.82f;
            var bg =Part(PrimitiveType.Quad, "Background", root.transform, Vector3.zero, Vector3.one, m.MarkerBg);
            bg.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var pivot = new GameObject("IconPivot").transform;
            pivot.SetParent(root.transform, false);
            var icon = Part(PrimitiveType.Quad, "Icon", pivot, new Vector3(0f, 0.001f, 0f), Vector3.one * 0.72f, m.MarkerIcon);
            icon.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Set(marker, ("background", bg.GetComponent<Renderer>()), ("icon", icon.GetComponent<Renderer>()), ("iconPivot", pivot));
            Save(root, "InstructionMarker");
        }

        private static void BuildCardPrefab(Sprites s)
        {
            // Soft warm-grey rounded key with a coloured "station" disc and a white glyph.
            var root = UI("InstructionCard", null, typeof(Image), typeof(LayoutElement), typeof(InstructionCard));
            root.sizeDelta = new Vector2(172f, 210f);
            var key = root.GetComponent<Image>();
            key.sprite = s.Rounded;
            key.type = Image.Type.Sliced;
            key.color = KeyGrey;
            var layout = root.GetComponent<LayoutElement>();
            layout.preferredWidth = 172f;
            layout.preferredHeight = 210f;

            var accent = Img(root, "Accent", s.Disc, AccentGreen);
            Place(accent.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(100f, 100f), new Vector2(0.5f, 1f));
            var icon = Img(accent.rectTransform, "Icon", s.Arrow, Color.white);
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f));
            icon.preserveAspect = true;

            var name = Label(root, "Name", "Arrow", 30, TextAnchor.MiddleCenter, Ink, true);
            Place(name.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 56f), new Vector2(0f, 38f));
            var cost = Label(root, "Cost", "1 pt", 22, TextAnchor.MiddleCenter, Muted, false);
            Place(cost.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 24f), new Vector2(0f, 28f));

            Set(root.GetComponent<InstructionCard>(), ("background", key), ("accent", accent), ("icon", icon), ("nameLabel", name), ("costLabel", cost));

            PrefabUtility.SaveAsPrefabAsset(root.gameObject, $"{PuzzlePrefabDir}/InstructionCard.prefab");
            Object.DestroyImmediate(root.gameObject);
        }

        private static void Save(GameObject go, string name) => SavePrefab(go, $"{PuzzlePrefabDir}/{name}.prefab");

        private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
        private static GameObject Prefab(string name) => Load<GameObject>($"{PuzzlePrefabDir}/{name}.prefab");
        private static Material Mat(string name) => Load<Material>($"{PuzzleMaterialDir}/{name}.mat");
        private static Sprite Icon(string file) => Load<Sprite>($"{IconDir}/{file}.png");

        private static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            ConfigureRenderer(go.GetComponent<MeshRenderer>(), material);
            return go.transform;
        }

        // ------------------------------------------------------------------ Scene

        private static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // NewScene unloads unused assets, which can invalidate objects created earlier in this
            // call. Load everything fresh from disk.
            var set = Load<InstructionSet>(InstructionSetPath);
            var s = new Sprites
            {
                Rounded = Icon("ui_rounded"), Pill = Icon("ui_pill"), PillOutline = Icon("ui_pill_stroke"), Shadow = Icon("ui_shadow"),
                Disc = Icon("ui_disc"), Crosshair = Icon("ui_recenter"), Ring = Icon("ui_ring"),
                RotateLeft = Icon("ui_rotate_left"), RotateRight = Icon("ui_rotate_right"),
                Arrow = Icon("icon_arrow"), TurnLeft = Icon("icon_turn_left"), TurnRight = Icon("icon_turn_right"),
                Reverse = Icon("icon_reverse"), Jump = Icon("icon_jump"),
            };
            if (set == null || Prefab("Board") == null || Prefab("Dude") == null || s.Pill == null)
            {
                EditorUtility.DisplayDialog("ColourWays", "Some puzzle assets couldn't be loaded. Check the Console and run the setup again.", "OK");
                return;
            }

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.fog = false;

            // ---- Camera (orthographic iso, interactive)
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ground;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = false;
            camData.renderShadows = false;
            camData.antialiasing = AntialiasingMode.None;
            camData.requiresDepthOption = CameraOverrideOption.Off;
            camData.requiresColorOption = CameraOverrideOption.Off;
            var rig = camGo.AddComponent<CameraRig>();
            camGo.transform.SetPositionAndRotation(new Vector3(-60f, 60f, -60f), Quaternion.Euler(32f, 45f, 0f));
            cam.orthographicSize = 9f;

            // ---- Paint
            var manager = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(ManagerPrefabPath));
            // Rougher, wetter splat edges so impacts read as chaotic paint, not neat circles.
            Set(manager.GetComponent<PaintManager>(), ("atlasSize", 2048), ("texelsPerMetre", 48f),
                ("splatEdgeNoise", 1.1f), ("splatEdgeSoftness", 0.04f));

            // ---- Grid overlay
            var gridGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            gridGo.name = "GridOverlay";
            Object.DestroyImmediate(gridGo.GetComponent<Collider>());
            var gridRenderer = gridGo.GetComponent<MeshRenderer>();
            gridRenderer.sharedMaterial = Mat("M_Grid");
            gridRenderer.shadowCastingMode = ShadowCastingMode.Off;
            var grid = gridGo.AddComponent<GridOverlay>();
            gridGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            // ---- Game
            var gameGo = new GameObject("PuzzleGame");
            var builder = gameGo.AddComponent<LevelBuilder>();
            var game = gameGo.AddComponent<PuzzleGame>();

            BuildUI(game, grid, set, s, cam, out var top, out var bottom);

            Set(rig, ("topBar", top), ("bottomBar", bottom));
            Set(builder,
                ("boardPrefab", Prefab("Board")), ("wallPrefab", Prefab("WallBlock")), // no rim: board edges stay invisible
                ("groundPrefab", Prefab("Ground")), ("startPrefab", Prefab("Start")), ("exitPrefab", Prefab("Exit")),
                ("markerPrefab", Prefab("InstructionMarker").GetComponent<InstructionMarker>()), ("grid", grid),
                ("sceneryPrefab", Prefab("SceneryBlock")),
                ("sceneryMaterials", new Object[] { Mat("M_SceneryLight"), Mat("M_SceneryDark"), Mat("M_AccentBlue"), Mat("M_AccentOchre"), Mat("M_AccentRose"), Mat("M_AccentGreen") }),
                ("accentChance", 0.25f),
                ("revealedWallMaterial", Mat("M_WallRevealed")));
            Set(game, ("instructions", set), ("builder", builder), ("cameraRig", rig), ("dudePrefab", Prefab("Dude").GetComponent<DudeView>()));

            // ---- Event system (new Input System: touch + mouse on web)
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            es.GetComponent<EventSystem>().pixelDragThreshold = 10;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            Selection.activeGameObject = gameGo;
            Debug.Log($"[ColourWays] Puzzle scene saved to {ScenePath}. Press Play (Game view: a portrait size like 1080x1920).");
        }

        private static void BuildUI(PuzzleGame game, GridOverlay grid, InstructionSet set, Sprites s, Camera worldCamera,
            out RectTransform topBar, out RectTransform bottomBar)
        {
            var canvasRect = UI("Canvas", null, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster),
                typeof(DragController), typeof(PuzzleHUD), typeof(MobileFrame));
            var canvas = canvasRect.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasRect.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // MobileFrame sets the scale at runtime
            scaler.referenceResolution = new Vector2(1080f, 1920f);

            // Side bars shown when the screen is wider than a phone (desktop web, landscape editor).
            var leftBar = Img(canvasRect, "LetterboxLeft", null, Letterbox);
            var rightBar = Img(canvasRect, "LetterboxRight", null, Letterbox);
            leftBar.raycastTarget = rightBar.raycastTarget = true;

            // Portrait column holding everything.
            var frame = UI("PhoneFrame", canvasRect);
            Stretch(frame);

            // World touches (pan / move instructions), behind everything else.
            var board = UI("Board", frame, typeof(Image), typeof(BoardInput));
            Stretch(board);
            board.GetComponent<Image>().color = Color.clear;

            var safe = UI("SafeArea", frame, typeof(SafeArea));
            Stretch(safe);

            // ================= Top: screen-space header + stats (no backgrounds, sits on the white world)
            topBar = UI("Top", safe);
            Place(topBar, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 250f), new Vector2(0.5f, 1f));

            var header = UI("Header", topBar);
            Place(header, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 134f), new Vector2(0.5f, 1f));
            var title = Label(header, "Title", "ColourWays", 52, TextAnchor.MiddleLeft, Ink, true);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -20f), new Vector2(560f, 60f), new Vector2(0f, 1f));
            var subtitle = Label(header, "Subtitle", "Today", 28, TextAnchor.MiddleLeft, Muted, false);
            Place(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(46f, -82f), new Vector2(560f, 36f), new Vector2(0f, 1f));
            // Round, soft icon buttons (Mini Metro style).
            var help = IconButton(header, "Help", null, s, new Vector2(-36f, 0f));
            var q = Label(help.transform, "Q", "?", 40, TextAnchor.MiddleCenter, Ink, true);
            Stretch(q.rectTransform);
            var recenter = IconButton(header, "Recenter", s.Crosshair, s, new Vector2(-132f, 0f));
            var rotateRight = IconButton(header, "RotateRight", s.RotateRight, s, new Vector2(-228f, 0f));
            var rotateLeft = IconButton(header, "RotateLeft", s.RotateLeft, s, new Vector2(-324f, 0f));

            var stats = UI("Stats", topBar);
            Place(stats, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -134f), new Vector2(0f, 116f), new Vector2(0.5f, 1f));
            var statsRow = UI("Row", stats, typeof(HorizontalLayoutGroup));
            Stretch(statsRow);
            var statsLayout = statsRow.GetComponent<HorizontalLayoutGroup>();
            statsLayout.childControlWidth = statsLayout.childControlHeight = true;
            statsLayout.childForceExpandWidth = statsLayout.childForceExpandHeight = true;
            statsLayout.padding = new RectOffset(30, 30, 0, 0);
            var scoreValue = Stat(statsRow, "SCORE");
            var parValue = Stat(statsRow, "PAR");
            var timeValue = Stat(statsRow, "TIME");
            var homeValue = Stat(statsRow, "HOME");

            // ================= Toast
            var toastRect = UI("Toast", safe, typeof(Image), typeof(CanvasGroup));
            Place(toastRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -276f), new Vector2(820f, 76f), new Vector2(0.5f, 1f));
            var toastBg = toastRect.GetComponent<Image>();
            Pill(toastBg, s.Pill, Ink);
            toastBg.raycastTarget = false;
            var toast = Label(toastRect, "Text", "", 28, TextAnchor.MiddleCenter, Color.white, true);
            Stretch(toast.rectTransform);
            var toastGroup = toastRect.GetComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false;

            // ================= Bottom: tray, actions
            bottomBar = UI("Bottom", safe);
            Place(bottomBar, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 420f), new Vector2(0.5f, 0f));
            // Floating rounded card with a soft shadow (comfy, Mini Metro style).
            var panelShadow = Img(bottomBar, "Shadow", s.Shadow, new Color(0f, 0f, 0f, 0.14f));
            panelShadow.type = Image.Type.Sliced;
            Stretch(panelShadow.rectTransform);
            panelShadow.rectTransform.offsetMin = new Vector2(24f - 30f, 24f - 38f);
            panelShadow.rectTransform.offsetMax = new Vector2(-24f + 30f, 0f + 22f);
            var panel = Img(bottomBar, "Panel", s.Rounded, Color.white);
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = true;
            Stretch(panel.rectTransform);
            panel.rectTransform.offsetMin = new Vector2(24f, 24f);
            panel.rectTransform.offsetMax = new Vector2(-24f, 0f);

            var tray = UI("Tray", bottomBar, typeof(HorizontalLayoutGroup));
            Place(tray, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), new Vector2(-80f, 214f), new Vector2(0.5f, 1f));
            var trayLayout = tray.GetComponent<HorizontalLayoutGroup>();
            trayLayout.spacing = 14f;
            trayLayout.childAlignment = TextAnchor.MiddleCenter;
            trayLayout.childControlWidth = trayLayout.childControlHeight = false;
            trayLayout.childForceExpandWidth = trayLayout.childForceExpandHeight = false;

            var actions = UI("Actions", bottomBar, typeof(HorizontalLayoutGroup));
            Place(actions, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 52f), new Vector2(-104f, 108f), new Vector2(0.5f, 0f));
            var actionsLayout = actions.GetComponent<HorizontalLayoutGroup>();
            actionsLayout.spacing = 18f;
            actionsLayout.childControlWidth = actionsLayout.childControlHeight = true;
            actionsLayout.childForceExpandWidth = false;
            actionsLayout.childForceExpandHeight = true;
            var clear = PillButton(actions, "Clear", "Clear", s, false, 1f, out _);
            var run = PillButton(actions, "Run", "Run", s, true, 1.8f, out var runLabel);
            var runAccent = Accent(run, AccentGreen, s);
            var newLevel = PillButton(actions, "New", "New", s, false, 1f, out _);

            // ================= Results sheet
            var results = Sheet(safe, "Results", s, 900f, 1120f, out var rCard);
            var kicker = Label(rCard, "Kicker", "COLOURWAYS", 24, TextAnchor.UpperCenter, Muted, true);
            Place(kicker.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -60f), new Vector2(0f, 34f), new Vector2(0.5f, 1f));
            var headline = Label(rCard, "Headline", "Splendid!", 74, TextAnchor.UpperCenter, Ink, true);
            Place(headline.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -104f), new Vector2(0f, 90f), new Vector2(0.5f, 1f));
            var rScore = Label(rCard, "Score", "0", 180, TextAnchor.MiddleCenter, Ink, true);
            Place(rScore.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -210f), new Vector2(0f, 200f), new Vector2(0.5f, 1f));
            var rCaption = Label(rCard, "Caption", "points  ·  lower is better", 28, TextAnchor.MiddleCenter, Muted, false);
            Place(rCaption.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -412f), new Vector2(0f, 40f), new Vector2(0.5f, 1f));
            var rStats = UI("Stats", rCard, typeof(HorizontalLayoutGroup));
            Place(rStats, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -480f), new Vector2(-120f, 120f), new Vector2(0.5f, 1f));
            var rStatsLayout = rStats.GetComponent<HorizontalLayoutGroup>();
            rStatsLayout.childControlWidth = rStatsLayout.childControlHeight = true;
            rStatsLayout.childForceExpandWidth = rStatsLayout.childForceExpandHeight = true;
            var rPar = Stat(rStats, "PAR");
            var rInstr = Stat(rStats, "INSTRUCTIONS");
            var rTime = Stat(rStats, "TIME");
            var rBest = Label(rCard, "Best", "", 28, TextAnchor.MiddleCenter, Muted, false);
            Place(rBest.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -620f), new Vector2(0f, 40f), new Vector2(0.5f, 1f));
            var share = PillButton(rCard, "Share", "Share", s, true, 0f, out var shareLabel);
            Place((RectTransform)share.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -690f), new Vector2(620f, 112f), new Vector2(0.5f, 1f));
            Accent(share, AccentPink, s);
            var rRow = UI("Row", rCard, typeof(HorizontalLayoutGroup));
            Place(rRow, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -824f), new Vector2(760f, 100f), new Vector2(0.5f, 1f));
            var rRowLayout = rRow.GetComponent<HorizontalLayoutGroup>();
            rRowLayout.spacing = 20f;
            rRowLayout.childControlWidth = rRowLayout.childControlHeight = true;
            rRowLayout.childForceExpandWidth = rRowLayout.childForceExpandHeight = true;
            var replay = PillButton(rRow, "Replay", "Replay", s, false, 1f, out _);
            var next = PillButton(rRow, "Next", "Next puzzle", s, false, 1f, out _);
            var daily = TextButton(rCard, "Daily", "Play today's puzzle", new Vector2(0f, -950f));

            // ================= Help sheet
            var helpSheet = Sheet(safe, "Help", s, 900f, 1380f, out var hCard);
            var hTitle = Label(hCard, "Title", "How to play", 64, TextAnchor.UpperLeft, Ink, true);
            Place(hTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -56f), new Vector2(-120f, 80f), new Vector2(0.5f, 1f));
            var lead = Label(hCard, "Lead", "Guide 3 dudes home in as few points as possible.", 34, TextAnchor.UpperLeft, Ink, false);
            Place(lead.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -146f), new Vector2(-120f, 90f), new Vector2(0.5f, 1f));
            Wrap(lead);
            float y = -250f;
            foreach (var def in Instructions)
            {
                var entry = set.Get(def.type);
                int cost = entry != null ? entry.cost : def.cost;
                // Same look as the tray keys: coloured station disc with a white glyph.
                var chip = Img(hCard, def.name + " Disc", s.Disc, def.colour);
                Place(chip.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, y), new Vector2(84f, 84f), new Vector2(0f, 1f));
                var chipIcon = Img(chip.rectTransform, "Icon", s.For(def.type), Color.white);
                Place(chipIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f));
                var line = Label(hCard, def.name + " Text", $"{(HasCustomFonts ? def.name : $"<b>{def.name}</b>")}  ·  {cost} pt{(cost == 1 ? "" : "s")}\n{def.help}", 28, TextAnchor.MiddleLeft, Ink, false);
                line.supportRichText = true;
                Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(80f, y), new Vector2(-260f, 84f), new Vector2(0.5f, 1f));
                y -= 104f;
            }
            var rules = Label(hCard, "Rules",
                "The board is invisible. Dudes paint what they touch.\nThey walk straight. Walls, pits and edges are fatal.\nDrop instructions any time, even mid-run.\nEvery second of running adds a point.\nDrag to pan · pinch to zoom · rotate with the arrows.",
                28, TextAnchor.UpperLeft, Muted, false);
            Place(rules.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, y - 10f), new Vector2(-120f, 190f), new Vector2(0.5f, 1f));
            Wrap(rules);
            var haptics = PillButton(hCard, "Haptics", "Haptics: On", s, false, 0f, out var hapticsLabel);
            Place((RectTransform)haptics.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-170f, 150f), new Vector2(320f, 100f), new Vector2(0.5f, 0f));
            var play = PillButton(hCard, "Play", "Play", s, true, 0f, out _);
            Place((RectTransform)play.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(170f, 150f), new Vector2(320f, 100f), new Vector2(0.5f, 0f));
            Accent(play, AccentGreen, s);
            var helpDaily = TextButton(hCard, "Daily", "Play today's puzzle", Vector2.zero);
            Place((RectTransform)helpDaily.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(600f, 60f), new Vector2(0.5f, 0f));

            // ================= Drag ghost (topmost)
            var ghost = Img(canvasRect, "DragGhost", s.Rounded, Ink); // tinted with the instruction colour at runtime
            ghost.type = Image.Type.Sliced;
            Place(ghost.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(128f, 128f));
            var ghostIcon = Img(ghost.rectTransform, "Icon", s.Arrow, Color.white);
            Place(ghostIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(88f, 88f));
            ghostIcon.preserveAspect = true;
            ghost.gameObject.SetActive(false);

            // ================= Wiring
            Set(canvasRect.GetComponent<MobileFrame>(), ("worldCamera", worldCamera), ("scaler", scaler), ("frame", frame),
                ("leftBar", leftBar.rectTransform), ("rightBar", rightBar.rectTransform));
            leftBar.gameObject.SetActive(false);
            rightBar.gameObject.SetActive(false);

            var drag = canvasRect.GetComponent<DragController>();
            Set(drag, ("game", game), ("grid", grid), ("canvas", canvas), ("ghost", ghost.rectTransform), ("ghostBackground", ghost),
                ("ghostIcon", ghostIcon), ("trayArea", bottomBar));
            Set(board.GetComponent<BoardInput>(), ("game", game), ("drag", drag));
            Set(canvasRect.GetComponent<PuzzleHUD>(),
                ("game", game), ("drag", drag),
                ("subtitle", subtitle), ("helpButton", help), ("recenterButton", recenter),
                ("rotateLeftButton", rotateLeft), ("rotateRightButton", rotateRight),
                ("scoreValue", scoreValue), ("parValue", parValue), ("timeValue", timeValue), ("homeValue", homeValue),
                ("tray", tray), ("cardPrefab", Prefab("InstructionCard").GetComponent<InstructionCard>()),
                ("runButton", run), ("runLabel", runLabel), ("runBackground", run.GetComponent<Image>()), ("runAccent", runAccent),
                ("runColour", Ink), ("stopColour", Ink), ("runAccentColour", MetroGreen), ("stopAccentColour", MetroRed),
                ("clearButton", clear), ("newButton", newLevel),
                ("toast", toast), ("toastGroup", toastGroup),
                ("resultsSheet", results), ("resultsHeadline", headline), ("resultsScore", rScore), ("resultsPar", rPar),
                ("resultsInstructions", rInstr), ("resultsTime", rTime), ("resultsBest", rBest),
                ("shareButton", share), ("shareLabel", shareLabel), ("nextButton", next), ("replayButton", replay), ("dailyButton", daily),
                ("helpSheet", helpSheet), ("helpCloseButton", play), ("helpDailyButton", helpDaily),
                ("hapticsButton", haptics), ("hapticsLabel", hapticsLabel));

            results.gameObject.SetActive(false);
            helpSheet.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ UI helpers

        private static RectTransform UI(string name, Transform parent, params Type[] components)
        {
            var types = new Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            Array.Copy(components, 0, types, 1, components.Length);
            var go = new GameObject(name, types);
            if (parent != null) go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Place(RectTransform r, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            r.anchorMin = anchorMin;
            r.anchorMax = anchorMax;
            r.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            r.anchoredPosition = position;
            r.sizeDelta = size;
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        private static Image Img(Transform parent, string name, Sprite sprite, Color colour)
        {
            var img = UI(name, parent, typeof(Image)).GetComponent<Image>();
            img.sprite = sprite;
            img.color = colour;
            img.raycastTarget = false;
            return img;
        }

        private static Text Label(Transform parent, string name, string text, int size, TextAnchor align, Color colour, bool bold)
        {
            var t = UI(name, parent, typeof(Text)).GetComponent<Text>();
            StyleText(t, RoleFor(name, bold));
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = colour;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            return t;
        }

        private static void Wrap(Text t)
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.lineSpacing = 1.15f;
        }

        /// <summary>Thin 2px rule along the bottom (or top) edge, NYT style.</summary>
        private static void RuleLine(RectTransform parent, bool top)
        {
            var rule = Img(parent, "Rule", null, Rule);
            Place(rule.rectTransform, new Vector2(0f, top ? 1f : 0f), new Vector2(1f, top ? 1f : 0f), Vector2.zero, new Vector2(0f, 2f), new Vector2(0.5f, top ? 1f : 0f));
        }

        private static Text Stat(RectTransform row, string caption)
        {
            var col = UI(caption, row);
            var value = Label(col, "Value", "0", 44, TextAnchor.MiddleCenter, Ink, true);
            Place(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 14f), new Vector2(0f, 54f));
            var label = Label(col, "Label", caption, 20, TextAnchor.MiddleCenter, Muted, true);
            Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -30f), new Vector2(0f, 28f));
            return value;
        }

        private static void Pill(Image image, Sprite pill, Color colour)
        {
            image.sprite = pill;
            image.type = Image.Type.Sliced;
            image.color = colour;
        }

        private static Button PillButton(Transform parent, string name, string label, Sprites s, bool primary, float flex, out Text text)
        {
            var r = UI(name, parent, typeof(Image), typeof(Button), typeof(LayoutElement));
            var img = r.GetComponent<Image>();
            // Fully rounded pills. Primary: soft charcoal. Secondary: warm grey, no outline.
            Pill(img, s.Pill, primary ? Ink : KeyGrey);
            var le = r.GetComponent<LayoutElement>();
            le.minHeight = 100f;
            le.flexibleWidth = flex;
            le.minWidth = 160f;
            var button = r.GetComponent<Button>();
            var colours = button.colors;
            colours.highlightedColor = new Color(0.96f, 0.96f, 0.96f);
            colours.pressedColor = new Color(0.86f, 0.86f, 0.86f);
            colours.disabledColor = new Color(0.8f, 0.8f, 0.8f, 0.6f);
            colours.fadeDuration = 0.08f;
            button.colors = colours;
            text = Label(r, "Label", label, 34, TextAnchor.MiddleCenter, primary ? Color.white : Ink, true);
            Stretch(text.rectTransform);
            return button;
        }

        /// <summary>A small coloured dot inside a button, like a line colour: colour as an accent, never a fill.</summary>
        private static Image Accent(Button button, Color colour, Sprites s)
        {
            var dot = Img(button.transform, "Accent", s.Disc, colour);
            Place(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(36f, 0f), new Vector2(22f, 22f), new Vector2(0f, 0.5f));
            return dot;
        }

        /// <summary>Round, soft icon button: warm-grey disc with a charcoal glyph.</summary>
        private static Button IconButton(RectTransform parent, string name, Sprite glyph, Sprites s, Vector2 position)
        {
            var r = UI(name, parent, typeof(Image), typeof(Button));
            Place(r, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), position, new Vector2(84f, 84f), new Vector2(1f, 0.5f));
            var img = r.GetComponent<Image>();
            img.sprite = s.Disc;
            img.color = KeyGrey;
            if (glyph != null)
            {
                var g = Img(r, "Glyph", glyph, Ink);
                Place(g.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 54f));
            }
            return r.GetComponent<Button>();
        }

        private static Button TextButton(RectTransform parent, string name, string label, Vector2 position)
        {
            var r = UI(name, parent, typeof(Image), typeof(Button));
            Place(r, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), position, new Vector2(600f, 60f), new Vector2(0.5f, 1f));
            r.GetComponent<Image>().color = Color.clear;
            var t = Label(r, "Label", label, 30, TextAnchor.MiddleCenter, Ink, true);
            Stretch(t.rectTransform);
            var underline = Img(r, "Underline", null, Ink);
            Place(underline.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(270f, 2f));
            return r.GetComponent<Button>();
        }

        /// <summary>
        /// Full-screen sheet: dim layer (child 0) + panel (child 1, animated by PuzzleHUD) holding a
        /// soft shadow and a big-radius white card.
        /// </summary>
        private static CanvasGroup Sheet(RectTransform parent, string name, Sprites s, float width, float height, out RectTransform card)
        {
            var root = UI(name + "Sheet", parent, typeof(CanvasGroup));
            Stretch(root);
            var dim = Img(root, "Dim", null, new Color(Cream.r, Cream.g, Cream.b, 0.82f));
            dim.raycastTarget = true;
            Stretch(dim.rectTransform);
            dim.rectTransform.offsetMin = new Vector2(0f, -400f);
            dim.rectTransform.offsetMax = new Vector2(0f, 400f);

            var panel = UI("Panel", root);
            Place(panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            var shadow = Img(panel, "Shadow", s.Shadow, new Color(0f, 0f, 0f, 0.16f));
            shadow.type = Image.Type.Sliced;
            Stretch(shadow.rectTransform);
            shadow.rectTransform.offsetMin = new Vector2(-30f, -44f);
            shadow.rectTransform.offsetMax = new Vector2(30f, 18f);

            var cardImg = Img(panel, "Card", s.Rounded, Color.white);
            cardImg.type = Image.Type.Sliced;
            cardImg.pixelsPerUnitMultiplier = 0.75f; // even rounder corners on big cards
            cardImg.raycastTarget = true;
            card = cardImg.rectTransform;
            Stretch(card);
            return root.GetComponent<CanvasGroup>();
        }
    }
}
