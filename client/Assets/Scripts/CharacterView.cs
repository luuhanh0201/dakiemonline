using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    public enum CharacterPose
    {
        Idle,
        Run,
        Walk,
        Rise,
        Fall,
        Dash,
        Windup,
        Strike,
        Windup2,
        Strike2,
        Windup3,
        Strike3,
        Hurt,
    }

    /// <summary>Where a cast is, for choosing what the arms do.</summary>
    public readonly struct SwingMoment
    {
        public SwingMoment(int comboStep, int hitIndex, int hitCount, float hitProgress)
        {
            ComboStep = comboStep;
            HitIndex = hitIndex;
            HitCount = hitCount;
            HitProgress = hitProgress;
        }

        public int ComboStep { get; }
        public int HitIndex { get; }
        public int HitCount { get; }
        public float HitProgress { get; }

        public static SwingMoment Of(CombatState combat)
        {
            return new SwingMoment(combat.ComboStep, combat.ActiveHitIndex, combat.ActiveHitCount, (float)combat.ActiveHitProgress);
        }
    }

    /// <summary>
    /// Draws the character from jointed parts and continuous limbs. Each frame builds the pose the
    /// body should reach, then every body part eases toward it at its own speed: legs fastest,
    /// hair slowest. That lag is what gives the motion weight.
    /// </summary>
    public sealed class CharacterView
    {
        private const string SpriteFolder = "Character/Base/";

        // Pose channels. Legs are stored as the ankle's place relative to the hip.
        private const int HipDrop = 0;
        private const int NearAnkleX = 1;
        private const int NearAnkleY = 2;
        private const int NearFoot = 3;
        private const int FarAnkleX = 4;
        private const int FarAnkleY = 5;
        private const int FarFoot = 6;
        private const int NearShoulder = 7;
        private const int NearElbow = 8;
        private const int FarShoulder = 9;
        private const int FarElbow = 10;
        private const int TorsoTurn = 11;
        private const int HeadTurn = 12;
        private const int HairTurn = 13;
        private const int TailBaseTurn = 14;
        private const int TailTipTurn = 15;
        private const int CapeUpper = 16;
        private const int CapeLower = 17;
        private const int WeaponTurn = 18;
        private const int CapeRipple = 19;
        private const int AttackHipDrop = 20;
        private const int AttackNearAnkle = 21;
        private const int AttackFarAnkle = 22;
        private const int AttackStep = 23;
        private const int BodyYaw = 24;
        private const int WeaponRoll = 25;
        private const int ChannelCount = 26;

        // How fast each channel closes on its target, per second.
        private const float LegEase = 45f;
        private const float HipEase = 30f;
        private const float ArmEase = 20f;
        private const float TorsoEase = 14f;
        private const float HeadEase = 11f;
        private const float HairEase = 8f;
        private const float TailBaseEase = 9f;
        private const float TailTipEase = 6f;
        private const float CapeUpperEase = 8f;
        private const float CapeLowerEase = 5f;
        private const float WeaponEase = 24f;
        private const float CapeRippleEase = 4f;
        private const float AttackBodyEase = 32f;

        // Arms and body snap into an attack or a hit far faster than they settle afterwards.
        private const float ActionEaseBoost = 3f;

        // After a blow lands the swing carries on for a moment, then the pose holds briefly.
        private const float ImpactDelay = 0.05f;
        private const float ImpactHold = 0.05f;
        private const float FinisherHold = 0.1f;

        // Row of the heaviest blow in AttackStances.
        private const int FinisherStance = 2;

        // Each hit of a multi-hit skill strikes first, then spends the rest of its time drawing
        // back for the next one. The blow itself and its hold take about 0.1 seconds, so a longer
        // share would leave the blade standing still between hits.
        private const float StrikeShareOfHit = 0.4f;

        private const float TrailOpacity = 0.75f;
        private const float FinisherTrailOpacity = 1f;

        // A foot travels StepReach either side of the hip while on the ground. The cycle advances
        // from these so the planted foot moves back exactly as fast as the body moves forward.
        private const float StepReach = 0.18f;
        private const float StanceShare = 0.24f;

        private const float MinRunSpeed = 0.2f;
        private const float RunBlendSpeed = 9f;
        private const float RiseSpeed = 6f;

        private const float SwingPitch = -22f;
        private const float RunTwist = 3f;
        private const float HeadCounterLean = 6f;
        private const float ArmSwing = 38f;
        private const float ArmBack = -12f;
        private const float RunHair = -6f;
        private const float TailLift = -18f;
        private const float TailSwing = 9f;

        private const float BreathsPerSecond = 0.35f;
        private const float BreathTurn = 1.4f;
        private const float BreathDrop = 0.005f;
        private const float RestElbow = 10f;
        private const float RestArm = 2f;

        // A temporary single image until the layered front-view parts exist.
        private const string FrontSprite = "front_idle";
        private const int FrontOrder = 20;
        private const float FrontHeightUnits = 1f;

        // Relaxed guard: hand in front of the lower chest, blade angled away from the face.
        // Weapon angles are from straight up, positive toward the back.
        private const float CarryShoulder = 50f;
        private const float CarryElbow = 40f;
        private const float CarrySwing = 3f;
        private const float CarryWeapon = -30f;
        private const float DashWeapon = 100f;

        private const float CapeRestUpper = -6f;
        private const float CapeRestLower = -2f;
        private const float CapeRunUpper = -40f;
        private const float CapeRunLower = -68f;
        private const float CapeDashUpper = -72f;
        private const float CapeDashLower = -96f;
        private const float CapeFallUpper = -70f;
        private const float CapeFallLower = -140f;
        private const float CapeRestRipple = 2.5f;
        private const float CapeRunRipple = 16f;
        private const float CapeAirRipple = 12f;
        private const float CapeRipplesPerSecondAtRest = 0.5f;
        private const float CapeRipplesPerSecondAtRun = 2.4f;
        private const float FrontShowsBelowRun = 0.15f;

        private const float FrontBreathsPerSecond = 0.42f;

        // Turning has no in-between drawings, so the front figure bends toward the side view and
        // the two swap at the middle of the turn.
        private const float TurnSpeed = 4.5f;
        private const float TurnDip = 0.03f;
        private const float TurnSwapPoint = 0.5f;
        private const float FlipDip = 0.03f;

        // Parts on the far side of the body are darker, which separates them from the near side.
        private const float FarShade = 0.82f;

        private const float ShadowOpacity = 0.4f;
        private const float ShadowWidth = 0.8f;
        private const float ShadowHeight = 0.16f;
        private const float ShadowLift = 0.01f;
        private const float ShadowFadeHeight = 2.5f;
        private const int ShadowOrder = 5;
        private const int ShadowTextureSize = 64;

        private const float LandingSquash = 0.075f;
        private const float LandingRecovery = 9f;
        private const float StraightLimbMargin = 0.001f;

        private static readonly Color HurtTint = new Color32(0xFF, 0x8A, 0x8A, 0xFF);
        private static readonly Color WindupTint = new Color32(0xC8, 0xDC, 0xFF, 0xFF);

        // Knee drawn up in front, back leg trailing, arms driving upward.
        private static readonly float[] RisePose =
            Pose(-0.01f, 0.09f, -0.14f, -20f, -0.09f, -0.25f, -42f, 34f, 48f, -46f, 30f, -5f, 2f, 7f, 16f, 10f);

        // Legs reach for the ground, arms lift for balance, hair streams upward.
        private static readonly float[] FallPose =
            Pose(0f, 0.08f, -0.23f, 8f, -0.05f, -0.19f, -14f, 62f, 24f, -58f, 22f, 4f, -3f, -9f, -26f, -14f);

        // Low lunge: front leg reaching, back leg driving, arms and hair thrown behind.
        private static readonly float[] DashPose =
            Pose(0.075f, 0.2f, -0.2f, 6f, -0.23f, -0.19f, -46f, -66f, 24f, -82f, 18f, -30f, 16f, -14f, -58f, -22f);

        // One row per combo hit and cast phase: windup, active, recovery. Columns are near shoulder,
        // near elbow, far shoulder, far elbow, torso, head, weapon angle, hip drop, near and far
        // ankle reach, and how far the whole body steps. Each blow winds up the opposite way first.
        private static readonly float[][][] AttackStances =
        {
            // A chop down from overhead.
            new[]
            {
                new[] { 195f, 35f, -20f, 35f, 14f, -3f, 35f, 0.05f, 0.02f, -0.06f, -0.03f },
                new[] { 65f, 5f, -60f, 20f, -24f, 8f, -110f, 0.09f, 0.15f, -0.17f, 0.07f },
                new[] { 45f, 15f, -35f, 20f, -14f, 5f, -80f, 0.07f, 0.13f, -0.15f, 0.06f },
            },
            // A rising cut, from a crouch up onto the toes.
            new[]
            {
                new[] { 35f, 20f, -30f, 30f, -20f, 8f, -150f, 0.1f, 0.12f, -0.12f, 0f },
                new[] { 110f, 5f, -60f, 25f, 10f, -6f, -35f, 0.01f, 0.1f, -0.1f, 0.06f },
                new[] { 95f, 15f, -40f, 20f, 6f, -3f, -30f, 0.03f, 0.08f, -0.08f, 0.04f },
            },
            // The finisher: a full coil, a long step, and it stays down after the blow.
            new[]
            {
                new[] { 205f, 45f, -5f, 45f, 22f, -6f, 60f, 0.08f, 0.03f, -0.1f, -0.07f },
                new[] { 55f, 0f, -90f, 10f, -34f, 10f, -125f, 0.13f, 0.22f, -0.22f, 0.15f },
                new[] { 40f, 10f, -70f, 15f, -26f, 8f, -100f, 0.12f, 0.21f, -0.21f, 0.14f },
            },
        };

        private static readonly float[] Ease = BuildEase();

        private readonly Transform root;
        private readonly Transform rig;
        private readonly FrontFigure front;
        private readonly FrontFigure frontGear;
        private readonly EquipmentView equipment;
        private readonly WeaponTrail trail = new WeaponTrail();
        private readonly Transform shadow;
        private readonly SpriteRenderer shadowRenderer;
        private readonly Material partMaterial;
        private SkinnedShorts shorts;
        private SpriteRenderer torsoRenderer;
        private float nearThigh;
        private float farThigh;
        private float actionBoost = 1f;
        private float capeRipplePhase;
        private int stanceRow;
        private CastPhase stanceStage;
        private float impactDelay;
        private float impactHold;
        private float pendingHold;
        private CastPhase previousActionStage;
        private int previousActionRow;
        private float actionAge;
        private float returnAge = 1f;
        private float armClearance;
        private float armReach;
        private float wristClearance;
        private float poseDeltaTime = 1f / 60f;
        private float frontAmount;
        private float frontAlpha;
        private float sideAlpha = 1f;
        private float groundY;
        private int shownFacing = 1;
        private readonly Dictionary<string, Transform> joints = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Vector3> restPositions = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, LimbStrip> limbs = new Dictionary<string, LimbStrip>();
        private readonly Dictionary<string, LimbShape> shapes = new Dictionary<string, LimbShape>();
        private readonly List<ShadedPart> parts = new List<ShadedPart>();
        private readonly float[] current = new float[ChannelCount];
        private readonly float[] target = new float[ChannelCount];
        private readonly float standingLegLength;
        private float runCycle;
        private bool hasPosition;
        private float lastVerticalSpeed;
        private float gait;
        private float Stance => Mathf.Lerp(0.56f, StanceShare, gait);
        private float Reach => Mathf.Lerp(0.17f, StepReach, gait);
        private float runAmount;
        private float landing;
        private float lastX;
        private bool wasGrounded = true;

        public CharacterView()
        {
            partMaterial = new Material(Shader.Find(LimbStrip.ShaderName));
            root = new GameObject("Character").transform;
            rig = new GameObject("Rig").transform;
            rig.SetParent(root, false);

            Sprite frontImage = Resources.Load<Sprite>(SpriteFolder + FrontSprite);
            float frontWidth = FrontHeightUnits * frontImage.texture.width / frontImage.texture.height;
            front = new FrontFigure("FrontFigure", root, frontWidth, FrontHeightUnits, Vector2.one);
            front.AddLayer("body", frontImage.texture, FrontOrder);
            frontGear = new FrontFigure("FrontGear", root, frontWidth, FrontHeightUnits, new Vector2(
                EquipmentView.FrontCanvasWidth / (float)frontImage.texture.width,
                EquipmentView.FrontCanvasHeight / (float)frontImage.texture.height));

            shadow = new GameObject("Shadow").transform;
            shadowRenderer = shadow.gameObject.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = CreateShadowSprite();
            shadowRenderer.sortingOrder = ShadowOrder;

            foreach (CharacterBone bone in CharacterRig.Bones)
            {
                CreateBone(bone);
            }

            foreach (CharacterLimb limb in CharacterRig.Limbs)
            {
                CreateLimb(limb);
            }

            equipment = new EquipmentView(
                joints[CharacterRig.Head],
                joints[CharacterRig.Torso],
                joints[CharacterRig.HandNear],
                torsoRenderer,
                limbs[CharacterRig.ArmNear],
                limbs[CharacterRig.ArmFar],
                limbs[CharacterRig.LegNear],
                limbs[CharacterRig.LegFar],
                shorts,
                frontGear);

            LimbShape leg = shapes[CharacterRig.LegNear];
            standingLegLength = leg.Upper + leg.Lower;
            BuildGroundPose();
            BuildCape(true, 0f, false);
            stanceStage = CastPhase.None;
            BuildAction(false, false, true);
            target.CopyTo(current, 0);
        }

        /// <summary>Holds the pose for a moment once a blow has landed, longer for a finisher.</summary>
        public void ShowImpact(bool finisher)
        {
            impactDelay = ImpactDelay;
            pendingHold = finisher ? FinisherHold : ImpactHold;
        }

        /// <summary>Holds the pose for a blow of a chain; true when it was the chain's heaviest.</summary>
        public bool ReactToHit(in CombatEvent hit, SkillConfig skill, int comboStep)
        {
            int hitCount = skill.ActivePhases.Count;

            if (skill.Combo == null && hitCount <= 1)
            {
                return false;
            }

            bool finisher = hitCount > 1
                ? hit.HitIndex == hitCount - 1
                : comboStep == skill.Combo.HitDamagePercents.Count - 1;

            ShowImpact(finisher);
            return finisher;
        }

        private bool ImpactHolding(float deltaTime)
        {
            if (impactDelay > 0f)
            {
                impactDelay -= deltaTime;
                if (impactDelay <= 0f) impactHold = pendingHold;
                return false;
            }

            impactHold -= deltaTime;
            return impactHold > 0f;
        }

        public void WearNext(GearSlot slot)
        {
            equipment.WearNext(slot);
        }

        public void Wear(GearSlot slot, int index)
        {
            equipment.Wear(slot, index);
        }

        public void Show(Vector2 feet, int facing, bool grounded, float verticalSpeed, bool dashing, CastPhase phase, bool hurt, float frameSeconds = -1f, SwingMoment swing = default)
        {
            float deltaTime = Mathf.Max(frameSeconds < 0f ? Time.deltaTime : frameSeconds, 0.0001f);
            float moved = hasPosition ? Mathf.Abs(feet.x - lastX) : 0f;
            hasPosition = true;
            if (moved > 1f) moved = 0f;
            bool running = grounded && !dashing && moved / deltaTime > MinRunSpeed;

            if (grounded && !wasGrounded)
            {
                landing = LandingSquash * Mathf.Clamp01(Mathf.Abs(lastVerticalSpeed) / 10f);
            }

            wasGrounded = grounded;
            lastX = feet.x;
            lastVerticalSpeed = verticalSpeed;
            if (running)
            {
                gait = Mathf.MoveTowards(gait, Mathf.InverseLerp(2f, 4f, moved / deltaTime), 6f * deltaTime);
                runCycle = Mathf.Repeat(runCycle + moved / (2f * Reach / Stance), 1f);
            }
            runAmount = Mathf.MoveTowards(runAmount, running ? 1f : 0f, RunBlendSpeed * deltaTime);
            landing *= Mathf.Exp(-LandingRecovery * deltaTime);

            root.position = new Vector3(feet.x, feet.y, 0f);

            BuildTarget(grounded, verticalSpeed, dashing);
            ChooseStance(phase, swing);
            if (stanceStage != previousActionStage || stanceRow != previousActionRow)
            {
                actionAge = 0f;
                if (stanceStage == CastPhase.None && previousActionStage != CastPhase.None) returnAge = 0f;
                previousActionStage = stanceStage;
                previousActionRow = stanceRow;
            }
            actionAge += deltaTime;
            returnAge += deltaTime;
            BuildAction(hurt, dashing, grounded);
            BuildCape(grounded, verticalSpeed, dashing);
            capeRipplePhase += Mathf.Lerp(CapeRipplesPerSecondAtRest, CapeRipplesPerSecondAtRun, runAmount) * Mathf.PI * 2f * deltaTime;
            EaseToTarget(ImpactHolding(deltaTime) ? 0f : deltaTime);
            if (grounded && !dashing)
            {
                // Hip lag must never pull a planted foot outside the IK reach.
                float maxReach = standingLegLength - StraightLimbMargin;
                float nearDepth = Mathf.Sqrt(Mathf.Max(0f, maxReach * maxReach - target[NearAnkleX] * target[NearAnkleX]));
                float farDepth = Mathf.Sqrt(Mathf.Max(0f, maxReach * maxReach - target[FarAnkleX] * target[FarAnkleX]));
                current[HipDrop] = Mathf.Max(current[HipDrop],
                    Mathf.Max(target[HipDrop] - target[NearAnkleY] - nearDepth,
                              target[HipDrop] - target[FarAnkleY] - farDepth));
                current[NearAnkleX] = target[NearAnkleX];
                current[FarAnkleX] = target[FarAnkleX];
                current[NearAnkleY] = target[NearAnkleY] + current[HipDrop] - target[HipDrop];
                current[FarAnkleY] = target[FarAnkleY] + current[HipDrop] - target[HipDrop];
                current[NearFoot] = target[NearFoot];
                current[FarFoot] = target[FarFoot];
            }
            Apply();
            trail.Show(
                stanceStage == CastPhase.Active,
                equipment.GripPoint,
                equipment.BladeInnerPoint,
                equipment.BladeOuterPoint,
                (stanceRow == 1 ? 1 : -1) * facing,
                stanceRow == FinisherStance ? FinisherTrailOpacity : TrailOpacity,
                deltaTime);
            bool standing = !equipment.HasWeapon && grounded && !dashing && !running && phase == CastPhase.None && !hurt && returnAge >= 0.2f && runAmount < FrontShowsBelowRun;

            frontAmount = Mathf.MoveTowards(frontAmount, standing ? 1f : 0f, TurnSpeed * deltaTime);

            if (facing != shownFacing)
            {
                shownFacing = facing;
                landing = Mathf.Max(landing, FlipDip);
            }

            if (grounded)
            {
                groundY = feet.y;
            }

            ShowTurn();
            ShowShadow(feet.x, feet.y - groundY);
            Tint(hurt ? HurtTint : phase == CastPhase.Windup ? WindupTint : Color.white);
        }

        /// <summary>
        /// Standing still faces the camera and any action turns side-on. The front figure bends
        /// toward the side and the two views swap halfway, with a small dip through the middle.
        /// </summary>
        private void ShowTurn()
        {
            frontAlpha = frontAmount >= TurnSwapPoint ? 1f : 0f;
            sideAlpha = 1f - frontAlpha;

            root.localScale = new Vector3(shownFacing, 1f, 1f);
            rig.gameObject.SetActive(sideAlpha > 0f);
            front.SetVisible(frontAlpha > 0f);
            frontGear.SetVisible(frontAlpha > 0f);

            if (frontAlpha > 0f)
            {
                float breath = 0.5f + 0.5f * Mathf.Sin(Time.time * FrontBreathsPerSecond * Mathf.PI * 2f);
                float dip = Mathf.Sin(frontAmount * Mathf.PI) * TurnDip;

                front.Pose(breath, 1f - frontAmount, shownFacing, dip);
                frontGear.Pose(breath, 1f - frontAmount, shownFacing, dip);
            }
        }

        private void ShowShadow(float x, float height)
        {
            float closeness = 1f - Mathf.Clamp01(height / ShadowFadeHeight);
            float size = Mathf.Lerp(0.6f, 1f, closeness);

            shadow.position = new Vector3(x, groundY + ShadowLift, 0f);
            shadow.localScale = new Vector3(ShadowWidth * size, ShadowHeight * size, 1f);
            shadowRenderer.color = new Color(0f, 0f, 0f, ShadowOpacity * Mathf.Lerp(0.4f, 1f, closeness));
        }

        private static Sprite CreateShadowSprite()
        {
            var texture = new Texture2D(ShadowTextureSize, ShadowTextureSize, TextureFormat.RGBA32, false);
            float middle = (ShadowTextureSize - 1) / 2f;

            for (int y = 0; y < ShadowTextureSize; y++)
            {
                for (int x = 0; x < ShadowTextureSize; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(middle, middle)) / middle;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(1f, 0f, distance)));
                }
            }

            texture.Apply();
            var area = new Rect(0f, 0f, ShadowTextureSize, ShadowTextureSize);
            return Sprite.Create(texture, area, new Vector2(0.5f, 0.5f), ShadowTextureSize);
        }

        /// <summary>Holds a pose exactly, for the pose sheet tool.</summary>
        public void Preview(CharacterPose pose, float momentInCycle)
        {
            runAmount = pose == CharacterPose.Run || pose == CharacterPose.Walk ? 1f : 0f;
            gait = pose == CharacterPose.Walk ? 0f : 1f;
            runCycle = momentInCycle;

            switch (pose)
            {
                case CharacterPose.Rise:
                    RisePose.CopyTo(target, 0);
                    OffsetAirAnkles();
                    break;
                case CharacterPose.Fall:
                    FallPose.CopyTo(target, 0);
                    OffsetAirAnkles();
                    break;
                case CharacterPose.Dash:
                    DashPose.CopyTo(target, 0);
                    OffsetAirAnkles();
                    break;
                default:
                    BuildGroundPose();
                    break;
            }

            bool inAir = pose == CharacterPose.Rise || pose == CharacterPose.Fall;
            bool isWindup = pose == CharacterPose.Windup || pose == CharacterPose.Windup2 || pose == CharacterPose.Windup3;
            bool isStrike = pose == CharacterPose.Strike || pose == CharacterPose.Strike2 || pose == CharacterPose.Strike3;
            stanceStage = isWindup ? CastPhase.Windup : isStrike ? CastPhase.Active : CastPhase.None;
            stanceRow = pose == CharacterPose.Windup3 || pose == CharacterPose.Strike3 ? FinisherStance
                : pose == CharacterPose.Windup2 || pose == CharacterPose.Strike2 ? 1
                : 0;
            BuildAction(pose == CharacterPose.Hurt, pose == CharacterPose.Dash, !inAir);
            BuildCape(!inAir, pose == CharacterPose.Fall ? -RiseSpeed : RiseSpeed, pose == CharacterPose.Dash);
            target.CopyTo(current, 0);
            Apply();
            actionBoost = 1f;
            frontAmount = pose == CharacterPose.Idle && !equipment.HasWeapon ? 1f : 0f;
            ShowTurn();
            ShowShadow(0f, pose == CharacterPose.Rise || pose == CharacterPose.Fall ? 1f : 0f);
            Tint(Color.white);
        }

        /// <summary>Sets the facing used by Preview, for the review tools.</summary>
        public void PreviewFacing(int facing)
        {
            shownFacing = facing;
        }

        /// <summary>Holds a moment of the turn, for the pose sheet tool. 1 is facing front.</summary>
        public void PreviewTurn(float amount)
        {
            Preview(CharacterPose.Idle, 0f);
            frontAmount = amount;
            ShowTurn();
            Tint(Color.white);
        }

        private void BuildTarget(bool grounded, float verticalSpeed, bool dashing)
        {
            if (dashing)
            {
                DashPose.CopyTo(target, 0);
                    OffsetAirAnkles();
            }
            else if (!grounded)
            {
                float fall = Mathf.InverseLerp(RiseSpeed, -RiseSpeed, verticalSpeed);

                for (int channel = 0; channel < RisePose.Length; channel++)
                {
                    target[channel] = Mathf.Lerp(RisePose[channel], FallPose[channel], fall);
                    if (channel == NearAnkleY || channel == FarAnkleY) target[channel] -= 40f / CharacterRigScale.LayoutPixelsPerUnit;
                }
            }
            else
            {
                BuildGroundPose();
            }
        }

        /// <summary>Attacks and hits take over the arms and body; the legs keep doing what they were.</summary>
        /// <summary>
        /// A single blow uses its combo row as it stands. A multi-hit skill alternates the first
        /// two rows and ends on the finisher, with the strike of each row timed to its hit.
        /// </summary>
        private void ChooseStance(CastPhase phase, in SwingMoment swing)
        {
            stanceStage = phase;

            if (swing.HitCount <= 1)
            {
                stanceRow = swing.ComboStep % AttackStances.Length;
                return;
            }

            int lastHit = swing.HitCount - 1;

            if (phase == CastPhase.Windup)
            {
                stanceRow = 0;
            }
            else if (phase == CastPhase.Recovery)
            {
                // Rising here leaves the character ready by the time control comes back.
                stanceStage = CastPhase.None;
            }
            else if (phase == CastPhase.Active)
            {
                bool striking = swing.HitProgress < StrikeShareOfHit;

                if (swing.HitIndex == lastHit)
                {
                    stanceRow = FinisherStance;
                    stanceStage = striking ? CastPhase.Active : CastPhase.Recovery;
                    return;
                }

                int hit = striking ? swing.HitIndex : swing.HitIndex + 1;

                stanceRow = hit == lastHit ? FinisherStance : hit % 2;
                stanceStage = striking ? CastPhase.Active : CastPhase.Windup;
            }
        }

        private void BuildAction(bool hurt, bool dashing, bool grounded)
        {
            CastPhase phase = stanceStage;
            actionBoost = hurt ? ActionEaseBoost : phase == CastPhase.Active ? 3.5f
                : phase == CastPhase.Windup ? 1.8f : phase == CastPhase.Recovery ? 1.4f : 1f;
            target[AttackHipDrop] = 0f;
            target[AttackNearAnkle] = 0f;
            target[AttackFarAnkle] = 0f;
            target[AttackStep] = 0f;
            target[BodyYaw] = 0f;
            // The rising cut leads with the opposite edge orientation. Roll during
            // preparation and retain it through the follow-through of that same hit.
            target[WeaponRoll] = phase != CastPhase.None && stanceRow == 1 ? 180f : 0f;

            if (phase != CastPhase.None)
            {
                float[] stance = AttackStances[stanceRow][(int)phase - 1];
                SetUpperBody(stance[0], stance[1], stance[2], stance[3], stance[4], stance[5]);
                target[WeaponTurn] = stance[6];
                target[AttackStep] = stance[10];
                // Hips lead, shoulders unwind next, and the weapon follows the torso.
                float coil = stanceRow == 1 ? 0.8f : stanceRow == FinisherStance ? -1f : -0.75f;
                target[BodyYaw] = phase == CastPhase.Windup ? coil
                    : phase == CastPhase.Active ? -coil : -coil * 0.55f;

                // Running legs keep their stride; only a body on the ground can brace into a blow.
                float planted = grounded ? 1f - runAmount : 0f;
                target[AttackHipDrop] = stance[7] * planted;
                target[AttackNearAnkle] = stance[8] * planted;
                target[AttackFarAnkle] = stance[9] * planted;
                return;
            }

            // Lower the resting/running guard toward the waist; blend through the
            // existing walking pose as movement starts and changes speed.
            float relaxedGuard = grounded && !hurt ? 1f - runAmount * (1f - gait) : 0f;
            float guardShoulder = CarryShoulder - 15f * relaxedGuard;
            float guardElbow = CarryElbow - 10f * relaxedGuard;
            float guardWeapon = CarryWeapon - 10f * relaxedGuard;
            target[WeaponTurn] = dashing ? DashWeapon : guardWeapon;
            if (returnAge < 0.18f && !dashing && !hurt)
            {
                // Bring the hand forward and lift into guard without sweeping behind the head.
                target[NearShoulder] = Mathf.Lerp(35f, guardShoulder, returnAge / 0.18f);
                target[NearElbow] = guardElbow;
                target[WeaponTurn] = Mathf.Lerp(-80f, guardWeapon, returnAge / 0.18f);
                return;
            }

            if (hurt)
            {
                SetUpperBody(-30f, 35f, 25f, 30f, 14f, 10f);
            }
            else if (!dashing)
            {
                target[NearShoulder] = guardShoulder + CarrySwing * Mathf.Cos(runCycle * Mathf.PI * 2f) * runAmount;
                target[NearElbow] = guardElbow;
            }
        }

        private void SetUpperBody(float nearShoulder, float nearElbow, float farShoulder, float farElbow, float torso, float head)
        {
            target[NearShoulder] = nearShoulder;
            target[NearElbow] = nearElbow;
            target[FarShoulder] = farShoulder;
            target[FarElbow] = farElbow;
            target[TorsoTurn] = torso;
            target[HeadTurn] = head;
        }

        private void BuildCape(bool grounded, float verticalSpeed, bool dashing)
        {
            float upper;
            float lower;
            float ripple = CapeAirRipple;

            if (dashing)
            {
                upper = CapeDashUpper;
                lower = CapeDashLower;
            }
            else if (!grounded)
            {
                float fall = Mathf.InverseLerp(0f, -RiseSpeed, verticalSpeed);
                upper = Mathf.Lerp(CapeRunUpper / 2f, CapeFallUpper, fall);
                lower = Mathf.Lerp(CapeRunLower / 2f, CapeFallLower, fall);
            }
            else
            {
                float speed = runAmount * Mathf.Lerp(0.45f, 1f, gait);
                upper = Mathf.Lerp(CapeRestUpper, CapeRunUpper, speed);
                lower = Mathf.Lerp(CapeRestLower, CapeRunLower, speed);
                ripple = Mathf.Lerp(CapeRestRipple, CapeRunRipple, speed);
            }

            target[CapeUpper] = upper;
            target[CapeLower] = lower;
            target[CapeRipple] = ripple;
        }

        private void OffsetAirAnkles()
        {
            target[NearAnkleY] -= 40f / CharacterRigScale.LayoutPixelsPerUnit;
            target[FarAnkleY] -= 40f / CharacterRigScale.LayoutPixelsPerUnit;
        }

        private void BuildGroundPose()
        {
            float cycle = Mathf.Repeat(runCycle, 1f);
            float breath = Mathf.Sin(Time.time * BreathsPerSecond * Mathf.PI * 2f);
            float rest = 1f - runAmount;

            // The body is lowest in the middle of each stance and highest in the flight between steps.
            float bounce = -Mathf.Cos((cycle - Stance / 2f) * Mathf.PI * 4f);
            float swing = Mathf.Cos(cycle * Mathf.PI * 2f);
            // Contact, weight acceptance, push-off, flight, next contact.
            float step = Mathf.Repeat(cycle, 0.5f);
            float runDrop = step < 0.08f ? SmoothBetween(0.075f, 0.095f, step / 0.08f)
                : step < 0.24f ? SmoothBetween(0.095f, 0.065f, (step - 0.08f) / 0.16f)
                : step < 0.36f ? SmoothBetween(0.065f, 0.03f, (step - 0.24f) / 0.12f)
                : SmoothBetween(0.03f, 0.075f, (step - 0.36f) / 0.14f);
            // Walking accepts weight after contact, then rises over the supporting leg.
            // Both feet overlap in support; there is no flight phase.
            float walkDrop = step < 0.07f ? SmoothBetween(0.055f, 0.065f, step / 0.07f)
                : step < 0.30f ? SmoothBetween(0.065f, 0.026f, (step - 0.07f) / 0.23f)
                : SmoothBetween(0.026f, 0.055f, (step - 0.30f) / 0.20f);
            float drop = Mathf.Lerp(walkDrop, runDrop, gait) * runAmount
                + (breath + 1f) * BreathDrop * rest + landing;

            target[HipDrop] = drop;

            Vector2 nearAnkle = Vector2.Lerp(new Vector2(0f, -standingLegLength), RunningAnkle(cycle), runAmount);
            Vector2 farAnkle = Vector2.Lerp(new Vector2(0f, -standingLegLength), RunningAnkle(Mathf.Repeat(cycle + 0.5f, 1f)), runAmount);

            target[NearAnkleX] = nearAnkle.x;
            target[NearAnkleY] = nearAnkle.y + drop;
            target[NearFoot] = FootPitch(cycle) * runAmount;
            target[FarAnkleX] = farAnkle.x;
            target[FarAnkleY] = farAnkle.y + drop;
            target[FarFoot] = FootPitch(Mathf.Repeat(cycle + 0.5f, 1f)) * runAmount;

            // Each arm swings against the leg on its own side.
            target[NearShoulder] = (Mathf.Lerp(-3f, ArmBack, gait) - Mathf.Lerp(16f, ArmSwing, gait) * swing) * runAmount + (RestArm + BreathTurn * breath) * rest;
            target[FarShoulder] = (Mathf.Lerp(-3f, ArmBack, gait) + Mathf.Lerp(16f, ArmSwing, gait) * swing) * runAmount - (RestArm + BreathTurn * breath) * rest;
            target[NearElbow] = (Mathf.Lerp(12f, 82f, gait) - Mathf.Lerp(4f, 10f, gait) * swing) * runAmount + RestElbow * rest;
            target[FarElbow] = (Mathf.Lerp(12f, 82f, gait) + Mathf.Lerp(4f, 10f, gait) * swing) * runAmount + RestElbow * rest;

            float bodyTwist = Mathf.Lerp(1.2f, RunTwist, gait) * swing;
            target[TorsoTurn] = (Mathf.Lerp(-2f, -12f, gait) + bodyTwist) * runAmount + BreathTurn * breath * rest;
            target[HeadTurn] = (Mathf.Lerp(2f, HeadCounterLean, gait) - bodyTwist) * runAmount - BreathTurn * breath * rest;
            target[HairTurn] = Mathf.Lerp(-2f, RunHair, gait) * runAmount + BreathTurn * breath * rest;
            target[TailBaseTurn] = (Mathf.Lerp(-5f, TailLift, gait) + Mathf.Lerp(3f, TailSwing, gait) * bounce) * runAmount + BreathTurn * 2f * breath * rest;
            target[TailTipTurn] = Mathf.Lerp(3f, TailSwing, gait) * Mathf.Sin(cycle * Mathf.PI * 4f - 0.8f) * runAmount + BreathTurn * 2f * breath * rest;
        }

        private void EaseToTarget(float deltaTime)
        {
            poseDeltaTime = deltaTime;
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                bool isBody = (channel >= NearShoulder && channel <= HeadTurn) || channel == WeaponTurn;
                float closing = 1f - Mathf.Exp(-Ease[channel] * (isBody ? actionBoost : 1f) * deltaTime);
                if (stanceStage != CastPhase.None)
                {
                    // Fast weight shift, then chest/shoulder rotation, then the striking hand.
                    float lead = channel == BodyYaw ? 0.012f : channel == TorsoTurn ? 0.018f
                        : channel == NearShoulder || channel == NearElbow || channel == WeaponTurn ? 0.032f : 0f;
                    closing *= Mathf.Clamp01((actionAge - lead) / 0.025f);
                }
                if (channel == WeaponTurn)
                {
                    float turn = Mathf.DeltaAngle(current[channel], target[channel]);
                    if (stanceStage == CastPhase.Active && Mathf.Abs(turn) > 0.1f)
                    {
                        // A heavy chop spans more than 180 degrees. Shortest-angle
                        // interpolation would send it backwards, through the back swing.
                        if (stanceRow == 1 && turn < 0f) turn += 360f;
                        else if (stanceRow != 1 && turn > 0f) turn -= 360f;
                    }
                    current[channel] += turn * closing;
                }
                else current[channel] = Mathf.Lerp(current[channel], target[channel], closing);
            }
        }

        private void Apply()
        {
            armReach *= Mathf.Exp(-18f * poseDeltaTime);
            // Keep the wrist correction through the cut; returning it during the
            // strike could make the tip reverse even while the arm keeps moving.
            if (stanceStage != CastPhase.Active) wristClearance *= Mathf.Exp(-12f * poseDeltaTime);
            rig.localPosition = Vector3.right * current[AttackStep];
            joints[CharacterRig.Hip].localPosition = restPositions[CharacterRig.Hip] + Vector3.down * (current[HipDrop] + current[AttackHipDrop]);

            // The ankles rise with a lowered hip, so the feet stay on the ground.
            float braced = current[AttackHipDrop];
            PoseLeg(CharacterRig.LegNear, CharacterRig.FootNear, new Vector2(current[NearAnkleX] + current[AttackNearAnkle], current[NearAnkleY] + braced), current[NearFoot]);
            PoseLeg(CharacterRig.LegFar, CharacterRig.FootFar, new Vector2(current[FarAnkleX] + current[AttackFarAnkle], current[FarAnkleY] + braced), current[FarFoot]);
            shorts.Pose(shapes[CharacterRig.LegNear].Root, nearThigh, shapes[CharacterRig.LegFar].Root, farThigh);
            PoseArm(CharacterRig.ArmNear, CharacterRig.HandNear, current[NearShoulder], current[NearElbow]);
            PoseArm(CharacterRig.ArmFar, CharacterRig.HandFar, current[FarShoulder], current[FarElbow]);

            Turn(CharacterRig.Torso, current[TorsoTurn]);
            Turn(CharacterRig.Head, current[HeadTurn]);
            Turn(CharacterRig.HairBack, current[HairTurn]);
            Turn(CharacterRig.TailBase, current[TailBaseTurn]);
            Turn(CharacterRig.TailTip, current[TailTipTurn]);
            equipment.TwistBody(current[BodyYaw]);
            equipment.RollWeapon(current[WeaponRoll]);

            // The cape hangs from the torso but falls with gravity, so the torso's lean is taken out.
            equipment.PoseCape(current[CapeUpper] - current[TorsoTurn], current[CapeLower] - current[TorsoTurn], current[CapeRipple], capeRipplePhase);
            float shoulder = current[NearShoulder];
            float elbow = current[NearElbow];
            // Retain the previous correction and release it gradually. Searching near
            // this pose avoids swapping between two sides of the head on adjacent frames.
            float preferred = armClearance * Mathf.Exp(-18f * poseDeltaTime);
            if (!TryClearArm(shoulder, elbow, preferred))
            {
                // A broad hat can extend beyond the arm's entire circular reach.
                // Let the shoulder reach across the torso silhouette, with
                // the sleeve still attached, instead of overturning the blade.
                bool clear = false;
                float initialReach = armReach;
                for (float offset = 0.01f; offset <= 0.1201f && !clear; offset += 0.01f)
                {
                    for (int side = 1; side >= -1 && !clear; side -= 2)
                    {
                        float reach = initialReach + offset * side;
                        if (Mathf.Abs(reach) > 0.0601f) continue;
                        armReach = reach;
                        clear = TryClearArm(shoulder, elbow, preferred);
                    }
                }
                if (!clear)
                {
                    float oldWrist = wristClearance;
                    armReach = initialReach;
                    for (int offset = 4; offset <= 48 && !clear; offset += 4)
                    for (int side = -1; side <= 1 && !clear; side += 2)
                    {
                        // Only a small wrist adjustment is permitted. During a cut
                        // it must continue in the cut's direction, never reverse it.
                        if (stanceStage == CastPhase.Active && side != (stanceRow == 1 ? 1 : -1)) continue;
                        float wrist = oldWrist + offset * side;
                        if (Mathf.Abs(wrist) > 24f) continue;
                        wristClearance = wrist;
                        clear = TryClearArm(shoulder, elbow, preferred);
                    }
                    if (!clear)
                    {
                        wristClearance = oldWrist;
                        armClearance = 0f;
                        armReach = 0f;
                        PoseArm(CharacterRig.ArmNear, CharacterRig.HandNear, shoulder, elbow);
                        equipment.TurnWeapon(current[WeaponTurn] + wristClearance - shoulder - elbow);
                    }
                }
            }
        }

        private bool TryClearArm(float shoulder, float elbow, float preferred)
        {
            for (int offset = 0; offset <= 180; offset += 4)
            for (int side = -1; side <= 1; side += 2)
            {
                float correction = Mathf.DeltaAngle(0f, preferred + offset * side);
                float guided = shoulder + correction;
                PoseArm(CharacterRig.ArmNear, CharacterRig.HandNear, guided, elbow);
                if (equipment.TurnWeapon(current[WeaponTurn] + wristClearance - guided - elbow))
                {
                    armClearance = correction;
                    return true;
                }
            }
            return false;
        }

        private void PoseLeg(string leg, string foot, Vector2 ankle, float footPitch)
        {
            LimbShape shape = shapes[leg];
            float reach = Mathf.Clamp(ankle.magnitude, Mathf.Abs(shape.Upper - shape.Lower) + StraightLimbMargin, shape.Upper + shape.Lower - StraightLimbMargin);
            float hipOpen = Mathf.Acos(Mathf.Clamp(
                (shape.Upper * shape.Upper + reach * reach - shape.Lower * shape.Lower) / (2f * shape.Upper * reach),
                -1f,
                1f));

            // Angles are measured from straight down, positive toward the front; the knee points forward.
            float thighAngle = Mathf.Atan2(ankle.x, -ankle.y) + hipOpen;
            if (leg == CharacterRig.LegNear) nearThigh = thighAngle;
            else farThigh = thighAngle;
            Vector2 knee = Hanging(thighAngle) * shape.Upper;
            Vector2 ankleReached = ankle.normalized * reach;

            limbs[leg].Bend(shape.Root, shape.Root + knee, shape.Root + ankleReached);
            joints[foot].localPosition = shape.Root + ankleReached;
            Turn(foot, footPitch);
        }

        private void PoseArm(string arm, string hand, float shoulderDegrees, float elbowDegrees)
        {
            LimbShape shape = shapes[arm];
            float upperAngle = shoulderDegrees * Mathf.Deg2Rad;
            float lowerAngle = upperAngle + elbowDegrees * Mathf.Deg2Rad;
            // The near/far shoulder separates as the chest opens and closes; the head stays aimed.
            Vector2 shoulder = shape.Root + Vector2.right * current[BodyYaw] * (arm == CharacterRig.ArmNear ? -0.028f : 0.028f);
            if (arm == CharacterRig.ArmNear) shoulder.x += armReach;
            Vector2 elbow = shoulder + Hanging(upperAngle) * shape.Upper;
            Vector2 wrist = elbow + Hanging(lowerAngle) * shape.Lower;

            limbs[arm].Bend(shoulder, elbow, wrist);
            joints[hand].localPosition = wrist;
            Turn(hand, shoulderDegrees + elbowDegrees);
        }

        private static Vector2 Hanging(float angle)
        {
            return new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle));
        }

        /// <summary>Where the ankle should be, relative to the hip, at a point in the running cycle.</summary>
        private Vector2 RunningAnkle(float cycle)
        {
            if (cycle < Stance)
            {
                return new Vector2(Reach * (1f - 2f * cycle / Stance), -standingLegLength);
            }

            float swing = (cycle - Stance) / (1f - Stance);
            float across = Mathf.SmoothStep(0f, 1f, swing);
            Vector2 walk = new Vector2(Reach * (2f * across - 1f), Mathf.Sin(swing * Mathf.PI) * 0.025f);
            // Recovery heel rises behind the body before the knee leads forward.
            Vector2 run = swing < 0.25f
                ? Vector2.Lerp(new Vector2(-Reach, 0f), new Vector2(-Reach * 0.8f, 0.12f), Mathf.SmoothStep(0f, 1f, swing / 0.25f))
                : swing < 0.55f
                ? Vector2.Lerp(new Vector2(-Reach * 0.8f, 0.12f), new Vector2(0.04f, 0.16f), Mathf.SmoothStep(0f, 1f, (swing - 0.25f) / 0.3f))
                : Vector2.Lerp(new Vector2(0.04f, 0.16f), new Vector2(Reach, 0f), Mathf.SmoothStep(0f, 1f, (swing - 0.55f) / 0.45f));
            Vector2 ankle = Vector2.Lerp(walk, run, gait);
            ankle.y -= standingLegLength;
            return ankle;
        }

        private static float SmoothBetween(float a, float b, float t) => Mathf.Lerp(a, b, Mathf.SmoothStep(0f, 1f, t));

        private float FootPitch(float cycle)
        {
            if (cycle < Stance)
            {
                return 0f;
            }

            float swing = (cycle - Stance) / (1f - Stance);
            float pitch = Mathf.Lerp(-8f, SwingPitch, gait);
            return swing < 0.5f
                ? Mathf.Lerp(0f, pitch, swing * 2f)
                : Mathf.Lerp(pitch, 0f, (swing - 0.5f) * 2f);
        }

        private static float[] Pose(
            float hipDrop,
            float nearX,
            float nearY,
            float nearFoot,
            float farX,
            float farY,
            float farFoot,
            float nearShoulder,
            float nearElbow,
            float farShoulder,
            float farElbow,
            float torso,
            float head,
            float hair,
            float tailBase,
            float tailTip)
        {
            return new[]
            {
                hipDrop, nearX, nearY, nearFoot, farX, farY, farFoot, nearShoulder,
                nearElbow, farShoulder, farElbow, torso, head, hair, tailBase, tailTip,
            };
        }

        private static float[] BuildEase()
        {
            return new[]
            {
                HipEase, LegEase, LegEase, LegEase, LegEase, LegEase, LegEase, ArmEase,
                ArmEase, ArmEase, ArmEase, TorsoEase, HeadEase, HairEase, TailBaseEase, TailTipEase,
                CapeUpperEase, CapeLowerEase, WeaponEase, CapeRippleEase,
                AttackBodyEase, AttackBodyEase, AttackBodyEase, AttackBodyEase,
                22f, 42f,
            };
        }

        private void Turn(string joint, float degrees)
        {
            joints[joint].localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        private void Tint(Color color)
        {
            front.Tint(color, frontAlpha);
            frontGear.Tint(color, frontAlpha);
            shorts.Tint(color, sideAlpha);
            equipment.Tint(color, sideAlpha);

            foreach (ShadedPart part in parts)
            {
                part.Renderer.color = new Color(color.r * part.Shade, color.g * part.Shade, color.b * part.Shade, sideAlpha);
            }

            foreach (LimbStrip limb in limbs.Values)
            {
                limb.Tint(color, sideAlpha);
            }
        }

        private void CreateBone(in CharacterBone bone)
        {
            var joint = new GameObject(bone.Name).transform;
            bool isRoot = bone.Parent == null;

            joint.SetParent(isRoot ? rig : joints[bone.Parent], false);
            joint.localPosition = isRoot
                ? ToLocal(bone.JointX - CharacterRig.FeetX, bone.JointY - CharacterRig.FeetY)
                : ToLocal(bone.JointX - FindBone(bone.Parent).JointX, bone.JointY - FindBone(bone.Parent).JointY);

            joints[bone.Name] = joint;
            restPositions[bone.Name] = joint.localPosition;

            if (bone.Name == CharacterRig.Hip)
            {
                shorts = new SkinnedShorts(Resources.Load<Sprite>(SpriteFolder + bone.Sprite), joint,
                    ToLocal(bone.CentreX - bone.JointX, bone.CentreY - bone.JointY), bone.Order);
                return;
            }

            var part = new GameObject(bone.Sprite);
            part.transform.SetParent(joint, false);
            part.transform.localPosition = ToLocal(bone.CentreX - bone.JointX, bone.CentreY - bone.JointY);

            var renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = Resources.Load<Sprite>(SpriteFolder + bone.Sprite);
            renderer.sharedMaterial = partMaterial;
            renderer.sortingOrder = bone.Order;
            parts.Add(new ShadedPart(renderer, ShadeOf(bone.Name)));

            if (bone.Name == CharacterRig.Torso)
            {
                torsoRenderer = renderer;
            }
        }

        private void CreateLimb(in CharacterLimb limb)
        {
            CharacterBone parent = FindBone(limb.Parent);
            Sprite sprite = Resources.Load<Sprite>(SpriteFolder + limb.Sprite);

            limbs[limb.Name] = new LimbStrip(sprite, joints[limb.Parent], limb.Order, limb.Name, ShadeOf(limb.Name));
            shapes[limb.Name] = new LimbShape(
                ToLocal(limb.RootX - parent.JointX, limb.RootY - parent.JointY),
                limb.UpperLength / CharacterRigScale.LayoutPixelsPerUnit,
                limb.LowerLength / CharacterRigScale.LayoutPixelsPerUnit);
        }

        private static float ShadeOf(string name)
        {
            return name.EndsWith("_far") ? FarShade : 1f;
        }

        private static CharacterBone FindBone(string name) => CharacterRig.Find(name);

        private static Vector3 ToLocal(float layoutX, float layoutY) => CharacterRig.Local(layoutX, layoutY);

        private readonly struct ShadedPart
        {
            public ShadedPart(SpriteRenderer renderer, float shade)
            {
                Renderer = renderer;
                Shade = shade;
            }

            public SpriteRenderer Renderer { get; }
            public float Shade { get; }
        }

        private readonly struct LimbShape
        {
            public LimbShape(Vector2 root, float upper, float lower)
            {
                Root = root;
                Upper = upper;
                Lower = lower;
            }

            public Vector2 Root { get; }
            public float Upper { get; }
            public float Lower { get; }
        }
    }
}
