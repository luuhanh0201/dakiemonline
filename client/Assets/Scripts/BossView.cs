using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>
    /// Draws the boss from two drawings: standing, and raising a blow from the warning until the
    /// strike is over. The art faces right.
    /// </summary>
    public sealed class BossView
    {
        private const string IdleArt = "World/boss_idle";
        private const string AttackArt = "World/boss_attack";
        private const float HitFlashSeconds = 0.1f;
        private const int BodyOrder = 1;
        private const int ShadowOrder = 0;
        private const float ShadowWidth = 2.4f;
        private const float ShadowHeight = 0.34f;
        private const float ShadowOpacity = 0.6f;

        // A two-step breath keeps the single drawing from looking frozen.
        private const float BreathStepsPerSecond = 3f;
        private const float BreathSquash = 0.02f;

        private static readonly Color HitColor = new Color32(0xFF, 0xB8, 0xB8, 0xFF);

        private readonly Transform body;
        private readonly Transform shadow;
        private readonly SpriteRenderer renderer;
        private readonly Sprite idle;
        private readonly Sprite attack;
        private float hitFlashUntil;

        public BossView()
        {
            idle = Resources.Load<Sprite>(IdleArt);
            attack = Resources.Load<Sprite>(AttackArt);

            var host = new GameObject("Boss");
            body = host.transform;
            renderer = host.AddComponent<SpriteRenderer>();
            renderer.sprite = idle;
            renderer.sortingOrder = BodyOrder;

            var shadowHost = new GameObject("BossShadow");
            var shadowRenderer = shadowHost.AddComponent<SpriteRenderer>();
            shadow = shadowHost.transform;
            shadowRenderer.sprite = CharacterView.CreateShadowSprite();
            shadowRenderer.sortingOrder = ShadowOrder;
            shadowRenderer.color = new Color(0f, 0f, 0f, ShadowOpacity);
            shadow.localScale = new Vector3(ShadowWidth, ShadowHeight, 1f);
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

            bool attacking = fight.Boss.Phase == BossPhase.Warning || fight.Boss.Phase == BossPhase.Striking;
            bool breathedIn = !attacking && Mathf.FloorToInt(Time.time * BreathStepsPerSecond) % 2 == 0;
            float squash = breathedIn ? 1f - BreathSquash : 1f;

            renderer.sprite = attacking ? attack : idle;
            renderer.flipX = fight.Boss.Facing < 0;
            renderer.color = Time.unscaledTime < hitFlashUntil ? HitColor : Color.white;
            body.localScale = new Vector3(1f, squash, 1f);
            body.position = new Vector3(x, renderer.sprite.bounds.size.y * squash / 2f, 0f);
            shadow.position = new Vector3(x, 0f, 0f);
        }
    }
}
