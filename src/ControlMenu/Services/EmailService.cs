using System.Net;
using System.Net.Mail;

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

        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return (false, "SMTP settings not configured. Set server, username, and password in Settings > Jellyfin.");

        var port = int.TryParse(portStr, out var p) ? p : 587;

        try
        {
            using var client = new SmtpClient(server, port)
            {
                Credentials = new NetworkCredential(username, password),
                EnableSsl = true
            };

            using var message = new MailMessage(username, to, subject, body);
            await client.SendMailAsync(message, ct);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Success, string? Error)> SendTestAsync(CancellationToken ct = default)
    {
        var to = await _config.GetSettingAsync("notification-email");
        if (string.IsNullOrEmpty(to))
            return (false, "Notification email not configured in Settings > Jellyfin.");

        return await SendAsync(to, "Control Menu — Test Email",
            $"This is a test email from Control Menu.\n\nSent at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC.", ct);
    }
}
