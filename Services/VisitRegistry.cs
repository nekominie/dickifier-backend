using Microsoft.Extensions.Caching.Memory;

namespace backend_dickifier.Services;

/** Caché local explícitamente acotada; visitas informativas, no auditoría de identidad. */
public sealed class VisitRegistry : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly object gate = new();
    public bool TryRecord(string key)
    {
        lock (gate)
        {
            if (cache.TryGetValue(key,out _)) return false;
            cache.Set(key,true,new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) });
            return true;
        }
    }
    public void Dispose() => cache.Dispose();
}
