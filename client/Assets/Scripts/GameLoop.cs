using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using DaKiem.Client.Accounts;
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

        // How high in the doorway, in map units, the door button stands: below the bobbing arrow.
        private const float DoorButtonHeight = 1.75f;

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
        private AccountScreens screens;
        private AccountInfo playingAccount;
        private OnlineClient online;
        private OnlineView onlineView;
        private bool refillPressed;

        // The door button (E on a keyboard) goes out with the next tick's input.
        private bool doorPressed;
        private bool bossBarShown;

        // Practising alone, clothes change at once with F1 to F3; online they come from the bag.
        private Appearance practiceAppearance;

        private InventoryScreen inventory;
        private GroundView ground;

        private const string ServerAddressKey = "server.address";
        private const string ServerAddressFile = "server-address.txt";

        /// <summary>How the login background moves; only the client reads it, so it is not in GameConfig.</summary>
        private const string LoginSceneFile = "login-scene.json";

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
#if UNITY_STANDALONE_WIN || UNITY_EDITOR
            if (AccountUiSmoke.Enabled) return;
#endif
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
            yield return ConfigFiles.Load(GameConfig.AccountsFile, files);
            yield return ConfigFiles.Load(LoginSceneFile, files);
            AccountConfig accountRules;

            try
            {
                config = GameConfig.Parse(files);
                accountRules = GameConfig.ParseAccounts(files.TryGetValue(GameConfig.AccountsFile, out string accounts)
                    ? accounts
                    : throw new InvalidConfigException(GameConfig.AccountsFile, "file", "file is missing"));
            }
            catch (InvalidConfigException error)
            {
                configError = error.Message;
                Debug.LogError(configError);
                yield break;
            }

            hud = new HudView();
            input = new InputReader(hud);
            view = new WorldView(config);
            character = new CharacterView();
            practiceAppearance = GearRules.Default(config);
            inventory = InventoryScreen.Create(config, (kind, target, to) => online?.ItemAction(kind, target, to));
            ground = new GroundView(config);
            hud.ShowBossBar(false);
            hud.SetVisible(false);
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

            bool joinNow = false;

#if UNITY_STANDALONE
            // "-join host:port" skips the menus with a new guest, so several windows can be started from a script.
            string[] args = System.Environment.GetCommandLineArgs();
            int join = System.Array.IndexOf(args, "-join");

            if (join >= 0 && join + 1 < args.Length)
            {
                serverAddress = args[join + 1];
                joinNow = true;
            }
#endif

            files.TryGetValue(LoginSceneFile, out string scene);
            screens = AccountScreens.Create(accountRules, LoginSceneConfig.Parse(scene), serverAddress, JoinServer, StartFight, resume: !joinNow);

            if (joinNow)
            {
                screens.JoinAsNewGuest();
            }
        }

        private void StartFight()
        {
            bossView ??= new BossView();
            fx ??= new FxView(view);
            hud.SetVisible(true);
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

            hud?.FitScreen();

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
#if UNITY_STANDALONE || UNITY_WEBGL
            GearSlot? changed = input.ConsumeGearChange();
            if (changed.HasValue) practiceAppearance = practiceAppearance.With(changed.Value, GearRules.Next(config, practiceAppearance, changed.Value));
#endif
            character.ShowAppearance(config, practiceAppearance);
#if UNITY_STANDALONE
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
            // The same face as the canvases, so status lines keep their marks on the web too.
            GUI.skin.font = UiKit.Font;

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

        /// <summary>From the account screens, once a server and a character are chosen.</summary>
        private void JoinServer(string url, string loginToken, AccountInfo account, string characterName)
        {
            serverAddress = screens.Address;
            PlayerPrefs.SetString(ServerAddressKey, serverAddress);
            PlayerPrefs.Save();
            playingAccount = account;
            online = new OnlineClient(url, config, loginToken);
            online.RoundTrip += milliseconds => NetLog.Write(online, milliseconds);
            bossView ??= new BossView();
            fx ??= new FxView(view);
            onlineView = new OnlineView(online, config, character, fx, bossView, view);
            hud.SetVisible(true);
            hud.ShowBossBar(bossBarShown = true);
            accumulator = 0f;

            // Each time a guest enters, a reminder to link the account (decided 11/11/2026).
            if (account != null && account.IsGuest)
            {
                screens.ShowGuestReminderInGame();
            }
        }

        private void LeaveServer(string reason)
        {
            RejectReason? rejected = online.Rejected;
            online.Quit();
            online.Dispose();
            onlineView.Clear();
            inventory.Close();
            ground.Clear();
            online = null;
            onlineView = null;
            hud.ShowBossBar(false);
            hud.SetVisible(false);
            screens.ShowAfterGame(rejected, reason);
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

#if UNITY_STANDALONE || UNITY_WEBGL
            // F1 to F3 only change clothes when practising alone; online the bag decides.
            input.ConsumeGearChange();

            if (UnityEngine.InputSystem.Keyboard.current?.eKey.wasPressedThisFrame == true && !screens.Visible && !inventory.Visible)
            {
                UseNearby();
            }

            if (UnityEngine.InputSystem.Keyboard.current?.bKey.wasPressedThisFrame == true && !screens.Visible)
            {
                inventory.Toggle(online.Inventory);
            }
#endif

            inventory.Show(online.Inventory);
            ground.Show(online.Ground);
            accumulator += frame;

            // While an account screen or the bag is over the game, touches belong to it and the character stands still.
            bool covered = screens.Visible || inventory.Visible;

            while (accumulator >= Ticks.DeltaSeconds)
            {
                bool dash = input.ConsumeDash();
                CombatIntent combat = input.ConsumeCombat();
                online.Tick(covered ? 0d : input.MoveX, covered ? 0d : input.MoveY, dash && !covered, covered ? default : combat, refillPressed && !covered, doorPressed && !covered);
                refillPressed = false;
                doorPressed = false;
                onlineView.AfterTick();
                accumulator -= (float)Ticks.DeltaSeconds;
            }

            onlineView.Show(accumulator / (float)Ticks.DeltaSeconds, frame);

            // The boss and its health bar live on the test map only, not in the house.
            if (bossBarShown != online.BossOnMap)
            {
                bossBarShown = online.BossOnMap;
                hud.ShowBossBar(bossBarShown);
            }

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
            // OnGUI draws above every canvas, so nothing of it may show while an account screen or the bag is up.
            if (screens.Visible || inventory.Visible)
            {
                return;
            }

            var small = new GUIStyle(style) { fontSize = style.fontSize * 2 / 3, alignment = TextAnchor.UpperCenter };
            var shadow = new GUIStyle(small) { normal = { textColor = Color.black } };
            LatencyStats latency = online.Latency;
            string status = online.Status == OnlineStatus.Playing
                ? $"{online.CharacterName} · Kênh {online.ChannelId} · P{online.PlayerId} · {online.Present.Count + 1} người · ping {online.LastRoundTripMs:0} ms (TB {latency.Mean:0}, p95 {latency.Percentile(95d):0}, {latency.Count} lần)"
                : online.Message;

            if (online.Status == OnlineStatus.Playing && online.Message.Length > 0)
            {
                status += "\n" + online.Message;
            }

            // Below the FPS counter, which sits from about 8% to 13% of the height.
            var line = new Rect(0f, Screen.height * 0.15f, Screen.width, Screen.height * 0.2f);
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

            // Only guests have something to link; the button goes once the account is linked.
            AccountInfo account = screens.Account ?? playingAccount;

            if (account != null
                && account.IsGuest
                && GUI.Button(new Rect(Screen.width - unit * 3.2f, unit * 2.4f, unit * 3f, unit * 0.9f), "Liên kết TK", button))
            {
                screens.ShowLinkInGame();
            }

            // Only the test room has a refill button; real play never shows it.
            if (online.TestRoom
                && online.Status == OnlineStatus.Playing
                && GUI.Button(new Rect(Screen.width - unit * 3.2f, unit * 1.3f, unit * 3f, unit * 0.9f), "Nạp đầy", button))
            {
                refillPressed = true;
            }

            DrawBagButtons(unit, button);
            DrawNotice(small, shadow);
        }

        /// <summary>
        /// "Túi đồ" opens the bag (B on a keyboard). Standing by an item on the ground, a button picks
        /// it up (E on a keyboard), as GDD 15.1 keeps the bag in a menu rather than always open.
        /// </summary>
        private void DrawBagButtons(float unit, GUIStyle button)
        {
            if (online.Status != OnlineStatus.Playing)
            {
                return;
            }

            if (GUI.Button(new Rect(Screen.width - unit * 3.2f, unit * 3.5f, unit * 3f, unit * 0.9f), "Túi đồ", button))
            {
                inventory.Toggle(online.Inventory);
            }

            GroundItem? near = Nearby();
            var place = new Rect(Screen.width / 2f - unit * 2.5f, Screen.height * 0.62f, unit * 5f, unit * 0.95f);

            if (near != null)
            {
                if (GUI.Button(place, $"Nhặt {config.Gear[near.Value.Item].Name}", button))
                {
                    online.ItemAction(ItemActionKind.PickUp, near.Value.Id);
                }

                return;
            }

            MapDoor door = DoorNearby();
            Camera camera = Camera.main;

            if (door == null || camera == null)
            {
                return;
            }

            // The button stands in the doorway itself, so it reads as the door and the action buttons never cover it.
            Vector3 at = camera.WorldToScreenPoint(new Vector3((float)door.X, DoorButtonHeight, 0f));
            var onDoor = new Rect(at.x - unit * 1.4f, Screen.height - at.y - unit * 0.45f, unit * 2.8f, unit * 0.9f);

            if (GUI.Button(onDoor, door.Name, button))
            {
                doorPressed = true;
            }
        }

        /// <summary>
        /// The door with a button that the player stands at, the same check the server makes. A door at the
        /// edge of a map has no button: walking into it is enough.
        /// </summary>
        private MapDoor DoorNearby()
        {
            Predictor predictor = online.Predictor;
            MapDoor door = predictor == null || predictor.Resources.IsDead
                ? null
                : MapRules.DoorAt(config.Maps[online.Map], predictor.Movement.X);
            return door != null && !door.Walk ? door : null;
        }

        private GroundItem? Nearby()
        {
            Predictor predictor = online.Predictor;
            return predictor == null || predictor.Resources.IsDead
                ? null
                : ground.Reachable(online.Ground, predictor.Movement.X, predictor.Movement.Y);
        }

        /// <summary>E picks up what lies at the feet; with nothing there, it goes through the door.</summary>
        private void UseNearby()
        {
            GroundItem? near = Nearby();

            if (near != null)
            {
                online.ItemAction(ItemActionKind.PickUp, near.Value.Id);
            }
            else if (DoorNearby() != null)
            {
                doorPressed = true;
            }
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
            else if (online.BossOnMap && online.Boss != null && online.Boss.IsDead)
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
