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
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(length);
        foreach (var b in bytes)
        {
            sb.Append(TokenChars[b % TokenChars.Length]);
        }
        return sb.ToString();
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
