using System.Security.Cryptography;
using backend_dickifier.Data;
using backend_dickifier.DTOs;
using backend_dickifier.Entities;
using backend_dickifier.Services;
using backend_dickifier.Utils;
using Microsoft.EntityFrameworkCore;

namespace backend_dickifier.Security;

public sealed class SessionService(DickifierDbContext db, ResourceQuota quota, IJwtService jwt, JwtOptions settings)
{
    public const string CookieName = "dickifier.refresh";
    private const string CookiePath = "/api/dickifier/auth";

    public async Task<(RefreshSession Session, string Raw)> CreateAsync(User user, HttpContext context, CancellationToken ct,
        Guid? family = null, DateTime? expires = null)
    {
        await quota.Charge(quota.Day($"session-ip:{quota.Client(context)}"),1,0,500,0,true,ct);
        await quota.Charge(quota.Day($"session-user:{user.Id}"),1,0,300,0,true,ct);
        await quota.Charge(quota.Day("sessions-global"),1,0,20000,0,true,ct);
        var raw = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var session = new RefreshSession
        {
            UserId = user.Id, FamilyId = family ?? Guid.NewGuid(), TokenHash = TokenUtils.ComputeSha256Hash(raw),
            CreatedAt = DateTime.UtcNow, ExpiresAt = expires ?? DateTime.UtcNow.AddDays(settings.RefreshDays)
        };
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return (session, raw);
    }

    public AuthResponse Response(User user, RefreshSession session) =>
        new(jwt.GenerateToken(user, session.FamilyId), new(user.Id,user.Username,user.Email,user.CreatedAt),
            DateTime.UtcNow.AddMinutes(settings.AccessMinutes));

    public void SetCookie(HttpContext context, string raw, DateTime expires)
    {
        context.Response.Cookies.Append(CookieName,raw,new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = CookiePath,
            Expires = new DateTimeOffset(expires), IsEssential = true
        });
    }
    public void ClearCookie(HttpContext context) => context.Response.Cookies.Delete(CookieName,
        new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = CookiePath });

    public async Task<AuthResponse?> RotateAsync(HttpContext context, CancellationToken ct)
    {
        var raw = context.Request.Cookies[CookieName];
        if (raw is null || raw.Length != 64) { ClearCookie(context); return null; }
        var hash = TokenUtils.ComputeSha256Hash(raw);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var old = await db.RefreshSessions.AsNoTracking().SingleOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (old is null) { ClearCookie(context); return null; }
        if (old.ExpiresAt <= DateTime.UtcNow || old.RevokedAt is not null)
        {
            await RevokeFamily(old.FamilyId,ct);
            await transaction.CommitAsync(ct);
            ClearCookie(context);
            return null;
        }
        var now = DateTime.UtcNow;
        var claimed = await db.RefreshSessions.Where(s => s.Id == old.Id && s.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(s => s.RevokedAt,now),ct);
        if (claimed != 1)
        {
            await RevokeFamily(old.FamilyId,ct);
            await transaction.CommitAsync(ct);
            ClearCookie(context);
            return null;
        }
        var user = await db.Users.AsNoTracking().Where(u => u.Id == old.UserId)
            .Select(u => new User { Id=u.Id, Username=u.Username, Email=u.Email, CreatedAt=u.CreatedAt }).SingleAsync(ct);
        var fresh = await CreateAsync(user,context,ct,old.FamilyId,old.ExpiresAt);
        await transaction.CommitAsync(ct);
        SetCookie(context,fresh.Raw,fresh.Session.ExpiresAt);
        return Response(user,fresh.Session);
    }

    public Task<int> RevokeFamily(Guid family, CancellationToken ct) => db.RefreshSessions
        .Where(s => s.FamilyId == family && s.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(s => s.RevokedAt,DateTime.UtcNow),ct);

    public async Task LogoutAsync(HttpContext context, CancellationToken ct)
    {
        var raw = context.Request.Cookies[CookieName];
        if (raw is { Length: 64 })
        {
            var hash = TokenUtils.ComputeSha256Hash(raw);
            var family = await db.RefreshSessions.Where(s => s.TokenHash == hash).Select(s => (Guid?)s.FamilyId).SingleOrDefaultAsync(ct);
            if (family.HasValue) await RevokeFamily(family.Value,ct);
        }
        ClearCookie(context);
    }
}
