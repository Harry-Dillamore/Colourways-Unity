using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ColourWays.EditorTools.PhoneTest
{
    /// <summary>
    /// Minimal static HTTP server for testing WebGL builds on a phone over Wi-Fi. Runs inside
    /// the Unity editor (no external executables), serves Unity's .gz/.br files with the right
    /// Content-Encoding and disables caching so a rebuild always loads fresh.
    /// Deliberately has no UnityEditor dependency so it can be tested on its own.
    /// </summary>
    public sealed class StaticFileServer
    {
        private readonly string _root;
        private readonly int _port;
        private TcpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public event Action<string> Log;
        public bool IsRunning => _running;
        public int Port => _port;
        public string Root => _root;

        private static readonly Dictionary<string, string> Mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".html"] = "text/html; charset=utf-8",
            [".js"] = "application/javascript",
            [".json"] = "application/json",
            [".css"] = "text/css",
            [".wasm"] = "application/wasm",
            [".data"] = "application/octet-stream",
            [".unityweb"] = "application/octet-stream",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".ico"] = "image/x-icon",
            [".svg"] = "image/svg+xml",
            [".txt"] = "text/plain; charset=utf-8",
        };

        public StaticFileServer(string root, int port)
        {
            _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _port = port;
        }

        public void Start()
        {
            if (_running) return;
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _running = true;
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "ColourWays phone server" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* already stopped */ }
            _listener = null;
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { break; } // listener stopped
                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        private void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 30000;
                    string requestLine = ReadLine(stream);
                    if (string.IsNullOrEmpty(requestLine)) return;
                    // Drain the request headers (GET/HEAD have no body).
                    string header;
                    do header = ReadLine(stream);
                    while (!string.IsNullOrEmpty(header));

                    string[] parts = requestLine.Split(' ');
                    if (parts.Length < 2) { Send(stream, 400, "text/plain", Encoding.UTF8.GetBytes("Bad request"), null, false); return; }
                    string method = parts[0];
                    bool head = method == "HEAD";
                    if (method != "GET" && !head) { Send(stream, 405, "text/plain", Encoding.UTF8.GetBytes("Method not allowed"), null, false); return; }

                    string path = parts[1];
                    int q = path.IndexOfAny(new[] { '?', '#' });
                    if (q >= 0) path = path.Substring(0, q);
                    path = Uri.UnescapeDataString(path);
                    if (path.EndsWith("/")) path += "index.html";

                    string full = Path.GetFullPath(Path.Combine(_root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
                    if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                    {
                        Log?.Invoke($"404 {path}");
                        Send(stream, 404, "text/plain", Encoding.UTF8.GetBytes("Not found"), null, head);
                        return;
                    }

                    string encoding = null;
                    string typePath = full;
                    if (full.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) { encoding = "gzip"; typePath = full.Substring(0, full.Length - 3); }
                    else if (full.EndsWith(".br", StringComparison.OrdinalIgnoreCase)) { encoding = "br"; typePath = full.Substring(0, full.Length - 3); }
                    string type = Mime.TryGetValue(Path.GetExtension(typePath), out var t) ? t : "application/octet-stream";

                    Send(stream, 200, type, File.ReadAllBytes(full), encoding, head);
                }
            }
            catch (Exception e)
            {
                if (_running) Log?.Invoke("Request failed: " + e.Message);
            }
        }

        private static string ReadLine(NetworkStream stream)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) return sb.Length > 0 ? sb.ToString() : null;
                if (b == '\n') break;
                if (b != '\r') sb.Append((char)b);
                if (sb.Length > 8192) break;
            }
            return sb.ToString();
        }

        private static void Send(NetworkStream stream, int status, string type, byte[] body, string encoding, bool headOnly)
        {
            string reason = status == 200 ? "OK" : status == 404 ? "Not Found" : status == 405 ? "Method Not Allowed" : "Bad Request";
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            sb.Append("Content-Type: ").Append(type).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            if (encoding != null) sb.Append("Content-Encoding: ").Append(encoding).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("Connection: close\r\n\r\n");
            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (!headOnly) stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        /// <summary>This machine's IPv4 addresses that a phone on the same network could reach.</summary>
        public static List<string> LanAddresses()
        {
            var result = new List<string>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string s = addr.Address.ToString();
                    if (!s.StartsWith("169.254.")) result.Add(s);
                }
            }
            return result;
        }
    }
}
