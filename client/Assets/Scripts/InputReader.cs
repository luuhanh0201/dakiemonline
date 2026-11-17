using Game.Rules;
using UnityEngine;
#if UNITY_STANDALONE || UNITY_WEBGL
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace DaKiem.Client
{
    /// <summary>Turns touches, and on the PC build the keyboard, into joystick tilt and button presses.</summary>
    public sealed class InputReader
    {
        private const float DeadZone = 0.1f;
        private const int NoFinger = -1;
#if UNITY_STANDALONE || UNITY_WEBGL
        private const double KeyboardWalkTilt = 0.3d;
#endif

        private readonly HudView hud;
        private int joystickFinger = NoFinger;
        private Vector2 joystickOrigin;
        private bool dashPressed;
        private bool potionPressed;
        private SkillSlot? skillPressed;
        private bool replayPressed;
        private bool portraitTouched;

        // A touch on the world, and whether it was the second of a quick pair on the same spot.
        private const float DoubleTouchSeconds = 0.35f;
        private const float DoubleTouchDp = 30f;
        private bool worldTouched;
        private bool worldTouchedTwice;
        private Vector2 worldTouch;
        private float lastWorldTouchTime = -10f;
        private Vector2 lastWorldTouch;

        // A skill square touched: it chooses the skill the attack button casts (decided 14/11/2026).
        private SkillSlot? squareTouched;
#if UNITY_STANDALONE || UNITY_WEBGL
        private GearSlot? gearPressed;
        private bool slashStylePressed;
        private bool bossHoldPressed;
        private bool freeCastingPressed;
#endif

        public InputReader(HudView hud)
        {
            this.hud = hud;
            EnhancedTouchSupport.Enable();
#if UNITY_STANDALONE || UNITY_WEBGL
            // Lets the mouse press the on-screen buttons and drag the joystick. A phone browser has real
            // touches and also fakes mouse clicks from them, which would press every button twice.
            if (!Application.isMobilePlatform)
            {
                TouchSimulation.Enable();
            }
#endif
        }

        public double MoveX { get; private set; }
        public double MoveY { get; private set; }

        public bool WorldInputBlocked { get; set; }

        public void Update()
        {
            foreach (Touch touch in Touch.activeTouches)
            {
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        BeginTouch(touch);
                        break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        MoveTouch(touch);
                        break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        EndTouch(touch);
                        break;
                }
            }

#if UNITY_STANDALONE || UNITY_WEBGL
            ReadKeyboard();
#endif
        }

        /// <summary>Returns true once per press, so a press between two ticks is not lost.</summary>
        public bool ConsumeDash()
        {
            bool pressed = dashPressed;
            dashPressed = false;
            return pressed;
        }

#if UNITY_STANDALONE || UNITY_WEBGL
        /// <summary>True once per press of F6, which turns cooldowns and energy costs off or on.</summary>
        public bool ConsumeFreeCastingChange()
        {
            bool pressed = freeCastingPressed;
            freeCastingPressed = false;
            return pressed;
        }

        /// <summary>True once per press of F5, which stops or restarts the boss for trying skills.</summary>
        public bool ConsumeBossHoldChange()
        {
            bool pressed = bossHoldPressed;
            bossHoldPressed = false;
            return pressed;
        }

        /// <summary>True once per press of F4, which compares the two sets of sword effects.</summary>
        public bool ConsumeSlashStyleChange()
        {
            bool pressed = slashStylePressed;
            slashStylePressed = false;
            return pressed;
        }

        /// <summary>Returns the slot whose item should change, once per press of F1 to F3.</summary>
        public GearSlot? ConsumeGearChange()
        {
            GearSlot? pressed = gearPressed;
            gearPressed = null;
            return pressed;
        }
#endif

        /// <summary>
        /// A touch on the world since the last call, once: where it was and whether it was the second of a
        /// quick pair on the same spot.
        /// </summary>
        public bool ConsumeWorldTouch(out Vector2 screen, out bool twice)
        {
            screen = worldTouch;
            twice = worldTouchedTwice;
            bool touched = worldTouched;
            worldTouched = false;
            return touched;
        }

        /// <summary>The skill whose square was touched since the last call, once; null when none was.</summary>
        public SkillSlot? ConsumeSquareTouched()
        {
            SkillSlot? slot = squareTouched;
            squareTouched = null;
            return slot;
        }

        /// <summary>Whether the portrait was touched since the last call, once: it opens the main menu.</summary>
        public bool ConsumePortrait()
        {
            bool touched = portraitTouched;
            portraitTouched = false;
            return touched;
        }

        public bool ConsumeReplay()
        {
            bool pressed = replayPressed;
            replayPressed = false;
            return pressed;
        }

        public CombatIntent ConsumeCombat()
        {
            var intent = new CombatIntent(skillPressed, potionPressed);
            skillPressed = null;
            potionPressed = false;
            return intent;
        }

        private void BeginTouch(Touch touch)
        {
            if (hud.EndScreenVisible)
            {
                replayPressed |= hud.ReplayButtonAt(touch.screenPosition);
                return;
            }

            if (hud.PortraitAt(touch.screenPosition))
            {
                portraitTouched = true;
                return;
            }

            // An NPC's words close at any touch.
            if (hud.DialogVisible)
            {
                hud.HideDialog();
                return;
            }

            if (WorldInputBlocked)
            {
                return;
            }

            // Buttons reach across the screen now that the skills sit in the bottom centre.
            HudButton button = hud.ButtonAt(touch.screenPosition);

            if (button != HudButton.None)
            {
                // A skill square only chooses; the attack button casts what is chosen.
                SkillSlot? square = button == HudButton.Attack ? null : HudLayout.SlotOf(button);

                if (square != null)
                {
                    hud.Flash(button);
                    squareTouched = square;
                }
                else
                {
                    PressButton(button);
                }

                return;
            }

            // The joystick is fixed: only a touch on or near its ring takes it, and it pushes from the centre.
            Vector2 rest = HudView.JoystickRestScreen;
            float reach = HudLayout.JoystickDiameterDp / 2f * HudLayout.JoystickReach * HudLayout.DpToPixels;

            if (Vector2.Distance(touch.screenPosition, rest) <= reach)
            {
                if (joystickFinger == NoFinger)
                {
                    joystickFinger = touch.finger.index;
                    joystickOrigin = rest;
                    MoveTouch(touch);
                }

                return;
            }

            // Anywhere else is the world: a dummy, an NPC, or empty ground.
            float near = DoubleTouchDp * HudLayout.DpToPixels;
            worldTouchedTwice = Time.unscaledTime - lastWorldTouchTime <= DoubleTouchSeconds
                && Vector2.Distance(touch.screenPosition, lastWorldTouch) <= near;
            worldTouched = true;
            worldTouch = touch.screenPosition;
            lastWorldTouch = touch.screenPosition;
            lastWorldTouchTime = worldTouchedTwice ? -10f : Time.unscaledTime;
        }


        private void PressButton(HudButton button)
        {
            hud.Flash(button);
            dashPressed |= button == HudButton.Dash;
            potionPressed |= button == HudButton.Potion;
            skillPressed = HudLayout.SlotOf(button) ?? skillPressed;
        }

#if UNITY_STANDALONE || UNITY_WEBGL
        private void ReadKeyboard()
        {
            Keyboard keys = Keyboard.current;

            if (keys == null)
            {
                return;
            }

            if (joystickFinger == NoFinger)
            {
                double way = (keys.dKey.isPressed || keys.rightArrowKey.isPressed ? 1d : 0d)
                    - (keys.aKey.isPressed || keys.leftArrowKey.isPressed ? 1d : 0d);

                MoveX = keys.leftCtrlKey.isPressed ? way * KeyboardWalkTilt : way;
                MoveY = keys.wKey.isPressed || keys.upArrowKey.isPressed || keys.spaceKey.isPressed ? 1d : 0d;
            }

            if (keys.f1Key.wasPressedThisFrame) gearPressed = GearSlot.Weapon;
            if (keys.f2Key.wasPressedThisFrame) gearPressed = GearSlot.Armor;
            if (keys.f3Key.wasPressedThisFrame) gearPressed = GearSlot.Pants;
            slashStylePressed |= keys.f4Key.wasPressedThisFrame;
            bossHoldPressed |= keys.f5Key.wasPressedThisFrame;
            freeCastingPressed |= keys.f6Key.wasPressedThisFrame;

            if (hud.EndScreenVisible)
            {
                replayPressed |= keys.enterKey.wasPressedThisFrame;
                return;
            }

            PressOnKey(keys.jKey, HudButton.Attack);
            PressOnKey(keys.lKey, HudButton.Dash);
            PressOnKey(keys.kKey, HudButton.Ranged);
            PressOnKey(keys.uKey, HudButton.SkillA);
            PressOnKey(keys.iKey, HudButton.SkillB);
            PressOnKey(keys.oKey, HudButton.Heal);
            PressOnKey(keys.pKey, HudButton.Ultimate);
            PressOnKey(keys.hKey, HudButton.Potion);
        }

        private void PressOnKey(KeyControl key, HudButton button)
        {
            if (key.wasPressedThisFrame)
            {
                PressButton(button);
            }
        }
#endif

        private void MoveTouch(Touch touch)
        {
            if (touch.finger.index != joystickFinger)
            {
                return;
            }

            float radius = HudLayout.JoystickDiameterDp / 2f * HudLayout.DpToPixels;
            Vector2 offset = Vector2.ClampMagnitude(touch.screenPosition - joystickOrigin, radius);
            Vector2 tilt = (touch.screenPosition - joystickOrigin) / radius;

            MoveX = ApplyDeadZone(tilt.x);
            MoveY = ApplyDeadZone(tilt.y);
            hud.ShowJoystick(true, joystickOrigin, offset);
        }

        private void EndTouch(Touch touch)
        {
            if (touch.finger.index != joystickFinger)
            {
                return;
            }

            joystickFinger = NoFinger;
            MoveX = 0d;
            MoveY = 0d;
            hud.ShowJoystick(false, Vector2.zero, Vector2.zero);
        }

        private static double ApplyDeadZone(float tilt)
        {
            float clamped = Mathf.Clamp(tilt, -1f, 1f);
            return Mathf.Abs(clamped) < DeadZone ? 0d : clamped;
        }
    }
}
