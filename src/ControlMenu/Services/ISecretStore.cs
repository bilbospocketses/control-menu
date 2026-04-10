namespace ControlMenu.Services;

public interface ISecretStore
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
