using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ControlMenu.Services;

public interface IEmailService
{
    Task<(bool Success, string? Error)> SendAsync(string to, string subject, string body, CancellationToken ct = default);
    Task<(bool Success, string? Error)> SendTestAsync(CancellationToken ct = default);
}

public class EmailService : IEmailService
{
    private readonly IConfigurationService _config;

    public EmailService(IConfigurationService config)
    {
        _config = config;
    }

    public async Task<(bool Success, string? Error)> SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var server = await _config.GetSettingAsync("smtp-server");
        var portStr = await _config.GetSettingAsync("smtp-port");
        var username = await _config.GetSettingAsync("smtp-username");
        var password = await _config.GetSecretAsync("smtp-password");
        var from = await _config.GetSettingAsync("smtp-from-email");

        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return (false, "SMTP not configured. Set server, username, and password in Settings > General.");

        if (string.IsNullOrWhiteSpace(from) || !from.Contains('@'))
            return (false, "From email not configured. Set an authorized sender address in Settings > General.");

        if (string.IsNullOrWhiteSpace(to) || !to.Contains('@'))
            return (false, $"Invalid recipient address: \"{to}\". Set a valid notification email in Settings > General.");

        var port = int.TryParse(portStr, out var p) ? p : 587;

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            // Enforce TLS. SecureSocketOptions.StartTls (NOT StartTlsWhenAvailable) requires
            // the server to offer STARTTLS and throws rather than falling back to cleartext;
            // port 465 uses implicit TLS (SMTPS), which the obsolete System.Net.Mail.SmtpClient
            // this replaces could not do at all.
            await client.ConnectAsync(server, port, ResolveSecureSocketOptions(port), ct);
            await client.AuthenticateAsync(username, password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Maps an SMTP port to a TLS-enforcing connection mode: implicit TLS on 465 (SMTPS),
    /// hard-required STARTTLS otherwise. Never returns <see cref="SecureSocketOptions.None"/>,
    /// <see cref="SecureSocketOptions.Auto"/>, or <see cref="SecureSocketOptions.StartTlsWhenAvailable"/>,
    /// all of which permit a cleartext session.
    /// </summary>
    internal static SecureSocketOptions ResolveSecureSocketOptions(int port)
        => port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

    public async Task<(bool Success, string? Error)> SendTestAsync(CancellationToken ct = default)
    {
        var to = await _config.GetSettingAsync("notification-email");
        if (string.IsNullOrEmpty(to))
            return (false, "Notification email not configured. Set it in Settings > General.");

        return await SendAsync(to, "Control Menu — Test Email",
            $"This is a test email from Control Menu.\n\nSent at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.", ct);
    }
}
