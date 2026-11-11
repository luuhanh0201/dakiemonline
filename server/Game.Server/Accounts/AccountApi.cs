using System.Net;
using Game.Rules;
using Game.Server.Db;

namespace Game.Server.Accounts;

/// <summary>
/// The HTTP JSON API the login screens call, on the game server's own address. A session token goes in
/// "Authorization: Bearer ...". A refusal answers with its HTTP status and
/// {error, message, field, waitSec, attemptsLeft}; message is Vietnamese text to show as it is.
/// </summary>
public static class AccountApi
{
    public sealed record CodeRequest(string? Purpose, string? Contact, string? Username);
    public sealed record RegisterRequest(string? Username, string? Password, string? Contact, string? Code);
    public sealed record LoginRequest(string? Username, string? Password);
    public sealed record GuestRequest(string? Secret);
    public sealed record ResetRequest(string? Contact, string? Code, string? Password);
    public sealed record CharacterRequest(int Server, string? Name);

    public static void Map(WebApplication app)
    {
        AccountService accounts = app.Services.GetRequiredService<AccountService>();
        GameServer game = app.Services.GetRequiredService<GameServer>();
        ServerConfig server = game.Server;
        RouteGroupBuilder api = app.MapGroup("/api");

        api.MapPost("/codes", (CodeRequest body, CancellationToken cancel) => Run(async () =>
            Results.Json(await accounts.SendCodeAsync(body.Purpose, body.Contact, body.Username, cancel))));

        api.MapPost("/register", (RegisterRequest body, CancellationToken cancel) => Run(async () =>
            Results.Json(await accounts.RegisterAsync(body.Username, body.Password, body.Contact, body.Code, cancel))));

        api.MapPost("/login", (LoginRequest body, HttpContext context, CancellationToken cancel) => Run(async () =>
            Results.Json(await accounts.LoginAsync(body.Username, body.Password, Address(context), cancel))));

        api.MapPost("/guest", (GuestRequest body, CancellationToken cancel) => Run(async () =>
            Results.Json(await accounts.PlayNowAsync(body.Secret, cancel))));

        api.MapPost("/reset", (ResetRequest body, CancellationToken cancel) => Run(async () =>
        {
            await accounts.ResetPasswordAsync(body.Contact, body.Code, body.Password, cancel);
            return Results.Json(new { ok = true });
        }));

        api.MapPost("/logout", (HttpContext context, CancellationToken cancel) => Run(async () =>
        {
            await accounts.LogoutAsync(Token(context), cancel);
            return Results.Json(new { ok = true });
        }));

        api.MapGet("/me", (HttpContext context, CancellationToken cancel) => Run(async () =>
        {
            AccountRecord account = await accounts.AuthenticateAsync(Token(context), cancel);
            return Results.Json(new { account = AccountService.View(account) });
        }));

        api.MapPost("/link", (RegisterRequest body, HttpContext context, CancellationToken cancel) => Run(async () =>
            Results.Json(new { account = await accounts.LinkAsync(Token(context), body.Username, body.Password, body.Contact, body.Code, cancel) })));

        api.MapGet("/servers", (HttpContext context, CancellationToken cancel) => Run(async () =>
        {
            AccountRecord account = await accounts.AuthenticateAsync(Token(context), cancel);
            CharacterRecord? character = await accounts.CharacterAsync(account, cancel);
            int capacity = server.Channels * server.PlayersPerChannel;
            int players = game.SessionCount;

            return Results.Json(new
            {
                servers = new[]
                {
                    new
                    {
                        number = server.ServerNumber,
                        name = server.ServerName,
                        isNew = server.ServerIsNew,
                        status = players >= capacity ? "full" : players * 5 >= capacity * 4 ? "busy" : "good",
                        character = character?.DisplayName,
                    },
                },
            });
        }));

        api.MapPost("/characters", (CharacterRequest body, HttpContext context, CancellationToken cancel) => Run(async () =>
        {
            AccountRecord account = await accounts.AuthenticateAsync(Token(context), cancel);
            CharacterRecord character = await accounts.CreateCharacterAsync(account, body.Server, body.Name, cancel);
            return Results.Json(new { character = new { name = character.DisplayName, server = character.ServerNumber } });
        }));
    }

    private static async Task<IResult> Run(Func<Task<IResult>> call)
    {
        try
        {
            return await call();
        }
        catch (AccountError error)
        {
            return Results.Json(
                new { error = error.Code, message = error.Message, field = error.Field, waitSec = error.WaitSec, attemptsLeft = error.AttemptsLeft },
                statusCode: error.Status);
        }
    }

    public static string? Token(HttpContext context)
    {
        string header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..].Trim() : null;
    }

    /// <summary>
    /// Who is asking, for the limit on failed logins. Through the cloudflared tunnel every request comes
    /// from this machine, so there the address Cloudflare reports is used; from anywhere else that header
    /// could be made up and is ignored.
    /// </summary>
    public static string? Address(HttpContext context)
    {
        IPAddress? remote = context.Connection.RemoteIpAddress;

        if (remote != null && IPAddress.IsLoopback(remote))
        {
            string forwarded = context.Request.Headers["CF-Connecting-IP"].ToString();

            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                return forwarded.Trim();
            }
        }

        return remote?.ToString();
    }
}
