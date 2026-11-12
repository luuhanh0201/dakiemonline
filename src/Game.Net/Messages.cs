using System;
using System.Collections.Generic;
using Game.Rules;

namespace Game.Net
{
    public enum MessageType : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        Input = 4,
        Snapshot = 5,
        Ping = 6,
        Pong = 7,
        Leave = 8,
        Wear = 9,
    }

    public enum RejectReason : byte
    {
        VersionMismatch = 1,
        ServerFull = 2,
        BadMessage = 3,

        /// <summary>No login token, or one that expired: back to the login screen.</summary>
        NotLoggedIn = 4,

        /// <summary>The account has no character on this server yet: to the create-character screen.</summary>
        NoCharacter = 5,

        /// <summary>The same character came in from another device, which now has it.</summary>
        LoggedInElsewhere = 6,
    }

    public abstract class NetMessage
    {
        public abstract MessageType Type { get; }

        internal abstract void Write(PacketWriter writer);
    }

    /// <summary>
    /// Client → server, first message. A resume token the server still holds brings the player back
    /// as they were; otherwise the login token from the account API says whose character comes in.
    /// </summary>
    public sealed class HelloMessage : NetMessage
    {
        public HelloMessage(ushort protocolVersion, byte[] resumeToken, string loginToken = "")
        {
            ProtocolVersion = protocolVersion;
            ResumeToken = resumeToken;
            LoginToken = loginToken;
        }

        public override MessageType Type => MessageType.Hello;
        public ushort ProtocolVersion { get; }
        public byte[] ResumeToken { get; }
        public string LoginToken { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.UInt16(ProtocolVersion);
            writer.Bytes(ResumeToken);
            writer.Text(LoginToken);
        }

        internal static HelloMessage Read(ref PacketReader reader)
        {
            ushort version = reader.UInt16();

            // An older client stops here; reading on would only turn a version mismatch into a bad message.
            if (version != Packet.ProtocolVersion)
            {
                reader.SkipRest();
                return new HelloMessage(version, Array.Empty<byte>());
            }

            return new HelloMessage(version, reader.Bytes(), reader.Text());
        }
    }

    /// <summary>Server → client after Hello. Resumed means the player came back as they were.</summary>
    public sealed class WelcomeMessage : NetMessage
    {
        public WelcomeMessage(
            int playerId,
            int channelId,
            uint serverTick,
            bool resumed,
            byte[] token,
            uint lastInputSequence,
            bool testRoom,
            PlayerSnapshot self,
            string characterName = "")
        {
            CharacterName = characterName;
            PlayerId = playerId;
            ChannelId = channelId;
            ServerTick = serverTick;
            Resumed = resumed;
            Token = token;
            LastInputSequence = lastInputSequence;
            TestRoom = testRoom;
            Self = self;
        }

        public override MessageType Type => MessageType.Welcome;
        public int PlayerId { get; }
        public int ChannelId { get; }
        public uint ServerTick { get; }
        public bool Resumed { get; }
        public byte[] Token { get; }

        /// <summary>The last input the server used, so a resumed client numbers on from it.</summary>
        public uint LastInputSequence { get; }

        /// <summary>The room allows the refill button; the client shows it only then.</summary>
        public bool TestRoom { get; }

        public PlayerSnapshot Self { get; }

        /// <summary>The name the player gave their character, for the HUD.</summary>
        public string CharacterName { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.Int32(PlayerId);
            writer.Byte(checked((byte)ChannelId));
            writer.UInt32(ServerTick);
            writer.Bool(Resumed);
            writer.Bytes(Token);
            writer.UInt32(LastInputSequence);
            writer.Bool(TestRoom);
            SnapshotCodec.Write(writer, Self);
            writer.Text(CharacterName);
        }

        internal static WelcomeMessage Read(ref PacketReader reader)
        {
            return new WelcomeMessage(
                reader.Int32(),
                reader.Byte(),
                reader.UInt32(),
                reader.Bool(),
                reader.Bytes(),
                reader.UInt32(),
                reader.Bool(),
                SnapshotCodec.ReadPlayer(ref reader),
                reader.Text());
        }
    }

    public sealed class RejectMessage : NetMessage
    {
        public RejectMessage(RejectReason reason)
        {
            Reason = reason;
        }

        public override MessageType Type => MessageType.Reject;
        public RejectReason Reason { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.Byte((byte)Reason);
        }

        internal static RejectMessage Read(ref PacketReader reader)
        {
            byte reason = reader.Byte();

            if (!Enum.IsDefined(typeof(RejectReason), reason))
            {
                throw new ProtocolException($"unknown reject reason {reason}");
            }

            return new RejectMessage((RejectReason)reason);
        }
    }

    /// <summary>
    /// Client → server, one per client tick. Joystick values travel as whole steps of 1/127, and the
    /// client predicts with <see cref="ToIntent"/> so it moves exactly as the server will.
    /// </summary>
    public sealed class InputMessage : NetMessage
    {
        private const double Steps = 127d;
        private const byte DashFlag = 1;
        private const byte PotionFlag = 2;
        private const byte RefillFlag = 4;

        public InputMessage(uint sequence, sbyte moveX, sbyte moveY, bool dash, SkillSlot? skill = null, bool potion = false, bool refill = false)
        {
            if (moveX == sbyte.MinValue || moveY == sbyte.MinValue)
            {
                throw new ProtocolException("joystick steps run from -127 to 127");
            }

            Sequence = sequence;
            MoveX = moveX;
            MoveY = moveY;
            Dash = dash;
            Skill = skill;
            Potion = potion;
            Refill = refill;
        }

        public override MessageType Type => MessageType.Input;
        public uint Sequence { get; }
        public sbyte MoveX { get; }
        public sbyte MoveY { get; }
        public bool Dash { get; }
        public SkillSlot? Skill { get; }
        public bool Potion { get; }

        /// <summary>The test room's refill button.</summary>
        public bool Refill { get; }

        public static InputMessage FromJoystick(
            uint sequence,
            double moveX,
            double moveY,
            bool dash,
            SkillSlot? skill = null,
            bool potion = false,
            bool refill = false)
        {
            return new InputMessage(sequence, Quantize(moveX), Quantize(moveY), dash, skill, potion, refill);
        }

        public RoomIntent ToIntent()
        {
            return new RoomIntent(
                new MovementIntent(MoveX / Steps, MoveY / Steps, Dash),
                new CombatIntent(Skill, Potion),
                Refill);
        }

        internal override void Write(PacketWriter writer)
        {
            writer.UInt32(Sequence);
            writer.SByte(MoveX);
            writer.SByte(MoveY);
            writer.Byte((byte)((Dash ? DashFlag : 0) | (Potion ? PotionFlag : 0) | (Refill ? RefillFlag : 0)));
            SnapshotCodec.WriteSlot(writer, Skill);
        }

        internal static InputMessage Read(ref PacketReader reader)
        {
            uint sequence = reader.UInt32();
            sbyte moveX = reader.SByte();
            sbyte moveY = reader.SByte();
            byte flags = reader.Byte();

            if ((flags & ~(DashFlag | PotionFlag | RefillFlag)) != 0)
            {
                throw new ProtocolException($"unknown input flags {flags}");
            }

            SkillSlot? skill = SnapshotCodec.ReadSlot(ref reader);
            return new InputMessage(
                sequence,
                moveX,
                moveY,
                (flags & DashFlag) != 0,
                skill,
                (flags & PotionFlag) != 0,
                (flags & RefillFlag) != 0);
        }

        private static sbyte Quantize(double value)
        {
            double clamped = Math.Max(-1d, Math.Min(1d, double.IsNaN(value) ? 0d : value));
            return (sbyte)Math.Round(clamped * Steps, MidpointRounding.AwayFromZero);
        }
    }

    [Flags]
    public enum RemoteFlags : byte
    {
        None = 0,
        Grounded = 1,
        Dashing = 2,

        /// <summary>Connection lost; the server is holding the place for a reconnect.</summary>
        Away = 4,

        /// <summary>Waiting to come back at the spawn point.</summary>
        Dead = 8,
    }

    /// <summary>What a client needs to draw another player, including the skill they are casting.</summary>
    public readonly struct RemotePlayer
    {
        public RemotePlayer(
            int id,
            float x,
            float y,
            sbyte facing,
            RemoteFlags flags,
            SkillSlot? casting = null,
            int castTicks = 0,
            int comboStep = 0,
            sbyte castDirection = 1,
            Appearance appearance = default)
        {
            Id = id;
            X = x;
            Y = y;
            Facing = facing;
            Flags = flags;
            Casting = casting;
            CastTicks = castTicks;
            ComboStep = comboStep;
            CastDirection = castDirection;
            Appearance = appearance;
        }

        public int Id { get; }
        public float X { get; }
        public float Y { get; }
        public sbyte Facing { get; }
        public RemoteFlags Flags { get; }
        public SkillSlot? Casting { get; }

        /// <summary>Ticks since the cast began, so the drawing can be rebuilt from the skill.</summary>
        public int CastTicks { get; }

        public int ComboStep { get; }
        public sbyte CastDirection { get; }

        /// <summary>What they wear. Sent every snapshot: three bytes, and right for anyone who comes in late.</summary>
        public Appearance Appearance { get; }
    }

    /// <summary>A projectile anyone in the room fired, for drawing.</summary>
    public readonly struct RemoteProjectile
    {
        public RemoteProjectile(int ownerId, SkillSlot slot, float x, float y, sbyte direction)
        {
            OwnerId = ownerId;
            Slot = slot;
            X = x;
            Y = y;
            Direction = direction;
        }

        public int OwnerId { get; }
        public SkillSlot Slot { get; }
        public float X { get; }
        public float Y { get; }
        public sbyte Direction { get; }
    }

    /// <summary>
    /// A <see cref="RoomEvent"/> on the wire. A boss attack travels as its place in the boss's cycle,
    /// so the client finds the attack in its own config.
    /// </summary>
    public readonly struct NetEvent
    {
        public NetEvent(
            RoomEventKind kind,
            int playerId,
            CombatEventKind combatKind = default,
            SkillSlot? slot = null,
            int amount = 0,
            int hitIndex = 0,
            BossEventKind bossKind = default,
            int bossAttack = 0)
        {
            Kind = kind;
            PlayerId = playerId;
            CombatKind = combatKind;
            Slot = slot;
            Amount = amount;
            HitIndex = hitIndex;
            BossKind = bossKind;
            BossAttack = bossAttack;
        }

        public RoomEventKind Kind { get; }
        public int PlayerId { get; }
        public CombatEventKind CombatKind { get; }
        public SkillSlot? Slot { get; }

        /// <summary>Damage dealt or health restored; for a boss hit, the damage taken.</summary>
        public int Amount { get; }

        public int HitIndex { get; }
        public BossEventKind BossKind { get; }

        /// <summary>Index into the boss's cycle of the attack that warned or hit.</summary>
        public int BossAttack { get; }

        public static NetEvent From(in RoomEvent item, BossConfig boss)
        {
            switch (item.Kind)
            {
                case RoomEventKind.Combat:
                    CombatEvent combat = item.Combat;
                    return new NetEvent(item.Kind, item.PlayerId, combat.Kind, combat.Slot, combat.Amount, combat.HitIndex);
                case RoomEventKind.Boss:
                    BossEvent hit = item.Boss;
                    int attack = 0;

                    for (int index = 0; index < boss.Cycle.Count; index++)
                    {
                        if (boss.Cycle[index] == hit.Attack)
                        {
                            attack = index;
                            break;
                        }
                    }

                    return new NetEvent(item.Kind, item.PlayerId, amount: hit.Damage, bossKind: hit.Kind, bossAttack: attack);
                default:
                    return new NetEvent(item.Kind, item.PlayerId);
            }
        }

        public CombatEvent ToCombatEvent(GameConfig config)
        {
            SkillSlow? slow = Slot == null ? null : config.Skill(Slot.Value).Slow;
            return new CombatEvent(CombatKind, Slot, Amount, CombatKind == CombatEventKind.SkillHit ? slow : null, HitIndex);
        }

        public BossEvent ToBossEvent(BossConfig boss)
        {
            return new BossEvent(BossKind, boss.Cycle[Math.Min(BossAttack, boss.Cycle.Count - 1)], Amount, PlayerId);
        }
    }

    /// <summary>
    /// Server → client. Self is exact, for prediction; others are rounded, for drawing. Events are
    /// everything that happened in the room since the previous snapshot.
    /// </summary>
    public sealed class SnapshotMessage : NetMessage
    {
        public SnapshotMessage(
            uint serverTick,
            uint ackSequence,
            PlayerSnapshot self,
            BossSnapshot boss,
            int bossRespawnTicks,
            IReadOnlyList<RemotePlayer> others,
            IReadOnlyList<RemoteProjectile> projectiles,
            IReadOnlyList<NetEvent> events,
            Appearance selfAppearance = default)
        {
            if (others.Count > byte.MaxValue || projectiles.Count > byte.MaxValue || events.Count > byte.MaxValue)
            {
                throw new ArgumentException("at most 255 players, projectiles and events");
            }

            ServerTick = serverTick;
            AckSequence = ackSequence;
            Self = self;
            Boss = boss;
            BossRespawnTicks = bossRespawnTicks;
            Others = others;
            Projectiles = projectiles;
            Events = events;
            SelfAppearance = selfAppearance;
        }

        public override MessageType Type => MessageType.Snapshot;
        public uint ServerTick { get; }

        /// <summary>The last input applied before <see cref="Self"/> was taken.</summary>
        public uint AckSequence { get; }

        public PlayerSnapshot Self { get; }
        public BossSnapshot Boss { get; }
        public int BossRespawnTicks { get; }
        public IReadOnlyList<RemotePlayer> Others { get; }
        public IReadOnlyList<RemoteProjectile> Projectiles { get; }
        public IReadOnlyList<NetEvent> Events { get; }

        /// <summary>What this player wears, as the server last accepted it.</summary>
        public Appearance SelfAppearance { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.UInt32(ServerTick);
            writer.UInt32(AckSequence);
            SnapshotCodec.Write(writer, Self);
            WriteAppearance(writer, SelfAppearance);
            SnapshotCodec.Write(writer, Boss);
            writer.UInt16(checked((ushort)BossRespawnTicks));
            writer.Byte((byte)Others.Count);

            foreach (RemotePlayer other in Others)
            {
                writer.Int32(other.Id);
                writer.Single(other.X);
                writer.Single(other.Y);
                writer.SByte(other.Facing);
                writer.Byte((byte)other.Flags);
                SnapshotCodec.WriteSlot(writer, other.Casting);
                writer.UInt16(checked((ushort)other.CastTicks));
                writer.Byte(checked((byte)other.ComboStep));
                writer.SByte(other.CastDirection);
                WriteAppearance(writer, other.Appearance);
            }

            writer.Byte((byte)Projectiles.Count);

            foreach (RemoteProjectile shot in Projectiles)
            {
                writer.Int32(shot.OwnerId);
                SnapshotCodec.WriteSlot(writer, shot.Slot);
                writer.Single(shot.X);
                writer.Single(shot.Y);
                writer.SByte(shot.Direction);
            }

            writer.Byte((byte)Events.Count);

            foreach (NetEvent item in Events)
            {
                writer.Byte((byte)item.Kind);
                writer.Int32(item.PlayerId);
                writer.Byte((byte)item.CombatKind);
                SnapshotCodec.WriteSlot(writer, item.Slot);
                writer.Int32(item.Amount);
                writer.Byte(checked((byte)item.HitIndex));
                writer.Byte((byte)item.BossKind);
                writer.Byte(checked((byte)item.BossAttack));
            }
        }

        internal static SnapshotMessage Read(ref PacketReader reader)
        {
            uint tick = reader.UInt32();
            uint ack = reader.UInt32();
            PlayerSnapshot self = SnapshotCodec.ReadPlayer(ref reader);
            Appearance selfAppearance = ReadAppearance(ref reader);
            BossSnapshot boss = SnapshotCodec.ReadBoss(ref reader);
            int bossRespawn = reader.UInt16();
            var others = new RemotePlayer[reader.Byte()];

            for (int index = 0; index < others.Length; index++)
            {
                int id = reader.Int32();
                float x = reader.Single();
                float y = reader.Single();
                sbyte facing = Facing(reader.SByte());
                byte flags = reader.Byte();

                if ((flags & ~(byte)(RemoteFlags.Grounded | RemoteFlags.Dashing | RemoteFlags.Away | RemoteFlags.Dead)) != 0)
                {
                    throw new ProtocolException($"unknown player flags {flags}");
                }

                SkillSlot? casting = SnapshotCodec.ReadSlot(ref reader);
                int castTicks = reader.UInt16();
                int combo = reader.Byte();
                sbyte direction = Facing(reader.SByte());
                others[index] = new RemotePlayer(id, x, y, facing, (RemoteFlags)flags, casting, castTicks, combo, direction, ReadAppearance(ref reader));
            }

            var projectiles = new RemoteProjectile[reader.Byte()];

            for (int index = 0; index < projectiles.Length; index++)
            {
                int owner = reader.Int32();
                SkillSlot slot = SnapshotCodec.ReadSlot(ref reader) ?? throw new ProtocolException("projectile without a skill");
                projectiles[index] = new RemoteProjectile(owner, slot, reader.Single(), reader.Single(), Facing(reader.SByte()));
            }

            var events = new NetEvent[reader.Byte()];

            for (int index = 0; index < events.Length; index++)
            {
                RoomEventKind kind = Known<RoomEventKind>(reader.Byte());
                int player = reader.Int32();
                CombatEventKind combatKind = Known<CombatEventKind>(reader.Byte());
                SkillSlot? slot = SnapshotCodec.ReadSlot(ref reader);
                int amount = reader.Int32();
                int hitIndex = reader.Byte();
                BossEventKind bossKind = Known<BossEventKind>(reader.Byte());
                events[index] = new NetEvent(kind, player, combatKind, slot, amount, hitIndex, bossKind, reader.Byte());
            }

            return new SnapshotMessage(tick, ack, self, boss, bossRespawn, others, projectiles, events, selfAppearance);
        }

        /// <summary>One byte per shown slot. The client checks the items against its own catalog.</summary>
        private static void WriteAppearance(PacketWriter writer, Appearance appearance)
        {
            writer.Byte(checked((byte)appearance.Weapon));
            writer.Byte(checked((byte)appearance.Armor));
            writer.Byte(checked((byte)appearance.Pants));
        }

        private static Appearance ReadAppearance(ref PacketReader reader)
        {
            return new Appearance(reader.Byte(), reader.Byte(), reader.Byte());
        }

        internal static sbyte Facing(sbyte value)
        {
            return value == 1 || value == -1 ? value : throw new ProtocolException($"facing must be 1 or -1, got {value}");
        }

        internal static T Known<T>(byte value)
            where T : struct, Enum
        {
            return Enum.IsDefined(typeof(T), (int)value)
                ? (T)Enum.ToObject(typeof(T), value)
                : throw new ProtocolException($"unknown {typeof(T).Name} {value}");
        }
    }

    /// <summary>Client → server; the server answers with Pong carrying the same time back.</summary>
    public sealed class PingMessage : NetMessage
    {
        public PingMessage(uint clientTimeMs)
        {
            ClientTimeMs = clientTimeMs;
        }

        public override MessageType Type => MessageType.Ping;
        public uint ClientTimeMs { get; }

        internal override void Write(PacketWriter writer) => writer.UInt32(ClientTimeMs);

        internal static PingMessage Read(ref PacketReader reader) => new PingMessage(reader.UInt32());
    }

    public sealed class PongMessage : NetMessage
    {
        public PongMessage(uint clientTimeMs, uint serverTick)
        {
            ClientTimeMs = clientTimeMs;
            ServerTick = serverTick;
        }

        public override MessageType Type => MessageType.Pong;
        public uint ClientTimeMs { get; }
        public uint ServerTick { get; }

        internal override void Write(PacketWriter writer)
        {
            writer.UInt32(ClientTimeMs);
            writer.UInt32(ServerTick);
        }

        internal static PongMessage Read(ref PacketReader reader) => new PongMessage(reader.UInt32(), reader.UInt32());
    }

    /// <summary>
    /// Client → server: put on an item, given as its place in <see cref="GameConfig.Gear"/>. The server
    /// checks it; the change shows in the next snapshot, for this player and for everyone around.
    /// </summary>
    public sealed class WearMessage : NetMessage
    {
        public WearMessage(int item)
        {
            if (item < 0 || item > byte.MaxValue)
            {
                throw new ProtocolException($"item {item} does not fit in a byte");
            }

            Item = item;
        }

        public override MessageType Type => MessageType.Wear;
        public int Item { get; }

        internal override void Write(PacketWriter writer) => writer.Byte((byte)Item);

        internal static WearMessage Read(ref PacketReader reader) => new WearMessage(reader.Byte());
    }

    /// <summary>Client → server: quitting on purpose, so the server frees the place at once.</summary>
    public sealed class LeaveMessage : NetMessage
    {
        public override MessageType Type => MessageType.Leave;

        internal override void Write(PacketWriter writer)
        {
        }
    }
}
