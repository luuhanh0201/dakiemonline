using Game.Rules;
using UnityEngine;
#if UNITY_STANDALONE
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
#if UNITY_STANDALONE
        private const double KeyboardWalkTilt = 0.3d;
#endif

        private readonly HudView hud;
        private int joystickFinger = NoFinger;
        private Vector2 joystickOrigin;
        private bool dashPressed;
        private bool potionPressed;
        private SkillSlot? skillPressed;
        private bool replayPressed;
#if UNITY_STANDALONE
        private GearSlot? gearPressed;
#endif

        public InputReader(HudView hud)
        {
            this.hud = hud;
            EnhancedTouchSupport.Enable();
#if UNITY_STANDALONE
            // Lets the mouse press the on-screen buttons and drag the joystick.
            TouchSimulation.Enable();
#endif
        }

        public double MoveX { get; private set; }
        public double MoveY { get; private set; }

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

#if UNITY_STANDALONE
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

#if UNITY_STANDALONE
        /// <summary>Returns the slot whose item should change, once per press of F1 to F5.</summary>
        public GearSlot? ConsumeGearChange()
        {
            GearSlot? pressed = gearPressed;
            gearPressed = null;
            return pressed;
        }
#endif

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

            bool onLeftHalf = touch.screenPosition.x < Screen.width / 2f;

            if (onLeftHalf)
            {
                if (joystickFinger == NoFinger)
                {
                    joystickFinger = touch.finger.index;
                    joystickOrigin = touch.screenPosition;
                    hud.ShowJoystick(true, joystickOrigin, Vector2.zero);
                }

                return;
            }

            HudButton button = hud.ButtonAt(touch.screenPosition);

            if (button == HudButton.None)
            {
                return;
            }

            PressButton(button);
        }

        private void PressButton(HudButton button)
        {
            hud.Flash(button);
            dashPressed |= button == HudButton.Dash;
            potionPressed |= button == HudButton.Potion;
            skillPressed = HudLayout.SlotOf(button) ?? skillPressed;
        }

#if UNITY_STANDALONE
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
