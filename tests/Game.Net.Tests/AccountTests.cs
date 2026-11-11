using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Game.Rules;
using Game.Server.Accounts;
using Game.Server.Db;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Game.Net.Tests;

internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
}

/// <summary>Keeps the codes instead of sending them, as the log sender would show them to whoever runs the server.</summary>
internal sealed class KeptCodes : ICodeSender
{
    public string Delivery { get; set; } = "operator";
    private readonly Dictionary<(string, string), string> codes = new();

    public int Sent { get; private set; }

    public Task SendAsync(Contact contact, string purpose, string code, CancellationToken cancel = default)
    {
        codes[(purpose, contact.Value)] = code;
        Sent++;
        return Task.CompletedTask;
    }

    public string For(string purpose, string contact) => codes[(purpose, AccountRules.ParseContact(contact)!.Value.Value)];
}

internal sealed class AccountHarness
{
    public AccountHarness(IAccountStore store, ICharacterStore characters)
    {
        Store = store;
        Config = GameConfig.ParseAccounts(File.ReadAllText(Path.Combine(TestConfig.Dir, GameConfig.AccountsFile)));
        Service = new AccountService(store, characters, Config, TestConfig.Server(), new PasswordHasher(iterations: 1_000), Codes, Clock);
    }

    public IAccountStore Store { get; }
    public AccountConfig Config { get; }
    public AccountService Service { get; }
    public FakeClock Clock { get; } = new();
    public KeptCodes Codes { get; } = new();

    /// <summary>Usernames and contacts of their own for each test, so tests sharing a database never collide.</summary>
    public static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    public static string Email() => Unique("u") + "@vidu.vn";

    public async Task<LoginResult> RegisterAsync(string username, string password, string contact)
    {
        await Service.SendCodeAsync(CodePurpose.Register, contact, username);
        return await Service.RegisterAsync(username, password, contact, Codes.For(CodePurpose.Register, contact));
    }
}

/// <summary>The same expectations whether accounts live in memory or in PostgreSQL.</summary>
internal static class AccountContract
{
    private static async Task<AccountError> Refused(Func<Task> call) => await Assert.ThrowsAsync<AccountError>(call);

    public static async Task RegisteringNeedsTheCodeSentToTheContact(AccountHarness test)
    {
        string username = AccountHarness.Unique("kiem_");
        string email = AccountHarness.Email();

        CodeSent sent = await test.Service.SendCodeAsync(CodePurpose.Register, " " + email.ToUpperInvariant(), username);
        string code = test.Codes.For(CodePurpose.Register, email);
        string wrong = code == "000000" ? "111111" : "000000";
        AccountError error = await Refused(() => test.Service.RegisterAsync(username, "matkhau1", email, wrong));
        LoginResult login = await test.Service.RegisterAsync(username.ToUpperInvariant(), "matkhau1", email, code);
        AccountRecord account = await test.Service.AuthenticateAsync(login.Token);

        Assert.Equal(60, sent.ResendSec);
        Assert.Equal(test.Codes.Delivery, sent.Delivery);
        Assert.EndsWith("@vidu.vn", sent.Contact);
        Assert.Contains("***", sent.Contact);
        Assert.Equal(("code_wrong", "code", 4), (error.Code, error.Field, error.AttemptsLeft));
        Assert.Equal(username, account.Username);
        Assert.Equal(AccountKind.Player, account.Kind);
        Assert.Equal(email, account.Contact);
        Assert.Equal(new AccountView(username, "player", sent.Contact), login.Account);
        Assert.Null(login.GuestSecret);
    }

    public static async Task APhoneNumberWorksLikeAnEmail(AccountHarness test)
    {
        string phone = "09" + Random.Shared.Next(10_000_000, 99_999_999);
        string username = AccountHarness.Unique("sdt_");

        LoginResult login = await test.RegisterAsync(username, "matkhau1", phone);
        AccountRecord account = await test.Service.AuthenticateAsync(login.Token);

        Assert.Equal("+84" + phone[1..], account.Contact);
        Assert.StartsWith("+84***", login.Account.Contact);
    }

    public static async Task ACodeIsUsedOnce(AccountHarness test)
    {
        string email = AccountHarness.Email();
        await test.Service.SendCodeAsync(CodePurpose.Register, email);
        string code = test.Codes.For(CodePurpose.Register, email);
        await test.Service.RegisterAsync(AccountHarness.Unique("mot_"), "matkhau1", email, code);

        // The contact is taken now, and the code is gone with it.
        AccountError again = await Refused(() => test.Service.RegisterAsync(AccountHarness.Unique("hai_"), "matkhau1", email, code));

        Assert.Equal("contact_taken", again.Code);
        Assert.Null(await test.Store.FindCodeAsync(CodePurpose.Register, email));
    }

    public static async Task ACodeWaitsBeforeItCanBeSentAgain(AccountHarness test)
    {
        string email = AccountHarness.Email();
        await test.Service.SendCodeAsync(CodePurpose.Register, email);

        test.Clock.Advance(20);
        AccountError tooSoon = await Refused(() => test.Service.SendCodeAsync(CodePurpose.Register, email));
        test.Clock.Advance(41);
        await test.Service.SendCodeAsync(CodePurpose.Register, email);

        Assert.Equal(("wait", 429, 40), (tooSoon.Code, tooSoon.Status, tooSoon.WaitSec));
        Assert.Equal(2, test.Codes.Sent);
    }

    public static async Task ACodeRunsOut(AccountHarness test)
    {
        string email = AccountHarness.Email();
        await test.Service.SendCodeAsync(CodePurpose.Register, email);
        string code = test.Codes.For(CodePurpose.Register, email);

        test.Clock.Advance(601);
        AccountError late = await Refused(() => test.Service.RegisterAsync(AccountHarness.Unique("tre_"), "matkhau1", email, code));

        Assert.Equal("code_expired", late.Code);
    }

    public static async Task FiveWrongCodesUseItUp(AccountHarness test)
    {
        string email = AccountHarness.Email();
        string username = AccountHarness.Unique("sai_");
        await test.Service.SendCodeAsync(CodePurpose.Register, email);
        string code = test.Codes.For(CodePurpose.Register, email);
        string wrong = code == "123456" ? "654321" : "123456";
        var left = new List<int?>();

        for (int i = 0; i < 5; i++)
        {
            left.Add((await Refused(() => test.Service.RegisterAsync(username, "matkhau1", email, wrong))).AttemptsLeft);
        }

        AccountError afterwards = await Refused(() => test.Service.RegisterAsync(username, "matkhau1", email, code));

        Assert.Equal(new int?[] { 4, 3, 2, 1, 0 }, left);
        Assert.Equal("code_expired", afterwards.Code);
    }

    public static async Task ACodeForOnePurposeDoesNotServeAnother(AccountHarness test)
    {
        string email = AccountHarness.Email();
        LoginResult guest = await test.Service.PlayNowAsync(null);
        await test.Service.SendCodeAsync(CodePurpose.Link, email);
        string code = test.Codes.For(CodePurpose.Link, email);

        AccountError error = await Refused(() => test.Service.RegisterAsync(AccountHarness.Unique("khac_"), "matkhau1", email, code));

        Assert.Equal("code_expired", error.Code);
        Assert.NotNull(guest.GuestSecret);
    }

    public static async Task TakenNamesAndContactsAreToldBeforeACodeIsSent(AccountHarness test)
    {
        string username = AccountHarness.Unique("trung_");
        string email = AccountHarness.Email();
        await test.RegisterAsync(username, "matkhau1", email);
        int sent = test.Codes.Sent;

        AccountError name = await Refused(() => test.Service.SendCodeAsync(CodePurpose.Register, AccountHarness.Email(), username));
        AccountError contact = await Refused(() => test.Service.SendCodeAsync(CodePurpose.Register, email, AccountHarness.Unique("moi_")));
        AccountError badName = await Refused(() => test.Service.SendCodeAsync(CodePurpose.Register, AccountHarness.Email(), "khach_123456"));
        AccountError badContact = await Refused(() => test.Service.SendCodeAsync(CodePurpose.Register, "khong-phai-email", null));

        Assert.Equal(("username_taken", "username"), (name.Code, name.Field));
        Assert.Equal(("contact_taken", "contact"), (contact.Code, contact.Field));
        Assert.Equal(("invalid", "username"), (badName.Code, badName.Field));
        Assert.Equal(AccountRules.Message(AccountProblem.UsernameReserved, test.Config), badName.Message);
        Assert.Equal(("invalid", "contact"), (badContact.Code, badContact.Field));
        Assert.Equal(sent, test.Codes.Sent);
    }

    public static async Task FiveWrongPasswordsLockTheAccountForAWhile(AccountHarness test)
    {
        string username = AccountHarness.Unique("khoa_");
        await test.RegisterAsync(username, "matkhau1", AccountHarness.Email());
        var codes = new List<string>();

        for (int i = 0; i < 5; i++)
        {
            codes.Add((await Refused(() => test.Service.LoginAsync(username, "sai_mat_khau", null))).Code);
        }

        AccountError locked = await Refused(() => test.Service.LoginAsync(username, "matkhau1", null));
        test.Clock.Advance(301);
        LoginResult login = await test.Service.LoginAsync(username, "matkhau1", null);

        Assert.Equal(new[] { "login_failed", "login_failed", "login_failed", "login_failed", "locked" }, codes);
        Assert.Equal(("locked", 429), (locked.Code, locked.Status));
        Assert.Equal(username, login.Account.Username);
        Assert.Equal(0, (await test.Store.FindByUsernameAsync(username))!.FailedLogins);
    }

    public static async Task OneAddressCannotTryNamesForever(AccountHarness test)
    {
        string username = AccountHarness.Unique("dia_");
        await test.RegisterAsync(username, "matkhau1", AccountHarness.Email());

        for (int i = 0; i < test.Config.AddressMaxFailures; i++)
        {
            await Refused(() => test.Service.LoginAsync(AccountHarness.Unique("khong_"), "matkhau1", "10.0.0.9"));
        }

        AccountError blocked = await Refused(() => test.Service.LoginAsync(username, "matkhau1", "10.0.0.9"));
        LoginResult elsewhere = await test.Service.LoginAsync(username, "matkhau1", "10.0.0.10");

        Assert.Equal("locked", blocked.Code);
        Assert.Equal(username, elsewhere.Account.Username);
    }

    public static async Task PlayNowMakesAGuestAndTheSameDeviceComesBackToIt(AccountHarness test)
    {
        LoginResult first = await test.Service.PlayNowAsync(null);
        LoginResult again = await test.Service.PlayNowAsync(first.GuestSecret);
        AccountError unknown = await Refused(() => test.Service.PlayNowAsync("bi-mat-khong-co"));
        AccountError noPassword = await Refused(() => test.Service.LoginAsync(first.Account.Username, string.Empty, null));

        Assert.Matches("^khach_[0-9]{6}$", first.Account.Username);
        Assert.Equal("guest", first.Account.Kind);
        Assert.Null(first.Account.Contact);
        Assert.Equal(first.Account, again.Account);
        Assert.Null(again.GuestSecret);
        Assert.NotEqual(first.Token, again.Token);
        Assert.Equal("guest_unknown", unknown.Code);
        Assert.Equal("login_failed", noPassword.Code);
    }

    public static async Task LinkingAGuestKeepsItsCharacter(AccountHarness test)
    {
        LoginResult guest = await test.Service.PlayNowAsync(null);
        AccountRecord account = await test.Service.AuthenticateAsync(guest.Token);
        string name = AccountHarness.Unique("Kh").Substring(0, 10);
        CharacterRecord character = await test.Service.CreateCharacterAsync(account, 1, name);
        string username = AccountHarness.Unique("lienket_");
        string email = AccountHarness.Email();

        await test.Service.SendCodeAsync(CodePurpose.Link, email, username);
        AccountView linked = await test.Service.LinkAsync(guest.Token, username, "matkhau1", email, test.Codes.For(CodePurpose.Link, email));
        AccountError secretGone = await Refused(() => test.Service.PlayNowAsync(guest.GuestSecret));
        AccountError twice = await Refused(() => test.Service.LinkAsync(guest.Token, AccountHarness.Unique("lan2_"), "matkhau1", AccountHarness.Email(), "000000"));
        LoginResult login = await test.Service.LoginAsync(username, "matkhau1", null);
        AccountRecord after = await test.Service.AuthenticateAsync(login.Token);

        Assert.Equal(("player", username), (linked.Kind, linked.Username));
        Assert.Equal(account.Id, after.Id);
        Assert.Equal(character.Id, (await test.Service.CharacterAsync(after))!.Id);
        Assert.Equal("guest_unknown", secretGone.Code);
        Assert.Equal("not_guest", twice.Code);
        Assert.Equal(account.Id, (await test.Service.AuthenticateAsync(guest.Token)).Id);
    }

    public static async Task ANewPasswordLogsOutEverySession(AccountHarness test)
    {
        string username = AccountHarness.Unique("quen_");
        string email = AccountHarness.Email();
        LoginResult first = await test.RegisterAsync(username, "matkhau1", email);
        LoginResult second = await test.Service.LoginAsync(username, "matkhau1", null);

        await test.Service.SendCodeAsync(CodePurpose.Reset, email.ToUpperInvariant());
        await test.Service.ResetPasswordAsync(email, test.Codes.For(CodePurpose.Reset, email), "matkhau2");

        AccountError firstGone = await Refused(() => test.Service.AuthenticateAsync(first.Token));
        AccountError secondGone = await Refused(() => test.Service.AuthenticateAsync(second.Token));
        AccountError oldPassword = await Refused(() => test.Service.LoginAsync(username, "matkhau1", null));
        LoginResult login = await test.Service.LoginAsync(username, "matkhau2", null);
        AccountError unknown = await Refused(() => test.Service.SendCodeAsync(CodePurpose.Reset, AccountHarness.Email()));

        Assert.Equal(("not_logged_in", 401), (firstGone.Code, firstGone.Status));
        Assert.Equal("not_logged_in", secondGone.Code);
        Assert.Equal("login_failed", oldPassword.Code);
        Assert.Equal(username, login.Account.Username);
        Assert.Equal("contact_unknown", unknown.Code);
    }

    public static async Task OneCharacterPerAccountAndOneNamePerServer(AccountHarness test)
    {
        AccountRecord first = await test.Service.AuthenticateAsync((await test.Service.PlayNowAsync(null)).Token);
        AccountRecord second = await test.Service.AuthenticateAsync((await test.Service.PlayNowAsync(null)).Token);
        string name = "Thủy" + Random.Shared.Next(1000, 9999);

        Assert.Null(await test.Service.CharacterAsync(first));
        CharacterRecord made = await test.Service.CreateCharacterAsync(first, 1, " " + name + " ");
        AccountError another = await Refused(() => test.Service.CreateCharacterAsync(first, 1, "Khac" + Random.Shared.Next(1000, 9999)));
        AccountError sameName = await Refused(() => test.Service.CreateCharacterAsync(second, 1, name.ToUpperInvariant()));
        AccountError banned = await Refused(() => test.Service.CreateCharacterAsync(second, 1, "Admin99"));
        AccountError spaces = await Refused(() => test.Service.CreateCharacterAsync(second, 1, "Kiếm Sĩ"));
        AccountError server = await Refused(() => test.Service.CreateCharacterAsync(second, 2, "Kiem" + Random.Shared.Next(1000, 9999)));

        Assert.Equal((name, 1, first.Id), (made.DisplayName, made.ServerNumber, made.AccountId));
        Assert.Equal(made.Id, (await test.Service.CharacterAsync(first))!.Id);
        Assert.Equal("character_exists", another.Code);
        Assert.Equal(("name_taken", "name"), (sameName.Code, sameName.Field));
        Assert.Equal(("invalid", "name"), (banned.Code, banned.Field));
        Assert.Equal(AccountRules.Message(AccountProblem.CharacterNameCharacters, test.Config), spaces.Message);
        Assert.Equal("server_unknown", server.Code);
        Assert.Null(await test.Service.CharacterAsync(second));
    }

    public static async Task SessionsLastWhileTheyAreUsed(AccountHarness test)
    {
        LoginResult used = await test.Service.PlayNowAsync(null);
        LoginResult idle = await test.Service.PlayNowAsync(null);

        test.Clock.Advance(TimeSpan.FromDays(20).TotalSeconds);
        await test.Service.AuthenticateAsync(used.Token);
        test.Clock.Advance(TimeSpan.FromDays(20).TotalSeconds);
        await test.Service.AuthenticateAsync(used.Token);
        AccountError idleGone = await Refused(() => test.Service.AuthenticateAsync(idle.Token));
        test.Clock.Advance(TimeSpan.FromDays(31).TotalSeconds);

        AccountError expired = await Refused(() => test.Service.AuthenticateAsync(used.Token));

        Assert.Equal("not_logged_in", idleGone.Code);
        Assert.Equal("not_logged_in", expired.Code);
    }

    public static async Task LoggingOutEndsOnlyThatSession(AccountHarness test)
    {
        LoginResult guest = await test.Service.PlayNowAsync(null);
        LoginResult other = await test.Service.PlayNowAsync(guest.GuestSecret);

        await test.Service.LogoutAsync(guest.Token);

        await Refused(() => test.Service.AuthenticateAsync(guest.Token));
        Assert.Equal(guest.Account.Username, (await test.Service.AuthenticateAsync(other.Token)).Username);
    }
}

public class InMemoryAccountTests
{
    private readonly AccountHarness test = new(new InMemoryAccountStore(), new InMemoryCharacterStore());

    [Fact]
    public async Task LogSenderReportsOperatorDeliveryWithoutReturningTheCode()
    {
        var sender = new LogCodeSender(Microsoft.Extensions.Logging.Abstractions.NullLogger<LogCodeSender>.Instance);
        var service = new AccountService(new InMemoryAccountStore(), new InMemoryCharacterStore(), test.Config,
            TestConfig.Server(), new PasswordHasher(iterations: 1_000), sender, test.Clock);
        CodeSent sent = await service.SendCodeAsync(CodePurpose.Register, "kiemthu@vidu.vn");
        Assert.Equal("operator", sent.Delivery);
        Assert.Equal("ki***@vidu.vn", sent.Contact);
    }

    [Fact] public Task RegisteringNeedsTheCodeSentToTheContact() => AccountContract.RegisteringNeedsTheCodeSentToTheContact(test);
    [Fact] public Task APhoneNumberWorksLikeAnEmail() => AccountContract.APhoneNumberWorksLikeAnEmail(test);
    [Fact] public Task ACodeIsUsedOnce() => AccountContract.ACodeIsUsedOnce(test);
    [Fact] public Task ACodeWaitsBeforeItCanBeSentAgain() => AccountContract.ACodeWaitsBeforeItCanBeSentAgain(test);
    [Fact] public Task ACodeRunsOut() => AccountContract.ACodeRunsOut(test);
    [Fact] public Task FiveWrongCodesUseItUp() => AccountContract.FiveWrongCodesUseItUp(test);
    [Fact] public Task ACodeForOnePurposeDoesNotServeAnother() => AccountContract.ACodeForOnePurposeDoesNotServeAnother(test);
    [Fact] public Task TakenNamesAndContactsAreToldBeforeACodeIsSent() => AccountContract.TakenNamesAndContactsAreToldBeforeACodeIsSent(test);
    [Fact] public Task FiveWrongPasswordsLockTheAccountForAWhile() => AccountContract.FiveWrongPasswordsLockTheAccountForAWhile(test);
    [Fact] public Task OneAddressCannotTryNamesForever() => AccountContract.OneAddressCannotTryNamesForever(test);
    [Fact] public Task PlayNowMakesAGuestAndTheSameDeviceComesBackToIt() => AccountContract.PlayNowMakesAGuestAndTheSameDeviceComesBackToIt(test);
    [Fact] public Task LinkingAGuestKeepsItsCharacter() => AccountContract.LinkingAGuestKeepsItsCharacter(test);
    [Fact] public Task ANewPasswordLogsOutEverySession() => AccountContract.ANewPasswordLogsOutEverySession(test);
    [Fact] public Task OneCharacterPerAccountAndOneNamePerServer() => AccountContract.OneCharacterPerAccountAndOneNamePerServer(test);
    [Fact] public Task SessionsLastWhileTheyAreUsed() => AccountContract.SessionsLastWhileTheyAreUsed(test);
    [Fact] public Task LoggingOutEndsOnlyThatSession() => AccountContract.LoggingOutEndsOnlyThatSession(test);
}

/// <summary>The same, on the real database, in a schema made for this run and dropped afterwards.</summary>
public sealed class PostgresAccountTests : IAsyncLifetime
{
    private readonly string schema = "t042_" + Guid.NewGuid().ToString("N");
    private NpgsqlDataSource? source;
    private AccountHarness test = null!;

    public async Task InitializeAsync()
    {
        if (new DatabaseFactAttribute().Skip != null)
        {
            return;
        }

        source = Database.CreateSource(DatabaseFactAttribute.ConnectionString);
        (ICharacterStore characters, _) = await Database.OpenAsync(source, schema);
        test = new AccountHarness(Database.Accounts(source, schema), characters);
    }

    public async Task DisposeAsync()
    {
        if (source == null)
        {
            return;
        }

        await using (NpgsqlCommand drop = source.CreateCommand($"drop schema if exists {Migrator.Quote(schema)} cascade"))
        {
            await drop.ExecuteNonQueryAsync();
        }

        await source.DisposeAsync();
    }

    [DatabaseFact] public Task RegisteringNeedsTheCodeSentToTheContact() => AccountContract.RegisteringNeedsTheCodeSentToTheContact(test);
    [DatabaseFact] public Task APhoneNumberWorksLikeAnEmail() => AccountContract.APhoneNumberWorksLikeAnEmail(test);
    [DatabaseFact] public Task ACodeIsUsedOnce() => AccountContract.ACodeIsUsedOnce(test);
    [DatabaseFact] public Task ACodeWaitsBeforeItCanBeSentAgain() => AccountContract.ACodeWaitsBeforeItCanBeSentAgain(test);
    [DatabaseFact] public Task ACodeRunsOut() => AccountContract.ACodeRunsOut(test);
    [DatabaseFact] public Task FiveWrongCodesUseItUp() => AccountContract.FiveWrongCodesUseItUp(test);
    [DatabaseFact] public Task ACodeForOnePurposeDoesNotServeAnother() => AccountContract.ACodeForOnePurposeDoesNotServeAnother(test);
    [DatabaseFact] public Task TakenNamesAndContactsAreToldBeforeACodeIsSent() => AccountContract.TakenNamesAndContactsAreToldBeforeACodeIsSent(test);
    [DatabaseFact] public Task FiveWrongPasswordsLockTheAccountForAWhile() => AccountContract.FiveWrongPasswordsLockTheAccountForAWhile(test);
    [DatabaseFact] public Task OneAddressCannotTryNamesForever() => AccountContract.OneAddressCannotTryNamesForever(test);
    [DatabaseFact] public Task PlayNowMakesAGuestAndTheSameDeviceComesBackToIt() => AccountContract.PlayNowMakesAGuestAndTheSameDeviceComesBackToIt(test);
    [DatabaseFact] public Task LinkingAGuestKeepsItsCharacter() => AccountContract.LinkingAGuestKeepsItsCharacter(test);
    [DatabaseFact] public Task ANewPasswordLogsOutEverySession() => AccountContract.ANewPasswordLogsOutEverySession(test);
    [DatabaseFact] public Task OneCharacterPerAccountAndOneNamePerServer() => AccountContract.OneCharacterPerAccountAndOneNamePerServer(test);
    [DatabaseFact] public Task SessionsLastWhileTheyAreUsed() => AccountContract.SessionsLastWhileTheyAreUsed(test);
    [DatabaseFact] public Task LoggingOutEndsOnlyThatSession() => AccountContract.LoggingOutEndsOnlyThatSession(test);

    [DatabaseFact]
    public async Task TheDatabaseKeepsOnlyHashes()
    {
        string username = AccountHarness.Unique("bam_");
        LoginResult login = await test.RegisterAsync(username, "matkhau1", AccountHarness.Email());
        LoginResult guest = await test.Service.PlayNowAsync(null);

        await using NpgsqlCommand read = source!.CreateCommand(
            $"select a.password_hash, s.token_hash from {Migrator.Quote(schema)}.accounts a join {Migrator.Quote(schema)}.sessions s on s.account_id = a.id where a.username = $1");
        read.Parameters.AddWithValue(username);
        await using NpgsqlDataReader row = await read.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());

        Assert.StartsWith("pbkdf2-sha256$1000$", row.GetString(0));
        Assert.DoesNotContain("matkhau1", row.GetString(0));
        Assert.Equal(Tokens.Hash(login.Token), row.GetFieldValue<byte[]>(1));
        await row.DisposeAsync();

        await using NpgsqlCommand secret = source.CreateCommand($"select guest_secret_hash from {Migrator.Quote(schema)}.accounts where username = $1");
        secret.Parameters.AddWithValue(guest.Account.Username);
        Assert.Equal(Tokens.Hash(guest.GuestSecret!), (byte[])(await secret.ExecuteScalarAsync())!);
    }
}

public class PasswordHasherTests
{
    private readonly PasswordHasher hasher = new(iterations: 1_000);

    [Fact]
    public void TheSamePasswordHashesDifferentlyEachTime()
    {
        string first = hasher.Hash("matkhau1");
        string second = hasher.Hash("matkhau1");

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("matkhau1", first));
        Assert.True(hasher.Verify("matkhau1", second));
        Assert.False(hasher.Verify("matkhau2", first));
        Assert.StartsWith("pbkdf2-sha256$1000$", first);
    }

    [Fact]
    public void AHashKeepsWorkingAfterTheIterationsAreRaised()
    {
        string old = hasher.Hash("matkhau1");

        Assert.True(new PasswordHasher(iterations: 2_000).Verify("matkhau1", old));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("pbkdf2-sha256$0$AA==$AA==")]
    public void NoProperHashMeansNoLogin(string stored)
    {
        Assert.False(hasher.Verify("", stored));
        Assert.False(hasher.Verify("plain", stored));
    }

    [Fact]
    public void CodesKeepTheirLeadingZeros()
    {
        Assert.All(Enumerable.Range(0, 200).Select(_ => Tokens.NewCode(6)), code => Assert.Matches("^[0-9]{6}$", code));
        Assert.NotEqual(Tokens.New(), Tokens.New());
        Assert.Equal(43, Tokens.New().Length);
    }
}

/// <summary>The HTTP API on the real server, as the login screens call it.</summary>
public sealed class AccountApiTests : IAsyncLifetime
{
    private WebApplication app = null!;
    private HttpClient http = null!;
    private readonly KeptCodes codes = new();

    public async Task InitializeAsync()
    {
        app = ServerApp.Build(new[] { "--urls", "http://127.0.0.1:0", "--config", TestConfig.Dir, "--web", "no-web-build", "--db", "memory", "--password-iterations", "1000" }, codes);
        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        http = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async Task DisposeAsync()
    {
        http.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> Call(HttpMethod method, string path, object? body = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using HttpResponseMessage response = await http.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("contact")]
    public async Task CodeResponseDescribesTheConfiguredSender(string delivery)
    {
        codes.Delivery = delivery;
        (HttpStatusCode status, JsonElement body) = await Call(HttpMethod.Post, "/api/codes",
            new { purpose = "register", contact = "delivery@vidu.vn" });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(delivery, body.GetProperty("delivery").GetString());
        Assert.Equal(4, body.EnumerateObject().Count());
        Assert.Equal(60, body.GetProperty("resendSec").GetInt32());
        Assert.Equal(600, body.GetProperty("lifetimeSec").GetInt32());
        Assert.Equal("de***@vidu.vn", body.GetProperty("contact").GetString());
    }

    [Fact]
    public async Task RegisterPickAServerAndMakeACharacter()
    {
        (HttpStatusCode sentStatus, JsonElement sent) = await Call(HttpMethod.Post, "/api/codes", new { purpose = "register", contact = "nguoichoi@vidu.vn", username = "nguoichoi" });
        string code = codes.For(CodePurpose.Register, "nguoichoi@vidu.vn");
        (HttpStatusCode wrongStatus, JsonElement wrong) = await Call(HttpMethod.Post, "/api/register", new { username = "nguoichoi", password = "matkhau1", contact = "nguoichoi@vidu.vn", code = "x" });
        (HttpStatusCode okStatus, JsonElement registered) = await Call(HttpMethod.Post, "/api/register", new { username = "nguoichoi", password = "matkhau1", contact = "nguoichoi@vidu.vn", code });
        string token = registered.GetProperty("token").GetString()!;
        (_, JsonElement before) = await Call(HttpMethod.Get, "/api/servers", token: token);
        (HttpStatusCode madeStatus, JsonElement made) = await Call(HttpMethod.Post, "/api/characters", new { server = 1, name = "Thủy" }, token);
        (_, JsonElement after) = await Call(HttpMethod.Get, "/api/servers", token: token);
        (HttpStatusCode anonymous, JsonElement refused) = await Call(HttpMethod.Get, "/api/servers");

        Assert.Equal(HttpStatusCode.OK, sentStatus);
        Assert.Equal("ng***@vidu.vn", sent.GetProperty("contact").GetString());
        Assert.Equal("operator", sent.GetProperty("delivery").GetString());
        Assert.False(sent.TryGetProperty("code", out _));
        Assert.Equal(HttpStatusCode.BadRequest, wrongStatus);
        Assert.Equal("code_wrong", wrong.GetProperty("error").GetString());
        Assert.Equal("code", wrong.GetProperty("field").GetString());
        Assert.Equal(HttpStatusCode.OK, okStatus);
        Assert.Equal("player", registered.GetProperty("account").GetProperty("kind").GetString());
        JsonElement server = before.GetProperty("servers")[0];
        Assert.Equal((1, "Thủy Nguyệt", true, "good"), (server.GetProperty("number").GetInt32(), server.GetProperty("name").GetString(), server.GetProperty("isNew").GetBoolean(), server.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, server.GetProperty("character").ValueKind);
        Assert.Equal(HttpStatusCode.OK, madeStatus);
        Assert.Equal("Thủy", made.GetProperty("character").GetProperty("name").GetString());
        Assert.Equal("Thủy", after.GetProperty("servers")[0].GetProperty("character").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous);
        Assert.Equal("not_logged_in", refused.GetProperty("error").GetString());
    }

    [Fact]
    public async Task PlayNowThenLinkThenLogInWithTheNewName()
    {
        (_, JsonElement guest) = await Call(HttpMethod.Post, "/api/guest", new { });
        string token = guest.GetProperty("token").GetString()!;
        string secret = guest.GetProperty("guestSecret").GetString()!;
        await Call(HttpMethod.Post, "/api/codes", new { purpose = "link", contact = "0912345678" });
        (HttpStatusCode linkStatus, JsonElement linked) = await Call(
            HttpMethod.Post, "/api/link", new { username = "dalienket", password = "matkhau1", contact = "0912345678", code = codes.For(CodePurpose.Link, "0912345678") }, token);
        (HttpStatusCode againStatus, JsonElement again) = await Call(HttpMethod.Post, "/api/guest", new { secret });
        (HttpStatusCode loginStatus, JsonElement login) = await Call(HttpMethod.Post, "/api/login", new { username = "DaLienKet", password = "matkhau1" });
        (_, JsonElement me) = await Call(HttpMethod.Get, "/api/me", token: login.GetProperty("token").GetString());
        (HttpStatusCode outStatus, _) = await Call(HttpMethod.Post, "/api/logout", token: token);
        (HttpStatusCode afterOut, _) = await Call(HttpMethod.Get, "/api/me", token: token);

        Assert.Equal(HttpStatusCode.OK, linkStatus);
        Assert.Equal("player", linked.GetProperty("account").GetProperty("kind").GetString());
        Assert.Equal("+84***5678", linked.GetProperty("account").GetProperty("contact").GetString());
        Assert.Equal(HttpStatusCode.NotFound, againStatus);
        Assert.Equal("guest_unknown", again.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.OK, loginStatus);
        Assert.Equal("dalienket", me.GetProperty("account").GetProperty("username").GetString());
        Assert.Equal(HttpStatusCode.OK, outStatus);
        Assert.Equal(HttpStatusCode.Unauthorized, afterOut);
    }

    [Fact]
    public async Task ThroughTheTunnelTheCloudflareAddressCounts()
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";
        var outside = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        outside.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.2");
        outside.Request.Headers["CF-Connecting-IP"] = "203.0.113.7";

        Assert.Equal("203.0.113.7", AccountApi.Address(context));
        Assert.Equal("198.51.100.2", AccountApi.Address(outside));
    }
}
