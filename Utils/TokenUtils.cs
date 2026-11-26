using System.Security.Cryptography;
using System.Text;

namespace backend_dickifier.Utils;

public static class TokenUtils
{
    private const string TokenChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>
    /// Genera un token aleatorio seguro de 32 caracteres criptográficos.
    /// </summary>
    public static string GenerateSecureToken(int length = 32)
    {
        return RandomNumberGenerator.GetString(TokenChars,length);
    }
    public static bool Matches(string? token, string? expected)
    {
        if (token is null || token.Length is < 24 or > 128 || expected?.Length != 64) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(ComputeSha256Hash(token)),Encoding.ASCII.GetBytes(expected.ToLowerInvariant()));
    }

    /// <summary>
    /// Calcula el hash SHA-256 en formato hexadecimal en minúsculas.
    /// </summary>
    public static string ComputeSha256Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
