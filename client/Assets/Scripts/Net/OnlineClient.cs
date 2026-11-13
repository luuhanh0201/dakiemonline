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
        private readonly List<NetEvent> events = new List<NetEvent>();
        private IReadOnlyList<RemoteProjectile> projectiles = Array.Empty<RemoteProjectile>();
        private INetTransport transport;
        private byte[] token = Array.Empty<byte>();
        private bool helloSent;
        private double nextPingSec;
        private double nextRetrySec;
        private double droppedAtSec;

        private readonly string loginToken;

        /// <param name="loginToken">From the account API; the server finds the account's character on it.</param>
        public OnlineClient(string url, GameConfig config, string loginToken)
        {
            Url = url;
            this.config = config;
            this.loginToken = loginToken ?? string.Empty;
            SelfAppearance = GearRules.Default(config);
            transport = NetTransport.Open(url);
        }

        public string Url { get; }

        /// <summary>Why the server turned this client away, when it did; the account screens act on it.</summary>
        public RejectReason? Rejected { get; private set; }

        /// <summary>The character's name, as the server sent it.</summary>
        public string CharacterName { get; private set; } = string.Empty;
        public OnlineStatus Status { get; private set; } = OnlineStatus.Connecting;
        public string Message { get; private set; } = "Đang kết nối...";
        public int PlayerId { get; private set; }
        public int ChannelId { get; private set; }
        public Predictor Predictor { get; private set; }
        public ServerClock ServerClock { get; } = new ServerClock();
        public IReadOnlyDictionary<int, RemoteTrack> Tracks => tracks;

        /// <summary>Players in the latest snapshot; tracks of anyone else are dropped.</summary>
        public IReadOnlyList<int> Present => present;

        /// <summary>The map this player is on, as its place in GameConfig.Maps; doors change it.</summary>
        public int Map { get; private set; }

        /// <summary>The server allows the refill button here.</summary>
        public bool TestRoom { get; private set; }

        /// <summary>The boss as of the latest snapshot.</summary>
        public BossState Boss { get; private set; }

        /// <summary>The boss's position over time, drawn slightly in the past like other players.</summary>
        public RemoteTrack BossTrack { get; private set; } = new RemoteTrack();

        /// <summary>Ticks until a beaten boss comes back; 0 while it is alive.</summary>
        public int BossRespawnTicks { get; private set; }

        /// <summary>Projectiles in flight from the latest snapshot, by owner.</summary>
        public IReadOnlyList<RemoteProjectile> Projectiles => projectiles;

        /// <summary>What the server last said this player wears; the default clothes until it says.</summary>
        public Appearance SelfAppearance { get; private set; }

        /// <summary>The bag as the server last sent it; null until it arrives after joining.</summary>
        public InventoryMessage Inventory { get; private set; }

        /// <summary>A snapshot has come since the last Welcome, so the world can be drawn.</summary>
        public bool HasSnapshot { get; private set; }

        /// <summary>The channel's weather, from the latest snapshot.</summary>
        public Weather Weather { get; private set; }

        /// <summary>The straw dummies of this player's map, from the latest snapshot; empty where there are none.</summary>
        public IReadOnlyList<RemoteDummy> Dummies { get; private set; } = Array.Empty<RemoteDummy>();

        /// <summary>Items lying on the ground in the channel, from the latest snapshot.</summary>
        public IReadOnlyList<GroundItem> Ground { get; private set; } = Array.Empty<GroundItem>();


        public LatencyStats Latency { get; } = new LatencyStats();
        public double LastRoundTripMs { get; private set; }
        public int Reconnects { get; private set; }

        /// <summary>Raised when the local player starts over from a Welcome, so views can stop blending.</summary>
        public event Action Welcomed;

        /// <summary>Raised when the server moves this player to another map, through a door.</summary>
        public event Action MapChanged;

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
                transport.Send(Packet.Encode(new HelloMessage(Packet.ProtocolVersion, token, loginToken)));
                helloSent = true;
            }

            for (byte[] packet = transport.Receive(); packet != null && Status != OnlineStatus.Failed; packet = transport.Receive())
            {
                Handle(packet);
            }

            // The server closes right after a refusal; its reason must not be replaced by "connection lost".
            if (Status == OnlineStatus.Failed)
            {
                return;
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
        public void Tick(double moveX, double moveY, bool dash, in CombatIntent combat, bool refill, bool door = false)
        {
            if (Status != OnlineStatus.Playing)
            {
                return;
            }

            Predictor.Aim = DummyAim();
            InputMessage input = Predictor.Advance(moveX, moveY, dash, combat.SkillPressed, combat.PotionPressed, refill, door);
            transport.Send(Packet.Encode(input));
        }

        /// <summary>
        /// One thing done with the bag. Nothing changes here until the server answers with the whole
        /// bag, and the clothes show in the next snapshot, so the player sees what everyone else sees.
        /// </summary>
        public void ItemAction(ItemActionKind kind, long target, int to = 0)
        {
            if (Status != OnlineStatus.Playing)
            {
                return;
            }

            transport.Send(Packet.Encode(new ItemActionMessage(kind, target, to)));
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
                case InventoryMessage inventory:
                    Inventory = inventory;
                    break;
                case PongMessage pong:
                    LastRoundTripMs = (uint)clock.ElapsedMilliseconds - pong.ClientTimeMs;
                    Latency.Add(LastRoundTripMs);
                    RoundTrip?.Invoke(LastRoundTripMs);
                    break;
                case RejectMessage reject:
                    Rejected = reject.Reason;
                    Fail(reject.Reason switch
                    {
                        RejectReason.VersionMismatch => "Server khác phiên bản, cần cập nhật bản chơi",
                        RejectReason.ServerFull => "Server đã đầy",
                        RejectReason.NotLoggedIn => "Phiên đăng nhập đã hết. Hãy đăng nhập lại.",
                        RejectReason.NoCharacter => "Tài khoản chưa có nhân vật trên máy chủ này.",
                        RejectReason.LoggedInElsewhere => "Nhân vật vừa được đăng nhập ở máy khác.",
                        RejectReason.Unavailable => "Máy chủ chưa đọc được dữ liệu nhân vật. Hãy thử lại sau ít phút.",
                        _ => "Server từ chối gói tin",
                    });
                    break;
            }
        }

        private void OnWelcome(WelcomeMessage welcome)
        {
            if (!KnownMap(welcome.Map))
            {
                return;
            }

            bool resumedHere = welcome.Resumed && welcome.PlayerId == PlayerId;
            token = welcome.Token;
            CharacterName = welcome.CharacterName;
            PlayerId = welcome.PlayerId;
            ChannelId = welcome.ChannelId;
            TestRoom = welcome.TestRoom;
            Map = welcome.Map;
            Predictor = new Predictor(config, welcome.Self, welcome.LastInputSequence, welcome.TestRoom, welcome.Map) { Boss = BossHere };
            BossTrack = new RemoteTrack();
            Ground = Array.Empty<GroundItem>();
            events.Clear();
            tracks.Clear();
            present.Clear();
            ServerClock.Observe(welcome.ServerTick);
            Status = OnlineStatus.Playing;
            HasSnapshot = false;
            nextPingSec = Now;
            Message = resumedHere ? "Đã nối lại, giữ nguyên vị trí" : string.Empty;
            Welcomed?.Invoke();
        }

        private void OnSnapshot(SnapshotMessage snapshot)
        {
            if (!KnownMap(snapshot.Map))
            {
                return;
            }

            bool moved = snapshot.Map != Map;
            HasSnapshot = true;
            Map = snapshot.Map;
            Predictor.Map = snapshot.Map;
            ServerClock.Observe(snapshot.ServerTick);
            UpdateBoss(snapshot);
            Predictor.Reconcile(snapshot.AckSequence, snapshot.Self);
            projectiles = snapshot.Projectiles;
            Ground = snapshot.Ground;
            Weather = snapshot.Weather;
            Dummies = snapshot.Dummies;

            if (GearRules.IsValid(config, snapshot.SelfAppearance))
            {
                SelfAppearance = snapshot.SelfAppearance;
            }
            events.AddRange(snapshot.Events);

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

            if (moved)
            {
                MapChanged?.Invoke();
            }
        }

        /// <summary>
        /// The box of the dummy nearest to x, standing or not, for drawing where a blow landed: the blow that
        /// knocks one down arrives in the same snapshot that shows it fallen. Null on a map without dummies.
        /// </summary>
        public CombatBox? DummyNear(double x)
        {
            TrainingConfig training = config.Maps[Map].Training;

            if (training == null || training.Dummies.Count == 0)
            {
                return null;
            }

            MapDummy nearest = training.Dummies[0];

            foreach (MapDummy dummy in training.Dummies)
            {
                if (Math.Abs(dummy.X - x) < Math.Abs(nearest.X - x))
                {
                    nearest = dummy;
                }
            }

            return TrainingRules.Box(training, nearest.X);
        }

        /// <summary>The nearest standing dummy, which blows aim at on the server too; null where there is none.</summary>
        private CombatTarget? DummyAim()
        {
            TrainingConfig training = config.Maps[Map].Training;

            if (training == null || Dummies.Count != training.Dummies.Count)
            {
                return null;
            }

            var yard = new List<DummyState>(Dummies.Count);

            for (int index = 0; index < Dummies.Count; index++)
            {
                yard.Add(new DummyState(training.Dummies[index].X));
            }

            int nearest = -1;
            double x = Predictor.Movement.X;

            for (int index = 0; index < yard.Count; index++)
            {
                if (!Dummies[index].Fallen && (nearest < 0 || Math.Abs(yard[index].X - x) < Math.Abs(yard[nearest].X - x)))
                {
                    nearest = index;
                }
            }

            return nearest < 0 ? (CombatTarget?)null : new CombatTarget(TrainingRules.Box(training, yard[nearest].X), 0m);
        }

        /// <summary>Whether the boss is on this player's map, so they can see and aim at it.</summary>
        public bool BossOnMap => config.Maps[Map].Boss;

        /// <summary>The boss for prediction to aim at; none on a map without it.</summary>
        private BossState BossHere => BossOnMap ? Boss : null;

        /// <summary>A map number this client has no file for means it is out of date with the server.</summary>
        private bool KnownMap(int map)
        {
            if (map < config.Maps.Count)
            {
                return true;
            }

            Fail($"Server gửi map số {map}, bản chơi này chưa có. Cần cập nhật bản chơi.");
            return false;
        }

        /// <summary>Hands over the room's events since the last call, so each is shown once.</summary>
        public List<NetEvent> DrainEvents(List<NetEvent> into)
        {
            into.Clear();
            into.AddRange(events);
            events.Clear();
            return into;
        }

        private void UpdateBoss(SnapshotMessage snapshot)
        {
            if (Boss == null)
            {
                Boss = BossState.FromSnapshot(config.Boss, snapshot.Boss);
            }
            else
            {
                Boss.Restore(snapshot.Boss);
            }

            Predictor.Boss = BossHere;
            BossRespawnTicks = snapshot.BossRespawnTicks;
            BossTrack.Add(
                snapshot.ServerTick,
                new RemotePlayer(0, (float)snapshot.Boss.X, 0f, (sbyte)snapshot.Boss.Facing, RemoteFlags.Grounded));
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
