using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using backend_dickifier.Entities;
using backend_dickifier.Security;
using Microsoft.IdentityModel.Tokens;

namespace backend_dickifier.Services;

public interface IJwtService
{
    string GenerateToken(User user, Guid familyId);
}

public sealed class JwtService(JwtOptions settings) : IJwtService
{
    public string GenerateToken(User user, Guid familyId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new("sid", familyId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ]),
            Expires = DateTime.UtcNow.AddMinutes(settings.AccessMinutes),
            Issuer = settings.Issuer, Audience = settings.Audience,
            SigningCredentials = new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey)), SecurityAlgorithms.HmacSha256),
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
