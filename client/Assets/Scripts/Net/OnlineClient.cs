using System;
using System.Collections.Generic;
using System.Diagnostics;
using Game.Net;
using Game.Rules;

namespace DaKiem.Client.Net
{
    public enum OnlineStatus
    {
        Connecting,
        Playing,
        Reconnecting,
        Failed,
    }

    /// <summary>
    /// One stay on the server: joins, sends an input every tick, keeps the local player predicted,
    /// keeps a track of everyone else and reconnects with the token when the connection drops.
    /// </summary>
    public sealed class OnlineClient : IDisposable
    {
        private const double PingEverySec = 1d;
        private const double RetryEverySec = 1d;

        /// <summary>A little longer than the server holds the place, so the last try can still resume.</summary>
        private const double GiveUpAfterSec = 35d;

        private readonly GameConfig config;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Dictionary<int, RemoteTrack> tracks = new Dictionary<int, RemoteTrack>();
        private readonly List<int> present = new List<int>();
        private INetTransport transport;
        private byte[] token = Array.Empty<byte>();
        private bool helloSent;
        private double nextPingSec;
        private double nextRetrySec;
        private double droppedAtSec;

        public OnlineClient(string url, GameConfig config)
        {
            Url = url;
            this.config = config;
            transport = NetTransport.Open(url);
        }

        public string Url { get; }
        public OnlineStatus Status { get; private set; } = OnlineStatus.Connecting;
        public string Message { get; private set; } = "Đang kết nối...";
        public int PlayerId { get; private set; }
        public int ChannelId { get; private set; }
        public Predictor Predictor { get; private set; }
        public ServerClock ServerClock { get; } = new ServerClock();
        public IReadOnlyDictionary<int, RemoteTrack> Tracks => tracks;

        /// <summary>Players in the latest snapshot; tracks of anyone else are dropped.</summary>
        public IReadOnlyList<int> Present => present;

        public LatencyStats Latency { get; } = new LatencyStats();
        public double LastRoundTripMs { get; private set; }
        public int Reconnects { get; private set; }

        /// <summary>Raised when the local player starts over from a Welcome, so views can stop blending.</summary>
        public event Action Welcomed;

        /// <summary>Raised for each round trip measured, in milliseconds.</summary>
        public event Action<double> RoundTrip;

        private double Now => clock.Elapsed.TotalSeconds;

        /// <summary>Polls the connection; call once a frame.</summary>
        public void Update(double frameSeconds)
        {
            ServerClock.Advance(frameSeconds * Ticks.PerSecond);

            if (Status == OnlineStatus.Failed)
            {
                return;
            }

            if (transport.State == TransportState.Open && !helloSent)
            {
                transport.Send(Packet.Encode(new HelloMessage(Packet.ProtocolVersion, token)));
                helloSent = true;
            }

            for (byte[] packet = transport.Receive(); packet != null; packet = transport.Receive())
            {
                Handle(packet);
            }

            if (transport.State == TransportState.Closed)
            {
                Dropped(transport.CloseReason);
            }
            else if (Status == OnlineStatus.Playing && Now >= nextPingSec)
            {
                nextPingSec = Now + PingEverySec;
                transport.Send(Packet.Encode(new PingMessage((uint)clock.ElapsedMilliseconds)));
            }
        }

        /// <summary>One game tick of input: predicted at once and sent to the server.</summary>
        public void Tick(double moveX, double moveY, bool dash)
        {
            if (Status != OnlineStatus.Playing)
            {
                return;
            }

            InputMessage input = Predictor.Advance(moveX, moveY, dash);
            transport.Send(Packet.Encode(input));
        }

        /// <summary>Leaves on purpose: the server frees the place at once.</summary>
        public void Quit()
        {
            if (transport.State == TransportState.Open)
            {
                transport.Send(Packet.Encode(new LeaveMessage()));
            }

            transport.Close();
            Status = OnlineStatus.Failed;
            Message = "Đã thoát";
        }

        /// <summary>Cuts the connection without saying goodbye, to try reconnecting on PC.</summary>
        public void SimulateDrop()
        {
            transport.Dispose();
        }

        public void Dispose()
        {
            transport.Dispose();
        }

        private void Handle(byte[] packet)
        {
            NetMessage message;

            try
            {
                message = Packet.Decode(packet);
            }
            catch (ProtocolException error)
            {
                Fail($"Gói từ server bị lỗi: {error.Message}");
                return;
            }

            switch (message)
            {
                case WelcomeMessage welcome:
                    OnWelcome(welcome);
                    break;
                case SnapshotMessage snapshot when Predictor != null:
                    OnSnapshot(snapshot);
                    break;
                case PongMessage pong:
                    LastRoundTripMs = (uint)clock.ElapsedMilliseconds - pong.ClientTimeMs;
                    Latency.Add(LastRoundTripMs);
                    RoundTrip?.Invoke(LastRoundTripMs);
                    break;
                case RejectMessage reject:
                    Fail(reject.Reason switch
                    {
                        RejectReason.VersionMismatch => "Server khác phiên bản, cần cập nhật bản chơi",
                        RejectReason.ServerFull => "Server đã đầy",
                        _ => "Server từ chối gói tin",
                    });
                    break;
            }
        }

        private void OnWelcome(WelcomeMessage welcome)
        {
            bool resumedHere = welcome.Resumed && welcome.PlayerId == PlayerId;
            token = welcome.Token;
            PlayerId = welcome.PlayerId;
            ChannelId = welcome.ChannelId;
            Predictor = new Predictor(config, welcome.Self, welcome.LastInputSequence);
            tracks.Clear();
            present.Clear();
            ServerClock.Observe(welcome.ServerTick);
            Status = OnlineStatus.Playing;
            nextPingSec = Now;
            Message = resumedHere ? "Đã nối lại, giữ nguyên vị trí" : string.Empty;
            Welcomed?.Invoke();
        }

        private void OnSnapshot(SnapshotMessage snapshot)
        {
            ServerClock.Observe(snapshot.ServerTick);
            Predictor.Reconcile(snapshot.AckSequence, snapshot.Self);

            present.Clear();

            foreach (RemotePlayer other in snapshot.Others)
            {
                if (!tracks.TryGetValue(other.Id, out RemoteTrack track))
                {
                    track = new RemoteTrack();
                    tracks.Add(other.Id, track);
                }

                track.Add(snapshot.ServerTick, other);
                present.Add(other.Id);
            }

            if (tracks.Count != present.Count)
            {
                var gone = new List<int>();

                foreach (int id in tracks.Keys)
                {
                    if (!present.Contains(id))
                    {
                        gone.Add(id);
                    }
                }

                foreach (int id in gone)
                {
                    tracks.Remove(id);
                }
            }
        }

        private void Dropped(string reason)
        {
            if (Status == OnlineStatus.Playing)
            {
                Status = OnlineStatus.Reconnecting;
                droppedAtSec = Now;
                nextRetrySec = Now;
            }

            if (Status == OnlineStatus.Connecting)
            {
                Fail($"Không kết nối được tới {Url}: {reason}");
                return;
            }

            if (Now - droppedAtSec > GiveUpAfterSec)
            {
                Fail($"Mất kết nối: {reason}");
                return;
            }

            Message = $"Mất kết nối ({reason}), đang nối lại... {GiveUpAfterSec - (Now - droppedAtSec):0} giây";

            if (Now >= nextRetrySec)
            {
                nextRetrySec = Now + RetryEverySec;
                transport.Dispose();
                transport = NetTransport.Open(Url);
                helloSent = false;
                Reconnects++;
            }
        }

        private void Fail(string message)
        {
            Status = OnlineStatus.Failed;
            Message = message;
            transport.Close();
        }
    }
}
