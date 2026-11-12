using System;
using System.Collections;
using System.Collections.Generic;
using DaKiem.Client.Net;
using Game.Net;
using Game.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace DaKiem.Client.Accounts
{
    /// <summary>
    /// The way into the game, as in Ngọc Rồng Online and Ninja School: log in, "Chơi ngay" as a guest,
    /// register with a code, or get a new password; then pick a server and, the first time, name a
    /// character. Guests are asked to link their account at the login screen and each time they enter
    /// the game. Screens follow the approved mock-ups in docs/giao-dien-goc (positions are mock-up
    /// pixels, see <see cref="UiKit"/>); the rules come from config/accounts.json, as on the server.
    /// </summary>
    public sealed class AccountScreens : MonoBehaviour
    {
        private AccountConfig rules;
        private AccountClient api;
        private string address;
        private Action<string, string, AccountInfo, string> play;
        private Action practice;
        private RectTransform canvas;
        private RectTransform screen;
        private LivingBackground background;
        private Text message;
        private Button sendCodeButton;
        private float resendAt;
        private bool busy;
        private int screenVersion;
        private int pending;
        private CanvasGroup inputGroup;
        private RectTransform content;
        private Button enterButton;
        private AccountInfo account;
        private ServerInfo chosenServer;
        private ServerInfo[] servers = Array.Empty<ServerInfo>();
        private bool recentTab = true;

        /// <summary>True while a screen is up; the game ignores touches then.</summary>
        public bool Visible => screen != null;

        public AccountInfo Account => account;

        public string Address => address;

        /// <summary>
        /// Starts on the login screen, or straight at the servers when this device still has a login.
        /// <paramref name="play"/> gets the WebSocket address, the login token, the account and the character name.
        /// </summary>
        public static AccountScreens Create(AccountConfig rules, LoginSceneConfig scene, string address, Action<string, string, AccountInfo, string> play, Action practice, bool resume = true)
        {
            var screens = new GameObject(nameof(AccountScreens)).AddComponent<AccountScreens>();
            screens.rules = rules;
            screens.play = play;
            screens.practice = practice;
            screens.background = LivingBackground.Create(scene, 40);
            screens.canvas = UiKit.CreateCanvas("AccountScreens", 50);
            screens.SetAddress(address);

            if (resume)
            {
                screens.Resume();
            }

            return screens;
        }

        /// <summary>Back from the game: to the servers, or to where a refusal points.</summary>
        public void ShowAfterGame(RejectReason? reason, string text)
        {
            switch (reason)
            {
                case RejectReason.NotLoggedIn:
                    LogOutHere();
                    ShowLogin(text ?? "Phiên đăng nhập đã hết. Hãy đăng nhập lại.");
                    break;
                case RejectReason.NoCharacter when chosenServer != null:
                    ShowCreateCharacter(chosenServer);
                    break;
                default:
                    ShowServers(text);
                    break;
            }
        }

        /// <summary>
        /// For "-join" on PC, which starts several windows from a script: each window makes a guest of its
        /// own and a character, and keeps neither, since all windows share one set of PlayerPrefs.
        /// </summary>
        public void JoinAsNewGuest()
        {
            ShowBusy("Đang tạo tài khoản khách để thử...");
            StartCoroutine(api.PlayNow(null, reply =>
            {
                var trial = new AccountClient(api.BaseUrl) { Token = reply.token };
                string name = "Thử" + UnityEngine.Random.Range(1000, 9999);
                chosenServer = new ServerInfo { number = 1, name = "1" };
                StartCoroutine(trial.CreateCharacter(1, name, made =>
                {
                    Close();
                    play(NetTransport.NormalizeUrl(address), reply.token, reply.account, made.character.name);
                }, error => ShowLogin(error.message)));
            }, error => ShowLogin(error.message)));
        }

        /// <summary>From the game's menu: the link form over the game; closing it returns to play.</summary>
        public void ShowLinkInGame()
        {
            ShowLink(inGame: true);
        }

        /// <summary>The reminder each time a guest enters the game.</summary>
        public void ShowGuestReminderInGame()
        {
            if (account == null || !account.IsGuest)
            {
                return;
            }

            RectTransform stage = Open(inGame: true);
            Frame(stage, 446f, 200f, 780f, 520f);
            UiKit.Title(stage, "TÀI KHOẢN KHÁCH", 571f, 270f, 530f, 72f);
            Text text = UiKit.Place(UiKit.Label(stage, $"Bạn đang chơi bằng tài khoản khách <color=#F2C76E>{account.username}</color>. Gỡ game hoặc đổi máy là mất nhân vật. Hãy liên kết tài khoản để giữ lại.", 30, Color.white), 520f, 365f, 632f, 170f);
            text.lineSpacing = 1.1f;
            UiKit.Place(UiKit.Button(stage, "ĐỂ SAU", UiKit.ButtonKind.Blue, Close), 530f, 562f, 280f, 82f);
            UiKit.Place(UiKit.Button(stage, "LIÊN KẾT NGAY", UiKit.ButtonKind.Gold, () => ShowLink(inGame: true), 32), 830f, 557f, 310f, 92f);
        }

        public void Close()
        {
            screenVersion++;
            pending = 0;
            busy = false;
            if (screen != null)
            {
                screen.gameObject.SetActive(false);
                Destroy(screen.gameObject);
                screen = null;
            }

            message = null;
            sendCodeButton = null;
            inputGroup = null;
            enterButton = null;
            background.Show(false);
            FpsOverlay.Hidden = false;
        }

        private void Update()
        {
            if (busy && message != null && message.text.StartsWith("Đang gửi"))
                message.text = "Đang gửi" + new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);
            if (sendCodeButton != null)
            {
                int wait = Mathf.CeilToInt(resendAt - Time.unscaledTime);
                sendCodeButton.interactable = wait <= 0 && !busy;
                UiKit.SetText(sendCodeButton, wait > 0 ? $"GỬI LẠI ({wait})" : "GỬI MÃ");
            }
        }

        private void OnDestroy()
        {
            if (canvas != null && Application.isPlaying)
            {
                Destroy(canvas.gameObject);
            }
            if (background != null && Application.isPlaying) Destroy(background.gameObject);
        }

        private void SetAddress(string text)
        {
            address = (text ?? string.Empty).Trim();

            try
            {
                api = new AccountClient(AccountClient.FromWebSocketUrl(NetTransport.NormalizeUrl(address))) { Token = AccountStorage.Token };
            }
            catch (UriFormatException)
            {
                api = null;
            }
        }

        private void Resume()
        {
            if (api == null || string.IsNullOrEmpty(AccountStorage.Token))
            {
                ShowLogin(null);
                return;
            }

            ShowBusy("Đang vào lại...");
            StartCoroutine(api.Me(
                reply =>
                {
                    account = reply.account;
                    ShowServers(null);
                },
                error =>
                {
                    if (error.error != "network")
                    {
                        LogOutHere();
                    }

                    ShowLogin(error.error == "network" ? error.message : null);
                }));
        }

        // ---- Login (mock-up docs/giao-dien-goc/dinhkem_14.png) --------------------------------

        private void ShowLogin(string note)
        {
            RectTransform stage = Open(inGame: false);
            Logo(stage, 360f, 20f, 950f);
            Frame(stage, 425f, 230f, 820f, 626f);

            // Everything sits inside the frame's navy area (x 480-1190, y 336-823), clear of the gold bottom corners.
            InputField username = Field(stage, "Tài khoản", InputField.ContentType.Standard, "icon_user", rules.UsernameMaxLength, 505f, 348f, 660f, 84f);
            username.text = AccountStorage.Username;
            InputField password = Field(stage, "Mật khẩu", InputField.ContentType.Password, "icon_lock", rules.PasswordMaxLength, 505f, 442f, 660f, 84f);
            username.onSubmit.AddListener(_ => { password.Select(); password.ActivateInputField(); });
            password.onSubmit.AddListener(_ => Login(username, password));

            Toggle remember = UiKit.Place(UiKit.Check(stage, "Ghi nhớ đăng nhập", AccountStorage.Remember, 32), 509f, 532f, 390f, 64f);
            remember.onValueChanged.AddListener(on => AccountStorage.Remember = on);
            UiKit.Place(UiKit.Button(stage, "Quên mật khẩu?", UiKit.ButtonKind.Link, ShowForgot, 32), 914f, 532f, 252f, 64f);

            UiKit.Place(UiKit.Button(stage, "ĐĂNG NHẬP", UiKit.ButtonKind.Gold, () => Login(username, password), 50), 505f, 604f, 660f, 96f);
            UiKit.Place(UiKit.Button(stage, "ĐĂNG KÝ", UiKit.ButtonKind.Blue, ShowRegister, 38), 520f, 712f, 310f, 84f);
            UiKit.Place(UiKit.Button(stage, "CHƠI NGAY", UiKit.ButtonKind.Blue, PlayNow, 38), 843f, 712f, 310f, 84f);

            string guestNote = string.IsNullOrEmpty(AccountStorage.GuestSecret) ? null : "Tài khoản khách trên máy: bấm Chơi ngay.\nLiên kết để giữ nhân vật khi đổi máy.";
            message = Message(stage, note ?? guestNote, 460f, 864f, 750f, 70f, note == null && guestNote != null ? UiKit.Gold : (Color?)null);
            Corners(showTools: true);
        }

        private void Login(InputField username, InputField password)
        {
            if (!Ready(username))
            {
                return;
            }

            UiKit.MarkError(username, false);
            UiKit.MarkError(password, false);
            string name = AccountRules.NormalizeUsername(username.text);

            if (name.Length == 0 || password.text.Length == 0)
            {
                Say("Nhập tài khoản và mật khẩu.", name.Length == 0 ? username : password);
                return;
            }

            Run(api.Login(name, password.text, reply =>
            {
                AccountStorage.Username = name;
                SignedIn(reply);
            }, error => Refused(error, null, null, null, password)));
        }

        private void PlayNow()
        {
            if (!Ready(null))
            {
                return;
            }

            Run(api.PlayNow(AccountStorage.GuestSecret, SignedIn, error =>
            {
                if (error.error == "guest_unknown")
                {
                    AccountStorage.GuestSecret = null;
                }

                Refused(error);
            }));
        }

        private void SignedIn(LoginReply reply)
        {
            if (!string.IsNullOrEmpty(reply.guestSecret))
            {
                AccountStorage.GuestSecret = reply.guestSecret;
            }

            // Without "Ghi nhớ đăng nhập" the token lives only until the game closes.
            AccountStorage.Token = AccountStorage.Remember ? reply.token : null;
            api.Token = reply.token;
            account = reply.account;
            ShowServers(null);
        }

        // ---- Register, link, forgot (mock-up dinhkem_13.png) --------------------------------

        private static class CodePurposes
        {
            public const string Register = "register";
            public const string Link = "link";
            public const string Reset = "reset";
        }

        /// <summary>The boxes register, link and forgot share: username (not for forgot), password twice, contact and code.</summary>
        private sealed class AccountForm
        {
            public InputField UsernameBox;
            public InputField Password;
            public InputField Again;
            public InputField Contact;
            public InputField Code;

            public string Username => UsernameBox == null ? null : AccountRules.NormalizeUsername(UsernameBox.text);

            public bool Check(AccountConfig rules, AccountScreens screens)
            {
                if (!screens.Ready(null))
                {
                    return false;
                }

                foreach (InputField box in new[] { UsernameBox, Password, Again, Contact, Code })
                {
                    UiKit.MarkError(box, false);
                }

                AccountProblem problem = UsernameBox == null ? AccountProblem.None : AccountRules.CheckUsername(UsernameBox.text, rules);

                if (problem != AccountProblem.None)
                {
                    return screens.Say(AccountRules.Message(problem, rules), UsernameBox);
                }

                problem = AccountRules.CheckPassword(Password.text, rules);

                if (problem != AccountProblem.None)
                {
                    return screens.Say(AccountRules.Message(problem, rules), Password);
                }

                if (Again.text != Password.text)
                {
                    return screens.Say("Mật khẩu xác nhận không khớp.", Again);
                }

                if (AccountRules.ParseContact(Contact.text) == null)
                {
                    return screens.Say(AccountRules.Message(AccountProblem.ContactInvalid, rules), Contact);
                }

                return Code.text.Trim().Length > 0 || screens.Say("Nhập mã xác nhận để tiếp tục.", Code);
            }

            public void Refused(ApiError error, AccountScreens screens)
            {
                screens.Refused(error, UsernameBox, Contact, Code, Password);
            }
        }

        /// <summary>The register-style dialog: logo, frame, title plaque and a close X.</summary>
        private RectTransform Dialog(string title, bool inGame, Action closed)
        {
            RectTransform stage = Open(inGame);

            if (!inGame)
            {
                Logo(stage, 548f, 4f, 535f);
            }

            // Navy area x 470-1200, y 226-874; the title plaque and the X sit on the beam above it.
            Frame(stage, 415f, 120f, 840f, 787f);
            UiKit.Title(stage, title, 570f, 188f, 528f, 74f);
            UiKit.Close(stage, 1116f, 172f, 80f, closed);
            return stage;
        }

        private AccountForm Form(RectTransform stage, bool withUsername, string purpose)
        {
            var form = new AccountForm();
            // Five rows fill y 274-694; without the username the four rows sit centred in the same space.
            const float row = 86f;
            float top = withUsername ? 274f : 318f;

            if (withUsername)
            {
                form.UsernameBox = Field(stage, $"Tài khoản ({rules.UsernameMinLength}–{rules.UsernameMaxLength} ký tự)", InputField.ContentType.Standard, "icon_user", rules.UsernameMaxLength, 505f, top, 662f, 76f);
                top += row;
            }

            form.Password = Field(stage, purpose == CodePurposes.Reset ? "Mật khẩu mới" : "Mật khẩu", InputField.ContentType.Password, "icon_lock", rules.PasswordMaxLength, 505f, top, 662f, 76f);
            form.Again = Field(stage, "Nhập lại mật khẩu", InputField.ContentType.Password, "icon_lock", rules.PasswordMaxLength, 505f, top + row, 662f, 76f);
            form.Contact = Field(stage, "Email hoặc số điện thoại", InputField.ContentType.EmailAddress, "icon_mail", 254, 505f, top + 2f * row, 662f, 76f);
            form.Code = Field(stage, "Mã xác nhận", InputField.ContentType.IntegerNumber, "icon_shield", rules.CodeDigits, 505f, top + 3f * row, 430f, 76f);
            sendCodeButton = UiKit.Place(UiKit.Button(stage, "GỬI MÃ", UiKit.ButtonKind.BlueSmall, () => SendCode(form, purpose), 30), 947f, top + 3f * row - 2f, 220f, 82f);
            InputField[] fields = withUsername ? new[] { form.UsernameBox, form.Password, form.Again, form.Contact, form.Code } : new[] { form.Password, form.Again, form.Contact, form.Code };
            for (int i = 0; i < fields.Length - 1; i++)
            {
                InputField next = fields[i + 1];
                fields[i].onSubmit.AddListener(_ => { next.Select(); next.ActivateInputField(); });
            }

            // Each form waits only on codes it sent itself; the server still refuses a resend too soon.
            resendAt = 0f;
            return form;
        }

        private void ShowRegister()
        {
            RectTransform stage = Dialog("ĐĂNG KÝ TÀI KHOẢN", false, () => ShowLogin(null));
            AccountForm form = Form(stage, withUsername: true, CodePurposes.Register);
            message = Message(stage, $"Tài khoản: chữ thường, số, _. Mật khẩu: {rules.PasswordMinLength}–{rules.PasswordMaxLength} ký tự, không có khoảng trắng.", 485f, 702f, 702f, 64f);
            Button submit = UiKit.Place(UiKit.Button(stage, "ĐĂNG KÝ", UiKit.ButtonKind.Gold, () =>
            {
                if (form.Check(rules, this))
                {
                    Run(api.Register(form.Username, form.Password.text, form.Contact.text, form.Code.text, reply =>
                    {
                        AccountStorage.Username = form.Username;
                        SignedIn(reply);
                    }, error => form.Refused(error, this)));
                }
            }, 48), 522f, 774f, 628f, 90f);
            form.Code.onSubmit.AddListener(_ => { if (submit.IsInteractable()) submit.onClick.Invoke(); });
            Corners(showTools: false);
        }

        private void ShowLink(bool inGame)
        {
            Action back = inGame ? Close : () => ShowServers(null);
            RectTransform stage = Dialog("LIÊN KẾT TÀI KHOẢN", inGame, back);
            AccountForm form = Form(stage, withUsername: true, CodePurposes.Link);
            message = Message(stage, $"Giữ nguyên nhân vật. Mật khẩu: {rules.PasswordMinLength}–{rules.PasswordMaxLength} ký tự, không có khoảng trắng.", 485f, 702f, 702f, 64f);
            Button submit = UiKit.Place(UiKit.Button(stage, "LIÊN KẾT", UiKit.ButtonKind.Gold, () =>
            {
                if (form.Check(rules, this))
                {
                    Run(api.Link(form.Username, form.Password.text, form.Contact.text, form.Code.text, reply =>
                    {
                        account = reply.account;
                        AccountStorage.GuestSecret = null;
                        AccountStorage.Username = reply.account.username;
                        Done($"Đã liên kết. Từ nay đăng nhập bằng tài khoản <color=#F2C76E>{reply.account.username}</color>.", inGame);
                    }, error => form.Refused(error, this)));
                }
            }, 48), 522f, 774f, 628f, 90f);
            form.Code.onSubmit.AddListener(_ => { if (submit.IsInteractable()) submit.onClick.Invoke(); });

            if (!inGame)
            {
                Corners(showTools: false);
            }
        }

        private void ShowForgot()
        {
            RectTransform stage = Dialog("QUÊN MẬT KHẨU", false, () => ShowLogin(null));
            AccountForm form = Form(stage, withUsername: false, CodePurposes.Reset);
            message = Message(stage, $"Dùng email/SĐT đã đăng ký. Mật khẩu mới: {rules.PasswordMinLength}–{rules.PasswordMaxLength} ký tự, không có khoảng trắng.", 485f, 702f, 702f, 64f);
            Button submit = UiKit.Place(UiKit.Button(stage, "ĐỔI MẬT KHẨU", UiKit.ButtonKind.Gold, () =>
            {
                if (form.Check(rules, this))
                {
                    Run(api.Reset(form.Contact.text, form.Code.text, form.Password.text, error => form.Refused(error, this), () =>
                    {
                        LogOutHere();
                        ShowPasswordReset();
                    }));
                }
            }, 48), 522f, 774f, 628f, 90f);
            form.Code.onSubmit.AddListener(_ => { if (submit.IsInteractable()) submit.onClick.Invoke(); });
            Corners(showTools: false);
        }

        private void SendCode(AccountForm form, string purpose)
        {
            if (!Ready(null))
            {
                return;
            }

            UiKit.MarkError(form.Contact, false);
            UiKit.MarkError(form.UsernameBox, false);

            if (AccountRules.ParseContact(form.Contact.text) == null)
            {
                Say(AccountRules.Message(AccountProblem.ContactInvalid, rules), form.Contact);
                return;
            }

            if (form.UsernameBox != null && form.UsernameBox.text.Trim().Length > 0)
            {
                AccountProblem problem = AccountRules.CheckUsername(form.UsernameBox.text, rules);

                if (problem != AccountProblem.None)
                {
                    Say(AccountRules.Message(problem, rules), form.UsernameBox);
                    return;
                }
            }

            Run(api.SendCode(purpose, form.Contact.text, form.Username, reply =>
            {
                resendAt = Time.unscaledTime + reply.resendSec;
                Say(reply.Notice, null, UiKit.Green);
            }, error =>
            {
                if (error.error == "wait")
                {
                    resendAt = Time.unscaledTime + error.waitSec;
                }

                form.Refused(error, this);
            }));
        }

        private void ShowPasswordReset()
        {
            ShowLogin("Đã đổi mật khẩu và đăng xuất trên mọi máy.\nHãy đăng nhập lại.");
            message.color = UiKit.Green;
        }

        // ---- Servers (mock-up dinhkem_12.png) and characters ------------------------------------

        private void ShowServers(string note)
        {
            chosenServer = null;
            RectTransform stage = Open(inGame: false);
            Logo(stage, 440f, 6f, 790f);
            // Centred on the screen (the mock-up sat 27 to the right). Navy area x 373-1298, y 266-725;
            // the account row and VÀO GAME stand below the frame, as in the mock-up.
            Frame(stage, 318f, 160f, 1035f, 598f);
            UiKit.Title(stage, "CHỌN MÁY CHỦ", 573f, 232f, 530f, 76f);

            var inset = UiKit.Place(UiKit.Rect("Tabs", stage).gameObject.AddComponent<Image>(), 384f, 314f, 182f, 376f);
            inset.color = new Color(0f, 0f, 0f, 0.22f);
            inset.raycastTarget = false;
            UiKit.Place(UiKit.Button(stage, "Gần đây", recentTab ? UiKit.ButtonKind.Gold : UiKit.ButtonKind.Blue, () => SwitchTab(true), 34), 389f, 342f, 172f, 72f);
            UiKit.Place(UiKit.Button(stage, "Tất cả", recentTab ? UiKit.ButtonKind.Blue : UiKit.ButtonKind.Gold, () => SwitchTab(false), 34), 389f, 438f, 172f, 72f);

            RectTransform grid = UiKit.Rect("Servers", stage);
            UiKit.Fill(grid);
            UiKit.Place(UiKit.Label(grid, "Đang tải danh sách máy chủ...", 30, Color.white), 584f, 335f, 698f, 105f);

            string who = account == null ? string.Empty : $"Tài khoản: {account.username}";
            UiKit.Place(UiKit.Label(stage, who, 30, Color.white, TextAnchor.MiddleLeft), 371f, 770f, 480f, 48f);
            UiKit.Place(UiKit.Button(stage, "Đổi tài khoản", UiKit.ButtonKind.BlueSmall, LogOut, 30), 613f, 822f, 238f, 80f);

            if (account != null && account.IsGuest)
            {
                UiKit.Place(UiKit.Button(stage, "Liên kết", UiKit.ButtonKind.BlueSmall, () => ShowLink(inGame: false), 32), 371f, 822f, 222f, 80f);
            }

            enterButton = UiKit.Place(UiKit.Button(stage, "VÀO GAME", UiKit.ButtonKind.Gold, EnterChosen, 54), 863f, 776f, 515f, 114f);
            enterButton.interactable = false;

            string guestNote = account != null && account.IsGuest ? "Liên kết để giữ nhân vật khi đổi máy." : null;
            message = Message(stage, note ?? guestNote, 385f, 902f, 900f, 38f, note == null && guestNote != null ? UiKit.Gold : (Color?)null);
            Corners(showTools: false);
            Run(api.Servers(reply =>
            {
                servers = reply.servers ?? Array.Empty<ServerInfo>();
                chosenServer = Array.Find(servers, server => server.number == AccountStorage.LastServer) ?? (servers.Length > 0 ? servers[0] : null);
                FillServers(grid);
                enterButton.interactable = chosenServer != null;
            }, error =>
            {
                foreach (Transform child in grid) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                UiKit.Place(UiKit.Label(grid, "Không tải được danh sách máy chủ.", 32, Color.white), 584f, 350f, 698f, 100f);
                UiKit.Place(UiKit.Button(grid, "THỬ LẠI", UiKit.ButtonKind.Blue, () => ShowServers(null), 34), 758f, 470f, 350f, 92f);
                Refused(error);
            }), quiet: true);
        }

        private void SwitchTab(bool recent)
        {
            recentTab = recent;
            ShowServers(null);
        }

        private void FillServers(RectTransform grid)
        {
            foreach (Transform child in grid)
            {
                Destroy(child.gameObject);
            }

            // "Gần đây" shows where this device last played, or every server before it has played anywhere.
            ServerInfo[] shown = recentTab && Array.Exists(servers, server => server.number == AccountStorage.LastServer)
                ? Array.FindAll(servers, server => server.number == AccountStorage.LastServer)
                : servers;

            if (shown.Length == 0)
            {
                UiKit.Place(UiKit.Label(grid, "Chưa có máy chủ nào mở.", 30, Color.white), 584f, 335f, 698f, 105f);
                return;
            }

            for (int i = 0; i < shown.Length && i < 6; i++)
            {
                float left = i % 2 == 0 ? 584f : 942f;
                float top = 335f + i / 2 * 125f;
                ServerCard(grid, shown[i], left, top);
            }
        }

        private void ServerCard(RectTransform grid, ServerInfo server, float left, float top)
        {
            bool chosen = chosenServer != null && chosenServer.number == server.number;
            Image card = UiKit.Place(UiKit.Image(grid, chosen ? "card_selected" : "card", sliced: true), left, top, 340f, 105f);
            card.pixelsPerUnitMultiplier = 2.2f;
            card.raycastTarget = true;
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = card;
            button.onClick.AddListener(() =>
            {
                chosenServer = server;
                FillServers(grid);
            });

            if (chosen)
            {
                card.transform.localScale = new Vector3(1.04f, 1.04f, 1f);
            }

            // Stops short of the "MỚI" ribbon in the top right corner.
            Text title = UiKit.Place(UiKit.Label(card.transform, $"{server.number} · {server.name}", 30, chosen ? UiKit.Navy : Color.white, TextAnchor.MiddleLeft, outlined: !chosen), 28f, 10f, server.isNew ? 232f : 292f, 42f);
            UiKit.FitOneLine(title, 26);
            string dot = server.status == "full" ? "dot_gray" : server.status == "busy" ? "dot_yellow" : "dot_green";
            string status = server.status == "full" ? "Đầy" : server.status == "busy" ? "Đông" : "Tốt";
            UiKit.Place(UiKit.Image(card.transform, dot), 32f, 58f, 30f, 30f);
            string character = string.IsNullOrEmpty(server.character) ? "chưa có nhân vật" : server.character;
            UiKit.Place(UiKit.Label(card.transform, $"{status} <size=22>· {character}</size>", 28, chosen ? UiKit.Navy : Color.white, TextAnchor.MiddleLeft, outlined: !chosen), 72f, 52f, 258f, 42f);

            if (server.isNew)
            {
                Image ribbon = UiKit.Place(UiKit.Image(card.transform, "ribbon"), 257f, -14f, 90f, 54f);
                UiKit.Place(UiKit.Label(ribbon.transform, "MỚI", 22, Color.white), 0f, 4f, 90f, 40f);
            }
        }

        private void EnterChosen()
        {
            if (busy || chosenServer == null)
            {
                Say("Chọn một máy chủ trước.", null);
                return;
            }

            AccountStorage.LastServer = chosenServer.number;

            if (string.IsNullOrEmpty(chosenServer.character))
            {
                ShowCreateCharacter(chosenServer);
                return;
            }

            Enter(chosenServer.character);
        }

        private void ShowCreateCharacter(ServerInfo server)
        {
            RectTransform stage = Open(inGame: false);
            Logo(stage, 548f, 4f, 535f);
            Frame(stage, 455f, 160f, 760f, 600f);
            // Navy area x 510-1160, y 266-727.
            UiKit.Title(stage, "TẠO NHÂN VẬT", 595f, 228f, 480f, 74f);
            UiKit.Close(stage, 1078f, 212f, 80f, () => ShowServers(null));
            UiKit.Place(UiKit.Label(stage, $"Máy chủ {server.number} - {server.name}. Mỗi tài khoản có một nhân vật trên mỗi máy chủ.", 26, Color.white), 522f, 318f, 626f, 70f);
            InputField name = Field(stage, $"Tên nhân vật ({rules.CharacterNameMinLength}–{rules.CharacterNameMaxLength} ký tự)", InputField.ContentType.Standard, "icon_user", rules.CharacterNameMaxLength, 522f, 400f, 626f, 96f);
            message = Message(stage, "Dùng chữ hoặc số, có thể có dấu tiếng Việt. Không có khoảng trắng.", 522f, 508f, 626f, 86f);
            Button submit = UiKit.Place(UiKit.Button(stage, "TẠO NHÂN VẬT", UiKit.ButtonKind.Gold, () =>
            {
                if (!Ready(name))
                {
                    return;
                }

                UiKit.MarkError(name, false);
                AccountProblem problem = AccountRules.CheckCharacterName(name.text, rules);

                if (problem != AccountProblem.None)
                {
                    Say(AccountRules.Message(problem, rules), name);
                    return;
                }

                Run(api.CreateCharacter(server.number, AccountRules.NormalizeCharacterName(name.text), reply => Enter(reply.character.name), error => Refused(error, null, null, null, null, name)));
            }, 52), 556f, 606f, 558f, 94f);
            name.onSubmit.AddListener(_ => { if (submit.IsInteractable()) submit.onClick.Invoke(); });
            Corners(showTools: false);
        }

        private void Enter(string characterName)
        {
            Close();
            play(NetTransport.NormalizeUrl(address), api.Token, account, characterName);
        }

        private void LogOut()
        {
            if (busy)
            {
                return;
            }

            Run(api.Logout(() =>
            {
                LogOutHere();
                ShowLogin(null);
            }));
        }

        private void LogOutHere()
        {
            AccountStorage.Token = null;

            if (api != null)
            {
                api.Token = null;
            }

            account = null;
            chosenServer = null;
            servers = Array.Empty<ServerInfo>();
        }

        private void Done(string text, bool inGame)
        {
            if (!inGame)
            {
                ShowServers(text);
                message.color = UiKit.Green;
                return;
            }

            RectTransform stage = Open(inGame: true);
            Frame(stage, 486f, 230f, 700f, 440f);
            UiKit.Title(stage, "ĐÃ LIÊN KẾT", 571f, 298f, 530f, 72f);
            UiKit.Place(UiKit.Label(stage, text, 30, UiKit.Green), 552f, 390f, 568f, 110f);
            UiKit.Place(UiKit.Button(stage, "CHƠI TIẾP", UiKit.ButtonKind.Gold, Close, 46), 610f, 524f, 452f, 90f);
        }

        // ---- Shared pieces -----------------------------------------------------------------

        /// <summary>A fresh screen: over the moving scene before the game, over a dimmed game inside it.</summary>
        private RectTransform Open(bool inGame)
        {
            Close();
            screen = UiKit.Rect("Screen", canvas);
            UiKit.Fill(screen);
            background.Show(!inGame);
            FpsOverlay.Hidden = !inGame;

            if (inGame)
            {
                UiKit.Shade(screen, 0.6f);
            }
            else
            {
                // Takes the touches the background would otherwise let through to the game.
                UiKit.Shade(screen, 0f);
            }

            content = UiKit.Rect("SafeContent", screen);
            UiKit.Fill(content);
            RectTransform stage = UiKit.Stage(content);
            inputGroup = content.gameObject.AddComponent<CanvasGroup>();
            var presentation = content.gameObject.AddComponent<AccountPresentation>();
            presentation.Stage = stage;
            presentation.Group = inputGroup;
            presentation.RefreshLayout();
            return stage;
        }

        private static void Logo(RectTransform stage, float left, float top, float width)
        {
            UiKit.Place(UiKit.Image(stage, "logo"), left, top, width, width * 486f / 2090f);
        }

        private void Frame(RectTransform stage, float left, float top, float width, float height)
        {
            Image frame = UiKit.Place(UiKit.Image(stage, "panel", sliced: true), left, top, width, height);
            frame.pixelsPerUnitMultiplier = 1.75f;
            background.SetPanel((RectTransform)frame.transform);
        }

        private static InputField Field(RectTransform stage, string placeholder, InputField.ContentType type, string icon, int limit, float left, float top, float width, float height)
        {
            return UiKit.Place(UiKit.Field(stage, placeholder, type, icon, limit), left, top, width, height);
        }

        private void ShowBusy(string text)
        {
            RectTransform stage = Open(inGame: false);
            Logo(stage, 330f, 120f, 1010f);
            UiKit.Place(UiKit.Label(stage, text, 36, Color.white), 330f, 520f, 1010f, 60f);
        }

        private static Text Message(RectTransform stage, string text, float left, float top, float width, float height, Color? color = null)
        {
            Image plate = UiKit.Place(UiKit.Rect("StatusPlate", stage).gameObject.AddComponent<Image>(), left, top, width, height);
            plate.color = new Color(UiKit.Navy.r, UiKit.Navy.g, UiKit.Navy.b, 0.92f);
            plate.raycastTarget = false;
            plate.enabled = !string.IsNullOrWhiteSpace(text);
            Text line = UiKit.Label(plate.transform, text ?? string.Empty, 30, color ?? Color.white, style: FontStyle.Normal);
            UiKit.Fill(line.rectTransform);
            var backdrop = line.gameObject.AddComponent<AccountMessageBackdrop>();
            backdrop.Label = line;
            backdrop.Plate = plate;
            line.gameObject.name = "StatusMessage";

            // AccountMessageBackdrop sizes the text, and the plate when the text needs more room.
            backdrop.Largest = 30;
            backdrop.Smallest = 20;
            return line;
        }

        /// <summary>
        /// The corners of the screen, outside the frame: the version bottom left as in the mock-ups, and on
        /// the login screen the server address and practising alone, both for trying things out. The
        /// settings and sound buttons of the mock-ups stay hidden until those features exist (11/11/2026).
        /// </summary>
        private void Corners(bool showTools)
        {
            Text version = UiKit.Label(content, $"Phiên bản {Application.version}", 26, Color.white, TextAnchor.LowerLeft, style: FontStyle.Normal);
            var versionRect = (RectTransform)version.transform;
            versionRect.anchorMin = versionRect.anchorMax = versionRect.pivot = Vector2.zero;
            versionRect.sizeDelta = new Vector2(420f, 50f);
            versionRect.anchoredPosition = new Vector2(30f, 24f);

            if (!showTools)
            {
                return;
            }

            RectTransform tools = UiKit.Rect("Tools", content);
            tools.anchorMin = tools.anchorMax = tools.pivot = new Vector2(1f, 0f);
            tools.sizeDelta = new Vector2(250f, 56f);
            tools.anchoredPosition = new Vector2(-24f, 20f);
            RectTransform options = UiKit.Rect("TrialOptions", tools);
            options.anchorMin = options.anchorMax = options.pivot = new Vector2(1f, 0f);
            options.anchoredPosition = new Vector2(0f, 72f);
            options.sizeDelta = new Vector2(580f, 220f);
            options.gameObject.AddComponent<Image>().color = UiKit.Navy;
            UiKit.Place(UiKit.Label(options, "CÔNG CỤ BẢN THỬ", 28, UiKit.Gold), 20f, 12f, 540f, 42f);
            Button toggleTools = UiKit.Button(tools, "Công cụ bản thử", UiKit.ButtonKind.Link, () => options.gameObject.SetActive(!options.gameObject.activeSelf), 26);
            UiKit.Fill((RectTransform)toggleTools.transform);
            InputField box = UiKit.Field(options, "Địa chỉ máy chủ", InputField.ContentType.Standard, null, 0, 26);
            var boxRect = (RectTransform)box.transform;
            UiKit.Place(boxRect, 20f, 64f, 540f, 66f);
            box.text = address;
            box.onEndEdit.AddListener(text =>
            {
                SetAddress(text);
                Say(api == null ? $"Địa chỉ không hợp lệ: {text}" : $"Dùng máy chủ {api.BaseUrl}", api == null ? box : null);
            });
            Button alone = UiKit.Button(options, "Luyện một mình", UiKit.ButtonKind.BlueSmall, () =>
            {
                Close();
                practice();
            }, 26);
            UiKit.Place(alone, 130f, 142f, 320f, 64f);
            options.gameObject.SetActive(false);
        }

        private bool Ready(InputField focus)
        {
            if (busy)
            {
                return false;
            }

            if (api == null)
            {
                return Say("Địa chỉ máy chủ không hợp lệ.", focus);
            }

            return true;
        }

        private bool Say(string text, InputField wrong, Color? color = null)
        {
            if (message != null)
            {
                message.text = text;
                message.color = color ?? (wrong != null ? UiKit.ErrorText : Color.white);
            }

            // The box is only marked, not selected: selecting it would pop the phone keyboard up over the message.
            if (wrong != null)
            {
                UiKit.MarkError(wrong, true);
            }

            return false;
        }

        /// <summary>Shows a refusal and marks the box it names, if that box is on this screen.</summary>
        private void Refused(ApiError error, InputField username = null, InputField contact = null, InputField code = null, InputField password = null, InputField name = null)
        {
            InputField wrong = error.field switch
            {
                "username" => username,
                "contact" => contact,
                "code" => code,
                "password" => password,
                "name" => name,
                _ => null,
            };

            Say(error.message, wrong, UiKit.ErrorText);

            if (error.error == "not_logged_in")
            {
                LogOutHere();
                ShowLogin(error.message);
            }
        }

        private void Run(IEnumerator call, bool quiet = false)
        {
            if (Application.isPlaying) StartCoroutine(Busy(call, quiet));
        }

        private IEnumerator Busy(IEnumerator call, bool quiet)
        {
            int version = screenVersion;
            pending++;
            busy = true;
            if (inputGroup != null) inputGroup.interactable = false;

            if (!quiet && message != null)
            {
                message.text = "Đang gửi...";
                message.color = Color.white;
            }

            try
            {
                // Drive nested enumerators so a response from a closed screen never reaches its callback.
                var stack = new Stack<IEnumerator>();
                stack.Push(call);
                try
                {
                    while (stack.Count > 0 && version == screenVersion)
                    {
                        IEnumerator current = stack.Peek();
                        if (!current.MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                        if (current.Current is IEnumerator nested) stack.Push(nested);
                        else yield return current.Current;
                    }
                }
                finally
                {
                    while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
                }
            }
            finally
            {
                if (version == screenVersion)
                {
                    pending = Mathf.Max(0, pending - 1);
                    busy = pending > 0;
                    if (inputGroup != null) inputGroup.interactable = !busy;
                }
            }
        }
    }
}
