using ControlMenu.Services;
using MailKit.Security;

namespace ControlMenu.Tests.Services;

public class EmailServiceTests
{
    [Theory]
    [InlineData(465, SecureSocketOptions.SslOnConnect)]   // implicit TLS (SMTPS) — the obsolete System.Net.Mail client couldn't do this
    [InlineData(587, SecureSocketOptions.StartTls)]       // submission — STARTTLS hard-required
    [InlineData(25, SecureSocketOptions.StartTls)]
    [InlineData(2525, SecureSocketOptions.StartTls)]
    public void ResolveSecureSocketOptions_EnforcesTls_PerPort(int port, SecureSocketOptions expected)
    {
        Assert.Equal(expected, EmailService.ResolveSecureSocketOptions(port));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(465)]
    [InlineData(587)]
    [InlineData(1025)]
    [InlineData(2525)]
    public void ResolveSecureSocketOptions_NeverAllowsPlaintextOrOpportunisticDowngrade(int port)
    {
        var opts = EmailService.ResolveSecureSocketOptions(port);
        Assert.NotEqual(SecureSocketOptions.None, opts);                 // cleartext
        Assert.NotEqual(SecureSocketOptions.Auto, opts);                 // may negotiate down to cleartext
        Assert.NotEqual(SecureSocketOptions.StartTlsWhenAvailable, opts);// opportunistic — silent cleartext fallback
    }
}
