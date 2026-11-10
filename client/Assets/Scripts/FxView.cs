using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Bounded, reusable water drawings driven by combat events and active hit windows.</summary>
    public sealed class FxView
    {
        private const int MaxEffects = 32;
        private const int MaxProjectiles = 16;
        private readonly Transform warningZone;
        private readonly List<WaterEffect> effects = new List<WaterEffect>();
        private readonly List<WaterEffect> projectiles = new List<WaterEffect>();
        private readonly PackageSlashFx packageSlash = new PackageSlashFx();
        private readonly WaterSprites drawnWater = new WaterSprites();
        private object lastOwner;
        private SkillConfig lastSkill;
        private int lastHitIndex = -1, replacement;
        private float hurtFlashUntil, clock;
        private float shakeAt = -1f;
        private bool gathering;
        private readonly WorldView world;

        public FxView(WorldView world)
        {
            this.world = world;
            warningZone = world.CreateBlock("WarningZone", new Color32(0xD9, 0x4F, 0x4F, 0x70), 41, Vector2.one).transform;
        }

        public bool PlayerHurt => Time.unscaledTime < hurtFlashUntil;

        /// <summary>Compares the Free Slash VFX blows with the game's own.</summary>
        public void ToggleSlashPackage() => packageSlash.Toggle();
        public int PooledEffectCount => effects.Count;
        public int ActiveEffectCount
        {
            get
            {
                int count = 0;
                foreach (var effect in effects) if (effect.Playing) count++;
                foreach (var effect in projectiles) if (effect.Playing) count++;
                return count;
            }
        }

        public void Show(Simulation fight, BossConfig boss, float frameSeconds = -1f)
        {
            bool hurt = false;
            foreach (BossEvent item in fight.BossEvents)
                hurt |= item.Kind == BossEventKind.PlayerHit;
            Show(fight, fight.Player, fight.Combat, fight.Boss, boss, fight.CombatEvents, hurt, true, frameSeconds);
        }

        /// <summary>
        /// Draws one character's effects. The owner changes when a new fight or player starts; only one
        /// view per screen shows the boss's red zone. The boss can be null while it is away.
        /// </summary>
        public void Show(object owner, MovementState player, CombatState combat, BossState boss, BossConfig bossConfig,
            IReadOnlyList<CombatEvent> events, bool hurt, bool showWarning, float frameSeconds = -1f)
        {
            if (owner != lastOwner)
            {
                foreach (var effect in effects) effect.Hide();
                foreach (var effect in projectiles) effect.Hide();
                drawnWater.Clear();
                shakeAt = -1f;
                lastOwner = owner; lastSkill = null; lastHitIndex = -1;
                hurtFlashUntil = 0f; clock = 0f;
            }
            float dt = Mathf.Max(0f, frameSeconds < 0f ? Time.deltaTime : frameSeconds);
            clock += dt;
            foreach (var effect in effects) effect.Advance(dt);
            drawnWater.Advance(dt);
            if (shakeAt >= 0f && clock >= shakeAt)
            {
                world.Shake();
                shakeAt = -1f;
            }
            ShowBox(warningZone, showWarning && boss != null ? BossAi.WarningZone(boss, bossConfig) : null);
            if (drawnWater.HasShots)
            {
                foreach (var effect in projectiles) effect.Hide();
                int landed = 0;
                foreach (CombatEvent item in events)
                    if (item.Kind == CombatEventKind.SkillHit && item.Slot == SkillSlot.Ranged) landed++;
                drawnWater.ShowShots(combat.Projectiles, landed, dt);
            }
            else
            {
                ShowProjectiles(combat.Projectiles);
            }
            StartSlash(player, combat);
            foreach (CombatEvent item in events)
            {
                if (item.Kind == CombatEventKind.SkillHit && boss != null) StartSpark(player, combat, boss, bossConfig, item);
                else if ((item.Kind == CombatEventKind.SelfHealed || item.Kind == CombatEventKind.PotionUsed) && drawnWater.HasHeal)
                    drawnWater.PlayHeal(new Vector2((float)player.X, (float)player.Y));
                else if (item.Kind == CombatEventKind.SelfHealed || item.Kind == CombatEventKind.PotionUsed)
                    Play(WaterForm.Heal, new Vector2((float)player.X, (float)player.Y + 0.5f),
                        new Vector2(1.3f, 1.2f), 1, 0.8f);
            }
            if (hurt) hurtFlashUntil = Time.unscaledTime + 0.2f;
        }

        private static void ShowBox(Transform block, CombatBox? box)
        {
            block.gameObject.SetActive(box != null);
            if (box == null) return;
            CombatBox area = box.Value;
            block.position = new Vector3((float)(area.MinX + area.MaxX) / 2f, (float)(area.MinY + area.MaxY) / 2f, 0f);
            block.localScale = new Vector3((float)(area.MaxX - area.MinX), (float)(area.MaxY - area.MinY), 1f);
        }

        private void ShowProjectiles(IReadOnlyList<Projectile> flying)
        {
            int count = Mathf.Min(flying.Count, MaxProjectiles);
            while (projectiles.Count < count) projectiles.Add(new WaterEffect("Water projectile"));
            for (int i = 0; i < projectiles.Count; i++)
            {
                if (i >= count) { projectiles[i].Hide(); continue; }
                Projectile shot = flying[i];
                projectiles[i].Projectile(new Vector2((float)shot.X, (float)shot.Y), (float)shot.Width * 1.9f, shot.Direction, clock);
            }
        }

        private void StartSlash(MovementState player, CombatState combat)
        {
            StartGather(player, combat);
            SkillConfig skill = combat.Phase == CastPhase.Active ? combat.Casting : null;
            if (skill == null) { lastSkill = null; lastHitIndex = -1; return; }
            if (skill == lastSkill && combat.ActiveHitIndex == lastHitIndex) return;
            lastSkill = skill; lastHitIndex = combat.ActiveHitIndex;
            int facing = player.Facing;
            var feet = new Vector2((float)player.X, (float)player.Y);
            float reach = skill.Hitbox == null ? 0f : (float)skill.Hitbox.Width;
            if (packageSlash.Enabled && PlayPackageSlash(combat, skill, feet, facing, reach)) return;
            switch (skill.Slot)
            {
                case SkillSlot.Attack:
                    // The blade's own trail draws these blows.
                    break;
                case SkillSlot.OptionA:
                    if (drawnWater.HasDash)
                    {
                        drawnWater.PlayDash(feet, facing, reach);
                        break;
                    }
                    Play(WaterForm.Surge, feet + new Vector2(facing * reach * 0.4f, 0.55f), new Vector2(reach + 0.6f, 0.8f), facing, 0.32f);
                    break;
                case SkillSlot.OptionB:
                    float height = (float)skill.Hitbox.Height;
                    if (drawnWater.Available)
                    {
                        // The screen jolts as the wave breaks.
                        shakeAt = clock + drawnWater.PlayWave(feet, facing, height);
                        break;
                    }
                    Play(WaterForm.Crest, feet + new Vector2(facing * reach * 0.5f, height * 0.52f), new Vector2(reach, height), facing, 0.52f);
                    break;
                case SkillSlot.Ultimate when drawnWater.HasUltimate:
                    // The blade trail draws the first two blows; the last raises the wall.
                    if (combat.ActiveHitIndex == combat.ActiveHitCount - 1)
                        shakeAt = clock + drawnWater.PlayWall(feet, facing, reach);
                    break;
                case SkillSlot.Ultimate:
                    bool final = combat.ActiveHitIndex == combat.ActiveHitCount - 1;
                    Play(final ? WaterForm.Dragon : WaterForm.Spiral,
                        feet + new Vector2(facing * reach * (final ? 0.48f : 0.38f), 0.85f),
                        final ? new Vector2(reach * 1.1f, 1.7f) : new Vector2(1.8f + lastHitIndex * 0.4f, 1.5f + lastHitIndex * 0.3f),
                        facing, final ? 0.55f : 0.34f, lastHitIndex, final ? 0f : lastHitIndex == 0 ? -25f : 20f);
                    break;
            }
        }

        /// <summary>Water gathers at the feet from the windup of the ultimate until its last blow.</summary>
        private void StartGather(MovementState player, CombatState combat)
        {
            SkillConfig skill = combat.Casting;
            bool ultimate = skill != null && skill.Slot == SkillSlot.Ultimate;
            if (!ultimate) { gathering = false; return; }
            if (gathering || !drawnWater.HasUltimate || packageSlash.Enabled) return;
            gathering = true;

            double seconds = skill.WindupSec;
            for (int i = 0; i < skill.ActivePhases.Count - 1; i++) seconds += skill.ActivePhases[i].DurationSec;
            drawnWater.PlayGather(new Vector2((float)player.X, (float)player.Y), player.Facing, (float)seconds);
        }

        /// <summary>Plays the blows the package covers; the wave and the shot keep their own art.</summary>
        private bool PlayPackageSlash(CombatState combat, SkillConfig skill, Vector2 feet, int facing, float reach)
        {
            bool lastHit = combat.ActiveHitIndex == combat.ActiveHitCount - 1;
            var front = feet + new Vector2(facing * reach * 0.35f, 0.6f);

            switch (skill.Slot)
            {
                case SkillSlot.Attack:
                    packageSlash.PlayComboSlash(front, facing, combat.ComboStep);
                    return true;
                case SkillSlot.OptionA:
                    packageSlash.PlaySlash(front, facing, 0.8f, 90f);
                    return true;
                case SkillSlot.Ultimate:
                    packageSlash.PlaySlash(front + Vector2.up * 0.25f, facing, lastHit ? 1.3f : 0.95f, 0f, combat.ActiveHitIndex == 1);
                    return true;
                default:
                    return false;
            }
        }

        private void StartSpark(MovementState player, CombatState combat, BossState bossState, BossConfig boss, in CombatEvent hit)
        {
            int way = bossState.X >= player.X ? -1 : 1;
            var place = new Vector2((float)bossState.X + way * (float)boss.Width * 0.4f,
                (float)boss.Height * 0.4f + hit.HitIndex % 2 * 0.15f);
            bool heavy = hit.Slot == SkillSlot.Ultimate && hit.HitIndex == 2
                || hit.Slot == SkillSlot.Attack && combat.ComboStep == 2;
            if (packageSlash.Enabled)
            {
                packageSlash.PlayImpact(place, heavy ? 0.9f : 0.6f);
                return;
            }
            if (drawnWater.HasHit)
            {
                drawnWater.PlayHit(place, player.Facing, heavy);
                return;
            }
            Play(WaterForm.Impact, place, Vector2.one * (heavy ? 1.4f : 0.9f), player.Facing,
                heavy ? 0.34f : 0.26f, hit.HitIndex);
        }

        private void Play(WaterForm form, Vector2 place, Vector2 size, int facing, float life, int variant = 0, float tilt = 0f)
        {
            WaterEffect effect = null;
            foreach (var candidate in effects)
                if (!candidate.Playing) { effect = candidate; break; }
            if (effect == null && effects.Count < MaxEffects)
            {
                effect = new WaterEffect(); effects.Add(effect);
            }
            if (effect == null) effect = effects[replacement++ % MaxEffects];
            effect.Start(form, place, size, facing, life, variant, tilt);
        }
    }
}
