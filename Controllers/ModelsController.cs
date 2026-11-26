using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using backend_dickifier.Data;
using backend_dickifier.DTOs;
using backend_dickifier.Entities;
using backend_dickifier.Security;
using backend_dickifier.Services;
using backend_dickifier.Utils;
using backend_dickifier.Validators;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace backend_dickifier.Controllers;

[ApiController, Route("api/models"), Route("api/dickifier/models")]
public sealed class ModelsController(DickifierDbContext db, SecurityOptions settings, ResourceQuota quota,
    PayloadValidator payloads, VisitRegistry visits) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetModels([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string filter = "all", [FromQuery] string sortBy = "recent",
        [FromQuery] string? country = null, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        if (page is < 1 or > 1000 || pageSize is < 1 or > 50 || search?.Length > 80 || country?.Length > 2
            || filter is not ("all" or "registered" or "anonymous")
            || sortBy is not ("recent" or "votes" or "length" or "girth" or "volume"))
            return BadRequest(new { message = "Filtros o paginación inválidos." });
        var query = db.Models.AsNoTracking().Where(m => m.Status != "flagged");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");
            if (text.Length < 2) return BadRequest(new { message = "La búsqueda requiere al menos dos caracteres." });
            var pattern = $"%{text}%";
            var code = ShortCodeGenerator.Normalize(search);
            query = query.Where(m => EF.Functions.ILike(m.Name,pattern,"\\") || m.ShortCode.StartsWith(code));
        }
        if (filter != "all") query = query.Where(m => m.Status == filter);
        if (!string.IsNullOrEmpty(country))
        {
            if (!Regex.IsMatch(country,"^[a-zA-Z]{2}$")) return BadRequest();
            var code = country.ToUpperInvariant(); query = query.Where(m => m.CountryCode == code);
        }
        query = sortBy switch
        {
            "votes" => query.OrderByDescending(m => m.UpvotesCount).ThenBy(m => m.Id),
            "length" => query.OrderByDescending(m => m.LengthCm).ThenBy(m => m.Id),
            "girth" => query.OrderByDescending(m => m.GirthCm).ThenBy(m => m.Id),
            "volume" => query.OrderByDescending(m => m.VolumeCm3).ThenBy(m => m.Id),
            _ => query.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id)
        };
        return Ok(await ModelQueries.Page(query,page,pageSize,ct));
    }

    [HttpGet("{idOrCode}")]
    public async Task<IActionResult> GetModelById(string idOrCode, CancellationToken ct)
    {
        if (idOrCode.Length > 40) return NotFound();
        var query = db.Models.AsNoTracking().Where(m => m.Status != "flagged");
        query = Guid.TryParse(idOrCode,out var id) ? query.Where(m => m.Id == id)
            : query.Where(m => m.ShortCode == ShortCodeGenerator.Normalize(idOrCode));
        var size = await query.Select(m => (int?)m.PayloadBytes).SingleOrDefaultAsync(ct);
        if (size > SecurityOptions.MaxPayloadBytes) return StatusCode(413,new { message = "Este payload antiguo supera el límite de lectura.", code = "payload_too_large" });
        var model = await query.Include(m => m.User).SingleOrDefaultAsync(ct);
        return model is null ? NotFound() : Ok(ModelQueries.Detail(model));
    }

    [HttpPost, EnableRateLimiting("write")]
    public async Task<IActionResult> CreateModel(CreateModelRequest request, CancellationToken ct)
    {
        var user = UserId();
        if ((Request.Headers.Authorization.Count > 0 && user is null) || (user is null && !settings.AnonymousPublishingEnabled))
            return Unauthorized(new { message = "Inicia sesión para publicar un modelo.", code = "authentication_required" });
        var validation = PlausibilityValidator.Validate(request.LengthCm,request.GirthCm,request.CurvatureDirection,
            request.CurvatureDegrees,request.Name,request.HeightCm,request.WeightKg,request.CountryCode);
        if (!validation.IsValid) return BadRequest(new { message = validation.ErrorMessage });
        if (!payloads.TryValidate(request.AnatomyPayload,false,out var json,out var error)) return BadRequest(new { message = error, code = "invalid_payload" });
        var bytes = Encoding.UTF8.GetByteCount(json);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await quota.Charge(quota.Day($"models-ip:{quota.Client(HttpContext)}"),1,0,user is null ? settings.AnonymousModelsPerIpPerDay : settings.ModelsPerIpPerDay,0,true,ct);
        if (user.HasValue)
        {
            await quota.Charge(quota.Day($"models-user:{user}"),1,0,settings.ModelsPerUserPerDay,0,true,ct);
        }
        await ChargeStorage(user,1,bytes,ct);
        var edit = user is null ? TokenUtils.GenerateSecureToken() : null;
        var model = new DickModel
        {
            UserId = user, EditTokenHash = edit is null ? null : TokenUtils.ComputeSha256Hash(edit),
            Status = user.HasValue ? "registered" : "anonymous", Name = request.Name.Trim(),
            LengthCm = Math.Round(request.LengthCm,1), GirthCm = Math.Round(request.GirthCm,1),
            VolumeCm3 = Volume(request.LengthCm,request.GirthCm), CurvatureDirection = request.CurvatureDirection.ToLowerInvariant(),
            CurvatureDegrees = request.CurvatureDegrees, HeightCm = request.HeightCm, WeightKg = request.WeightKg,
            CountryCode = request.CountryCode?.ToUpperInvariant(), AnatomyPayload = json, PayloadBytes = bytes
        };
        db.Models.Add(model);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            model.ShortCode = ShortCodeGenerator.Generate();
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException ex) when (attempt < 2 && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_dick_models_ShortCode" }) { }
        }
        await transaction.CommitAsync(ct);
        return CreatedAtAction(nameof(GetModelById),new { idOrCode = model.ShortCode },
            new CreateModelResponse(model.Id,model.ShortCode,model.Name,model.Status,user,model.LengthCm,model.GirthCm,model.VolumeCm3,edit,model.CreatedAt));
    }

    [HttpPut("{id:guid}"), EnableRateLimiting("write")]
    public async Task<IActionResult> UpdateModel(Guid id, UpdateModelRequest request, CancellationToken ct)
    {
        if (Request.Query.ContainsKey("editToken")) return BadRequest(new { message = "Usa X-Edit-Token, no la URL." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var model = await LockedModel(id,ct);
        if (model is null) return NotFound();
        if (!CanModify(model)) return StatusCode(403,new { message = "No tienes permiso para modificar este modelo." });
        if (model.PayloadBytes > SecurityOptions.MaxPayloadBytes && !request.AnatomyPayload.HasValue)
            return BadRequest(new { message = "Sustituye el payload antiguo por uno dentro del límite." });
        var validation = PlausibilityValidator.Validate(request.LengthCm ?? model.LengthCm,request.GirthCm ?? model.GirthCm,
            request.CurvatureDirection ?? model.CurvatureDirection,request.CurvatureDegrees ?? model.CurvatureDegrees,
            request.Name ?? model.Name,request.HeightCm ?? model.HeightCm,request.WeightKg ?? model.WeightKg,request.CountryCode ?? model.CountryCode);
        if (!validation.IsValid) return BadRequest(new { message = validation.ErrorMessage });
        if (request.AnatomyPayload.HasValue)
        {
            if (!payloads.TryValidate(request.AnatomyPayload,false,out var json,out var error)) return BadRequest(new { message = error, code = "invalid_payload" });
            var bytes = Encoding.UTF8.GetByteCount(json);
            await ChargeStorage(model.UserId,0,bytes-model.PayloadBytes,ct);
            model.AnatomyPayload = json; model.PayloadBytes = bytes;
        }
        model.Name = request.Name?.Trim() ?? model.Name;
        model.LengthCm = request.LengthCm ?? model.LengthCm; model.GirthCm = request.GirthCm ?? model.GirthCm;
        model.VolumeCm3 = Volume(model.LengthCm,model.GirthCm);
        model.CurvatureDirection = request.CurvatureDirection?.ToLowerInvariant() ?? model.CurvatureDirection;
        model.CurvatureDegrees = request.CurvatureDegrees ?? model.CurvatureDegrees;
        model.CountryCode = request.CountryCode?.ToUpperInvariant() ?? model.CountryCode;
        model.HeightCm = request.HeightCm ?? model.HeightCm; model.WeightKg = request.WeightKg ?? model.WeightKg;
        model.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        model.AnatomyPayload = await db.Models.AsNoTracking().Where(m => m.Id == id).Select(m => m.AnatomyPayload).SingleAsync(ct);
        await db.Entry(model).Reference(m => m.User).LoadAsync(ct);
        return Ok(ModelQueries.Detail(model));
    }

    [HttpDelete("{id:guid}"), EnableRateLimiting("write")]
    public async Task<IActionResult> DeleteModel(Guid id, CancellationToken ct)
    {
        if (Request.Query.ContainsKey("editToken")) return BadRequest(new { message = "Usa X-Edit-Token, no la URL." });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var model = await LockedModel(id,ct);
        if (model is null) return NotFound();
        if (!CanModify(model)) return StatusCode(403,new { message = "No tienes permiso para eliminar este modelo." });
        await ChargeStorage(model.UserId,-1,-model.PayloadBytes,ct);
        db.Models.Remove(model); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/vote"), EnableRateLimiting("write")]
    public async Task<IActionResult> Vote(Guid id, CancellationToken ct)
    {
        var user = UserId();
        if (Request.Headers.Authorization.Count > 0 && user is null) return Unauthorized();
        if (!await db.Models.AnyAsync(m => m.Id == id && m.Status != "flagged",ct)) return NotFound();
        // La huella indicada por el cliente se ignora; no permite multiplicar votos.
        var identity = quota.Fingerprint(user.HasValue ? $"user:{user}" : $"ip:{quota.Client(HttpContext)}");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var voteId = Guid.NewGuid(); var now = DateTime.UtcNow;
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO model_votes ("Id","ModelId","VoterFingerprintHash","CreatedAt")
            SELECT {voteId},{id},{identity},{now}
            WHERE EXISTS (SELECT 1 FROM dick_models WHERE "Id"={id} AND "Status"<>'flagged')
            ON CONFLICT ("ModelId","VoterFingerprintHash") DO NOTHING
            """,ct);
        if (inserted > 0) await db.Models.Where(m => m.Id == id).ExecuteUpdateAsync(update => update.SetProperty(m => m.UpvotesCount,m => m.UpvotesCount+1),ct);
        var count = await db.Models.Where(m => m.Id == id).Select(m => m.UpvotesCount).SingleOrDefaultAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new VoteResponse(true,count,inserted == 0 ? "Ya has votado por este modelo." : null));
    }

    [HttpPost("{id:guid}/view"), EnableRateLimiting("write")]
    public async Task<IActionResult> View(Guid id, CancellationToken ct)
    {
        if (visits.TryRecord($"{id}:{quota.Client(HttpContext)}"))
            if (await db.Models.Where(m => m.Id == id && m.Status != "flagged")
                .ExecuteUpdateAsync(update => update.SetProperty(m => m.ViewsCount,m => m.ViewsCount+1),ct) == 0) return NotFound();
        return NoContent();
    }

    private Guid? UserId() => User.Identity?.IsAuthenticated == true && Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id) ? id : null;
    private bool CanModify(DickModel model) => (model.UserId.HasValue && model.UserId == UserId())
        || TokenUtils.Matches(Request.Headers["X-Edit-Token"].FirstOrDefault(),model.EditTokenHash);
    private Task<DickModel?> LockedModel(Guid id, CancellationToken ct) =>
        db.Models.FromSqlInterpolated($$"""
            SELECT "Id","ShortCode","UserId","EditTokenHash","Name","Status","LengthCm","GirthCm","VolumeCm3",
              "CurvatureDirection","CurvatureDegrees","CountryCode","HeightCm","WeightKg","PayloadBytes",
              "UpvotesCount","ViewsCount","CreatedAt","UpdatedAt",'{}'::jsonb AS "AnatomyPayload"
            FROM dick_models WHERE "Id"={{id}} FOR UPDATE
            """).SingleOrDefaultAsync(ct);
    private async Task ChargeStorage(Guid? user, int count, long bytes, CancellationToken ct)
    {
        await quota.Charge("models:global",count,bytes,settings.MaximumModels,settings.MaximumStoredBytes,false,ct);
        if (user.HasValue) await quota.Charge($"storage-user:{user}",count,bytes,settings.ModelsPerUser,settings.MaximumUserBytes,false,ct);
    }
    private static decimal Volume(decimal length, decimal girth) => Math.Round((decimal)(Math.Pow((double)girth,2)*(double)length/(4*Math.PI)),1);
}
