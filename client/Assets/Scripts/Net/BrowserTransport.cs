#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;

namespace DaKiem.Client.Net
{
    /// <summary>The browser's WebSocket, through Plugins/WebGL/DaKiemWebSocket.jslib.</summary>
    public sealed class BrowserTransport : INetTransport
    {
        private readonly int id;
        private bool closed;

        public BrowserTransport(string url)
        {
            id = DaKiemWsOpen(url);
        }

        public TransportState State
        {
            get
            {
                if (closed)
                {
                    return TransportState.Closed;
                }

                switch (DaKiemWsState(id))
                {
                    case 0: return TransportState.Connecting;
                    case 1: return TransportState.Open;
                    default: return TransportState.Closed;
                }
            }
        }

        public string CloseReason
        {
            get { return closed ? "đã đóng" : DaKiemWsReason(id) ?? string.Empty; }
        }

        public void Send(byte[] packet)
        {
            if (!closed)
            {
                DaKiemWsSend(id, packet, packet.Length);
            }
        }

        public byte[] Receive()
        {
            if (closed)
            {
                return null;
            }

            int length = DaKiemWsPeek(id);

            if (length == 0)
            {
                return null;
            }

            var packet = new byte[length];
            DaKiemWsTake(id, packet, length);
            return packet;
        }

        public void Close()
        {
            if (!closed)
            {
                DaKiemWsClose(id);
                closed = true;
            }
        }

        public void Dispose() => Close();

        [DllImport("__Internal")]
        private static extern int DaKiemWsOpen(string url);

        [DllImport("__Internal")]
        private static extern int DaKiemWsState(int id);

        [DllImport("__Internal")]
        private static extern string DaKiemWsReason(int id);

        [DllImport("__Internal")]
        private static extern void DaKiemWsSend(int id, byte[] data, int length);

        [DllImport("__Internal")]
        private static extern int DaKiemWsPeek(int id);

        [DllImport("__Internal")]
        private static extern int DaKiemWsTake(int id, byte[] buffer, int length);

        [DllImport("__Internal")]
        private static extern void DaKiemWsClose(int id);
    }
}
#endif
