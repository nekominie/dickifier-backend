using Microsoft.AspNetCore.Http.Features;

namespace backend_dickifier.Security;

public sealed class RequestProtection(RequestDelegate next, SecurityOptions settings, IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim concurrency = new(settings.ConcurrentRequests, settings.ConcurrentRequests);
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health")) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (!environment.IsDevelopment() && !context.Request.IsHttps)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { message = "Se requiere HTTPS.", code = "https_required" });
            return;
        }
        if (!await concurrency.WaitAsync(0, context.RequestAborted))
        {
            context.Response.StatusCode = 429; context.Response.Headers.RetryAfter = "1";
            await context.Response.WriteAsJsonAsync(new { message = "Servidor ocupado. Inténtalo de nuevo.", code = "rate_limited" });
            return;
        }
        try
        {
            var path = context.Request.Path.Value ?? "";
            var small = path.EndsWith("/login") || path.EndsWith("/register") || path.EndsWith("/refresh") || path.EndsWith("/logout");
            var limit = small ? 8192 : SecurityOptions.MaxRequestBytes;
            if (context.Request.ContentLength > limit) throw new BadHttpRequestException("Petición demasiado grande.", 413);
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;
            context.Request.Body = new LimitedReadStream(context.Request.Body, limit);
            if (small && HttpMethods.IsPost(context.Request.Method))
            {
                var origin = context.Request.Headers.Origin.ToString();
                var permitted = origin == settings.PublicOrigin.TrimEnd('/')
                    || (environment.IsDevelopment() && settings.DevelopmentOrigins.Contains(origin));
                var hasCookie = context.Request.Cookies.ContainsKey(SessionService.CookieName);
                if ((origin.Length > 0 && !permitted) || (hasCookie && !permitted)
                    || context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { message = "Origen no permitido.", code = "origin_forbidden" });
                    return;
                }
            }
            await next(context);
        }
        finally { concurrency.Release(); }
    }
}

internal sealed class LimitedReadStream(Stream inner, long limit) : Stream
{
    private long received;
    private int Count(int bytes)
    {
        received += bytes;
        if (received > limit) throw new BadHttpRequestException("Petición demasiado grande.", 413);
        return bytes;
    }
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => Count(await inner.ReadAsync(buffer, ct));
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Count(await inner.ReadAsync(buffer.AsMemory(offset,count), ct));
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
