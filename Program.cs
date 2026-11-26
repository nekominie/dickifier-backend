using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using backend_dickifier.Data;
using backend_dickifier.Security;
using backend_dickifier.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Healthcheck del contenedor: no inicializa servicios ni lee secretos.
if (args.Contains("--healthcheck"))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:8080/health/ready");
    request.Headers.Host = "dickifier.nekomisystems.com";
    try { using var response = await client.SendAsync(request); Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1; }
    catch { Environment.ExitCode = 1; }
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true, reloadOnChange: false);
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (args.Contains("--migrate"))
{
    if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Falta la conexión de migración.");
    var options = new DbContextOptionsBuilder<DickifierDbContext>().UseNpgsql(connection).Options;
    await using var db = new DickifierDbContext(options);
    await db.Database.MigrateAsync();
    return;
}
var security = builder.Configuration.GetSection("Security").Get<SecurityOptions>() ?? new();
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new();
security.Validate(builder.Environment.IsDevelopment(), jwt, connection, migrating: false);
builder.Services.AddSingleton(security);
builder.Services.AddSingleton(jwt);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = SecurityOptions.MaxRequestBytes;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
    options.AddServerHeader = false;
});
builder.Services.AddDbContext<DickifierDbContext>(options => options.UseNpgsql(connection, pg => pg.CommandTimeout(10)));
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<ResourceQuota>();
builder.Services.AddSingleton<PayloadValidator>();
builder.Services.AddSingleton<StatisticsService>();
builder.Services.AddSingleton<VisitRegistry>();
builder.Services.AddHostedService<ResourceCleanup>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (var ip in security.KnownProxies) options.KnownProxies.Add(IPAddress.Parse(ip));
    if (security.KnownProxies.Length == 0) options.KnownProxies.Add(IPAddress.None);
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.SaveToken = false;
    options.TokenValidationParameters = new()
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
        ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(15),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var user = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var family = context.Principal?.FindFirstValue("sid") ?? context.Principal?.FindFirstValue(ClaimTypes.Sid);
            if (!Guid.TryParse(user, out var userId) || !Guid.TryParse(family, out var familyId)) { context.Fail("Sesión inválida."); return; }
            var db = context.HttpContext.RequestServices.GetRequiredService<DickifierDbContext>();
            if (!await db.RefreshSessions.AsNoTracking().AnyAsync(s => s.UserId == userId && s.FamilyId == familyId
                && s.RevokedAt == null && s.ExpiresAt > DateTime.UtcNow, context.HttpContext.RequestAborted)) context.Fail("Sesión revocada.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Environment.IsDevelopment() ? security.DevelopmentOrigins.Append(security.PublicOrigin).ToArray() : [security.PublicOrigin];
    policy.WithOrigins(origins).WithHeaders("Content-Type","Authorization","X-Edit-Token").WithMethods("GET","POST","PUT","DELETE").AllowCredentials();
}));
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
            _ => new() { PermitLimit = security.RequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = security.AuthRequestsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = security.RegistrationsPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy("write", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new() { PermitLimit = security.WritesPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
    {
        var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var time) ? Math.Max(1,(int)Math.Ceiling(time.TotalSeconds)) : 1;
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = retry.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Demasiadas peticiones. Inténtalo más tarde.", code = "rate_limited", retryAfterSeconds = retry }, ct);
    };
});
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.MaxDepth = 16);
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = _ =>
    new BadRequestObjectResult(new { message = "Datos de petición inválidos.", code = "validation_failed" }));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var status = error is ResourceLimitException ? 429 : error is BadHttpRequestException request ? request.StatusCode : 500;
    if (error is OperationCanceledException && context.RequestAborted.IsCancellationRequested) { context.Response.StatusCode = 499; return; }
    context.Response.StatusCode = status;
    if (error is ResourceLimitException quota) context.Response.Headers.RetryAfter = quota.RetryAfter.ToString();
    if (status == 500) app.Logger.LogError("Fallo no controlado {ExceptionType}, trace {TraceId}.", error?.GetType().Name, context.TraceIdentifier);
    await context.Response.WriteAsJsonAsync(new {
        message = status == 429 ? "Se alcanzó la cuota de recursos. Inténtalo más tarde." : status == 413 ? "La petición supera el tamaño admitido." : "No se pudo completar la petición.",
        code = status == 429 ? "quota_exceeded" : status == 413 ? "payload_too_large" : "server_error",
        traceId = context.TraceIdentifier
    });
}));
app.UseForwardedHeaders();
app.UseHsts();
app.UseRouting();
app.UseMiddleware<RequestProtection>();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
if (app.Environment.IsDevelopment() || security.SwaggerEnabled) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapControllers();
app.MapGet("/api/dickifier/capabilities", () => Results.Ok(new
{
    security.AnonymousPublishingEnabled, maxRequestBodyBytes = SecurityOptions.MaxRequestBytes,
    maxTextureCharacters = SecurityOptions.MaxTextureCharacters
}));
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).DisableRateLimiting();
app.MapGet("/health/ready", async (DickifierDbContext db, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct) || (await db.Database.GetPendingMigrationsAsync(ct)).Any())
            return Results.StatusCode(503);
        return Results.Ok(new { status = "ready" });
    }
    catch { return Results.StatusCode(503); }
}).DisableRateLimiting();
app.MapGet("/health", () => Results.Redirect("/health/ready")).DisableRateLimiting();

// No ejecutar migraciones aquí. Producción debe usar un rol sin facultades administrativas/DDL.
if (!app.Environment.IsDevelopment())
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DickifierDbContext>();
        var privileged = await db.Database.SqlQuery<bool>($"""
            SELECT (rolsuper OR rolcreatedb OR rolcreaterole OR has_schema_privilege(current_user, 'public', 'CREATE')) AS "Value"
            FROM pg_roles WHERE rolname=current_user
            """).SingleAsync();
        if (privileged) throw new InvalidOperationException();
    }
    catch { throw new InvalidOperationException("No se pudo verificar un rol PostgreSQL de ejecución sin privilegios. Revise permisos y disponibilidad."); }
}
app.Run();
public partial class Program { }
