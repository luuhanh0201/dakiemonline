using System;

namespace DaKiem.Client.Net
{
    public enum TransportState
    {
        Connecting,
        Open,
        Closed,
    }

    /// <summary>
    /// A WebSocket that is polled once a frame from the main thread. Android and PC use .NET's
    /// ClientWebSocket; the web build uses the browser's WebSocket through a .jslib.
    /// </summary>
    public interface INetTransport : IDisposable
    {
        TransportState State { get; }

        /// <summary>Why the connection closed, for the screen. Empty while open.</summary>
        string CloseReason { get; }

        void Send(byte[] packet);

        /// <summary>The next message received, or null when none is waiting.</summary>
        byte[] Receive();

        void Close();
    }

    public static class NetTransport
    {
        public static INetTransport Open(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new BrowserTransport(url);
#else
            return new SocketTransport(url);
#endif
        }

        /// <summary>Accepts "host:port", "ws://host:port/ws" or "https://name.example" and returns a ws:// or wss:// address ending in /ws.</summary>
        public static string NormalizeUrl(string address)
        {
            string text = (address ?? string.Empty).Trim();

            if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                text = "wss://" + text.Substring("https://".Length);
            }
            else if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                text = "ws://" + text.Substring("http://".Length);
            }
            else if (!text.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                text = "ws://" + text;
            }

            var uri = new Uri(text);
            return uri.AbsolutePath == "/" ? new Uri(uri, "/ws").ToString() : uri.ToString();
        }
    }
}
