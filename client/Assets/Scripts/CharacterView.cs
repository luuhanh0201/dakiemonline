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
        private const int ChannelCount = 16;

        // How fast each channel closes on its target, per second.
        private const float LegEase = 45f;
        private const float HipEase = 30f;
        private const float ArmEase = 20f;
        private const float TorsoEase = 14f;
        private const float HeadEase = 11f;
        private const float HairEase = 8f;
        private const float TailBaseEase = 9f;
        private const float TailTipEase = 6f;

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

        private static readonly float[] Ease = BuildEase();

        private readonly Transform root;
        private readonly Transform rig;
        private readonly FrontFigure front;
        private readonly Transform shadow;
        private readonly SpriteRenderer shadowRenderer;
        private readonly Material partMaterial;
        private SkinnedShorts shorts;
        private float nearThigh;
        private float farThigh;
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

            front = new FrontFigure(Resources.Load<Sprite>(SpriteFolder + FrontSprite), root, FrontOrder, FrontHeightUnits);

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

            LimbShape leg = shapes[CharacterRig.LegNear];
            standingLegLength = leg.Upper + leg.Lower;
            BuildGroundPose();
            target.CopyTo(current, 0);
        }

        public void Show(Vector2 feet, int facing, bool grounded, float verticalSpeed, bool dashing, CastPhase phase, bool hurt, float frameSeconds = -1f)
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
            EaseToTarget(deltaTime);
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
            bool standing = grounded && !dashing && !running && phase == CastPhase.None && runAmount < FrontShowsBelowRun;

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

            if (frontAlpha > 0f)
            {
                float breath = 0.5f + 0.5f * Mathf.Sin(Time.time * FrontBreathsPerSecond * Mathf.PI * 2f);
                float dip = Mathf.Sin(frontAmount * Mathf.PI) * TurnDip;

                front.Pose(breath, 1f - frontAmount, shownFacing, dip);
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

            target.CopyTo(current, 0);
            Apply();
            frontAmount = pose == CharacterPose.Idle ? 1f : 0f;
            ShowTurn();
            ShowShadow(0f, pose == CharacterPose.Rise || pose == CharacterPose.Fall ? 1f : 0f);
            Tint(Color.white);
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

                for (int channel = 0; channel < ChannelCount; channel++)
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
            for (int channel = 0; channel < ChannelCount; channel++)
            {
                float closing = 1f - Mathf.Exp(-Ease[channel] * deltaTime);
                current[channel] = Mathf.Lerp(current[channel], target[channel], closing);
            }
        }

        private void Apply()
        {
            joints[CharacterRig.Hip].localPosition = restPositions[CharacterRig.Hip] + Vector3.down * current[HipDrop];

            PoseLeg(CharacterRig.LegNear, CharacterRig.FootNear, new Vector2(current[NearAnkleX], current[NearAnkleY]), current[NearFoot]);
            PoseLeg(CharacterRig.LegFar, CharacterRig.FootFar, new Vector2(current[FarAnkleX], current[FarAnkleY]), current[FarFoot]);
            shorts.Pose(shapes[CharacterRig.LegNear].Root, nearThigh, shapes[CharacterRig.LegFar].Root, farThigh);
            PoseArm(CharacterRig.ArmNear, CharacterRig.HandNear, current[NearShoulder], current[NearElbow]);
            PoseArm(CharacterRig.ArmFar, CharacterRig.HandFar, current[FarShoulder], current[FarElbow]);

            Turn(CharacterRig.Torso, current[TorsoTurn]);
            Turn(CharacterRig.Head, current[HeadTurn]);
            Turn(CharacterRig.HairBack, current[HairTurn]);
            Turn(CharacterRig.TailBase, current[TailBaseTurn]);
            Turn(CharacterRig.TailTip, current[TailTipTurn]);
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
            Vector2 elbow = shape.Root + Hanging(upperAngle) * shape.Upper;
            Vector2 wrist = elbow + Hanging(lowerAngle) * shape.Lower;

            limbs[arm].Bend(shape.Root, elbow, wrist);
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
            };
        }

        private void Turn(string joint, float degrees)
        {
            joints[joint].localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        private void Tint(Color color)
        {
            front.Tint(color, frontAlpha);
            shorts.Tint(color, sideAlpha);

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

        private static CharacterBone FindBone(string name)
        {
            foreach (CharacterBone candidate in CharacterRig.Bones)
            {
                if (candidate.Name == name)
                {
                    return candidate;
                }
            }

            throw new KeyNotFoundException(name);
        }

        private static Vector3 ToLocal(float layoutX, float layoutY)
        {
            return new Vector3(layoutX, -layoutY, 0f) / CharacterRigScale.LayoutPixelsPerUnit;
        }

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
