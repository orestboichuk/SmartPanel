using System.Security.Cryptography;

namespace SmartPanel.Api.Auth;

public static class PasswordHasher
{
    public static (string Hash, string Salt) HashPassword(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var hash = HashPasswordWithSalt(password, saltBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(saltBytes));
    }

    public static bool VerifyPassword(string password, string hashBase64, string saltBase64)
    {
        try
        {
            var salt = Convert.FromBase64String(saltBase64);
            var expectedHash = Convert.FromBase64String(hashBase64);
            var actualHash = HashPasswordWithSalt(password, salt);
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] HashPasswordWithSalt(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);
    }
}
