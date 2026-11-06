namespace DaKiem.Client
{
    /// <summary>Size of the character art. Shared with the importer in the editor assembly.</summary>
    public static class CharacterRigScale
    {
        /// <summary>The figure is drawn 802 layout pixels tall, and that height is one world unit.</summary>
        public const float LayoutPixelsPerUnit = 802f;

        /// <summary>Layout pixels are baked smaller for the phone by tools/bake-character.ps1.</summary>
        public const float BakeScale = 0.3f;

        public const float PixelsPerUnit = LayoutPixelsPerUnit * BakeScale;
    }

    public readonly struct CharacterBone
    {
        public CharacterBone(string name, string parent, string sprite, float jointX, float jointY, float centreX, float centreY, int order)
        {
            Name = name;
            Parent = parent;
            Sprite = sprite;
            JointX = jointX;
            JointY = jointY;
            CentreX = centreX;
            CentreY = centreY;
            Order = order;
        }

        public string Name { get; }

        /// <summary>Null for the bone attached to the feet point.</summary>
        public string Parent { get; }

        public string Sprite { get; }

        /// <summary>Where the bone turns, in layout pixels with Y pointing down.</summary>
        public float JointX { get; }
        public float JointY { get; }

        /// <summary>Middle of the sprite, in the same layout pixels.</summary>
        public float CentreX { get; }
        public float CentreY { get; }

        /// <summary>Draw order; higher is in front.</summary>
        public int Order { get; }
    }

    /// <summary>An arm or leg: one image running from the shoulder or hip to the wrist or ankle.</summary>
    public readonly struct CharacterLimb
    {
        public CharacterLimb(string name, string parent, string sprite, float rootX, float rootY, float upperLength, float lowerLength, int order)
        {
            Name = name;
            Parent = parent;
            Sprite = sprite;
            RootX = rootX;
            RootY = rootY;
            UpperLength = upperLength;
            LowerLength = lowerLength;
            Order = order;
        }

        public string Name { get; }
        public string Parent { get; }
        public string Sprite { get; }

        /// <summary>Shoulder or hip, in layout pixels with Y pointing down.</summary>
        public float RootX { get; }
        public float RootY { get; }

        public float UpperLength { get; }
        public float LowerLength { get; }
        public int Order { get; }
    }

    /// <summary>The character in side view facing right, measured from the assembled test pose.</summary>
    public static class CharacterRig
    {
        public const float FeetX = 310f;
        public const float FeetY = 808f;

        public const string Hip = "hip";
        public const string Torso = "torso";
        public const string Head = "head";
        public const string HairBack = "hair_back";
        public const string TailBase = "tail_base";
        public const string TailTip = "tail_tip";
        public const string HandNear = "hand_near";
        public const string HandFar = "hand_far";
        public const string FootNear = "foot_near";
        public const string FootFar = "foot_far";
        public const string ArmNear = "arm_near";
        public const string ArmFar = "arm_far";
        public const string LegNear = "leg_near";
        public const string LegFar = "leg_far";

        private const float UpperArm = 81f;
        private const float Forearm = 78f;
        private const float Thigh = 135f;
        private const float Shin = 120f;

        // Parents come before their children. Hands and feet are placed each frame.
        public static readonly CharacterBone[] Bones =
        {
            new CharacterBone(Hip, null, "shorts", 310f, 470f, 310f, 482f, 24),
            new CharacterBone(Torso, Hip, "torso", 310f, 440f, 310f, 323f, 18),
            new CharacterBone(Head, Torso, "head", 305f, 250f, 310f, 145f, 25),
            new CharacterBone("hair_front", Head, "hair_front", 305f, 250f, 300f, 127f, 26),
            new CharacterBone(HairBack, Head, "hair_back", 300f, 60f, 275f, 139f, 13),
            new CharacterBone(TailBase, Head, "tail_base", 240f, 175f, 215f, 194f, 12),
            new CharacterBone(TailTip, TailBase, "tail_tip", 205f, 180f, 170f, 192f, 11),
            new CharacterBone(HandFar, Torso, "hand_far", 330f, 520f, 335f, 561f, 8),
            new CharacterBone(FootFar, Hip, "foot", 320f, 735f, 345f, 768f, 17),
            new CharacterBone(FootNear, Hip, "foot", 300f, 735f, 325f, 768f, 23),
            new CharacterBone(HandNear, Torso, "hand_near", 285f, 520f, 295f, 561f, 29),
        };

        public static readonly CharacterLimb[] Limbs =
        {
            new CharacterLimb(ArmFar, Torso, "arm", 322f, 292f, UpperArm, Forearm, 9),
            new CharacterLimb(LegFar, Hip, "leg", 320f, 480f, Thigh, Shin, 16),
            new CharacterLimb(LegNear, Hip, "leg", 300f, 480f, Thigh, Shin, 22),
            new CharacterLimb(ArmNear, Torso, "arm", 300f, 292f, UpperArm, Forearm, 28),
        };
    }
}
