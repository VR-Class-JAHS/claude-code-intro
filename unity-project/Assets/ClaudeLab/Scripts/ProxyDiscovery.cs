using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// Finds vr-lab-proxy on the Wi-Fi so the headset build doesn't need the computer's IP typed in.
    /// The proxy broadcasts a signed announcement every 2 seconds (see vr-lab-proxy/discovery.py).
    /// This listens for it, checks the signature with the proxy token, and gives ClaudeAgent the URL.
    /// Until an announcement arrives, or if the network blocks broadcasts, ClaudeAgent uses proxyUrl
    /// from the settings asset.
    /// </summary>
    public class ProxyDiscovery : MonoBehaviour
    {
        public const int DiscoveryPort = 47800;

        ClaudeProxySettings settings; // loaded at runtime, never linked in the scene

        [Tooltip("Listen in the Editor too. Off by default: Windows asks to allow Unity through the firewall, " +
                 "and in the Editor the settings' proxyUrl already works.")]
        public bool listenInEditor = false;

        [Tooltip("Ignore announcements whose timestamp is further than this from the headset's clock (seconds).")]
        public int maxClockDifferenceSeconds = 300;

        /// <summary>e.g. "http://192.168.1.161:8000", or null if nothing found yet. Safe to read from any thread.</summary>
        public string DiscoveredUrl => Volatile.Read(ref _discoveredUrl);

        string _discoveredUrl;
        string _loggedUrl;
        string _token;
        Thread _thread;
        UdpClient _udp;
        volatile bool _stopping;

        void Awake()
        {
            var agent = GetComponent<ClaudeAgent>();
            settings = agent != null ? agent.Settings : ClaudeProxySettings.Load();
        }

        void Start()
        {
            if (Application.isEditor && !listenInEditor) return;
            if (settings == null || string.IsNullOrEmpty(settings.proxyToken))
            {
                Debug.LogWarning("[ProxyDiscovery] No proxy token, so announcements can't be checked. Using proxyUrl from settings.", this);
                return;
            }

            _token = settings.proxyToken; // read on the main thread; the listener thread uses this copy
            try
            {
                _udp = new UdpClient();
                _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _udp.Client.ReceiveTimeout = 1000; // so the thread notices when we stop
            }
            catch (SocketException e)
            {
                Debug.LogWarning($"[ProxyDiscovery] Can't listen on UDP {DiscoveryPort} ({e.Message}). Using proxyUrl from settings.", this);
                _udp?.Close();
                _udp = null;
                return;
            }

            _thread = new Thread(Listen) { IsBackground = true, Name = "ProxyDiscovery" };
            _thread.Start();
        }

        void Update()
        {
            // Log on the main thread whenever the proxy is found or moves to a new address.
            string url = DiscoveredUrl;
            if (url != null && url != _loggedUrl)
            {
                _loggedUrl = url;
                Debug.Log($"[ProxyDiscovery] Found the lab computer at {url}.", this);
            }
        }

        void Listen()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (!_stopping)
            {
                byte[] data;
                try { data = _udp.Receive(ref from); }
                catch (SocketException) { continue; } // timeout
                catch (ObjectDisposedException) { return; }

                string url = Verify(data, from.Address);
                if (url != null) Volatile.Write(ref _discoveredUrl, url);
            }
        }

        /// <summary>Returns the proxy URL if the announcement is genuine, otherwise null.</summary>
        string Verify(byte[] data, IPAddress sender)
        {
            try
            {
                var msg = JObject.Parse(Encoding.UTF8.GetString(data));
                if ((string)msg["service"] != "vr-lab-proxy") return null;

                string ip = (string)msg["ip"];
                int port = (int)msg["port"];
                long ts = (long)msg["ts"];
                string sig = (string)msg["sig"];

                // The announced address must be the one the packet came from.
                if (ip != sender.ToString()) return null;

                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (Math.Abs(now - ts) > maxClockDifferenceSeconds) return null;

                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_token));
                byte[] expected = hmac.ComputeHash(Encoding.UTF8.GetBytes($"vr-lab-proxy|{ip}|{port}|{ts}"));
                if (!FixedTimeEquals(expected, FromHex(sig))) return null;

                return $"http://{ip}:{port}";
            }
            catch (Exception)
            {
                return null; // not one of ours, or malformed
            }
        }

        static byte[] FromHex(string hex)
        {
            if (hex == null || hex.Length % 2 != 0) return Array.Empty<byte>();
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        void OnDestroy()
        {
            _stopping = true;
            _udp?.Close();
            _udp = null;
        }
    }
}
