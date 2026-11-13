using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    public enum HudButton
    {
        None,
        Attack,
        Dash,
        Ranged,
        SkillA,
        SkillB,
        Heal,
        Ultimate,
        Potion,
    }

    public readonly struct HudButtonSpec
    {
        public HudButtonSpec(HudButton button, string label, float diameterDp, float arcRadiusDp, float angleDegrees)
        {
            Button = button;
            Label = label;
            DiameterDp = diameterDp;
            CentreDp = HudLayout.AttackCentreDp + arcRadiusDp * new Vector2(
                Mathf.Cos(angleDegrees * Mathf.Deg2Rad),
                Mathf.Sin(angleDegrees * Mathf.Deg2Rad));
        }

        public HudButton Button { get; }
        public string Label { get; }
        public float DiameterDp { get; }

        /// <summary>Offset from the bottom-right corner of the screen.</summary>
        public Vector2 CentreDp { get; }
    }

    /// <summary>Default arc layout around the attack button, sized per SPEC-0.5 step 3.</summary>
    public static class HudLayout
    {
        public const float JoystickDiameterDp = 128f;
        public const float BaselineDpi = 160f;

        public static readonly Vector2 AttackCentreDp = new Vector2(-68f, 68f);

        private const float AttackDp = 80f;
        private const float DashDp = 60f;
        private const float SkillDp = 52f;
        private const float InnerArcDp = 82f;
        private const float OuterArcDp = 130f;

        public static readonly HudButtonSpec[] Buttons =
        {
            new HudButtonSpec(HudButton.Attack, "Đánh", AttackDp, 0f, 0f),
            new HudButtonSpec(HudButton.Dash, "Lướt", DashDp, InnerArcDp, 201.5f),
            new HudButtonSpec(HudButton.Ranged, "Xa", SkillDp, InnerArcDp, 154f),
            new HudButtonSpec(HudButton.SkillA, "A", SkillDp, InnerArcDp, 110f),
            new HudButtonSpec(HudButton.SkillB, "B", SkillDp, InnerArcDp, 66f),
            new HudButtonSpec(HudButton.Heal, "Hồi", SkillDp, OuterArcDp, 176f),
            new HudButtonSpec(HudButton.Ultimate, "Tuyệt", SkillDp, OuterArcDp, 132f),
            new HudButtonSpec(HudButton.Potion, "Thuốc", SkillDp, OuterArcDp, 88f),
        };

#if UNITY_STANDALONE || UNITY_WEBGL
        // A desktop window, and a browser page that fills its window, have no meaningful dpi, so the
        // HUD is scaled to match the phone's layout by the height of the game frame (GameFrame).
        private const float PhoneHeightDp = 393f;

        public static float DpToPixels => GameFrame.Pixels.height / PhoneHeightDp;
#else
        public static float DpToPixels => (Screen.dpi > 0f ? Screen.dpi : BaselineDpi) / BaselineDpi;
#endif

        /// <summary>The combat slot a button casts, or null for dash and potion.</summary>
        public static SkillSlot? SlotOf(HudButton button)
        {
            switch (button)
            {
                case HudButton.Attack:
                    return SkillSlot.Attack;
                case HudButton.Ranged:
                    return SkillSlot.Ranged;
                case HudButton.SkillA:
                    return SkillSlot.OptionA;
                case HudButton.SkillB:
                    return SkillSlot.OptionB;
                case HudButton.Heal:
                    return SkillSlot.Support;
                case HudButton.Ultimate:
                    return SkillSlot.Ultimate;
                default:
                    return null;
            }
        }
    }
}
