using System.Security.Cryptography;
using System.Text;

namespace SkyNetwork.Site.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256, 200 000 iterations, 16-byte salt, 32-byte hash — exactly what skynet-fsd
/// (src/accounts.cpp) stores, so both can check the same passwords.
/// </summary>
public static class PasswordHasher
{
    public const int Iterations = 200_000;
    public const int SaltLength = 16;
    public const int HashLength = 32;

    public static (byte[] Salt, byte[] Hash) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        return (salt, Derive(password, salt));
    }

    public static bool Verify(string password, byte[] salt, byte[] hash) =>
        salt.Length == SaltLength && hash.Length == HashLength &&
        CryptographicOperations.FixedTimeEquals(Derive(password, salt), hash);

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashLength);
}
