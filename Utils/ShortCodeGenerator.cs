using System.Security.Cryptography;

namespace backend_dickifier.Utils;

public static class ShortCodeGenerator
{
    // Alfabeto legible sin caracteres confusos (se excluyen 0, O, 1, I, L)
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    public const string Prefix = "DK-";

    public static string Generate(int length = 6)
    {
        return $"{Prefix}{RandomNumberGenerator.GetString(Alphabet,length)}";
    }

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var clean = input.Trim().ToUpperInvariant();
        if (!clean.StartsWith(Prefix) && clean.Length <= 9)
        {
            return $"{Prefix}{clean}";
        }
        return clean;
    }
}
