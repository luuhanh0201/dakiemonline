using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Placeholder effects: blocks for hit areas and projectiles, a flash for healing.</summary>
    public sealed class FxView
    {
        private const int SortingOrder = 3;
        private const int WarningOrder = 4;
        private const float HurtFlashSeconds = 0.2f;
        private const float HealFlashSeconds = 0.3f;
        private const float HealFlashSize = 1.6f;

        private static readonly Color HitColor = new Color32(0x3D, 0x8B, 0xF2, 0x80);
        private static readonly Color ProjectileColor = new Color32(0x9C, 0xC4, 0xFA, 0xFF);
        private static readonly Color HealColor = new Color32(0x46, 0xB3, 0x7E, 0x80);
        private static readonly Color WarningColor = new Color32(0xD9, 0x4F, 0x4F, 0x70);

        private readonly WorldView world;
        private readonly Transform hitArea;
        private readonly Transform healFlash;
        private readonly Transform warningZone;
        private readonly List<Transform> projectiles = new List<Transform>();
        private float healFlashUntil;
        private float hurtFlashUntil;

        public FxView(WorldView world)
        {
            this.world = world;
            hitArea = world.CreateBlock("HitArea", HitColor, SortingOrder, Vector2.one).transform;
            healFlash = world.CreateBlock("HealFlash", HealColor, SortingOrder, Vector2.one * HealFlashSize).transform;
            warningZone = world.CreateBlock("WarningZone", WarningColor, WarningOrder, Vector2.one).transform;
        }

        public bool PlayerHurt => Time.unscaledTime < hurtFlashUntil;

        public void Show(Simulation fight, BossConfig boss)
        {
            ShowBox(hitArea, Combat.ActiveHitbox(fight.Combat, fight.Player));
            ShowBox(warningZone, BossAi.WarningZone(fight.Boss, boss));
            ShowProjectiles(fight.Combat.Projectiles);
            ShowHealFlash(fight.Player, fight.CombatEvents);

            foreach (BossEvent item in fight.BossEvents)
            {
                if (item.Kind == BossEventKind.PlayerHit)
                {
                    hurtFlashUntil = Time.unscaledTime + HurtFlashSeconds;
                }
            }
        }

        private static void ShowBox(Transform block, CombatBox? box)
        {
            block.gameObject.SetActive(box != null);

            if (box == null)
            {
                return;
            }

            CombatBox area = box.Value;
            block.position = new Vector3((float)(area.MinX + area.MaxX) / 2f, (float)(area.MinY + area.MaxY) / 2f, 0f);
            block.localScale = new Vector3((float)(area.MaxX - area.MinX), (float)(area.MaxY - area.MinY), 1f);
        }

        private void ShowProjectiles(IReadOnlyList<Projectile> flying)
        {
            while (projectiles.Count < flying.Count)
            {
                projectiles.Add(world.CreateBlock("Projectile", ProjectileColor, SortingOrder, Vector2.one).transform);
            }

            for (int index = 0; index < projectiles.Count; index++)
            {
                bool inUse = index < flying.Count;
                projectiles[index].gameObject.SetActive(inUse);

                if (inUse)
                {
                    Projectile shot = flying[index];
                    float size = (float)shot.Width;
                    projectiles[index].position = new Vector3((float)shot.X, (float)shot.Y, 0f);
                    projectiles[index].localScale = new Vector3(size, size, 1f);
                }
            }
        }

        private void ShowHealFlash(MovementState movement, IReadOnlyList<CombatEvent> events)
        {
            foreach (CombatEvent item in events)
            {
                if (item.Kind == CombatEventKind.SelfHealed || item.Kind == CombatEventKind.PotionUsed)
                {
                    healFlashUntil = Time.unscaledTime + HealFlashSeconds;
                }
            }

            bool flashing = Time.unscaledTime < healFlashUntil;
            healFlash.gameObject.SetActive(flashing);

            if (flashing)
            {
                healFlash.position = new Vector3((float)movement.X, (float)movement.Y + HealFlashSize / 2f, 0f);
            }
        }
    }
}
