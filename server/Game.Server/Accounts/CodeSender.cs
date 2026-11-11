using Game.Rules;

namespace Game.Server.Accounts;

/// <summary>Sends a verification code to an email address or a phone. A real email or SMS service plugs in here.</summary>
public interface ICodeSender
{
    string Delivery { get; }
    Task SendAsync(Contact contact, string purpose, string code, CancellationToken cancel = default);
}

/// <summary>
/// For now (decided 11/11/2026) codes are only written to the server log, so registration can be
/// tried before an email or SMS provider is chosen. Whoever runs the server reads the code there.
/// </summary>
public sealed class LogCodeSender : ICodeSender
{
    public string Delivery => "operator";
    private readonly ILogger<LogCodeSender> logger;

    public LogCodeSender(ILogger<LogCodeSender> logger)
    {
        this.logger = logger;
    }

    public Task SendAsync(Contact contact, string purpose, string code, CancellationToken cancel = default)
    {
        logger.LogWarning("Ma xac nhan ({Purpose}) cho {Contact}: {Code} (chua gui email/SMS that, chi ghi log)", purpose, contact.Masked, code);
        return Task.CompletedTask;
    }
}
