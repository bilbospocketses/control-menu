using Microsoft.AspNetCore.DataProtection;

namespace ControlMenu.Services;

public class SecretStore : ISecretStore
{
    private readonly IDataProtector _protector;

    public SecretStore(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ControlMenu.Settings");
    }

    public string Encrypt(string plaintext)
    {
        return _protector.Protect(plaintext);
    }

    public string Decrypt(string ciphertext)
    {
        return _protector.Unprotect(ciphertext);
    }
}
