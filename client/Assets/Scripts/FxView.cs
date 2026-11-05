using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Placeholder effects: blocks for hit areas and projectiles, a flash for healing.</summary>
    public sealed class FxView
    {
        private const int SortingOrder = 3;
        private const float HealFlashSeconds = 0.3f;
        private const float HealFlashSize = 1.6f;

        private static readonly Color HitColor = new Color32(0x3D, 0x8B, 0xF2, 0x80);
        private static readonly Color ProjectileColor = new Color32(0x9C, 0xC4, 0xFA, 0xFF);
        private static readonly Color HealColor = new Color32(0x46, 0xB3, 0x7E, 0x80);

        private readonly WorldView world;
        private readonly Transform hitArea;
        private readonly Transform healFlash;
        private readonly List<Transform> projectiles = new List<Transform>();
        private float healFlashUntil;

        public FxView(WorldView world)
        {
            this.world = world;
            hitArea = world.CreateBlock("HitArea", HitColor, SortingOrder, Vector2.one).transform;
            healFlash = world.CreateBlock("HealFlash", HealColor, SortingOrder, Vector2.one * HealFlashSize).transform;
        }

        public void Show(CombatState combat, MovementState movement, List<CombatEvent> events)
        {
            ShowHitArea(Combat.ActiveHitbox(combat, movement));
            ShowProjectiles(combat.Projectiles);
            ShowHealFlash(movement, events);
        }

        private void ShowHitArea(CombatBox? box)
        {
            hitArea.gameObject.SetActive(box != null);

            if (box == null)
            {
                return;
            }

            CombatBox area = box.Value;
            hitArea.position = new Vector3((float)(area.MinX + area.MaxX) / 2f, (float)(area.MinY + area.MaxY) / 2f, 0f);
            hitArea.localScale = new Vector3((float)(area.MaxX - area.MinX), (float)(area.MaxY - area.MinY), 1f);
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

        private void ShowHealFlash(MovementState movement, List<CombatEvent> events)
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
