using backend_dickifier.Services;
using backend_dickifier.Utils;
using Xunit;

namespace backend_dickifier.Tests;

public class SecurityAndTokenTests
{
    [Fact]
    public void GenerateSecureToken_ReturnsExpectedLengthAndComplexity()
    {
        var token1 = TokenUtils.GenerateSecureToken(32);
        var token2 = TokenUtils.GenerateSecureToken(32);

        Assert.Equal(32, token1.Length);
        Assert.Equal(32, token2.Length);
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void ComputeSha256Hash_IsDeterministicAndValidHex()
    {
        var input = "secret-token-12345";
        var hash1 = TokenUtils.ComputeSha256Hash(input);
        var hash2 = TokenUtils.ComputeSha256Hash(input);

        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash2);
        Assert.Matches("^[0-9a-f]{64}$", hash1);
    }

    [Fact]
    public void PasswordHasher_HashesAndVerifiesCorrectly()
    {
        var hasher = new PasswordHasher();
        var password = "SafePassword2026!";

        var hash = hasher.HashPassword(password);
        Assert.NotEmpty(hash);
        Assert.NotEqual(password, hash);

        Assert.True(hasher.VerifyPassword(password, hash));
        Assert.False(hasher.VerifyPassword("WrongPassword", hash));
    }
}
