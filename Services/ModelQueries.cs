using System.Text.Json;
using backend_dickifier.DTOs;
using backend_dickifier.Entities;
using Microsoft.EntityFrameworkCore;

namespace backend_dickifier.Services;

public static class ModelQueries
{
    public static async Task<PagedResult<ModelSummaryResponse>> Page(IQueryable<DickModel> query, int page, int size, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page-1)*size).Take(size).Select(m => new ModelSummaryResponse(
            m.Id,m.ShortCode,m.Name,m.Status,m.UserId,m.User != null ? m.User.Username : null,
            m.LengthCm,m.GirthCm,m.VolumeCm3,m.CurvatureDirection,m.CurvatureDegrees,
            m.CountryCode,m.HeightCm,m.WeightKg,null,m.UpvotesCount,m.ViewsCount,m.CreatedAt,m.UpdatedAt
        )).ToListAsync(ct);
        return new(items,total,page,size,(int)Math.Ceiling(total/(double)size));
    }
    public static JsonElement ReadPayload(string? json)
    {
        try { using var document = JsonDocument.Parse(string.IsNullOrEmpty(json) ? "{}" : json); return document.RootElement.Clone(); }
        catch (JsonException) { using var document = JsonDocument.Parse("{}"); return document.RootElement.Clone(); }
    }
    public static ModelDetailResponse Detail(DickModel m) => new(
        m.Id,m.ShortCode,m.Name,m.Status,m.UserId,m.User?.Username,m.LengthCm,m.GirthCm,m.VolumeCm3,
        m.CurvatureDirection,m.CurvatureDegrees,m.CountryCode,m.HeightCm,m.WeightKg,ReadPayload(m.AnatomyPayload),
        m.UpvotesCount,m.ViewsCount,m.CreatedAt,m.UpdatedAt);
}
