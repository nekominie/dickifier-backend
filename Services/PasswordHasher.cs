namespace backend_dickifier.Services;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}

public class PasswordHasher : IPasswordHasher
{
    private static readonly SemaphoreSlim Work = new(2,2);
    public string HashPassword(string password)
    {
        if (!Work.Wait(0)) throw new Security.ResourceLimitException("Servidor ocupado.",1);
        try { return BCrypt.Net.BCrypt.EnhancedHashPassword(password, workFactor: 11); }
        finally { Work.Release(); }
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (!Work.Wait(0)) throw new Security.ResourceLimitException("Servidor ocupado.",1);
        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, passwordHash);
        }
        catch
        {
            return false;
        }
        finally { Work.Release(); }
    }
}
