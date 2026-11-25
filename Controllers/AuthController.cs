using System.Security.Claims;
using System.Text.Json;
using backend_dickifier.Data;
using backend_dickifier.DTOs;
using backend_dickifier.Entities;
using backend_dickifier.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend_dickifier.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/dickifier/[controller]")]
public class AuthController : ControllerBase
{
    private readonly DickifierDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;

    public AuthController(
        DickifierDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtService jwtService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
    }

    /// <summary>
    /// Registra un nuevo usuario seudónimo. El correo es opcional.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedUsername = request.Username.Trim();
        var exists = await _dbContext.Users.AnyAsync(u => u.Username.ToLower() == normalizedUsername.ToLower());
        if (exists)
        {
            return Conflict(new { message = $"El nombre de usuario '{normalizedUsername}' ya está en uso." });
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = normalizedUsername,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var token = _jwtService.GenerateToken(user);
        var userDto = new UserPublicDto(user.Id, user.Username, user.Email, user.CreatedAt);

        return CreatedAtAction(nameof(Me), new { }, new AuthResponse(token, userDto));
    }

    /// <summary>
    /// Inicia sesión con nombre de usuario y contraseña para obtener el token JWT.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedUsername = request.Username.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername);
        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Credenciales inválidas." });
        }

        var token = _jwtService.GenerateToken(user);
        var userDto = new UserPublicDto(user.Id, user.Username, user.Email, user.CreatedAt);

        return Ok(new AuthResponse(token, userDto));
    }

    /// <summary>
    /// Devuelve los datos del usuario autenticado y la lista de sus modelos creados.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .Include(u => u.Models.Where(m => m.Status != "flagged"))
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound(new { message = "Usuario no encontrado." });
        }

        var userDto = new UserPublicDto(user.Id, user.Username, user.Email, user.CreatedAt);
        var models = user.Models
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => {
                JsonElement anatomyElement;
                try {
                    anatomyElement = JsonDocument.Parse(string.IsNullOrWhiteSpace(m.AnatomyPayload) ? "{}" : m.AnatomyPayload).RootElement.Clone();
                } catch {
                    anatomyElement = JsonDocument.Parse("{}").RootElement.Clone();
                }
                return new ModelSummaryResponse(
                    m.Id,
                    m.ShortCode,
                    m.Name,
                    m.Status,
                    m.UserId,
                    user.Username,
                    m.LengthCm,
                    m.GirthCm,
                    m.VolumeCm3,
                    m.CurvatureDirection,
                    m.CurvatureDegrees,
                    m.CountryCode,
                    m.HeightCm,
                    m.WeightKg,
                    anatomyElement,
                    m.UpvotesCount,
                    m.ViewsCount,
                    m.CreatedAt,
                    m.UpdatedAt
                );
            }).ToList();

        JsonElement? workspaceDraft = null;
        if (!string.IsNullOrWhiteSpace(user.WorkspaceDraftJson))
        {
            try
            {
                workspaceDraft = JsonDocument.Parse(user.WorkspaceDraftJson).RootElement.Clone();
            }
            catch
            {
                workspaceDraft = null;
            }
        }

        return Ok(new UserProfileResponse(userDto, models, workspaceDraft, user.WorkspaceDraftUpdatedAt));
    }

    [Authorize]
    [HttpPut("workspace")]
    public async Task<IActionResult> SaveWorkspaceDraft([FromBody] SaveWorkspaceDraftRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return NotFound(new { message = "Usuario no encontrado." });

        user.WorkspaceDraftJson = request.ModelPayload.GetRawText();
        user.WorkspaceDraftUpdatedAt = request.UpdatedAt?.ToUniversalTime() ?? DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        return Ok(new { updatedAt = user.WorkspaceDraftUpdatedAt });
    }
}
