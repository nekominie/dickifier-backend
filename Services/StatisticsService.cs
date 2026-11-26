using backend_dickifier.Data;
using backend_dickifier.DTOs;
using backend_dickifier.Security;
using Microsoft.EntityFrameworkCore;

namespace backend_dickifier.Services;

public sealed class StatisticsService(IServiceScopeFactory scopes, SecurityOptions settings)
{
    private readonly SemaphoreSlim gate = new(1,1);
    private GlobalStatsResponse? global;
    private DemographicStatsResponse? demographics;
    private DateTime globalUntil, demographicUntil;

    public async Task<GlobalStatsResponse> Global(CancellationToken ct)
    {
        if (global is not null && globalUntil > DateTime.UtcNow) return global;
        await gate.WaitAsync(ct);
        try
        {
            if (global is not null && globalUntil > DateTime.UtcNow) return global;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DickifierDbContext>();
            var row = await db.Database.SqlQuery<GlobalRow>($"""
                SELECT COUNT(*)::int AS "Total",
                  COUNT(*) FILTER (WHERE "Status"='registered')::int AS "Registered",
                  COUNT(*) FILTER (WHERE "Status"='anonymous')::int AS "Anonymous",
                  COALESCE(AVG("LengthCm"),0)::float8 AS "Length",
                  COALESCE(AVG("GirthCm"),0)::float8 AS "Girth",
                  COALESCE(STDDEV_SAMP("LengthCm"),0)::float8 AS "LengthSd",
                  COALESCE(STDDEV_SAMP("GirthCm"),0)::float8 AS "GirthSd",
                  COALESCE(percentile_cont(0.10) WITHIN GROUP (ORDER BY "LengthCm"),0)::float8 AS "L10",
                  COALESCE(percentile_cont(0.25) WITHIN GROUP (ORDER BY "LengthCm"),0)::float8 AS "L25",
                  COALESCE(percentile_cont(0.50) WITHIN GROUP (ORDER BY "LengthCm"),0)::float8 AS "L50",
                  COALESCE(percentile_cont(0.75) WITHIN GROUP (ORDER BY "LengthCm"),0)::float8 AS "L75",
                  COALESCE(percentile_cont(0.90) WITHIN GROUP (ORDER BY "LengthCm"),0)::float8 AS "L90",
                  COALESCE(percentile_cont(0.10) WITHIN GROUP (ORDER BY "GirthCm"),0)::float8 AS "G10",
                  COALESCE(percentile_cont(0.25) WITHIN GROUP (ORDER BY "GirthCm"),0)::float8 AS "G25",
                  COALESCE(percentile_cont(0.50) WITHIN GROUP (ORDER BY "GirthCm"),0)::float8 AS "G50",
                  COALESCE(percentile_cont(0.75) WITHIN GROUP (ORDER BY "GirthCm"),0)::float8 AS "G75",
                  COALESCE(percentile_cont(0.90) WITHIN GROUP (ORDER BY "GirthCm"),0)::float8 AS "G90",
                  COUNT(*) FILTER (WHERE "CurvatureDirection"='straight')::int AS "Straight",
                  COUNT(*) FILTER (WHERE "CurvatureDirection"='up')::int AS "Up",
                  COUNT(*) FILTER (WHERE "CurvatureDirection"='down')::int AS "Down",
                  COUNT(*) FILTER (WHERE "CurvatureDirection"='left')::int AS "Left",
                  COUNT(*) FILTER (WHERE "CurvatureDirection"='right')::int AS "Right"
                FROM dick_models WHERE "Status"<>'flagged'
                """).SingleAsync(ct);
            var distribution = new Dictionary<string,double>
            {
                ["straight"] = Percentage(row.Straight,row.Total), ["up"] = Percentage(row.Up,row.Total),
                ["down"] = Percentage(row.Down,row.Total), ["left"] = Percentage(row.Left,row.Total), ["right"] = Percentage(row.Right,row.Total)
            };
            global = row.Total == 0
                ? new(0,0,0,13.5m,1.8m,11.5m,1.2m,new(11.2m,12.3m,13.5m,14.7m,15.8m,10m,10.7m,11.5m,12.3m,13m),
                    new Dictionary<string,double> { ["straight"]=100,["up"]=0,["down"]=0,["left"]=0,["right"]=0 })
                : new(row.Total,row.Registered,row.Anonymous,Round(row.Length,1),Round(row.LengthSd,2),Round(row.Girth,1),Round(row.GirthSd,2),
                    new(Round(row.L10,1),Round(row.L25,1),Round(row.L50,1),Round(row.L75,1),Round(row.L90,1),
                        Round(row.G10,1),Round(row.G25,1),Round(row.G50,1),Round(row.G75,1),Round(row.G90,1)),distribution);
            globalUntil = DateTime.UtcNow.AddSeconds(settings.StatisticsCacheSeconds);
            return global;
        }
        finally { gate.Release(); }
    }

    public async Task<DemographicStatsResponse> Demographics(CancellationToken ct)
    {
        if (demographics is not null && demographicUntil > DateTime.UtcNow) return demographics;
        await gate.WaitAsync(ct);
        try
        {
            if (demographics is not null && demographicUntil > DateTime.UtcNow) return demographics;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DickifierDbContext>();
            var countries = await db.Models.AsNoTracking().Where(m => m.Status != "flagged" && m.CountryCode != null)
                .GroupBy(m => m.CountryCode!).Select(g => new CountryMetricDto(g.Key,g.Count(),
                    Math.Round(g.Average(m => m.LengthCm),1),Math.Round(g.Average(m => m.GirthCm),1)))
                .OrderByDescending(g => g.Count).ToListAsync(ct);
            var correlation = await db.Database.SqlQuery<CorrelationRow>($"""
                SELECT COALESCE(corr("HeightCm"::float8,"LengthCm"::float8),0) AS "Height",
                  COALESCE(corr("WeightKg"::float8,"GirthCm"::float8),0) AS "Weight",
                  COUNT("HeightCm")::int AS "HeightCount",COUNT("WeightKg")::int AS "WeightCount"
                FROM dick_models WHERE "Status"<>'flagged'
                """).SingleAsync(ct);
            demographics = new(countries,new(Math.Round(Finite(correlation.Height),3),Math.Round(Finite(correlation.Weight),3),correlation.HeightCount,correlation.WeightCount));
            demographicUntil = DateTime.UtcNow.AddSeconds(settings.StatisticsCacheSeconds);
            return demographics;
        }
        finally { gate.Release(); }
    }
    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
    private static double Percentage(int part, int total) => total == 0 ? 0 : Math.Round(100.0*part/total,1);
    private static decimal Round(double value, int digits) => Math.Round((decimal)Finite(value),digits);

    public sealed class GlobalRow
    {
        public int Total { get; set; } public int Registered { get; set; } public int Anonymous { get; set; }
        public double Length { get; set; } public double Girth { get; set; } public double LengthSd { get; set; } public double GirthSd { get; set; }
        public double L10 { get; set; } public double L25 { get; set; } public double L50 { get; set; } public double L75 { get; set; } public double L90 { get; set; }
        public double G10 { get; set; } public double G25 { get; set; } public double G50 { get; set; } public double G75 { get; set; } public double G90 { get; set; }
        public int Straight { get; set; } public int Up { get; set; } public int Down { get; set; } public int Left { get; set; } public int Right { get; set; }
    }
    public sealed class CorrelationRow
    {
        public double Height { get; set; } public double Weight { get; set; } public int HeightCount { get; set; } public int WeightCount { get; set; }
    }
}
