using System.Security.Claims;
using System.Text;
using backend_dickifier.Data;
using backend_dickifier.DTOs;
using backend_dickifier.Entities;
using backend_dickifier.Security;
using backend_dickifier.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend_dickifier.Controllers;

[ApiController]
[Route("api/auth")]
[Route("api/dickifier/auth")]
public sealed class AuthController(DickifierDbContext db, IPasswordHasher passwords, SessionService sessions,
    ResourceQuota quota, SecurityOptions settings, PayloadValidator payloads) : ControllerBase
{
    [HttpPost("register"), EnableRateLimiting("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var normalized = username.ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedUsername == normalized,ct))
            return Conflict(new { message = "El nombre de usuario ya está en uso.", code = "username_unavailable" });
        var user = new User
        {
            Username = username, NormalizedUsername = normalized, PasswordHash = passwords.HashPassword(request.Password),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant()
        };
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await quota.Charge(quota.Day($"register:{quota.Client(HttpContext)}"),1,0,10,0,true,ct);
        await quota.Charge("users:global",1,0,settings.MaximumUsers,0,false,ct);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            var issued = await sessions.CreateAsync(user,HttpContext,ct);
            await transaction.CommitAsync(ct);
            sessions.SetCookie(HttpContext,issued.Raw,issued.Session.ExpiresAt);
            return StatusCode(201,sessions.Response(user,issued.Session));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { message = "El nombre de usuario ya está en uso.", code = "username_unavailable" });
        }
    }
    public sealed class DraftLedger { public Guid Id { get; set; } public int WorkspaceBytes { get; set; } }

    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var normalized = request.Username.Trim().ToUpperInvariant();
        var user = await db.Users.AsNoTracking().Where(u => u.NormalizedUsername == normalized)
            .Select(u => new User { Id=u.Id, Username=u.Username, Email=u.Email, CreatedAt=u.CreatedAt, PasswordHash=u.PasswordHash })
            .SingleOrDefaultAsync(ct);
        if (user is null || !passwords.VerifyPassword(request.Password,user.PasswordHash))
            return Unauthorized(new { message = "Credenciales inválidas.", code = "invalid_credentials" });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var issued = await sessions.CreateAsync(user,HttpContext,ct);
        await transaction.CommitAsync(ct);
        sessions.SetCookie(HttpContext,issued.Raw,issued.Session.ExpiresAt);
        return Ok(sessions.Response(user,issued.Session));
    }

    [HttpPost("refresh"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var response = await sessions.RotateAsync(HttpContext,ct);
        return response is null ? Unauthorized(new { message = "La sesión ha caducado.", code = "session_expired" }) : Ok(response);
    }

    [HttpPost("logout"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await sessions.LogoutAsync(HttpContext,ct);
        return NoContent();
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new { u.Id,u.Username,u.Email,u.CreatedAt,u.WorkspaceBytes,u.WorkspaceDraftUpdatedAt }).SingleOrDefaultAsync(ct);
        if (user is null) return Unauthorized();
        var draft = user.WorkspaceBytes <= SecurityOptions.MaxPayloadBytes
            ? await db.Users.Where(u => u.Id == id).Select(u => u.WorkspaceDraftJson).SingleAsync(ct) : null;
        return Ok(new UserProfileResponse(new(user.Id,user.Username,user.Email,user.CreatedAt),[],
            string.IsNullOrEmpty(draft) ? null : ModelQueries.ReadPayload(draft),
            user.WorkspaceDraftUpdatedAt));
    }

    [Authorize, HttpGet("models")]
    public async Task<IActionResult> Models([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (page is < 1 or > 1000 || pageSize is < 1 or > 50) return BadRequest(new { message = "Paginación inválida." });
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Ok(await ModelQueries.Page(db.Models.AsNoTracking().Where(m => m.UserId == id && m.Status != "flagged")
            .OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id),page,pageSize,ct));
    }

    [Authorize, HttpPut("workspace"), EnableRateLimiting("write")]
    public async Task<IActionResult> SaveWorkspaceDraft(SaveWorkspaceDraftRequest request, CancellationToken ct)
    {
        if (!payloads.TryValidate(request.ModelPayload,true,out var json,out var error))
            return BadRequest(new { message = error, code = "invalid_payload" });
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Database.SqlQuery<DraftLedger>($"SELECT \"Id\",\"WorkspaceBytes\" FROM users WHERE \"Id\"={id} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (user is null) return Unauthorized();
        var bytes = Encoding.UTF8.GetByteCount(json);
        await quota.Charge("workspaces:global",0,bytes-user.WorkspaceBytes,int.MaxValue,settings.MaximumStoredBytes,false,ct);
        // La autoridad temporal es el servidor, no una fecha futura controlada por el cliente.
        var now = DateTime.UtcNow;
        await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(update => update
            .SetProperty(u => u.WorkspaceDraftJson,json).SetProperty(u => u.WorkspaceBytes,bytes)
            .SetProperty(u => u.WorkspaceDraftUpdatedAt,now),ct);
        await transaction.CommitAsync(ct);
        return Ok(new { updatedAt = now });
    }
}
