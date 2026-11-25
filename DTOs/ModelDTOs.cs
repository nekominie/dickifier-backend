using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace backend_dickifier.DTOs;

public record CreateModelRequest(
    [Required]
    [MaxLength(80)]
    string Name,

    [Required]
    decimal LengthCm,

    [Required]
    decimal GirthCm,

    string CurvatureDirection = "straight",
    int CurvatureDegrees = 0,
    string? CountryCode = null,
    decimal? HeightCm = null,
    decimal? WeightKg = null,
    JsonElement? AnatomyPayload = null
);

public record UpdateModelRequest(
    [MaxLength(80)]
    string? Name,
    decimal? LengthCm,
    decimal? GirthCm,
    string? CurvatureDirection,
    int? CurvatureDegrees,
    string? CountryCode,
    decimal? HeightCm,
    decimal? WeightKg,
    JsonElement? AnatomyPayload,
    string? EditToken = null
);

public record ModelSummaryResponse(
    Guid Id,
    string ShortCode,
    string Name,
    string Status,
    Guid? UserId,
    string? AuthorUsername,
    decimal LengthCm,
    decimal GirthCm,
    decimal VolumeCm3,
    string CurvatureDirection,
    int CurvatureDegrees,
    string? CountryCode,
    decimal? HeightCm,
    decimal? WeightKg,
    JsonElement AnatomyPayload,
    int UpvotesCount,
    int ViewsCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record ModelDetailResponse(
    Guid Id,
    string ShortCode,
    string Name,
    string Status,
    Guid? UserId,
    string? AuthorUsername,
    decimal LengthCm,
    decimal GirthCm,
    decimal VolumeCm3,
    string CurvatureDirection,
    int CurvatureDegrees,
    string? CountryCode,
    decimal? HeightCm,
    decimal? WeightKg,
    JsonElement AnatomyPayload,
    int UpvotesCount,
    int ViewsCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record CreateModelResponse(
    Guid Id,
    string ShortCode,
    string Name,
    string Status,
    Guid? UserId,
    decimal LengthCm,
    decimal GirthCm,
    decimal VolumeCm3,
    string? EditToken,
    DateTime CreatedAt
);

public record PagedResult<T>(
    List<T> Items,
    int Total,
    int Page,
    int PageSize,
    int TotalPages
);

public record VoteResponse(
    bool HasVoted,
    int UpvotesCount,
    string? Message = null
);
