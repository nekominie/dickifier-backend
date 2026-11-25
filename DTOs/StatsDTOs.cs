namespace backend_dickifier.DTOs;

public record GlobalStatsResponse(
    int TotalModels,
    int RegisteredModels,
    int AnonymousModels,
    decimal MeanLengthCm,
    decimal StdDevLengthCm,
    decimal MeanGirthCm,
    decimal StdDevGirthCm,
    PercentileDistribution Percentiles,
    Dictionary<string, double> CurvatureDistribution
);

public record PercentileDistribution(
    decimal P10Length,
    decimal P25Length,
    decimal P50Length,
    decimal P75Length,
    decimal P90Length,
    decimal P10Girth,
    decimal P25Girth,
    decimal P50Girth,
    decimal P75Girth,
    decimal P90Girth
);

public record CountryMetricDto(
    string CountryCode,
    int Count,
    decimal MeanLength,
    decimal MeanGirth
);

public record CorrelationMetrics(
    double HeightLengthCorrelation,
    double WeightGirthCorrelation,
    int HeightSampleSize,
    int WeightSampleSize
);

public record DemographicStatsResponse(
    List<CountryMetricDto> ByCountry,
    CorrelationMetrics Correlations
);
