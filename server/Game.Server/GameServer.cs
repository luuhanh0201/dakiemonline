using System.Security.Cryptography;
using Game.Net;
using Game.Rules;

namespace Game.Server;

/// <summary>All channels of one map in one process. Sends each new connection to a channel and back again on reconnect.</summary>
public sealed class GameServer
{
    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessionsByToken = new();
    private readonly Dictionary<int, Session> sessionsById = new();
    private readonly Dictionary<long, Session> sessionsByCharacter = new();
    private readonly Dictionary<int, int> occupancy = new();
    private int nextPlayerId;

    /// <param name="requireLogin">
    /// True on the real server: a new player must come with a character. Server tests leave it off
    /// and join players without accounts.
    /// </param>
    /// <param name="items">Where bags are kept; memory when null, as in server tests.</param>
    public GameServer(GameConfig config, ServerConfig server, bool requireLogin = false, Db.IItemStore? items = null, ILogger? logger = null)
    {
        Config = config;
        Server = server;
        RequireLogin = requireLogin;
        Inventory = new InventoryService(config, items ?? new Db.InMemoryItemStore(), logger);
        Runners = Enumerable.Range(1, server.Channels)
            .Select(id => new ChannelRunner(id, config, server))
            .ToList();

        foreach (ChannelRunner runner in Runners)
        {
            occupancy[runner.Id] = 0;
            runner.PlayerRemoved += playerId => Forget(runner, playerId);
            runner.PlayerLeaving += (session, spot) => _ = Inventory.LeftAsync(session, spot);
        }
    }

    public GameConfig Config { get; }
    public ServerConfig Server { get; }
    public bool RequireLogin { get; }
    public IReadOnlyList<ChannelRunner> Runners { get; }
    public InventoryService Inventory { get; }

    /// <summary>When the server stops, keeps where everyone in a channel stood, as leaving would.</summary>
    public async Task SavePositionsAsync()
    {
        List<(Session Session, MapSpot Spot)> playing;

        lock (gate)
        {
            playing = sessionsById.Values
                .Where(session => session.Identity != null && session.Runner.Positions.ContainsKey(session.PlayerId))
                .Select(session => (session, session.Runner.Positions[session.PlayerId]))
                .ToList();
        }

        await Task.WhenAll(playing.Select(entry => Inventory.LeftAsync(entry.Session, entry.Spot)));
    }

    public int SessionCount
    {
        get
        {
            lock (gate)
            {
                return sessionsById.Count;
            }
        }
    }

    public IReadOnlyList<Thread> Start(CancellationToken stop) => Runners.Select(runner => runner.Start(stop)).ToList();

    /// <summary>True when Hello with this resume token would bring a player back, so no login is needed.</summary>
    public bool CanResume(byte[] resumeToken)
    {
        lock (gate)
        {
            return resumeToken.Length > 0 && sessionsByToken.ContainsKey(Convert.ToHexString(resumeToken));
        }
    }

    /// <summary>
    /// Handles Hello and returns the session the connection now speaks for, or null if refused. A
    /// character already in a channel is taken over by the new connection, and the old one is told so.
    /// </summary>
    /// <param name="spawn">Where a saved character last stood; used only when it comes into a channel anew.</param>
    public Session? Hello(HelloMessage hello, IConnection connection, PlayerIdentity? identity = null, MapSpot? spawn = null)
    {
        if (hello.ProtocolVersion != Packet.ProtocolVersion)
        {
            Refuse(connection, RejectReason.VersionMismatch);
            return null;
        }

        lock (gate)
        {
            if (hello.ResumeToken.Length > 0
                && sessionsByToken.TryGetValue(Convert.ToHexString(hello.ResumeToken), out Session? existing))
            {
                existing.Runner.Resume(existing, connection);
                return existing;
            }

            if (identity == null && RequireLogin)
            {
                Refuse(connection, RejectReason.NotLoggedIn);
                return null;
            }

            if (identity != null && sessionsByCharacter.TryGetValue(identity.CharacterId, out Session? playing))
            {
                playing.Runner.Resume(playing, connection);
                return playing;
            }

            ChannelRunner? runner = Runners
                .Where(candidate => occupancy[candidate.Id] < Server.PlayersPerChannel)
                .OrderBy(candidate => candidate.Id)
                .FirstOrDefault();

            if (runner == null)
            {
                Refuse(connection, RejectReason.ServerFull);
                return null;
            }

            var session = new Session(++nextPlayerId, RandomNumberGenerator.GetBytes(Packet.TokenSize), runner, identity);
            sessionsByToken[Convert.ToHexString(session.Token)] = session;
            sessionsById[session.PlayerId] = session;

            if (identity != null)
            {
                sessionsByCharacter[identity.CharacterId] = session;
            }

            occupancy[runner.Id]++;
            runner.Join(session, connection, spawn);
            return session;
        }
    }

    /// <summary>Answers a Ping at once, without waiting for a tick, so it measures only the network.</summary>
    public static void Ping(PingMessage ping, Session session, IConnection connection)
    {
        connection.Send(Packet.Encode(new PongMessage(ping.ClientTimeMs, session.Runner.CurrentTick)));
    }

    public static void Refuse(IConnection connection, RejectReason reason)
    {
        connection.Send(Packet.Encode(new RejectMessage(reason)));
        connection.Close();
    }

    public IReadOnlyDictionary<int, int> Occupancy()
    {
        lock (gate)
        {
            return new Dictionary<int, int>(occupancy);
        }
    }

    private void Forget(ChannelRunner runner, int playerId)
    {
        lock (gate)
        {
            if (sessionsById.Remove(playerId, out Session? session))
            {
                sessionsByToken.Remove(Convert.ToHexString(session.Token));
                occupancy[runner.Id]--;

                if (session.Identity != null)
                {
                    sessionsByCharacter.Remove(session.Identity.CharacterId);
                }
            }
        }
    }
}
