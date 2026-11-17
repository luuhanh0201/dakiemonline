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

        /// <summary>Opens chat once chat exists (H6); until then it only lights up.</summary>
        Chat,
    }

    public readonly struct HudButtonSpec
    {
        public HudButtonSpec(HudButton button, string label, float sizeDp, Vector2 anchor, Vector2 centreDp, bool square = false)
        {
            Button = button;
            Label = label;
            DiameterDp = sizeDp;
            Anchor = anchor;
            CentreDp = centreDp;
            Square = square;
        }

        public HudButton Button { get; }
        public string Label { get; }

        /// <summary>The width of the button: the diameter of a round one, the side of a square one.</summary>
        public float DiameterDp { get; }

        /// <summary>The point of the game frame it hangs from: (0,0) bottom left, (1,1) top right.</summary>
        public Vector2 Anchor { get; }

        /// <summary>Offset of the centre from the anchor.</summary>
        public Vector2 CentreDp { get; }

        public bool Square { get; }
    }

    /// <summary>
    /// The skills in one row of squares at the bottom centre, attack in the bottom right corner with dash
    /// above it and the potion to its left, chat in the top right, and a fixed joystick in the bottom left.
    /// Chosen by the user on 14/11/2026 in place of the arc of SPEC-0.5 step 3; button sizes are kept.
    /// </summary>
    public static class HudLayout
    {
        public const float JoystickDiameterDp = 128f;
        public const float BaselineDpi = 160f;

        /// <summary>Where the fixed joystick rests, from the bottom-left corner.</summary>
        public static readonly Vector2 JoystickCentreDp = new Vector2(100f, 88f);

        /// <summary>How far past its ring a touch still takes the joystick, as a share of its radius.</summary>
        public const float JoystickReach = 1.3f;

        private const float AttackDp = 80f;
        private const float DashDp = 60f;
        private const float SkillDp = 52f;
        private const float SkillGapDp = 7f;
        private const float ChatDp = 44f;
        private const float EdgeDp = 16f;

        private static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        private static readonly Vector2 BottomCentre = new Vector2(0.5f, 0f);
        private static readonly Vector2 AttackCentreDp = new Vector2(-68f, 68f);
        private static readonly float SkillRowDp = EdgeDp + SkillDp / 2f;

        public static readonly HudButtonSpec[] Buttons =
        {
            new HudButtonSpec(HudButton.Attack, "Đánh", AttackDp, BottomRight, AttackCentreDp),
            new HudButtonSpec(HudButton.Dash, "Lướt", DashDp, BottomRight, AttackCentreDp + new Vector2(0f, 78f)),
            new HudButtonSpec(HudButton.Potion, "Thuốc", SkillDp, BottomRight, new Vector2(-150f, 50f)),
            Skill(HudButton.Ranged, "Xa", -2),
            Skill(HudButton.SkillA, "A", -1),
            Skill(HudButton.SkillB, "B", 0),
            Skill(HudButton.Heal, "Hồi", 1),
            Skill(HudButton.Ultimate, "Tuyệt", 2),
            new HudButtonSpec(HudButton.Chat, "Chat", ChatDp, Vector2.one, new Vector2(-EdgeDp - ChatDp / 2f, -EdgeDp - ChatDp / 2f)),
        };

        private static HudButtonSpec Skill(HudButton button, string label, int place)
        {
            return new HudButtonSpec(button, label, SkillDp, BottomCentre, new Vector2(place * (SkillDp + SkillGapDp), SkillRowDp), square: true);
        }

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
