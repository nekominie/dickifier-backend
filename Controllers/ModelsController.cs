using System.Security.Claims;
using System.Text.Json;
using backend_dickifier.Data;
using backend_dickifier.DTOs;
using backend_dickifier.Entities;
using backend_dickifier.Utils;
using backend_dickifier.Validators;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend_dickifier.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/dickifier/[controller]")]
public class ModelsController : ControllerBase
{
    private readonly DickifierDbContext _dbContext;

    public ModelsController(DickifierDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Lista pública paginada con filtros y ordenamiento del Repositorio Global.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ModelSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetModels(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string filter = "all",
        [FromQuery] string sortBy = "recent",
        [FromQuery] string? country = null,
        [FromQuery] string? search = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.Models
            .Include(m => m.User)
            .Where(m => m.Status != "flagged")
            .AsNoTracking();

        // Búsqueda por nombre o código único (ShortCode)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var normalizedCode = s.ToUpperInvariant();
            query = query.Where(m => EF.Functions.ILike(m.Name, $"%{s}%") || m.ShortCode.Contains(normalizedCode));
        }

        // Filtro por tipo de cuenta
        if (string.Equals(filter, "registered", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(m => m.Status == "registered");
        }
        else if (string.Equals(filter, "anonymous", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(m => m.Status == "anonymous");
        }

        // Filtro por país
        if (!string.IsNullOrWhiteSpace(country))
        {
            var c = country.Trim().ToUpperInvariant();
            query = query.Where(m => m.CountryCode == c);
        }

        // Ordenamiento
        query = sortBy.ToLowerInvariant() switch
        {
            "votes" => query.OrderByDescending(m => m.UpvotesCount).ThenByDescending(m => m.CreatedAt),
            "length" => query.OrderByDescending(m => m.LengthCm).ThenByDescending(m => m.CreatedAt),
            "girth" => query.OrderByDescending(m => m.GirthCm).ThenByDescending(m => m.CreatedAt),
            "volume" => query.OrderByDescending(m => m.VolumeCm3).ThenByDescending(m => m.CreatedAt),
            _ => query.OrderByDescending(m => m.CreatedAt) // "recent" por defecto
        };

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);

        var rawItems = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new {
                m.Id,
                m.ShortCode,
                m.Name,
                m.Status,
                m.UserId,
                AuthorUsername = m.User != null ? m.User.Username : null,
                m.LengthCm,
                m.GirthCm,
                m.VolumeCm3,
                m.CurvatureDirection,
                m.CurvatureDegrees,
                m.CountryCode,
                m.HeightCm,
                m.WeightKg,
                m.UpvotesCount,
                m.ViewsCount,
                m.CreatedAt,
                m.UpdatedAt,
                m.AnatomyPayload
            })
            .ToListAsync();

        var items = rawItems.Select(m => {
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
                m.AuthorUsername,
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

        return Ok(new PagedResult<ModelSummaryResponse>(items, total, page, pageSize, totalPages));
    }

    /// <summary>
    /// Obtiene un modelo completo por su ID (Guid) o Código Corto (ej. DK-8X2K)
    /// con su payload anatómico JSONB para Three.js. Incrementa las visitas.
    /// </summary>
    [HttpGet("{idOrCode}")]
    [ProducesResponseType(typeof(ModelDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetModelById(string idOrCode)
    {
        DickModel? model;
        if (Guid.TryParse(idOrCode, out var guidId))
        {
            model = await _dbContext.Models
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == guidId && m.Status != "flagged");
        }
        else
        {
            var normalizedCode = ShortCodeGenerator.Normalize(idOrCode);
            model = await _dbContext.Models
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.ShortCode == normalizedCode && m.Status != "flagged");
        }

        if (model == null)
        {
            return NotFound(new { message = "El modelo solicitado no existe o fue retirado." });
        }

        // Incrementar visitas de forma segura
        model.ViewsCount++;
        await _dbContext.SaveChangesAsync();

        JsonElement anatomyElement;
        try
        {
            anatomyElement = JsonDocument.Parse(string.IsNullOrWhiteSpace(model.AnatomyPayload) ? "{}" : model.AnatomyPayload).RootElement.Clone();
        }
        catch
        {
            anatomyElement = JsonDocument.Parse("{}").RootElement.Clone();
        }

        var response = new ModelDetailResponse(
            model.Id,
            model.ShortCode,
            model.Name,
            model.Status,
            model.UserId,
            model.User?.Username,
            model.LengthCm,
            model.GirthCm,
            model.VolumeCm3,
            model.CurvatureDirection,
            model.CurvatureDegrees,
            model.CountryCode,
            model.HeightCm,
            model.WeightKg,
            anatomyElement,
            model.UpvotesCount,
            model.ViewsCount,
            model.CreatedAt,
            model.UpdatedAt
        );

        return Ok(response);
    }

    /// <summary>
    /// Publica un nuevo modelo en el Repositorio Global.
    /// Si está autenticado, se asocia a su usuario. Si es anónimo, devuelve un editToken.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateModelResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateModel([FromBody] CreateModelRequest request)
    {
        // Validación de plausibilidad biológica y anti-trolls
        var validation = PlausibilityValidator.Validate(
            request.LengthCm,
            request.GirthCm,
            request.CurvatureDirection,
            request.CurvatureDegrees,
            request.Name,
            request.HeightCm,
            request.WeightKg,
            request.CountryCode
        );

        if (!validation.IsValid)
        {
            return BadRequest(new { message = validation.ErrorMessage });
        }

        // Cálculo volumétrico cilíndrico aproximado V = (girth^2 * length) / (4 * pi)
        var volumeCm3 = Math.Round((decimal)(Math.Pow((double)request.GirthCm, 2) * (double)request.LengthCm / (4 * Math.PI)), 1);

        Guid? userId = null;
        string? editToken = null;
        string? editTokenHash = null;
        string status;

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (User.Identity?.IsAuthenticated == true && Guid.TryParse(userIdClaim, out var parsedUserId))
        {
            userId = parsedUserId;
            status = "registered";
        }
        else
        {
            status = "anonymous";
            editToken = TokenUtils.GenerateSecureToken(32);
            editTokenHash = TokenUtils.ComputeSha256Hash(editToken);
        }

        string anatomyJson = "{}";
        if (request.AnatomyPayload.HasValue && request.AnatomyPayload.Value.ValueKind != JsonValueKind.Null && request.AnatomyPayload.Value.ValueKind != JsonValueKind.Undefined)
        {
            anatomyJson = request.AnatomyPayload.Value.GetRawText();
        }

        // Generación de ID Corto único permanente (DK-XXXX)
        string shortCode;
        do
        {
            shortCode = ShortCodeGenerator.Generate();
        } while (await _dbContext.Models.AnyAsync(m => m.ShortCode == shortCode));

        var model = new DickModel
        {
            Id = Guid.NewGuid(),
            ShortCode = shortCode,
            UserId = userId,
            EditTokenHash = editTokenHash,
            Name = request.Name.Trim(),
            Status = status,
            LengthCm = Math.Round(request.LengthCm, 1),
            GirthCm = Math.Round(request.GirthCm, 1),
            VolumeCm3 = volumeCm3,
            CurvatureDirection = request.CurvatureDirection.ToLowerInvariant(),
            CurvatureDegrees = request.CurvatureDegrees,
            CountryCode = string.IsNullOrWhiteSpace(request.CountryCode) ? null : request.CountryCode.Trim().ToUpperInvariant(),
            HeightCm = request.HeightCm.HasValue ? Math.Round(request.HeightCm.Value, 1) : null,
            WeightKg = request.WeightKg.HasValue ? Math.Round(request.WeightKg.Value, 1) : null,
            AnatomyPayload = anatomyJson,
            UpvotesCount = 0,
            ViewsCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Models.Add(model);
        await _dbContext.SaveChangesAsync();

        var response = new CreateModelResponse(
            model.Id,
            model.ShortCode,
            model.Name,
            model.Status,
            model.UserId,
            model.LengthCm,
            model.GirthCm,
            model.VolumeCm3,
            editToken, // Devuelto solo aquí para que el autor anónimo lo guarde
            model.CreatedAt
        );

        return CreatedAtAction(nameof(GetModelById), new { idOrCode = model.ShortCode }, response);
    }

    /// <summary>
    /// Actualiza un modelo existente. Requiere ser el usuario dueño o proveer el editToken.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ModelDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateModel(
        Guid id,
        [FromBody] UpdateModelRequest request,
        [FromHeader(Name = "X-Edit-Token")] string? headerEditToken)
    {
        var model = await _dbContext.Models
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (model == null)
        {
            return NotFound(new { message = "El modelo no existe." });
        }

        // Verificación de autorización
        if (!IsAuthorizedToModify(model, headerEditToken ?? request.EditToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tienes permiso para modificar este modelo. Se requiere ser el usuario propietario o proveer el token de edición válido." });
        }

        var newLength = request.LengthCm ?? model.LengthCm;
        var newGirth = request.GirthCm ?? model.GirthCm;
        var newDirection = request.CurvatureDirection ?? model.CurvatureDirection;
        var newDegrees = request.CurvatureDegrees ?? model.CurvatureDegrees;
        var newName = request.Name ?? model.Name;
        var newHeight = request.HeightCm ?? model.HeightCm;
        var newWeight = request.WeightKg ?? model.WeightKg;
        var newCountry = request.CountryCode ?? model.CountryCode;

        var validation = PlausibilityValidator.Validate(
            newLength,
            newGirth,
            newDirection,
            newDegrees,
            newName,
            newHeight,
            newWeight,
            newCountry
        );

        if (!validation.IsValid)
        {
            return BadRequest(new { message = validation.ErrorMessage });
        }

        model.Name = newName.Trim();
        model.LengthCm = Math.Round(newLength, 1);
        model.GirthCm = Math.Round(newGirth, 1);
        model.VolumeCm3 = Math.Round((decimal)(Math.Pow((double)model.GirthCm, 2) * (double)model.LengthCm / (4 * Math.PI)), 1);
        model.CurvatureDirection = newDirection.ToLowerInvariant();
        model.CurvatureDegrees = newDegrees;
        model.CountryCode = string.IsNullOrWhiteSpace(newCountry) ? null : newCountry.Trim().ToUpperInvariant();
        model.HeightCm = newHeight.HasValue ? Math.Round(newHeight.Value, 1) : null;
        model.WeightKg = newWeight.HasValue ? Math.Round(newWeight.Value, 1) : null;

        if (request.AnatomyPayload.HasValue && request.AnatomyPayload.Value.ValueKind != JsonValueKind.Null && request.AnatomyPayload.Value.ValueKind != JsonValueKind.Undefined)
        {
            model.AnatomyPayload = request.AnatomyPayload.Value.GetRawText();
        }

        model.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        JsonElement anatomyElement;
        try
        {
            anatomyElement = JsonDocument.Parse(model.AnatomyPayload).RootElement.Clone();
        }
        catch
        {
            anatomyElement = JsonDocument.Parse("{}").RootElement.Clone();
        }

        return Ok(new ModelDetailResponse(
            model.Id,
            model.ShortCode,
            model.Name,
            model.Status,
            model.UserId,
            model.User?.Username,
            model.LengthCm,
            model.GirthCm,
            model.VolumeCm3,
            model.CurvatureDirection,
            model.CurvatureDegrees,
            model.CountryCode,
            model.HeightCm,
            model.WeightKg,
            anatomyElement,
            model.UpvotesCount,
            model.ViewsCount,
            model.CreatedAt,
            model.UpdatedAt
        ));
    }

    /// <summary>
    /// Elimina un modelo. Requiere ser el usuario dueño o proveer el editToken.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteModel(
        Guid id,
        [FromHeader(Name = "X-Edit-Token")] string? headerEditToken,
        [FromQuery] string? editToken)
    {
        var model = await _dbContext.Models.FirstOrDefaultAsync(m => m.Id == id);
        if (model == null)
        {
            return NotFound(new { message = "El modelo no existe." });
        }

        if (!IsAuthorizedToModify(model, headerEditToken ?? editToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "No tienes permiso para eliminar este modelo." });
        }

        _dbContext.Models.Remove(model);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Incrementa los votos de un modelo si no ha votado antes desde la misma sesión o usuario.
    /// </summary>
    [HttpPost("{id:guid}/vote")]
    [ProducesResponseType(typeof(VoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Vote(
        Guid id,
        [FromHeader(Name = "X-Voter-Fingerprint")] string? clientFingerprint)
    {
        var model = await _dbContext.Models.FirstOrDefaultAsync(m => m.Id == id && m.Status != "flagged");
        if (model == null)
        {
            return NotFound(new { message = "El modelo no existe." });
        }

        string rawFingerprint;
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (User.Identity?.IsAuthenticated == true && Guid.TryParse(userIdClaim, out var userId))
        {
            rawFingerprint = $"user:{userId}";
        }
        else
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
            rawFingerprint = $"anon:{clientFingerprint ?? "nofp"}:{ip}";
        }

        var fingerprintHash = TokenUtils.ComputeSha256Hash(rawFingerprint);

        var alreadyVoted = await _dbContext.ModelVotes
            .AnyAsync(v => v.ModelId == id && v.VoterFingerprintHash == fingerprintHash);

        if (alreadyVoted)
        {
            return Ok(new VoteResponse(true, model.UpvotesCount, "Ya has votado por este modelo anteriormente."));
        }

        var vote = new ModelVote
        {
            Id = Guid.NewGuid(),
            ModelId = id,
            VoterFingerprintHash = fingerprintHash,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ModelVotes.Add(vote);
        model.UpvotesCount++;
        await _dbContext.SaveChangesAsync();

        return Ok(new VoteResponse(true, model.UpvotesCount));
    }

    private bool IsAuthorizedToModify(DickModel model, string? token)
    {
        // 1. Si el usuario actual es el dueño registrado (vía JWT)
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (User.Identity?.IsAuthenticated == true &&
            Guid.TryParse(userIdClaim, out var currentUserId) &&
            model.UserId.HasValue &&
            model.UserId.Value == currentUserId)
        {
            return true;
        }

        // 2. Si se suministró un token de edición que coincida con el hash almacenado
        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(model.EditTokenHash))
        {
            var hash = TokenUtils.ComputeSha256Hash(token.Trim());
            if (string.Equals(hash, model.EditTokenHash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
