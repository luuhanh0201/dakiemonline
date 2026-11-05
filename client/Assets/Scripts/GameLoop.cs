using System.Collections;
using System.Collections.Generic;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Runs the rules at a fixed 30 ticks per second and shows the result.</summary>
    public sealed class GameLoop : MonoBehaviour
    {
        private const float MaxFrameSeconds = 0.25f;
        private const double StartX = 5d;

        private InputReader input;
        private HudView hud;
        private WorldView view;
        private GameConfig config;
        private string configError;
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
            var files = new Dictionary<string, string>();
            yield return ConfigFiles.LoadAll(files);

            try
            {
                config = GameConfig.Parse(files);
            }
            catch (InvalidConfigException error)
            {
                configError = error.Message;
                Debug.LogError(configError);
                yield break;
            }

            hud = new HudView();
            input = new InputReader(hud);
            map = new CollisionMap(config.MapBoxes);
            view = new WorldView(config.MapBoxes, config.Movement);
            state = new MovementState(StartX, 0d, config.Character.MaxStamina);
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
                Movement.Step(state, intent, config.Movement, map);
                previousFeet = currentFeet;
                currentFeet = ReadFeet();
                accumulator -= (float)Ticks.DeltaSeconds;
            }

            float blend = accumulator / (float)Ticks.DeltaSeconds;
            view.Show(Vector2.Lerp(previousFeet, currentFeet, blend), state.Facing);

            // Health and energy stay full until T014 adds them to the rules.
            hud.ShowResources(1f, 1f, (float)(state.Stamina / config.Character.MaxStamina));
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Screen.height / 24,
                normal = { textColor = Color.white },
            };

            if (configError != null)
            {
                GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), configError, style);
                return;
            }

            if (state == null)
            {
                return;
            }

            string text = $"x {state.X:0.00}  y {state.Y:0.00}  stamina {state.Stamina:0}";

            GUI.Label(new Rect(Screen.width * 0.3f, Screen.height / 8f, Screen.width, Screen.height / 8f), text, style);
        }

        private Vector2 ReadFeet()
        {
            return new Vector2((float)state.X, (float)state.Y);
        }
    }
}
