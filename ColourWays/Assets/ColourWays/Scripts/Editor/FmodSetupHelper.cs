using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ColourWays.Audio;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ColourWays.EditorTools
{
    /// <summary>
    /// Editor workflow utilities for integrating and validating FMOD Studio in ColourWays:
    ///  • Automatically toggles the <c>COLOURWAYS_FMOD</c> scripting define when FMOD for Unity is imported/removed.
    ///  • One-click creation of <c>AudioConfig.asset</c> in Resources.
    ///  • One-click disabling of Unity's built-in AudioManager (prevents conflicts with FMOD).
    ///  • One-click swap from Unity <see cref="AudioListener"/> to <c>FMODUnity.StudioListener</c> on Player prefab &amp; MainMenu.
    ///  • Readiness validator + Windows x64 validation build shortcut.
    /// </summary>
    [InitializeOnLoad]
    public static class FmodSetupHelper
    {
        private const string FmodDefine = "COLOURWAYS_FMOD";
        private const string AudioConfigAssetPath = "Assets/ColourWays/Resources/ColourWays/AudioConfig.asset";
        private const string PlayerPrefabPath = "Assets/ColourWays/Prefabs/Player.prefab";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

        static FmodSetupHelper()
        {
            EditorApplication.delayCall += SyncFmodScriptingDefine;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }

        private static bool IsFmodImported() => FindType("FMODUnity.RuntimeManager") != null;

        private static void SyncFmodScriptingDefine()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            bool hasFmod = IsFmodImported();
            var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            if (target == NamedBuildTarget.Unknown)
                target = NamedBuildTarget.Standalone;

            SyncDefineForTarget(target, hasFmod);
            if (target != NamedBuildTarget.Standalone)
                SyncDefineForTarget(NamedBuildTarget.Standalone, hasFmod);
        }

        private static void SyncDefineForTarget(NamedBuildTarget target, bool shouldExist)
        {
            string raw = PlayerSettings.GetScriptingDefineSymbols(target);
            var defines = new HashSet<string>(
                raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
                StringComparer.Ordinal);

            bool changed = shouldExist ? defines.Add(FmodDefine) : defines.Remove(FmodDefine);
            if (!changed) return;

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            Debug.Log(shouldExist
                ? $"[ColourWays.Audio] FMOD for Unity detected! Enabled '{FmodDefine}' scripting define for {target.TargetName}."
                : $"[ColourWays.Audio] FMOD for Unity not found. Removed '{FmodDefine}' scripting define for {target.TargetName}.");
        }

        // ------------------------------------------------------------------ Menu Actions

        [MenuItem("ColourWays/Audio (FMOD)/1. Create or Select Audio Config", priority = 10)]
        public static void CreateOrSelectAudioConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioConfig>(AudioConfigAssetPath);
            if (existing == null)
            {
                string dir = Path.GetDirectoryName(AudioConfigAssetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                existing = ScriptableObject.CreateInstance<AudioConfig>();
                AssetDatabase.CreateAsset(existing, AudioConfigAssetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[ColourWays.Audio] Created AudioConfig at '{AudioConfigAssetPath}'.");
            }

            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
        }

        [MenuItem("ColourWays/Audio (FMOD)/2. Disable Unity Built-in Audio", priority = 20)]
        public static void DisableUnityAudio()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[ColourWays.Audio] Could not load ProjectSettings/AudioManager.asset.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("m_DisableAudio");
            if (prop == null)
            {
                Debug.LogError("[ColourWays.Audio] Property 'm_DisableAudio' not found in AudioManager.asset.");
                return;
            }

            prop.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[ColourWays.Audio] Disabled Unity's built-in AudioManager (m_DisableAudio = true) as recommended by FMOD.");
        }

        [MenuItem("ColourWays/Audio (FMOD)/3. Configure FMOD StudioListeners (Player + MainMenu)", priority = 30)]
        public static void ConfigureFmodListeners()
        {
            var studioListenerType = FindType("FMODUnity.StudioListener");
            if (studioListenerType == null)
            {
                EditorUtility.DisplayDialog(
                    "FMOD Not Imported Yet",
                    "Import the official 'FMOD for Unity' .unitypackage first, wait for compilation to finish, then run this menu item again.",
                    "OK");
                return;
            }

            // 1. Update Player.prefab
            if (File.Exists(PlayerPrefabPath))
            {
                var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
                try
                {
                    var cam = root.GetComponentInChildren<Camera>(true);
                    if (cam != null)
                    {
                        var oldListener = cam.GetComponent<AudioListener>();
                        if (oldListener != null)
                            UnityEngine.Object.DestroyImmediate(oldListener, true);

                        var studioListener = cam.GetComponent(studioListenerType) ?? cam.gameObject.AddComponent(studioListenerType);
                        var so = new SerializedObject(studioListener);
                        var attenProp = so.FindProperty("attenuationObject");
                        if (attenProp != null)
                        {
                            attenProp.objectReferenceValue = root;
                            so.ApplyModifiedPropertiesWithoutUndo();
                        }

                        PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                        Debug.Log("[ColourWays.Audio] Updated Player.prefab: replaced AudioListener with FMOD StudioListener (attenuation anchored to Player root).");
                    }
                    else
                    {
                        Debug.LogWarning("[ColourWays.Audio] No Camera found inside Player.prefab.");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            // 2. Update MainMenu.unity
            if (File.Exists(MainMenuScenePath) && (Application.isBatchMode || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()))
            {
                var previousScene = SceneManager.GetActiveScene().path;
                var menuScene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
                var cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
                if (cam != null)
                {
                    var oldListener = cam.GetComponent<AudioListener>();
                    if (oldListener != null)
                        UnityEngine.Object.DestroyImmediate(oldListener);

                    if (cam.GetComponent(studioListenerType) == null)
                        cam.gameObject.AddComponent(studioListenerType);

                    EditorSceneManager.MarkSceneDirty(menuScene);
                    EditorSceneManager.SaveScene(menuScene);
                    Debug.Log("[ColourWays.Audio] Updated MainMenu.unity: added FMOD StudioListener to Main Camera.");
                }

                if (!string.IsNullOrEmpty(previousScene) && previousScene != MainMenuScenePath && File.Exists(previousScene))
                    EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            }
        }

        /// <summary>Batchmode-friendly entry point to sync defines, disable Unity audio, configure listeners, and log readiness.</summary>
        public static void BatchConfigureFmod()
        {
            SyncFmodScriptingDefine();
            CreateOrSelectAudioConfig();
            DisableUnityAudio();
            ConfigureFmodListeners();
            CheckReadiness();
        }

        [MenuItem("ColourWays/Audio (FMOD)/3b. Link FMOD Project & Auto-Bind Events", priority = 35)]
        public static void LinkProjectAndPopulateEvents()
        {
#if COLOURWAYS_FMOD
            const string relProjectPath = "../FMODProject/ColourWays/ColourWays.fspro";
            const string relBankPath = "../FMODProject/ColourWays/Build";
            string fullProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relProjectPath));
            if (!File.Exists(fullProjectPath))
            {
                Debug.LogError($"[ColourWays.Audio] FMOD Studio project not found at: {fullProjectPath}");
                return;
            }

            var settings = FMODUnity.Settings.Instance;
            settings.HasSourceProject = true;
            settings.HasPlatforms = true;
            settings.SourceProjectPath = relProjectPath;
            settings.SourceBankPath = relBankPath;
            settings.ImportType = FMODUnity.ImportType.StreamingAssets;
            settings.BankLoadType = FMODUnity.BankLoadType.All;
            EditorUtility.SetDirty(settings);

            FMODUnity.EventManager.RefreshBanks();
            FMODUnity.EventManager.CopyToStreamingAssets(BuildTarget.StandaloneWindows64);
            AssetDatabase.Refresh();

            var cfg = AssetDatabase.LoadAssetAtPath<AudioConfig>(AudioConfigAssetPath);
            if (cfg != null)
            {
                int bound = 0;
                if (TryBindEvent(cfg.mainMusicPath, out var ev)) { cfg.mainMusic = ev; bound++; }
                if (TryBindEvent(cfg.footstepPath, out ev)) { cfg.footstep = ev; bound++; }
                if (TryBindEvent(cfg.wallSplatPath, out ev)) { cfg.wallSplat = ev; bound++; }
                if (TryBindEvent(cfg.landPath, out ev)) { cfg.land = ev; bound++; }
                if (TryBindEvent(cfg.respawnPath, out ev)) { cfg.respawn = ev; bound++; }
                if (TryBindEvent(cfg.paintBlobCollectPath, out ev)) { cfg.paintBlobCollect = ev; bound++; }
                if (TryBindEvent(cfg.levelCompletePath, out ev)) { cfg.levelComplete = ev; bound++; }
                if (TryBindEvent(cfg.uiHoverPath, out ev)) { cfg.uiHover = ev; bound++; }
                if (TryBindEvent(cfg.uiClickPath, out ev)) { cfg.uiClick = ev; bound++; }
                if (TryBindEvent(cfg.sceneTransitionPath, out ev)) { cfg.sceneTransition = ev; bound++; }

                EditorUtility.SetDirty(cfg);
                Debug.Log($"[ColourWays.Audio] Linked '{relProjectPath}', refreshed StreamingAssets banks, and bound {bound} FMOD EventReferences on AudioConfig.");
            }

            AssetDatabase.SaveAssets();
            CheckReadiness();
#else
            Debug.LogWarning("[ColourWays.Audio] Import FMOD for Unity before linking the FMOD Studio project.");
#endif
        }

#if COLOURWAYS_FMOD
        private static bool TryBindEvent(string path, out FMODUnity.EventReference eventRef)
        {
            eventRef = default;
            if (string.IsNullOrEmpty(path)) return false;
            var editorEv = FMODUnity.EventManager.EventFromPath(path);
            if (editorEv == null) return false;
            eventRef = new FMODUnity.EventReference { Guid = editorEv.Guid, Path = editorEv.Path };
            return true;
        }
#endif

        [MenuItem("ColourWays/Audio (FMOD)/4. Check FMOD Integration Readiness", priority = 40)]
        public static void CheckReadiness()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== ColourWays FMOD Integration Readiness Report ===");

            bool fmodImported = IsFmodImported();
            AppendCheck(sb, fmodImported, "FMOD for Unity plugin imported (FMODUnity.RuntimeManager found)",
                "Import FMOD for Unity (.unitypackage) from fmod.com/download.");

            bool defineActive = false;
#if COLOURWAYS_FMOD
            defineActive = true;
#endif
            AppendCheck(sb, defineActive, "COLOURWAYS_FMOD scripting define active",
                "Auto-enables once FMOD for Unity finishes importing.");

            bool unityAudioDisabled = IsUnityAudioDisabled();
            AppendCheck(sb, unityAudioDisabled, "Unity built-in Audio disabled (AudioManager.m_DisableAudio = true)",
                "Run 'ColourWays > Audio (FMOD) > 2. Disable Unity Built-in Audio'.");

            bool hasConfig = AssetDatabase.LoadAssetAtPath<AudioConfig>(AudioConfigAssetPath) != null;
            AppendCheck(sb, hasConfig, $"AudioConfig asset present ({AudioConfigAssetPath})",
                "Run 'ColourWays > Audio (FMOD) > 1. Create or Select Audio Config'.");

            string streamingAssets = Application.streamingAssetsPath;
            string[] banks = Directory.Exists(streamingAssets)
                ? Directory.GetFiles(streamingAssets, "*.bank", SearchOption.AllDirectories)
                : Array.Empty<string>();
            bool hasMasterBank = banks.Any(b => Path.GetFileName(b).Equals("Master.bank", StringComparison.OrdinalIgnoreCase));
            bool hasStringsBank = banks.Any(b => Path.GetFileName(b).Equals("Master.strings.bank", StringComparison.OrdinalIgnoreCase));
            AppendCheck(sb, banks.Length > 0 && hasMasterBank && hasStringsBank,
                $"FMOD banks in StreamingAssets ({banks.Length} .bank files found)",
                "Link FMOD Project in FMOD > Edit Settings and press F7 (Build) in FMOD Studio.");

            bool playerHasStudioListener = CheckPrefabHasStudioListener(PlayerPrefabPath);
            AppendCheck(sb, playerHasStudioListener, "Player.prefab uses FMOD StudioListener",
                "Run 'ColourWays > Audio (FMOD) > 3. Configure FMOD StudioListeners'.");

            int enabledScenes = EditorBuildSettings.scenes.Count(s => s.enabled);
            AppendCheck(sb, enabledScenes >= 4, $"Build Settings scenes enabled ({enabledScenes} scenes)",
                "Ensure MainMenu, Level_1, Level_2, and Level_3 are checked in Build Profiles.");

            string report = sb.ToString();
            Debug.Log(report);
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("FMOD Integration Readiness", report, "OK");
        }

        [MenuItem("ColourWays/Audio (FMOD)/5. Build Windows x64 Validation Build", priority = 50)]
        public static void BuildWindowsValidationBuild()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("No Scenes Enabled", "Add at least one enabled scene in Build Profiles before building.", "OK");
                return;
            }

            string buildDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Builds", "Windows"));
            string exePath = Path.Combine(buildDir, "ColourWays.exe");
            Directory.CreateDirectory(buildDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development, // Keeps Console / FMOD Live Update available for profiling & validation
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[ColourWays.Audio] Windows x64 Development Build succeeded ({report.summary.totalSize / 1048576f:F1} MB): {exePath}");
                if (!Application.isBatchMode)
                    EditorUtility.RevealInFinder(exePath);
            }
            else
            {
                Debug.LogError($"[ColourWays.Audio] Windows build failed with result: {report.summary.result}");
            }
        }

        // ------------------------------------------------------------------ Helpers

        private static bool IsUnityAudioDisabled()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset");
            if (assets == null || assets.Length == 0) return false;
            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("m_DisableAudio");
            return prop != null && prop.boolValue;
        }

        private static bool CheckPrefabHasStudioListener(string prefabPath)
        {
            var studioListenerType = FindType("FMODUnity.StudioListener");
            if (studioListenerType == null) return false;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return false;
            return prefab.GetComponentInChildren(studioListenerType, true) != null;
        }

        private static void AppendCheck(StringBuilder sb, bool ok, string label, string fixHint)
        {
            sb.AppendLine(ok ? $"[PASS] {label}" : $"[TODO] {label}\n       -> {fixHint}");
        }
    }
}
