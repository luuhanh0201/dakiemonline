using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DaKiem.Client.Net;
using Game.Net;
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
        private string serverAddress;
        private string menuError;
        private OnlineClient online;
        private OnlineView onlineView;
        private bool refillPressed;

        private const string ServerAddressKey = "server.address";
        private const string ServerAddressFile = "server-address.txt";

#if UNITY_STANDALONE
        // The PC build is for trying things out, so the boss starts held; F5 lets it fight.
        private bool bossHeld = true;

        // F6 lets every skill be cast again at once, for looking at effects.
        private bool freeCasting;
#else
        private const bool bossHeld = false;
        private const bool freeCasting = false;
#endif

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
            hud.ShowBossBar(false);
            serverAddress = PlayerPrefs.GetString(ServerAddressKey, DefaultServerAddress());

#if !UNITY_WEBGL || UNITY_EDITOR
            // A tunnel address is long to type on a phone, so it can be pushed with adb instead:
            // adb push server-address.txt /sdcard/Android/data/com.dakiemonline.game/files/
            string addressFile = System.IO.Path.Combine(Application.persistentDataPath, ServerAddressFile);

            if (System.IO.File.Exists(addressFile))
            {
                serverAddress = System.IO.File.ReadAllText(addressFile).Trim();
            }
#endif

#if UNITY_STANDALONE
            // "-join host:port" skips the menu, so several windows can be started from a script.
            string[] args = System.Environment.GetCommandLineArgs();
            int join = System.Array.IndexOf(args, "-join");

            if (join >= 0 && join + 1 < args.Length)
            {
                serverAddress = args[join + 1];
                JoinServer();
            }
#endif
        }

        private void StartFight()
        {
            bossView ??= new BossView();
            fx ??= new FxView(view);
            hud.ShowBossBar(true);
            fight = new Simulation(config, StartX) { BossHeld = bossHeld, FreeCasting = freeCasting };
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

            if (online != null)
            {
                UpdateOnline();
                return;
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
            if (input.ConsumeSlashStyleChange()) fx.ToggleSlashPackage();
            if (input.ConsumeBossHoldChange()) fight.BossHeld = bossHeld = !bossHeld;
            if (input.ConsumeFreeCastingChange()) fight.FreeCasting = freeCasting = !freeCasting;
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
            ShowButtons(fight.Combat, fight.Resources);

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

        private void ShowButtons(CombatState combat, ResourceState resources)
        {

            foreach (HudButtonSpec spec in HudLayout.Buttons)
            {
                SkillSlot? slot = HudLayout.SlotOf(spec.Button);

                if (slot != null)
                {
                    double left = combat.CooldownSecondsLeft(slot.Value);
                    SkillConfig skill = config.Skill(slot.Value);
                    bool ready = Combat.IsSkillReady(combat, resources, skill);
                    hud.SetButton(spec.Button, left > 0d ? WholeSeconds(left) : spec.Label, !ready);
                    hud.SetCooldown(spec.Button, skill.CooldownSec > 0d ? (float)(left / skill.CooldownSec) : 0f);
                }
                else if (spec.Button == HudButton.Potion)
                {
                    double left = combat.PotionCooldownSecondsLeft;
                    string label = left > 0d ? WholeSeconds(left) : $"{spec.Label}\n{combat.PotionsLeft}";
                    hud.SetButton(spec.Button, label, !Combat.IsPotionReady(combat));
                    hud.SetCooldown(spec.Button, (float)(left / config.Potion.CooldownSec));
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

            if (online != null)
            {
                DrawOnline(style);
            }
            else if (fight == null && config != null)
            {
                DrawMenu(style);
            }
        }

        private static string DefaultServerAddress()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // The server hands out the web build too, so the page's own address is the server.
            return new System.Uri(Application.absoluteURL).GetLeftPart(System.UriPartial.Authority);
#else
            return "127.0.0.1:7777";
#endif
        }

        private void DrawMenu(GUIStyle style)
        {
            float unit = Screen.height / 12f;
            float width = Mathf.Min(Screen.width * 0.6f, unit * 12f);
            float left = (Screen.width - width) / 2f;
            var field = new GUIStyle(GUI.skin.textField) { fontSize = style.fontSize, alignment = TextAnchor.MiddleLeft };
            var button = new GUIStyle(GUI.skin.button) { fontSize = style.fontSize };

            GUI.Box(new Rect(left - unit * 0.5f, unit * 1.5f, width + unit, unit * 8f), GUIContent.none);
            GUI.Label(new Rect(left, unit * 2f, width, unit), "Dạ Kiếm Online", style);

            if (GUI.Button(new Rect(left, unit * 3.2f, width, unit * 1.2f), "Chơi một mình (đánh boss)", button))
            {
                StartFight();
            }

            GUI.Label(new Rect(left, unit * 5f, width, unit * 0.8f), "Địa chỉ server:", style);
            serverAddress = GUI.TextField(new Rect(left, unit * 5.8f, width, unit), serverAddress ?? string.Empty, field);

            if (menuError != null)
            {
                var error = new GUIStyle(style) { fontSize = style.fontSize * 2 / 3, wordWrap = true, normal = { textColor = new Color(1f, 0.75f, 0.6f) } };
                GUI.Label(new Rect(left, unit * 8.3f, width, unit * 1.2f), menuError, error);
            }

            if (GUI.Button(new Rect(left, unit * 7f, width, unit * 1.2f), "Vào server", button))
            {
                JoinServer();
            }
        }

        private void JoinServer()
        {
            string url;

            try
            {
                url = NetTransport.NormalizeUrl(serverAddress);
            }
            catch (System.UriFormatException)
            {
                menuError = $"Địa chỉ không hợp lệ: {serverAddress}";
                return;
            }

            PlayerPrefs.SetString(ServerAddressKey, serverAddress);
            PlayerPrefs.Save();
            menuError = null;
            online = new OnlineClient(url, config);
            online.RoundTrip += milliseconds => NetLog.Write(online, milliseconds);
            bossView ??= new BossView();
            fx ??= new FxView(view);
            onlineView = new OnlineView(online, config, character, fx, bossView, view);
            hud.ShowBossBar(true);
            accumulator = 0f;
        }

        private void LeaveServer(string reason)
        {
            online.Quit();
            online.Dispose();
            onlineView.Clear();
            online = null;
            onlineView = null;
            hud.ShowBossBar(false);
            menuError = reason;
        }

        private void UpdateOnline()
        {
            input.Update();
            input.ConsumeReplay();
            float frame = Mathf.Min(Time.unscaledDeltaTime, MaxFrameSeconds);
            online.Update(frame);
            onlineView.SmoothCorrection();

#if UNITY_STANDALONE
            if (UnityEngine.InputSystem.Keyboard.current?.f8Key.wasPressedThisFrame == true)
            {
                online.SimulateDrop();
            }
#endif

            if (online.Status == OnlineStatus.Failed)
            {
                LeaveServer(online.Message);
                return;
            }

            accumulator += frame;

            while (accumulator >= Ticks.DeltaSeconds)
            {
                online.Tick(input.MoveX, input.MoveY, input.ConsumeDash(), input.ConsumeCombat(), refillPressed);
                refillPressed = false;
                onlineView.AfterTick();
                accumulator -= (float)Ticks.DeltaSeconds;
            }

            onlineView.Show(accumulator / (float)Ticks.DeltaSeconds, frame);

            Predictor predictor = online.Predictor;

            if (predictor == null)
            {
                return;
            }

            CharacterConfig stats = config.Character;
            hud.ShowResources(
                (float)predictor.Resources.Hp / stats.MaxHp,
                (float)(predictor.Resources.Energy / stats.MaxEnergy),
                (float)(predictor.Movement.Stamina / stats.MaxStamina));
            hud.ShowBossHealth(online.Boss == null ? 1f : (float)online.Boss.Hp / config.Boss.MaxHp);
            ShowButtons(predictor.Combat, predictor.Resources);
        }

        private void DrawOnline(GUIStyle style)
        {
            var small = new GUIStyle(style) { fontSize = style.fontSize * 2 / 3, alignment = TextAnchor.UpperCenter };
            var shadow = new GUIStyle(small) { normal = { textColor = Color.black } };
            LatencyStats latency = online.Latency;
            string status = online.Status == OnlineStatus.Playing
                ? $"Kênh {online.ChannelId} · P{online.PlayerId} · {online.Present.Count + 1} người · ping {online.LastRoundTripMs:0} ms (TB {latency.Mean:0}, p95 {latency.Percentile(95d):0}, {latency.Count} lần)"
                : online.Message;

            if (online.Status == OnlineStatus.Playing && online.Message.Length > 0)
            {
                status += "\n" + online.Message;
            }

            var line = new Rect(0f, Screen.height * 0.11f, Screen.width, Screen.height * 0.2f);
            GUI.Label(new Rect(line.x + 1f, line.y + 1f, line.width, line.height), status, shadow);
            GUI.Label(line, status, small);
            onlineView.DrawLabels(new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });

            float unit = Screen.height / 12f;
            var button = new GUIStyle(GUI.skin.button) { fontSize = small.fontSize };

            if (GUI.Button(new Rect(Screen.width - unit * 3.2f, unit * 0.2f, unit * 3f, unit * 0.9f), "Thoát", button))
            {
                LeaveServer(null);
                return;
            }

            // Only the test room has a refill button; real play never shows it.
            if (online.TestRoom
                && online.Status == OnlineStatus.Playing
                && GUI.Button(new Rect(Screen.width - unit * 3.2f, unit * 1.3f, unit * 3f, unit * 0.9f), "Nạp đầy", button))
            {
                refillPressed = true;
            }

            DrawNotice(small, shadow);
        }

        /// <summary>Tells a fallen player, or everyone after a won fight, when things come back.</summary>
        private void DrawNotice(GUIStyle style, GUIStyle shadow)
        {
            Predictor predictor = online.Predictor;
            string notice = null;

            if (predictor != null && predictor.Resources.IsDead)
            {
                notice = $"Bạn đã gục. Hồi sinh sau {Mathf.CeilToInt(predictor.RespawnTicksLeft / (float)Ticks.PerSecond)} giây";
            }
            else if (online.Boss != null && online.Boss.IsDead)
            {
                notice = $"Đã hạ boss! Boss xuất hiện lại sau {Mathf.CeilToInt(online.BossRespawnTicks / (float)Ticks.PerSecond)} giây";
            }

            if (notice == null)
            {
                return;
            }

            var big = new GUIStyle(style) { fontSize = style.fontSize * 3 / 2, alignment = TextAnchor.MiddleCenter };
            var bigShadow = new GUIStyle(big) { normal = { textColor = Color.black } };
            var area = new Rect(0f, Screen.height * 0.3f, Screen.width, Screen.height * 0.15f);
            GUI.Label(new Rect(area.x + 2f, area.y + 2f, area.width, area.height), notice, bigShadow);
            GUI.Label(area, notice, big);
        }

        private void OnApplicationQuit()
        {
            online?.Quit();
        }

        private Vector2 ReadFeet()
        {
            return new Vector2((float)fight.Player.X, (float)fight.Player.Y);
        }
    }
}
