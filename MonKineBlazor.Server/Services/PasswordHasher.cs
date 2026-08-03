using System;
using System.Security.Cryptography;
using System.Text;

namespace MonKineBlazor.Server.Services;

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        const int iterations = 100000;
        const int saltSize = 16;
        const int keySize = 32;

        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[saltSize];
        rng.GetBytes(salt);

        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, keySize);
        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string hashValue, string password)
    {
        if (string.IsNullOrWhiteSpace(hashValue) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var parts = hashValue.Split('.', 3);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var iterations) || iterations <= 0)
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var hash = Convert.FromBase64String(parts[2]);
        var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, hash.Length);
        return CryptographicOperations.FixedTimeEquals(hash, computedHash);
    }
}
