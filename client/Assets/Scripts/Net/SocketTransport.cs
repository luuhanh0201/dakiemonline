#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Game.Net;

namespace DaKiem.Client.Net
{
    /// <summary>ClientWebSocket on background tasks; the main thread only touches the queues.</summary>
    public sealed class SocketTransport : INetTransport
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);

        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();
        private readonly BlockingCollection<byte[]> outbox = new BlockingCollection<byte[]>();
        private volatile TransportState state = TransportState.Connecting;
        private volatile string closeReason = string.Empty;

        public SocketTransport(string url)
        {
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            Task.Run(() => RunAsync(new Uri(url)));
        }

        public TransportState State => state;
        public string CloseReason => closeReason;

        public void Send(byte[] packet)
        {
            if (state == TransportState.Open && !outbox.IsAddingCompleted)
            {
                outbox.Add(packet);
            }
        }

        public byte[] Receive()
        {
            return inbox.TryDequeue(out byte[] packet) ? packet : null;
        }

        public void Close()
        {
            outbox.CompleteAdding();
        }

        public void Dispose()
        {
            Close();
            stop.Cancel();
            socket.Abort();
        }

        private async Task RunAsync(Uri url)
        {
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    timeout.CancelAfter(ConnectTimeout);
                    await socket.ConnectAsync(url, timeout.Token).ConfigureAwait(false);
                }

                state = TransportState.Open;
                Task sending = Task.Run(SendLoopAsync);
                await ReceiveLoopAsync().ConfigureAwait(false);
                outbox.CompleteAdding();
                await sending.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Fail(stop.IsCancellationRequested ? "đã đóng" : "hết thời gian kết nối");
            }
            catch (Exception error)
            {
                Fail(error.InnerException?.Message ?? error.Message);
            }
            finally
            {
                Fail(string.Empty);
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[Packet.MaxSize];

            while (socket.State == WebSocketState.Open)
            {
                int length = 0;
                WebSocketReceiveResult result;

                do
                {
                    if (length == buffer.Length)
                    {
                        throw new ProtocolException("gói quá lớn");
                    }

                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length), stop.Token).ConfigureAwait(false);
                    length += result.Count;
                }
                while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Fail(result.CloseStatusDescription ?? "server đóng kết nối");

                    if (socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);
                    }

                    return;
                }

                var packet = new byte[length];
                Buffer.BlockCopy(buffer, 0, packet, 0, length);
                inbox.Enqueue(packet);
            }
        }

        private async Task SendLoopAsync()
        {
            try
            {
                foreach (byte[] packet in outbox.GetConsumingEnumerable(stop.Token))
                {
                    await socket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, stop.Token).ConfigureAwait(false);
                }

                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                socket.Abort();
            }
        }

        private void Fail(string reason)
        {
            if (state != TransportState.Closed)
            {
                closeReason = reason.Length > 0 ? reason : "mất kết nối";
                state = TransportState.Closed;
            }
        }
    }
}
#endif
