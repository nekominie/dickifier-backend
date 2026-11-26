using System.Security.Cryptography;
using System.Text;
using backend_dickifier.Data;
using Microsoft.EntityFrameworkCore;

namespace backend_dickifier.Security;

public sealed class ResourceLimitException(string message, int retryAfter = 3600) : Exception(message)
{
    public int RetryAfter { get; } = retryAfter;
}

public sealed class ResourceQuota(DickifierDbContext db, SecurityOptions settings)
{
    public string Fingerprint(string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.FingerprintKey), Encoding.UTF8.GetBytes(value)));
    public string Client(HttpContext context) => Fingerprint(context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown");
    public string Day(string scope) => $"{scope}:{DateTime.UtcNow:yyyyMMdd}";

    // Ejecutar dentro de la transacción que guarda el recurso. ON CONFLICT serializa competidores.
    public async Task Charge(string key, int count, long bytes, int maxCount, long maxBytes, bool daily, CancellationToken ct)
    {
        if (count > maxCount || bytes > maxBytes) throw new ResourceLimitException("Se alcanzó la cuota de recursos.");
        DateTime? expiry = daily ? DateTime.UtcNow.Date.AddDays(3) : null;
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO resource_budgets ("Key","Count","Bytes","ExpiresAt")
            VALUES ({key},GREATEST(0,{count}),GREATEST(0,{bytes}),{expiry})
            ON CONFLICT ("Key") DO UPDATE SET
              "Count"=GREATEST(0,resource_budgets."Count"+{count}),
              "Bytes"=GREATEST(0,resource_budgets."Bytes"+{bytes})
            WHERE ({count}<=0 OR resource_budgets."Count"+{count}<={maxCount})
              AND ({bytes}<=0 OR resource_budgets."Bytes"+{bytes}<={maxBytes})
            """, ct);
        if (affected == 0) throw new ResourceLimitException("Se alcanzó la cuota de recursos.");
    }
}

public sealed class ResourceCleanup(IServiceScopeFactory scopes, ILogger<ResourceCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<DickifierDbContext>();
                    var before = DateTime.UtcNow.AddDays(-1);
                    await db.RefreshSessions.Where(s => s.ExpiresAt < before).ExecuteDeleteAsync(stoppingToken);
                    await db.ResourceBudgets.Where(b => b.ExpiresAt < before).ExecuteDeleteAsync(stoppingToken);
                }
                catch (Exception) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning("No se completó la limpieza periódica de recursos."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
