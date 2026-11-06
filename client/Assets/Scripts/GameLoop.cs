using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Game.Rules;
using UnityEngine;

namespace DaKiem.Client
{
    /// <summary>Runs the rules at a fixed 30 ticks per second and shows the result.</summary>
    public sealed class GameLoop : MonoBehaviour
    {
        private const float MaxFrameSeconds = 0.25f;
        private const double StartX = 5d;
        private const float SystemBarCheckSeconds = 0.5f;

        private InputReader input;
        private HudView hud;
        private WorldView view;
        private CharacterView character;
        private BossView bossView;
        private FxView fx;
        private GameConfig config;
        private string configError;
        private Simulation fight;
        private Vector2 previousFeet;
        private Vector2 currentFeet;
        private float previousBossX;
        private float currentBossX;
        private float accumulator;
        private float nextSystemBarCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            new GameObject(nameof(GameLoop)).AddComponent<GameLoop>();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                AndroidImmersive.Apply();
            }
        }

        private IEnumerator Start()
        {
            AndroidImmersive.Apply();

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
            view = new WorldView(config.MapBoxes);
            character = new CharacterView();
            bossView = new BossView();
            fx = new FxView(view);
            StartFight();
        }

        private void StartFight()
        {
            fight = new Simulation(config, StartX);
            previousFeet = currentFeet = ReadFeet();
            previousBossX = currentBossX = (float)fight.Boss.X;
            hud.HideEndScreen();
            FpsOverlay.ResetLowestFps();
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextSystemBarCheck)
            {
                nextSystemBarCheck = Time.unscaledTime + SystemBarCheckSeconds;
                AndroidImmersive.Apply();
            }

            if (fight == null)
            {
                return;
            }

            input.Update();

            if (input.ConsumeReplay())
            {
                StartFight();
            }

            accumulator += Mathf.Min(Time.unscaledDeltaTime, MaxFrameSeconds);

            while (accumulator >= Ticks.DeltaSeconds)
            {
                var intent = new MovementIntent(input.MoveX, input.MoveY, input.ConsumeDash());
                fight.Step(intent, input.ConsumeCombat());
                previousFeet = currentFeet;
                currentFeet = ReadFeet();
                previousBossX = currentBossX;
                currentBossX = (float)fight.Boss.X;
                accumulator -= (float)Ticks.DeltaSeconds;
            }

            ShowWorld();
            ShowHud();
            fight.ClearEvents();
        }

        private void ShowWorld()
        {
            float blend = accumulator / (float)Ticks.DeltaSeconds;

            fx.Show(fight, config.Boss);
            ShowImpacts();
            Vector2 feet = Vector2.Lerp(previousFeet, currentFeet, blend);

            view.Follow(feet, fight.Player.Facing);
#if UNITY_STANDALONE
            GearSlot? changed = input.ConsumeGearChange();
            if (changed.HasValue) character.WearNext(changed.Value);
#endif
            character.Show(
                feet,
                fight.Player.Facing,
                fight.Player.Grounded,
                (float)fight.Player.VelocityY,
                fight.Player.IsDashing,
                fight.Combat.Phase,
                fx.PlayerHurt,
                swing: SwingMoment.Of(fight.Combat));
            bossView.Show(Mathf.Lerp(previousBossX, currentBossX, blend), fight);
        }

        /// <summary>Only the picture reacts to a landed blow: the fight itself never pauses.</summary>
        private void ShowImpacts()
        {
            foreach (CombatEvent item in fight.CombatEvents)
            {
                if (item.Kind == CombatEventKind.SkillHit
                    && item.Slot != null
                    && character.ReactToHit(item, config.Skill(item.Slot.Value), fight.Combat.ComboStep))
                {
                    view.Shake();
                }
            }
        }

        private void ShowHud()
        {
            CharacterConfig character = config.Character;

            hud.ShowResources(
                (float)fight.Resources.Hp / character.MaxHp,
                (float)(fight.Resources.Energy / character.MaxEnergy),
                (float)(fight.Player.Stamina / character.MaxStamina));
            hud.ShowBossHealth((float)fight.Boss.Hp / config.Boss.MaxHp);
            ShowButtons();

            if (fight.Result != MatchResult.Playing && !hud.EndScreenVisible)
            {
                hud.ShowEndScreen(fight.Result == MatchResult.Won ? "Thắng" : "Thua", EndSummary());
                MatchLog.Write(fight, FpsOverlay.LowestFps);
            }
        }

        private string EndSummary()
        {
            string seconds = fight.SecondsPlayed.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
            string summary = $"Thời gian: {seconds} giây\nSố đòn bị trúng: {fight.HitsTaken}\nThuốc đã dùng: {fight.PotionsUsed}";

            return fight.KilledBy == null ? summary : $"{summary}\nĐòn khiến thua: {fight.KilledBy.Name}";
        }

        private void ShowButtons()
        {
            CombatState combat = fight.Combat;

            foreach (HudButtonSpec spec in HudLayout.Buttons)
            {
                SkillSlot? slot = HudLayout.SlotOf(spec.Button);

                if (slot != null)
                {
                    double left = combat.CooldownSecondsLeft(slot.Value);
                    bool ready = Combat.IsSkillReady(combat, fight.Resources, config.Skill(slot.Value));
                    hud.SetButton(spec.Button, left > 0d ? WholeSeconds(left) : spec.Label, !ready);
                }
                else if (spec.Button == HudButton.Potion)
                {
                    double left = combat.PotionCooldownSecondsLeft;
                    string label = left > 0d ? WholeSeconds(left) : $"{spec.Label}\n{combat.PotionsLeft}";
                    hud.SetButton(spec.Button, label, !Combat.IsPotionReady(combat));
                }
            }
        }

        private static string WholeSeconds(double seconds)
        {
            return Mathf.CeilToInt((float)seconds).ToString();
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

            if (fight == null)
            {
                return;
            }

            MovementState player = fight.Player;
            string text = $"x {player.X:0.00}  y {player.Y:0.00}  stamina {player.Stamina:0}";

            GUI.Label(new Rect(Screen.width * 0.3f, Screen.height / 6f, Screen.width, Screen.height / 8f), text, style);
        }

        private Vector2 ReadFeet()
        {
            return new Vector2((float)fight.Player.X, (float)fight.Player.Y);
        }
    }
}
