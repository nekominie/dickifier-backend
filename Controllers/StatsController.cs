using backend_dickifier.Data;
using backend_dickifier.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend_dickifier.Controllers;

[ApiController]
[Route("api/[controller]")]
[Route("api/dickifier/[controller]")]
public class StatsController : ControllerBase
{
    private readonly DickifierDbContext _dbContext;

    public StatsController(DickifierDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Estadísticas demográficas y percentiles calculados dinámicamente sobre la base comunitaria.
    /// </summary>
    [HttpGet("global")]
    [ProducesResponseType(typeof(GlobalStatsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGlobalStats()
    {
        var models = await _dbContext.Models
            .Where(m => m.Status != "flagged")
            .Select(m => new
            {
                m.LengthCm,
                m.GirthCm,
                m.Status,
                m.CurvatureDirection
            })
            .AsNoTracking()
            .ToListAsync();

        var total = models.Count;
        if (total == 0)
        {
            return Ok(new GlobalStatsResponse(
                TotalModels: 0,
                RegisteredModels: 0,
                AnonymousModels: 0,
                MeanLengthCm: 13.5m,
                StdDevLengthCm: 1.8m,
                MeanGirthCm: 11.5m,
                StdDevGirthCm: 1.2m,
                Percentiles: new PercentileDistribution(11.2m, 12.3m, 13.5m, 14.7m, 15.8m, 10.0m, 10.7m, 11.5m, 12.3m, 13.0m),
                CurvatureDistribution: new Dictionary<string, double>
                {
                    ["straight"] = 100.0,
                    ["up"] = 0.0,
                    ["down"] = 0.0,
                    ["left"] = 0.0,
                    ["right"] = 0.0
                }
            ));
        }

        var registered = models.Count(m => m.Status == "registered");
        var anonymous = models.Count(m => m.Status == "anonymous");

        var lengths = models.Select(m => (double)m.LengthCm).OrderBy(x => x).ToList();
        var girths = models.Select(m => (double)m.GirthCm).OrderBy(x => x).ToList();

        var meanLength = lengths.Average();
        var stdDevLength = CalculateStdDev(lengths, meanLength);

        var meanGirth = girths.Average();
        var stdDevGirth = CalculateStdDev(girths, meanGirth);

        var percentiles = new PercentileDistribution(
            P10Length: (decimal)CalculatePercentile(lengths, 0.10),
            P25Length: (decimal)CalculatePercentile(lengths, 0.25),
            P50Length: (decimal)CalculatePercentile(lengths, 0.50),
            P75Length: (decimal)CalculatePercentile(lengths, 0.75),
            P90Length: (decimal)CalculatePercentile(lengths, 0.90),
            P10Girth: (decimal)CalculatePercentile(girths, 0.10),
            P25Girth: (decimal)CalculatePercentile(girths, 0.25),
            P50Girth: (decimal)CalculatePercentile(girths, 0.50),
            P75Girth: (decimal)CalculatePercentile(girths, 0.75),
            P90Girth: (decimal)CalculatePercentile(girths, 0.90)
        );

        // Curvatura
        var curvatureCounts = models
            .GroupBy(m => m.CurvatureDirection.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => Math.Round(g.Count() / (double)total * 100.0, 1));

        foreach (var dir in new[] { "straight", "up", "down", "left", "right" })
        {
            if (!curvatureCounts.ContainsKey(dir)) curvatureCounts[dir] = 0.0;
        }

        return Ok(new GlobalStatsResponse(
            TotalModels: total,
            RegisteredModels: registered,
            AnonymousModels: anonymous,
            MeanLengthCm: Math.Round((decimal)meanLength, 1),
            StdDevLengthCm: Math.Round((decimal)stdDevLength, 2),
            MeanGirthCm: Math.Round((decimal)meanGirth, 1),
            StdDevGirthCm: Math.Round((decimal)stdDevGirth, 2),
            Percentiles: percentiles,
            CurvatureDistribution: curvatureCounts
        ));
    }

    /// <summary>
    /// Medias por país y correlación estadística con estatura y peso de la comunidad.
    /// </summary>
    [HttpGet("demographics")]
    [ProducesResponseType(typeof(DemographicStatsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDemographicStats()
    {
        var rawCountryModels = await _dbContext.Models
            .Where(m => m.Status != "flagged" && m.CountryCode != null)
            .Select(m => new { m.CountryCode, m.LengthCm, m.GirthCm })
            .AsNoTracking()
            .ToListAsync();

        var countryData = rawCountryModels
            .GroupBy(m => m.CountryCode!)
            .Select(g => new CountryMetricDto(
                CountryCode: g.Key,
                Count: g.Count(),
                MeanLength: Math.Round(g.Average(m => m.LengthCm), 1),
                MeanGirth: Math.Round(g.Average(m => m.GirthCm), 1)
            ))
            .OrderByDescending(c => c.Count)
            .ToList();

        // Correlaciones
        var heightModels = await _dbContext.Models
            .Where(m => m.Status != "flagged" && m.HeightCm.HasValue)
            .Select(m => new { Height = (double)m.HeightCm!.Value, Length = (double)m.LengthCm })
            .AsNoTracking()
            .ToListAsync();

        var weightModels = await _dbContext.Models
            .Where(m => m.Status != "flagged" && m.WeightKg.HasValue)
            .Select(m => new { Weight = (double)m.WeightKg!.Value, Girth = (double)m.GirthCm })
            .AsNoTracking()
            .ToListAsync();

        var heightLengthCorr = CalculatePearson(
            heightModels.Select(m => m.Height).ToList(),
            heightModels.Select(m => m.Length).ToList()
        );

        var weightGirthCorr = CalculatePearson(
            weightModels.Select(m => m.Weight).ToList(),
            weightModels.Select(m => m.Girth).ToList()
        );

        return Ok(new DemographicStatsResponse(
            ByCountry: countryData,
            Correlations: new CorrelationMetrics(
                HeightLengthCorrelation: Math.Round(heightLengthCorr, 3),
                WeightGirthCorrelation: Math.Round(weightGirthCorr, 3),
                HeightSampleSize: heightModels.Count,
                WeightSampleSize: weightModels.Count
            )
        ));
    }

    private static double CalculateStdDev(List<double> values, double mean)
    {
        if (values.Count <= 1) return 0.0;
        var sumSquares = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumSquares / (values.Count - 1));
    }

    private static double CalculatePercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0.0;
        if (sortedValues.Count == 1) return sortedValues[0];

        var index = percentile * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper) return Math.Round(sortedValues[lower], 1);

        var weight = index - lower;
        return Math.Round(sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight, 1);
    }

    private static double CalculatePearson(List<double> x, List<double> y)
    {
        if (x.Count < 2 || x.Count != y.Count) return 0.0;

        var meanX = x.Average();
        var meanY = y.Average();

        double sumNumerator = 0.0;
        double sumXSquare = 0.0;
        double sumYSquare = 0.0;

        for (int i = 0; i < x.Count; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            sumNumerator += dx * dy;
            sumXSquare += dx * dx;
            sumYSquare += dy * dy;
        }

        var denominator = Math.Sqrt(sumXSquare * sumYSquare);
        if (denominator == 0.0) return 0.0;

        return sumNumerator / denominator;
    }
}
