using System.Collections.Concurrent;
using Game.Rules;
using Game.Server.Db;

namespace Game.Server.Accounts;

/// <summary>A refusal the screens can show. Code is for the client to act on; Message is Vietnamese text for the player.</summary>
public sealed class AccountError : Exception
{
    public AccountError(int status, string code, string message, string? field = null, int? waitSec = null, int? attemptsLeft = null)
        : base(message)
    {
        Status = status;
        Code = code;
        Field = field;
        WaitSec = waitSec;
        AttemptsLeft = attemptsLeft;
    }

    /// <summary>The HTTP status the API answers with.</summary>
    public int Status { get; }

    public string Code { get; }

    /// <summary>Which input was wrong, so the screen can mark it: "username", "password", "contact", "code" or "name".</summary>
    public string? Field { get; }

    public int? WaitSec { get; }

    public int? AttemptsLeft { get; }
}

/// <summary>What a code is for. Each has its own code, so one sent for registering cannot reset a password.</summary>
public static class CodePurpose
{
    public const string Register = "register";
    public const string Link = "link";
    public const string Reset = "reset";

    public static bool IsKnown(string? purpose) => purpose is Register or Link or Reset;
}

/// <summary>An account as the client sees it: never the password hash, and only a masked contact.</summary>
public sealed record AccountView(string Username, string Kind, string? Contact);

/// <summary>A new session. GuestSecret is set only when "Chơi ngay" made a new guest, for the device to keep.</summary>
public sealed record LoginResult(string Token, AccountView Account, string? GuestSecret = null);

public sealed record CodeSent(string Contact, int ResendSec, int LifetimeSec, string Delivery);

/// <summary>
/// Registering, logging in, "Chơi ngay", linking a guest, forgetting a password, and the one character
/// an account has on this server. The HTTP API in <see cref="AccountApi"/> only turns JSON into these calls.
/// </summary>
public sealed class AccountService
{
    private readonly IAccountStore store;
    private readonly ICharacterStore characters;
    private readonly AccountConfig config;
    private readonly ServerConfig server;
    private readonly PasswordHasher hasher;
    private readonly ICodeSender sender;
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset First)> addresses = new();

    public AccountService(IAccountStore store, ICharacterStore characters, AccountConfig config, ServerConfig server, PasswordHasher hasher, ICodeSender sender, TimeProvider clock)
    {
        this.store = store;
        this.characters = characters;
        this.config = config;
        this.server = server;
        this.hasher = hasher;
        this.sender = sender;
        this.clock = clock;
    }

    public AccountConfig Config => config;

    private DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>
    /// Sends a code. For registering and linking, the contact must be free, and the username too when
    /// one is given, so the player hears about a taken name before waiting for a code. For a reset, the
    /// contact must belong to an account.
    /// </summary>
    public async Task<CodeSent> SendCodeAsync(string? purpose, string? contactText, string? username = null, CancellationToken cancel = default)
    {
        if (!CodePurpose.IsKnown(purpose))
        {
            throw new AccountError(400, "invalid", "Yêu cầu không hợp lệ.");
        }

        Contact contact = ReadContact(contactText);

        if (purpose == CodePurpose.Reset)
        {
            if (await store.FindByContactAsync(contact.Value, cancel) == null)
            {
                throw new AccountError(404, "contact_unknown", "Không có tài khoản nào dùng email hoặc số điện thoại này.", "contact");
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                await CheckFreeUsername(username, cancel);
            }

            await CheckFreeContact(contact, cancel);
        }

        CodeRecord? waiting = await store.FindCodeAsync(purpose!, contact.Value, cancel);

        if (waiting != null)
        {
            double wait = config.CodeResendSec - (Now - waiting.SentAt).TotalSeconds;

            if (wait > 0d)
            {
                int seconds = (int)Math.Ceiling(wait);
                throw new AccountError(429, "wait", $"Chờ {seconds} giây rồi gửi lại mã.", "code", waitSec: seconds);
            }
        }

        string code = Tokens.NewCode(config.CodeDigits);
        await store.SaveCodeAsync(new CodeRecord(purpose!, contact.Value, Tokens.HashCode(purpose!, contact.Value, code), 0, Now, Now.AddSeconds(config.CodeLifetimeSec)), cancel);
        await sender.SendAsync(contact, purpose!, code, cancel);
        return new CodeSent(contact.Masked, (int)Math.Ceiling(config.CodeResendSec), (int)Math.Ceiling(config.CodeLifetimeSec), sender.Delivery);
    }

    /// <summary>Makes a player account once the code sent to the contact is typed in, and logs it in.</summary>
    public async Task<LoginResult> RegisterAsync(string? usernameText, string? password, string? contactText, string? code, CancellationToken cancel = default)
    {
        string username = await CheckFreeUsername(usernameText, cancel);
        CheckPassword(password);
        Contact contact = ReadContact(contactText);
        await CheckFreeContact(contact, cancel);
        await UseCodeAsync(CodePurpose.Register, contact, code, cancel);

        AccountRecord account;

        try
        {
            account = await store.CreateAsync(username, hasher.Hash(password!), AccountKind.Player, contact.Value, null, cancel);
        }
        catch (DuplicateException taken)
        {
            throw Taken(taken.Field);
        }

        return await StartSession(account, null, cancel);
    }

    /// <summary>
    /// Logs in with a username and password. Too many wrong passwords lock the account for a while, and
    /// too many from one address lock that address, so names cannot be tried one after another.
    /// </summary>
    public async Task<LoginResult> LoginAsync(string? usernameText, string? password, string? address, CancellationToken cancel = default)
    {
        CheckAddress(address);
        string username = AccountRules.NormalizeUsername(usernameText);
        AccountRecord? account = await store.FindByUsernameAsync(username, cancel);

        if (account?.LockedUntil is DateTimeOffset until && until > Now)
        {
            throw Locked(until);
        }

        // A guest has no password, so Verify is false for it, and it takes as long as a real check.
        if (account == null || !hasher.Verify(password ?? string.Empty, account.PasswordHash))
        {
            FailAddress(address);

            if (account != null)
            {
                int failures = account.FailedLogins + 1;
                bool lockNow = failures >= config.LoginMaxFailures;
                DateTimeOffset? lockedUntil = lockNow ? Now.AddSeconds(config.LoginLockSec) : null;
                await store.SetLoginFailuresAsync(account.Id, lockNow ? 0 : failures, lockedUntil, cancel);

                if (lockNow)
                {
                    throw Locked(lockedUntil!.Value);
                }
            }

            throw new AccountError(401, "login_failed", "Sai tài khoản hoặc mật khẩu.");
        }

        if (account.FailedLogins != 0 || account.LockedUntil != null)
        {
            await store.SetLoginFailuresAsync(account.Id, 0, null, cancel);
        }

        return await StartSession(account, null, cancel);
    }

    /// <summary>
    /// "Chơi ngay". With the secret this device kept, logs in to its guest; without one, makes a new guest
    /// named <see cref="AccountConfig.GuestPrefix"/> and digits, and hands its secret over to be kept.
    /// </summary>
    public async Task<LoginResult> PlayNowAsync(string? guestSecret, CancellationToken cancel = default)
    {
        if (!string.IsNullOrEmpty(guestSecret))
        {
            AccountRecord? guest = await store.FindGuestAsync(Tokens.Hash(guestSecret), cancel);

            // Linking clears the secret, so a linked guest is logged in with its username from then on.
            return guest == null
                ? throw new AccountError(404, "guest_unknown", "Tài khoản khách trên máy này không còn hoặc đã được liên kết. Hãy đăng nhập, hoặc bấm Chơi ngay lần nữa để tạo tài khoản khách mới.")
                : await StartSession(guest, null, cancel);
        }

        string secret = Tokens.New();

        for (int tries = 0; ; tries++)
        {
            string username = config.GuestPrefix + Tokens.NewCode(config.GuestDigits);

            try
            {
                AccountRecord guest = await store.CreateAsync(username, string.Empty, AccountKind.Guest, null, Tokens.Hash(secret), cancel);
                return await StartSession(guest, secret, cancel);
            }
            catch (DuplicateException) when (tries < 20)
            {
                // Another guest has those digits; draw again.
            }
        }
    }

    /// <summary>The account a session token belongs to. Each use pushes the expiry back, at most once a day.</summary>
    public async Task<AccountRecord> AuthenticateAsync(string? token, CancellationToken cancel = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw NotLoggedIn();
        }

        byte[] hash = Tokens.Hash(token);
        SessionRecord? session = await store.FindSessionAsync(hash, Now, cancel);
        AccountRecord? account = session == null ? null : await store.FindByIdAsync(session.AccountId, cancel);

        if (session == null || account == null)
        {
            throw NotLoggedIn();
        }

        DateTimeOffset renewed = Now.AddDays(config.SessionLifetimeDays);

        if (renewed - session.ExpiresAt > TimeSpan.FromDays(1))
        {
            await store.SaveSessionAsync(hash, account.Id, renewed, cancel);
        }

        return account;
    }

    public async Task LogoutAsync(string? token, CancellationToken cancel = default)
    {
        if (!string.IsNullOrEmpty(token))
        {
            await store.DeleteSessionAsync(Tokens.Hash(token), cancel);
        }
    }

    /// <summary>Gives a guest a username, password and verified contact. Its id stays, so its characters stay with it.</summary>
    public async Task<AccountView> LinkAsync(string? token, string? usernameText, string? password, string? contactText, string? code, CancellationToken cancel = default)
    {
        AccountRecord account = await AuthenticateAsync(token, cancel);

        if (account.Kind != AccountKind.Guest)
        {
            throw new AccountError(409, "not_guest", "Tài khoản này đã được liên kết.");
        }

        string username = await CheckFreeUsername(usernameText, cancel);
        CheckPassword(password);
        Contact contact = ReadContact(contactText);
        await CheckFreeContact(contact, cancel);
        await UseCodeAsync(CodePurpose.Link, contact, code, cancel);

        try
        {
            return View(await store.PromoteGuestAsync(account.Id, username, hasher.Hash(password!), contact.Value, cancel));
        }
        catch (DuplicateException taken)
        {
            throw Taken(taken.Field);
        }
    }

    /// <summary>Sets a new password with a code sent to the account's contact, and logs out every session it had.</summary>
    public async Task ResetPasswordAsync(string? contactText, string? code, string? password, CancellationToken cancel = default)
    {
        Contact contact = ReadContact(contactText);
        CheckPassword(password);
        AccountRecord account = await store.FindByContactAsync(contact.Value, cancel)
            ?? throw new AccountError(404, "contact_unknown", "Không có tài khoản nào dùng email hoặc số điện thoại này.", "contact");
        await UseCodeAsync(CodePurpose.Reset, contact, code, cancel);
        await store.SetPasswordAsync(account.Id, hasher.Hash(password!), cancel);
        await store.DeleteSessionsAsync(account.Id, cancel);
    }

    /// <summary>The account's character on this server, or null when it has none yet.</summary>
    public Task<CharacterRecord?> CharacterAsync(AccountRecord account, CancellationToken cancel = default)
    {
        return characters.FindByAccountAsync(account.Id, server.ServerNumber, cancel);
    }

    /// <summary>Makes the account's one character on this server.</summary>
    public async Task<CharacterRecord> CreateCharacterAsync(AccountRecord account, int serverNumber, string? nameText, CancellationToken cancel = default)
    {
        if (serverNumber != server.ServerNumber)
        {
            throw new AccountError(404, "server_unknown", "Máy chủ này không có.");
        }

        AccountProblem problem = AccountRules.CheckCharacterName(nameText, config);

        if (problem != AccountProblem.None)
        {
            throw Invalid(problem, "name");
        }

        try
        {
            return await characters.CreateForAccountAsync(account.Id, serverNumber, AccountRules.NormalizeCharacterName(nameText), cancel);
        }
        catch (DuplicateException taken)
        {
            throw taken.Field == "name"
                ? new AccountError(409, "name_taken", "Tên nhân vật đã có người dùng trên máy chủ này.", "name")
                : new AccountError(409, "character_exists", "Tài khoản đã có nhân vật trên máy chủ này.");
        }
    }

    public static AccountView View(AccountRecord account)
    {
        string? contact = account.Contact == null ? null : AccountRules.ParseContact(account.Contact)?.Masked;
        return new AccountView(account.Username, account.Kind == AccountKind.Guest ? "guest" : "player", contact);
    }

    private async Task<LoginResult> StartSession(AccountRecord account, string? guestSecret, CancellationToken cancel)
    {
        string token = Tokens.New();
        await store.SaveSessionAsync(Tokens.Hash(token), account.Id, Now.AddDays(config.SessionLifetimeDays), cancel);
        return new LoginResult(token, View(account), guestSecret);
    }

    /// <summary>
    /// Checks a code and uses it up. A wrong code counts against it; after the last attempt it is gone and
    /// a new one must be sent. The comparison takes the same time however much of the code is right.
    /// </summary>
    private async Task UseCodeAsync(string purpose, Contact contact, string? code, CancellationToken cancel)
    {
        CodeRecord? waiting = await store.FindCodeAsync(purpose, contact.Value, cancel);

        if (waiting == null || waiting.ExpiresAt <= Now || waiting.Attempts >= config.CodeMaxAttempts)
        {
            throw new AccountError(400, "code_expired", "Mã xác nhận đã hết hạn hoặc chưa được gửi. Hãy gửi mã mới.", "code");
        }

        byte[] typed = Tokens.HashCode(purpose, contact.Value, (code ?? string.Empty).Trim());

        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(typed, waiting.CodeHash))
        {
            int attempts = waiting.Attempts + 1;
            int left = config.CodeMaxAttempts - attempts;

            if (left <= 0)
            {
                await store.DeleteCodeAsync(purpose, contact.Value, cancel);
                throw new AccountError(400, "code_expired", "Nhập sai mã quá nhiều lần. Hãy gửi mã mới.", "code", attemptsLeft: 0);
            }

            await store.SetCodeAttemptsAsync(purpose, contact.Value, attempts, cancel);
            throw new AccountError(400, "code_wrong", $"Mã xác nhận không đúng, còn {left} lần thử.", "code", attemptsLeft: left);
        }

        await store.DeleteCodeAsync(purpose, contact.Value, cancel);
    }

    private async Task<string> CheckFreeUsername(string? text, CancellationToken cancel)
    {
        AccountProblem problem = AccountRules.CheckUsername(text, config);

        if (problem != AccountProblem.None)
        {
            throw Invalid(problem, "username");
        }

        string username = AccountRules.NormalizeUsername(text);
        return await store.FindByUsernameAsync(username, cancel) == null ? username : throw Taken("username");
    }

    private async Task CheckFreeContact(Contact contact, CancellationToken cancel)
    {
        if (await store.FindByContactAsync(contact.Value, cancel) != null)
        {
            throw Taken("contact");
        }
    }

    private void CheckPassword(string? password)
    {
        AccountProblem problem = AccountRules.CheckPassword(password, config);

        if (problem != AccountProblem.None)
        {
            throw Invalid(problem, "password");
        }
    }

    private Contact ReadContact(string? text)
    {
        return AccountRules.ParseContact(text) ?? throw Invalid(AccountProblem.ContactInvalid, "contact");
    }

    /// <summary>Counts a failed login against the address; the address waits once it reaches the limit.</summary>
    private void FailAddress(string? address)
    {
        if (address == null)
        {
            return;
        }

        DateTimeOffset now = Now;
        addresses.AddOrUpdate(
            address,
            _ => (1, now),
            (_, entry) => (now - entry.First).TotalSeconds > config.LoginLockSec ? (1, now) : (entry.Failures + 1, entry.First));
    }

    private void CheckAddress(string? address)
    {
        if (address != null && addresses.TryGetValue(address, out var entry) && entry.Failures >= config.AddressMaxFailures)
        {
            DateTimeOffset until = entry.First.AddSeconds(config.LoginLockSec);

            if (until > Now)
            {
                throw Locked(until);
            }

            addresses.TryRemove(address, out _);
        }
    }

    private AccountError Invalid(AccountProblem problem, string field) => new(400, "invalid", AccountRules.Message(problem, config), field);

    private static AccountError Taken(string field) => field switch
    {
        "contact" => new AccountError(409, "contact_taken", "Email hoặc số điện thoại này đã dùng cho tài khoản khác.", "contact"),
        _ => new AccountError(409, "username_taken", "Tài khoản này đã có người dùng.", "username"),
    };

    private AccountError Locked(DateTimeOffset until)
    {
        int seconds = Math.Max(1, (int)Math.Ceiling((until - Now).TotalSeconds));
        int minutes = (seconds + 59) / 60;
        return new AccountError(429, "locked", $"Đăng nhập sai quá nhiều lần. Thử lại sau {minutes} phút.", waitSec: seconds);
    }

    private static AccountError NotLoggedIn() => new(401, "not_logged_in", "Phiên đăng nhập đã hết. Hãy đăng nhập lại.");
}
