using Game.Rules;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace DaKiem.Client
{
    /// <summary>Turns touches into joystick tilt and button presses.</summary>
    public sealed class InputReader
    {
        private const float DeadZone = 0.1f;
        private const int NoFinger = -1;

        private readonly HudView hud;
        private int joystickFinger = NoFinger;
        private Vector2 joystickOrigin;
        private bool dashPressed;
        private bool potionPressed;
        private SkillSlot? skillPressed;

        public InputReader(HudView hud)
        {
            this.hud = hud;
            EnhancedTouchSupport.Enable();
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
        }

        /// <summary>Returns true once per press, so a press between two ticks is not lost.</summary>
        public bool ConsumeDash()
        {
            bool pressed = dashPressed;
            dashPressed = false;
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

            hud.Flash(button);
            dashPressed |= button == HudButton.Dash;
            potionPressed |= button == HudButton.Potion;
            skillPressed = HudLayout.SlotOf(button) ?? skillPressed;
        }

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
