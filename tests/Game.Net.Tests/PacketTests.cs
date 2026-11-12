using Game.Rules;

namespace Game.Net.Tests;

public class PacketTests
{
    private static readonly MovementSnapshot Moving = new(12.345678901234d, 1.5d, -3.25d, -1, false, 73.3333333d, 0.6d, 4, 0, 2, 5, -1, 1, 29);

    private static readonly BossSnapshot Boss = new(4321, 17.25d, -1, BossPhase.Warning, 3, 12, 15.5d, 20, 20d);

    private static PlayerSnapshot Fighting()
    {
        var combat = new CombatSnapshot
        {
            Casting = SkillSlot.Ultimate,
            CastTicks = 14,
            ComboStep = 2,
            Direction = -1,
            OriginX = 11.75d,
            LastHitPhase = -1,
            PotionsLeft = 3,
            BufferedSkill = SkillSlot.Ranged,
            SkillBufferTicks = 2,
            PotionBufferTicks = 1,
            PotionCooldownTicks = 120,
            ComboWindowTicks = 4,
            Projectiles = new[] { new ProjectileSnapshot(SkillSlot.Ranged, 13.5d, 0.5d, -1, 2.25d) },
        };
        combat.Cooldowns[(int)SkillSlot.Ranged] = 90;
        combat.Cooldowns[(int)SkillSlot.Ultimate] = 1040;
        return new PlayerSnapshot(Moving, combat, new ResourceSnapshot(640, 47.5d), 0);
    }

    private static SnapshotMessage FullSnapshot(int others = 2)
    {
        RemotePlayer[] players = Enumerable.Range(0, others)
            .Select(index => new RemotePlayer(7 + index, 3.5f + index, 0f, 1, RemoteFlags.Grounded, SkillSlot.OptionB, 9, 0, -1, new Appearance(1, 2 + index, 5)))
            .ToArray();
        return new SnapshotMessage(
            900u,
            880u,
            Fighting(),
            Boss,
            0,
            players,
            new[] { new RemoteProjectile(7, SkillSlot.Ranged, 6f, 0.5f, 1) },
            new[]
            {
                new NetEvent(RoomEventKind.Combat, 7, CombatEventKind.SkillHit, SkillSlot.OptionB, 130, 0),
                new NetEvent(RoomEventKind.Boss, 9, amount: 120, bossKind: BossEventKind.PlayerHit, bossAttack: 4),
                new NetEvent(RoomEventKind.PlayerDied, 9),
            },
            new Appearance(0, 3, 254));
    }

    private static T RoundTrip<T>(T message)
        where T : NetMessage
    {
        byte[] packet = Packet.Encode(message);
        Assert.Equal((byte)message.Type, packet[0]);
        return Assert.IsType<T>(Packet.Decode(packet));
    }

    [Fact]
    public void HelloRoundTrips()
    {
        byte[] token = Enumerable.Range(1, Packet.TokenSize).Select(value => (byte)value).ToArray();

        HelloMessage hello = RoundTrip(new HelloMessage(Packet.ProtocolVersion, token, "phien-dang-nhap_43-ky-tu"));

        Assert.Equal(Packet.ProtocolVersion, hello.ProtocolVersion);
        Assert.Equal(token, hello.ResumeToken);
        Assert.Equal("phien-dang-nhap_43-ky-tu", hello.LoginToken);
        Assert.Empty(RoundTrip(new HelloMessage(7, token, "x")).ResumeToken);
    }

    [Fact]
    public void TextThatIsNotUtf8IsRejected()
    {
        byte[] hello = Packet.Encode(new HelloMessage(Packet.ProtocolVersion, Array.Empty<byte>(), "ab"));
        hello[^1] = 0xFF;

        Assert.Throws<ProtocolException>(() => Packet.Decode(hello));
    }

    [Fact]
    public void WelcomeKeepsTheExactPlayerState()
    {
        var token = new byte[Packet.TokenSize];
        token[3] = 200;

        WelcomeMessage welcome = RoundTrip(new WelcomeMessage(42, 2, 123456u, true, token, 999u, true, Fighting(), "Thủy"));

        Assert.Equal("Thủy", welcome.CharacterName);

        Assert.Equal(42, welcome.PlayerId);
        Assert.Equal(2, welcome.ChannelId);
        Assert.Equal(123456u, welcome.ServerTick);
        Assert.True(welcome.Resumed);
        Assert.Equal(token, welcome.Token);
        Assert.Equal(999u, welcome.LastInputSequence);
        Assert.True(welcome.TestRoom);
        Assert.Equal(Fighting(), welcome.Self);
    }

    [Fact]
    public void RejectRoundTrips()
    {
        Assert.Equal(RejectReason.ServerFull, RoundTrip(new RejectMessage(RejectReason.ServerFull)).Reason);
        Assert.Equal(RejectReason.NoCharacter, RoundTrip(new RejectMessage(RejectReason.NoCharacter)).Reason);
    }

    [Theory]
    [InlineData(1d, 127)]
    [InlineData(-1d, -127)]
    [InlineData(0.5d, 64)]
    [InlineData(0d, 0)]
    [InlineData(2d, 127)]
    [InlineData(-5d, -127)]
    public void InputJoystickBecomesWholeSteps(double tilt, int expected)
    {
        InputMessage input = RoundTrip(InputMessage.FromJoystick(uint.MaxValue, tilt, -tilt, true));

        Assert.Equal(uint.MaxValue, input.Sequence);
        Assert.Equal(expected, input.MoveX);
        Assert.Equal(-expected, input.MoveY);
        Assert.True(input.Dash);
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(SkillSlot.Attack, false, false)]
    [InlineData(SkillSlot.OptionB, true, false)]
    [InlineData(SkillSlot.Ultimate, false, true)]
    public void InputCarriesButtons(SkillSlot? skill, bool potion, bool refill)
    {
        InputMessage input = RoundTrip(InputMessage.FromJoystick(5, 0.73d, 0.61d, false, skill, potion, refill));
        RoomIntent intent = input.ToIntent();

        Assert.Equal(skill, intent.Combat.SkillPressed);
        Assert.Equal(potion, intent.Combat.PotionPressed);
        Assert.Equal(refill, intent.Refill);
        Assert.False(intent.Move.DashPressed);
        Assert.Equal(InputMessage.FromJoystick(5, 0.73d, 0.61d, false).ToIntent().Move.MoveX, intent.Move.MoveX);
        Assert.Equal(1d, InputMessage.FromJoystick(1, 1d, 0d, false).ToIntent().Move.MoveX);
    }

    [Fact]
    public void InputHasNoRoomForDamage()
    {
        // The client can only say which buttons it pressed: 1 type, 4 sequence, 2 stick, 1 flags, 1 skill.
        Assert.Equal(9, Packet.Encode(InputMessage.FromJoystick(1, 1d, 1d, true, SkillSlot.Ultimate, true, true)).Length);
    }

    [Fact]
    public void SnapshotCarriesEverything()
    {
        SnapshotMessage snapshot = RoundTrip(FullSnapshot());

        Assert.Equal(900u, snapshot.ServerTick);
        Assert.Equal(880u, snapshot.AckSequence);
        Assert.Equal(Fighting(), snapshot.Self);
        Assert.Equal(Boss, snapshot.Boss);
        Assert.Equal(2, snapshot.Others.Count);
        RemotePlayer other = snapshot.Others[1];
        Assert.Equal(8, other.Id);
        Assert.Equal(4.5f, other.X);
        Assert.Equal(SkillSlot.OptionB, other.Casting);
        Assert.Equal(9, other.CastTicks);
        Assert.Equal(-1, other.CastDirection);
        Assert.Equal(new Appearance(1, 3, 5), other.Appearance);
        Assert.Equal(new Appearance(0, 3, 254), snapshot.SelfAppearance);
        RemoteProjectile shot = Assert.Single(snapshot.Projectiles);
        Assert.Equal(SkillSlot.Ranged, shot.Slot);
        Assert.Equal(6f, shot.X);
        Assert.Equal(3, snapshot.Events.Count);
        Assert.Equal(130, snapshot.Events[0].Amount);
        Assert.Equal(SkillSlot.OptionB, snapshot.Events[0].Slot);
        Assert.Equal(BossEventKind.PlayerHit, snapshot.Events[1].BossKind);
        Assert.Equal(4, snapshot.Events[1].BossAttack);
        Assert.Equal(RoomEventKind.PlayerDied, snapshot.Events[2].Kind);
    }

    [Fact]
    public void EventsTurnBackIntoTheRulesEvents()
    {
        GameConfig config = TestConfig.Load();
        BossAttackConfig attack = config.Boss.Cycle[4];
        var hit = new RoomEvent(RoomEventKind.Boss, 3, boss: new BossEvent(BossEventKind.PlayerHit, attack, 250, 3));
        var slash = new RoomEvent(RoomEventKind.Combat, 2, new CombatEvent(CombatEventKind.SkillHit, SkillSlot.OptionB, 130, config.Skill(SkillSlot.OptionB).Slow, 0));

        BossEvent bossBack = NetEvent.From(hit, config.Boss).ToBossEvent(config.Boss);
        CombatEvent combatBack = NetEvent.From(slash, config.Boss).ToCombatEvent(config);

        Assert.Same(attack, bossBack.Attack);
        Assert.Equal(250, bossBack.Damage);
        Assert.Equal(3, bossBack.PlayerId);
        Assert.Equal(130, combatBack.Amount);
        Assert.Same(config.Skill(SkillSlot.OptionB).Slow, combatBack.Slow);
    }

    [Fact]
    public void SnapshotForTwentyPlayersFitsInOnePacket()
    {
        byte[] packet = Packet.Encode(FullSnapshot(others: 19));

        Assert.True(packet.Length < 1000, $"{packet.Length} bytes");
    }

    [Fact]
    public void PingPongAndLeaveRoundTrip()
    {
        Assert.Equal(55u, RoundTrip(new PingMessage(55u)).ClientTimeMs);
        PongMessage pong = RoundTrip(new PongMessage(55u, 77u));
        Assert.Equal(55u, pong.ClientTimeMs);
        Assert.Equal(77u, pong.ServerTick);
        RoundTrip(new LeaveMessage());
    }

    [Theory]
    [InlineData(ItemActionKind.Wear, 9_000_000_001L, 0)]
    [InlineData(ItemActionKind.Move, 3L, 29)]
    [InlineData(ItemActionKind.PickUp, 7L, 0)]
    public void ItemActionRoundTrips(ItemActionKind kind, long target, int to)
    {
        ItemActionMessage action = RoundTrip(new ItemActionMessage(kind, target, to));

        Assert.Equal(kind, action.Kind);
        Assert.Equal(target, action.Target);
        Assert.Equal(to, action.To);
    }

    [Fact]
    public void UnknownItemActionIsRejected()
    {
        byte[] packet = Packet.Encode(new ItemActionMessage(ItemActionKind.Drop, 1));
        packet[1] = 99;

        Assert.Throws<ProtocolException>(() => Packet.Decode(packet));
    }

    [Fact]
    public void InventoryRoundTrips()
    {
        var items = new[]
        {
            new OwnedItem(12_345_678_901L, 0, 1, ItemPlace.Worn, 0),
            new OwnedItem(2, 9, 300, ItemPlace.Bag, 29),
        };

        InventoryMessage inventory = RoundTrip(new InventoryMessage(30, items, InventoryProblem.BagFull));

        Assert.Equal(30, inventory.BagSlots);
        Assert.Equal(InventoryProblem.BagFull, inventory.Problem);
        Assert.Equal(items, inventory.Items);
    }

    [Fact]
    public void ThirtyItemsFitInOnePacket()
    {
        var items = Enumerable.Range(0, 35).Select(index => new OwnedItem(index + 1, index % 10, 1, ItemPlace.Bag, index)).ToArray();

        Assert.True(Packet.Encode(new InventoryMessage(30, items)).Length < 500);
    }

    [Fact]
    public void SnapshotCarriesItemsOnTheGround()
    {
        SnapshotMessage source = FullSnapshot();
        var ground = new[] { new GroundItem(4, 6, 7.5f, 0f), new GroundItem(5, 9, -1f, 2.25f) };
        var withGround = new SnapshotMessage(source.ServerTick, source.AckSequence, source.Self, source.Boss, 0, source.Others, source.Projectiles, source.Events, source.SelfAppearance, ground);

        SnapshotMessage read = RoundTrip(withGround);

        Assert.Equal(2, read.Ground.Count);
        Assert.Equal((5, 9, -1f, 2.25f), (read.Ground[1].Id, read.Ground[1].Item, read.Ground[1].X, read.Ground[1].Y));
        Assert.Empty(RoundTrip(FullSnapshot()).Ground);
    }

    [Fact]
    public void EmptyPacketIsRejected()
    {
        Assert.Throws<ProtocolException>(() => Packet.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void UnknownTypeIsRejected()
    {
        ProtocolException error = Assert.Throws<ProtocolException>(() => Packet.Decode(new byte[] { 99 }));
        Assert.Contains("unknown message type", error.Message);
    }

    [Fact]
    public void EveryTruncationOfASnapshotIsRejected()
    {
        byte[] packet = Packet.Encode(FullSnapshot());

        for (int length = 1; length < packet.Length; length++)
        {
            byte[] cut = packet[..length];
            Assert.Throws<ProtocolException>(() => Packet.Decode(cut));
        }
    }

    [Fact]
    public void TrailingBytesAreRejected()
    {
        byte[] packet = Packet.Encode(new PingMessage(1u)).Append((byte)0).ToArray();

        Assert.Contains("left over", Assert.Throws<ProtocolException>(() => Packet.Decode(packet)).Message);
    }

    [Theory]
    [InlineData(5, 0x80)]
    [InlineData(7, 8)]
    [InlineData(8, 7)]
    public void BadInputValuesAreRejected(int offset, byte value)
    {
        byte[] input = Packet.Encode(InputMessage.FromJoystick(1, 0d, 0d, false));
        input[offset] = value;

        Assert.Throws<ProtocolException>(() => Packet.Decode(input));
    }

    [Fact]
    public void UnknownRejectReasonIsRejected()
    {
        byte[] reject = Packet.Encode(new RejectMessage(RejectReason.BadMessage));
        reject[1] = 77;

        Assert.Throws<ProtocolException>(() => Packet.Decode(reject));
    }

    [Fact]
    public void NotFiniteNumbersAreRejected()
    {
        byte[] packet = Packet.Encode(FullSnapshot());
        BitConverter.GetBytes(double.NaN).CopyTo(packet, 9);

        Assert.Contains("finite", Assert.Throws<ProtocolException>(() => Packet.Decode(packet)).Message);
    }

    [Fact]
    public void OversizedPacketIsRejectedBeforeReading()
    {
        var packet = new byte[Packet.MaxSize + 1];
        packet[0] = (byte)MessageType.Ping;

        Assert.Throws<ProtocolException>(() => Packet.Decode(packet));
    }
}
