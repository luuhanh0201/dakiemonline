using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace DaKiem.Client
{
    /// <summary>Turns touches into joystick tilt and dash presses.</summary>
    public sealed class InputReader
    {
        private const float JoystickRadiusDp = 64f;
        private const float BaselineDpi = 160f;
        private const float DeadZone = 0.1f;
        private const int NoFinger = -1;

        private int joystickFinger = NoFinger;
        private Vector2 joystickOrigin;
        private bool dashPressed;

        public InputReader()
        {
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

        private void BeginTouch(Touch touch)
        {
            bool onLeftHalf = touch.screenPosition.x < Screen.width / 2f;

            if (onLeftHalf && joystickFinger == NoFinger)
            {
                joystickFinger = touch.finger.index;
                joystickOrigin = touch.screenPosition;
            }
            else if (!onLeftHalf)
            {
                dashPressed = true;
            }
        }

        private void MoveTouch(Touch touch)
        {
            if (touch.finger.index != joystickFinger)
            {
                return;
            }

            Vector2 tilt = (touch.screenPosition - joystickOrigin) / JoystickRadiusPixels();
            MoveX = ApplyDeadZone(tilt.x);
            MoveY = ApplyDeadZone(tilt.y);
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
        }

        private static float JoystickRadiusPixels()
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : BaselineDpi;
            return JoystickRadiusDp * dpi / BaselineDpi;
        }

        private static double ApplyDeadZone(float tilt)
        {
            float clamped = Mathf.Clamp(tilt, -1f, 1f);
            return Mathf.Abs(clamped) < DeadZone ? 0d : clamped;
        }
    }
}
