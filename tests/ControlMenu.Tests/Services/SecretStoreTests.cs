using ControlMenu.Services;
using Microsoft.AspNetCore.DataProtection;

namespace ControlMenu.Tests.Services;

public class SecretStoreTests
{
    private static SecretStore CreateSecretStore()
    {
        var provider = DataProtectionProvider.Create("ControlMenu-Tests");
        return new SecretStore(provider);
    }

    [Fact]
    public void Encrypt_ProducesDifferentStringThanInput()
    {
        var store = CreateSecretStore();
        var plaintext = "my-api-key-12345";
        var encrypted = store.Encrypt(plaintext);
        Assert.NotEqual(plaintext, encrypted);
        Assert.NotEmpty(encrypted);
    }

    [Fact]
    public void Decrypt_ReturnsOriginalPlaintext()
    {
        var store = CreateSecretStore();
        var plaintext = "my-api-key-12345";
        var encrypted = store.Encrypt(plaintext);
        var decrypted = store.Decrypt(encrypted);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void RoundTrip_EmptyString()
    {
        var store = CreateSecretStore();
        var encrypted = store.Encrypt("");
        var decrypted = store.Decrypt(encrypted);
        Assert.Equal("", decrypted);
    }

    [Fact]
    public void RoundTrip_SpecialCharacters()
    {
        var store = CreateSecretStore();
        var plaintext = "p@$$w0rd!#%&*()_+-=[]{}|;':\",./<>?";
        var encrypted = store.Encrypt(plaintext);
        var decrypted = store.Decrypt(encrypted);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SameInput_BothDecryptCorrectly()
    {
        var store = CreateSecretStore();
        var plaintext = "test-value";
        var encrypted1 = store.Encrypt(plaintext);
        var encrypted2 = store.Encrypt(plaintext);
        Assert.Equal(plaintext, store.Decrypt(encrypted1));
        Assert.Equal(plaintext, store.Decrypt(encrypted2));
    }
}
