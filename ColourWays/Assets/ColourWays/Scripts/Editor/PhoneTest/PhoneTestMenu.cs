using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ColourWays.EditorTools.PhoneTest
{
    /// <summary>
    /// ColourWays > Phone Test: build the game for the web and serve it on this PC's Wi-Fi so it
    /// can be played in Safari / Chrome on a phone. The server runs inside the editor and
    /// restarts itself after script reloads until you stop it.
    /// </summary>
    [InitializeOnLoad]
    public static class PhoneTestMenu
    {
        private const string BuildFolder = "Builds/WebPhone";
        private const string ScenePath = "Assets/Scenes/Puzzle.unity";
        private const string ServingKey = "ColourWays.PhoneTest.Serving";
        private const int PreferredPort = 8080;

        private static StaticFileServer _server;

        static PhoneTestMenu()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopQuietly;
            EditorApplication.quitting += StopQuietly;

            // Keep serving across script reloads (a WebGL build triggers one).
            if (SessionState.GetBool(ServingKey, false) && !HasBuild())
                SessionState.SetBool(ServingKey, false);
            if (SessionState.GetBool(ServingKey, false) && HasBuild())
                EditorApplication.delayCall += () => StartServer(false);
        }

        private static string BuildPath => Path.GetFullPath(BuildFolder);
        private static bool HasBuild() => File.Exists(Path.Combine(BuildPath, "index.html"));

        // ------------------------------------------------------------------ Menu

        [MenuItem("ColourWays/Phone Test/Build Web + Serve on Wi-Fi", priority = 100)]
        public static void BuildAndServe()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                EditorUtility.DisplayDialog("Web Build Support needed",
                    "Unity can't build for the web yet.\n\n" +
                    "Open Unity Hub > Installs > " + Application.unityVersion + " > (gear icon) > Add modules > tick 'Web Build Support' > Install.\n\n" +
                    "Then come back and run this again.", "OK");
                return;
            }
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("ColourWays", "Build the Puzzle scene first (ColourWays > Setup Puzzle Game).", "OK");
                return;
            }
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            ConfigureWebSettings();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            StopQuietly();
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog("ColourWays", $"Web build {report.summary.result}. Check the Console for errors.", "OK");
                return;
            }

            // Unity can report success even when the final web packaging step failed (e.g. Web
            // Build Support was installed while the editor was open). Trust the files, not the summary.
            if (!HasBuild())
            {
                EditorUtility.DisplayDialog("Restart Unity",
                    "The build didn't produce a web page (no index.html in " + BuildFolder + ").\n\n" +
                    "If you just installed Web Build Support, Unity has to be restarted before it can package web builds. " +
                    "Save, close and reopen Unity, then run this again.", "OK");
                return;
            }

            Debug.Log($"[ColourWays] Web build ready ({report.summary.totalSize / (1024f * 1024f):0.0} MB) in {BuildPath}");
            StartServer(true);
        }

        [MenuItem("ColourWays/Phone Test/Serve Last Build", priority = 101)]
        public static void ServeLastBuild()
        {
            if (!HasBuild())
            {
                EditorUtility.DisplayDialog("ColourWays", "No web build yet. Use 'Build Web + Serve on Wi-Fi' first.", "OK");
                return;
            }
            StartServer(true);
        }

        [MenuItem("ColourWays/Phone Test/Show Phone Address", priority = 102)]
        public static void ShowAddress()
        {
            if (_server == null || !_server.IsRunning)
            {
                EditorUtility.DisplayDialog("ColourWays", "The phone server isn't running. Use 'Serve Last Build' or 'Build Web + Serve on Wi-Fi'.", "OK");
                return;
            }
            ShowInstructions();
        }

        [MenuItem("ColourWays/Phone Test/Stop Server", priority = 103)]
        public static void StopServer()
        {
            SessionState.SetBool(ServingKey, false);
            StopQuietly();
            Debug.Log("[ColourWays] Phone server stopped.");
        }

        [MenuItem("ColourWays/Phone Test/Stop Server", true)]
        private static bool StopServerValidate() => _server != null && _server.IsRunning;

        // ------------------------------------------------------------------ Internals

        /// <summary>Web settings that work over a plain-HTTP local server and on iPhone Safari.</summary>
        private static void ConfigureWebSettings()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip; // gzip works over http (brotli needs https)
            PlayerSettings.WebGL.decompressionFallback = false;                  // our server sends Content-Encoding
            PlayerSettings.WebGL.dataCaching = false;                            // always load the latest build
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        }

        private static void StartServer(bool showDialog)
        {
            StopQuietly();
            for (int port = PreferredPort; port < PreferredPort + 10; port++)
            {
                try
                {
                    _server = new StaticFileServer(BuildPath, port);
                    _server.Log += message => Debug.Log("[ColourWays phone server] " + message);
                    _server.Start();
                    break;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    _server = null; // port busy, try the next one
                }
            }

            if (_server == null)
            {
                Debug.LogError("[ColourWays] Couldn't start the phone server (ports 8080-8089 busy).");
                return;
            }

            SessionState.SetBool(ServingKey, true);
            string urls = string.Join("   ", StaticFileServer.LanAddresses().Select(ip => $"http://{ip}:{_server.Port}"));
            Debug.Log($"[ColourWays] Phone server running. On your phone (same Wi-Fi) open: {urls}");
            if (showDialog) ShowInstructions();
        }

        private static void ShowInstructions()
        {
            var addresses = StaticFileServer.LanAddresses().Select(ip => $"http://{ip}:{_server.Port}").ToList();
            string first = addresses.FirstOrDefault() ?? $"http://<this PC's IP>:{_server.Port}";
            EditorGUIUtility.systemCopyBuffer = first;

            EditorUtility.DisplayDialog("Play ColourWays on your phone",
                "On your iPhone, open Safari and go to:\n\n" +
                string.Join("\n", addresses) + "\n\n" +
                "(Copied to the clipboard.)\n\n" +
                "Phone and PC must be on the same Wi-Fi. If Windows asks, allow Unity through the firewall.\n\n" +
                "Won't load? Some school/work Wi-Fi blocks devices from seeing each other: turn on the iPhone's " +
                "Personal Hotspot, connect this PC to it, then use ColourWays > Phone Test > Show Phone Address.\n\n" +
                "Tip: in Safari tap Share > Add to Home Screen for a full-screen, app-like test.",
                "OK");
        }

        private static void StopQuietly()
        {
            _server?.Stop();
            _server = null;
        }
    }
}
