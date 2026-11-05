using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Draws the boss as a placeholder block that flashes when hit.</summary>
    public sealed class BossView
    {
        private const float HitFlashSeconds = 0.1f;
        private const int BodyOrder = 1;
        private const int FaceOrder = 2;

        private static readonly Color BodyColor = new Color32(0x8C, 0x3A, 0x4E, 0xFF);
        private static readonly Color FaceColor = new Color32(0xDC, 0xE6, 0xFF, 0xFF);
        private static readonly Color HitColor = new Color32(0xDC, 0xE6, 0xFF, 0xFF);

        private readonly Transform body;
        private readonly Transform face;
        private readonly SpriteRenderer bodyRenderer;
        private readonly float width;
        private readonly float height;
        private float hitFlashUntil;

        public BossView(WorldView world, BossConfig config)
        {
            width = (float)config.Width;
            height = (float)config.Height;
            body = world.CreateBlock("Boss", BodyColor, BodyOrder, new Vector2(width, height)).transform;
            bodyRenderer = body.GetComponent<SpriteRenderer>();
            face = world.CreateBlock("BossFace", FaceColor, FaceOrder, new Vector2(width / 3f, height / 4f)).transform;
        }

        public void Show(float x, Simulation fight)
        {
            foreach (CombatEvent item in fight.CombatEvents)
            {
                if (item.Kind == CombatEventKind.SkillHit)
                {
                    hitFlashUntil = Time.unscaledTime + HitFlashSeconds;
                }
            }

            bodyRenderer.color = Time.unscaledTime < hitFlashUntil ? HitColor : BodyColor;
            body.position = new Vector3(x, height / 2f, 0f);
            face.position = new Vector3(x + fight.Boss.Facing * width / 3f, height * 0.75f, 0f);
        }
    }
}
