using Game.Rules;

namespace Game.Net
{
    /// <summary>The wire layout of the snapshot types shared by Welcome and Snapshot.</summary>
    internal static class SnapshotCodec
    {
        private const byte NoSlot = 0;

        public static void WriteSlot(PacketWriter writer, SkillSlot? slot)
        {
            writer.Byte(slot == null ? NoSlot : (byte)((int)slot.Value + 1));
        }

        public static SkillSlot? ReadSlot(ref PacketReader reader)
        {
            byte value = reader.Byte();

            if (value == NoSlot)
            {
                return null;
            }

            if (value > CombatSnapshot.SlotCount)
            {
                throw new ProtocolException($"unknown skill slot {value}");
            }

            return (SkillSlot)(value - 1);
        }

        public static void Write(PacketWriter writer, PlayerSnapshot snapshot)
        {
            Write(writer, snapshot.Movement);
            Write(writer, snapshot.Combat);
            writer.Int32(snapshot.Resources.Hp);
            writer.Double(snapshot.Resources.Energy);
            writer.UInt16(checked((ushort)snapshot.RespawnTicksLeft));
        }

        public static PlayerSnapshot ReadPlayer(ref PacketReader reader)
        {
            MovementSnapshot movement = ReadMovement(ref reader);
            CombatSnapshot combat = ReadCombat(ref reader);
            int hp = reader.Int32();
            double energy = reader.Double();

            if (hp < 0 || energy < 0d)
            {
                throw new ProtocolException("health and energy cannot be negative");
            }

            return new PlayerSnapshot(movement, combat, new ResourceSnapshot(hp, energy), reader.UInt16());
        }

        public static void Write(PacketWriter writer, in MovementSnapshot snapshot)
        {
            writer.Double(snapshot.X);
            writer.Double(snapshot.Y);
            writer.Double(snapshot.VelocityY);
            writer.SByte(checked((sbyte)snapshot.Facing));
            writer.Bool(snapshot.Grounded);
            writer.Double(snapshot.Stamina);
            writer.Double(snapshot.PreviousMoveY);
            writer.Int16(checked((short)snapshot.JumpBufferTicks));
            writer.Int16(checked((short)snapshot.DashBufferTicks));
            writer.Int16(checked((short)snapshot.CoyoteTicks));
            writer.Int16(checked((short)snapshot.DashTicksLeft));
            writer.SByte(checked((sbyte)snapshot.DashDirection));
            writer.Int16(checked((short)snapshot.AirDashesUsed));
            writer.Int16(checked((short)snapshot.StaminaDelayTicks));
        }

        public static MovementSnapshot ReadMovement(ref PacketReader reader)
        {
            double x = reader.Double();
            double y = reader.Double();
            double velocityY = reader.Double();
            sbyte facing = SnapshotMessage.Facing(reader.SByte());
            bool grounded = reader.Bool();
            double stamina = reader.Double();
            double previousMoveY = reader.Double();
            int jumpBuffer = NotNegative(reader.Int16());
            int dashBuffer = NotNegative(reader.Int16());
            int coyote = NotNegative(reader.Int16());
            int dashTicks = NotNegative(reader.Int16());
            int dashDirection = Direction(reader.SByte());
            int airDashes = NotNegative(reader.Int16());
            int staminaDelay = NotNegative(reader.Int16());

            return new MovementSnapshot(
                x,
                y,
                velocityY,
                facing,
                grounded,
                stamina,
                previousMoveY,
                jumpBuffer,
                dashBuffer,
                coyote,
                dashTicks,
                dashDirection,
                airDashes,
                staminaDelay);
        }

        public static void Write(PacketWriter writer, CombatSnapshot snapshot)
        {
            WriteSlot(writer, snapshot.Casting);
            writer.Int16(checked((short)snapshot.CastTicks));
            writer.Byte(checked((byte)snapshot.ComboStep));
            writer.SByte(checked((sbyte)snapshot.Direction));
            writer.Double(snapshot.OriginX);
            writer.SByte(checked((sbyte)snapshot.LastHitPhase));
            writer.Byte(checked((byte)snapshot.PotionsLeft));

            foreach (int cooldown in snapshot.Cooldowns)
            {
                writer.Int16(checked((short)cooldown));
            }

            WriteSlot(writer, snapshot.BufferedSkill);
            writer.Int16(checked((short)snapshot.SkillBufferTicks));
            writer.Int16(checked((short)snapshot.PotionBufferTicks));
            writer.Int16(checked((short)snapshot.PotionCooldownTicks));
            writer.Int16(checked((short)snapshot.ComboWindowTicks));
            writer.Byte(checked((byte)snapshot.Projectiles.Count));

            foreach (ProjectileSnapshot shot in snapshot.Projectiles)
            {
                WriteSlot(writer, shot.Slot);
                writer.Double(shot.X);
                writer.Double(shot.Y);
                writer.SByte(checked((sbyte)shot.Direction));
                writer.Double(shot.Travelled);
            }
        }

        public static CombatSnapshot ReadCombat(ref PacketReader reader)
        {
            var snapshot = new CombatSnapshot
            {
                Casting = ReadSlot(ref reader),
                CastTicks = NotNegative(reader.Int16()),
                ComboStep = reader.Byte(),
                Direction = Direction(reader.SByte()),
                OriginX = reader.Double(),
                LastHitPhase = reader.SByte(),
                PotionsLeft = reader.Byte(),
            };

            for (int slot = 0; slot < snapshot.Cooldowns.Length; slot++)
            {
                snapshot.Cooldowns[slot] = NotNegative(reader.Int16());
            }

            snapshot.BufferedSkill = ReadSlot(ref reader);
            snapshot.SkillBufferTicks = NotNegative(reader.Int16());
            snapshot.PotionBufferTicks = NotNegative(reader.Int16());
            snapshot.PotionCooldownTicks = NotNegative(reader.Int16());
            snapshot.ComboWindowTicks = NotNegative(reader.Int16());
            var projectiles = new ProjectileSnapshot[reader.Byte()];

            for (int index = 0; index < projectiles.Length; index++)
            {
                SkillSlot slot = ReadSlot(ref reader) ?? throw new ProtocolException("projectile without a skill");
                double x = reader.Double();
                double y = reader.Double();
                sbyte direction = SnapshotMessage.Facing(reader.SByte());
                projectiles[index] = new ProjectileSnapshot(slot, x, y, direction, reader.Double());
            }

            snapshot.Projectiles = projectiles;
            return snapshot;
        }

        public static void Write(PacketWriter writer, in BossSnapshot boss)
        {
            writer.Int32(boss.Hp);
            writer.Double(boss.X);
            writer.SByte(checked((sbyte)boss.Facing));
            writer.Byte((byte)boss.Phase);
            writer.Byte(checked((byte)boss.CycleIndex));
            writer.Int16(checked((short)boss.PhaseTicksLeft));
            writer.Double(boss.LockedX);
            writer.Int16(checked((short)boss.SlowTicksLeft));
            writer.Double(boss.SlowPercent);
        }

        public static BossSnapshot ReadBoss(ref PacketReader reader)
        {
            int hp = reader.Int32();
            double x = reader.Double();
            sbyte facing = SnapshotMessage.Facing(reader.SByte());
            BossPhase phase = SnapshotMessage.Known<BossPhase>(reader.Byte());
            int cycle = reader.Byte();
            int phaseTicks = NotNegative(reader.Int16());
            double lockedX = reader.Double();
            int slowTicks = NotNegative(reader.Int16());
            double slowPercent = reader.Double();

            if (hp < 0)
            {
                throw new ProtocolException("boss health cannot be negative");
            }

            return new BossSnapshot(hp, x, facing, phase, cycle, phaseTicks, lockedX, slowTicks, slowPercent);
        }

        private static int Direction(sbyte value)
        {
            return value >= -1 && value <= 1 ? value : throw new ProtocolException($"direction must be -1, 0 or 1, got {value}");
        }

        private static int NotNegative(short value)
        {
            return value >= 0 ? value : throw new ProtocolException($"timer cannot be negative, got {value}");
        }
    }
}
