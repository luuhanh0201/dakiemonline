using System.Collections;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Runs the rules at a fixed 30 ticks per second and shows the result.</summary>
    public sealed class GameLoop : MonoBehaviour
    {
        private const float MaxFrameSeconds = 0.25f;
        private const double StartX = 5d;
        private const double StartStamina = 100d;

        private InputReader input;
        private WorldView view;
        private MovementConfig movementConfig;
        private CollisionMap map;
        private MovementState state;
        private Vector2 previousFeet;
        private Vector2 currentFeet;
        private float accumulator;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            new GameObject(nameof(GameLoop)).AddComponent<GameLoop>();
        }

        private IEnumerator Start()
        {
            MapBox[] boxes = null;

            yield return ConfigFiles.Load("movement.json", json => movementConfig = ConfigFiles.ParseMovement(json));
            yield return ConfigFiles.Load("map.test.json", json => boxes = ConfigFiles.ParseMap(json));

            input = new InputReader();
            map = new CollisionMap(boxes);
            view = new WorldView(boxes, movementConfig);
            state = new MovementState(StartX, 0d, StartStamina);
            previousFeet = currentFeet = ReadFeet();
        }

        private void Update()
        {
            if (state == null)
            {
                return;
            }

            input.Update();
            accumulator += Mathf.Min(Time.unscaledDeltaTime, MaxFrameSeconds);

            while (accumulator >= Ticks.DeltaSeconds)
            {
                var intent = new MovementIntent(input.MoveX, input.MoveY, input.ConsumeDash());
                Movement.Step(state, intent, movementConfig, map);
                previousFeet = currentFeet;
                currentFeet = ReadFeet();
                accumulator -= (float)Ticks.DeltaSeconds;
            }

            float blend = accumulator / (float)Ticks.DeltaSeconds;
            view.Show(Vector2.Lerp(previousFeet, currentFeet, blend), state.Facing);
        }

        private void OnGUI()
        {
            if (state == null)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Screen.height / 24,
                normal = { textColor = Color.white },
            };
            string text = $"x {state.X:0.00}  y {state.Y:0.00}  stamina {state.Stamina:0}";

            GUI.Label(new Rect(20f, Screen.height / 8f, Screen.width, Screen.height / 8f), text, style);
        }

        private Vector2 ReadFeet()
        {
            return new Vector2((float)state.X, (float)state.Y);
        }
    }
}
