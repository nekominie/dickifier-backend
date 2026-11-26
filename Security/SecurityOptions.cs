using Npgsql;

namespace backend_dickifier.Security;

public sealed class SecurityOptions
{
    public string PublicOrigin { get; set; } = "https://dickifier.nekomisystems.com";
    public string FingerprintKey { get; set; } = "";
    public bool AnonymousPublishingEnabled { get; set; }
    public bool SwaggerEnabled { get; set; }
    public string[] KnownProxies { get; set; } = [];
    public string[] DevelopmentOrigins { get; set; } = ["http://localhost:5173", "http://localhost:5174", "http://127.0.0.1:5198"];
    public int RequestsPerMinute { get; set; } = 240;
    public int AuthRequestsPerMinute { get; set; } = 20;
    public int RegistrationsPerHour { get; set; } = 5;
    public int WritesPerMinute { get; set; } = 30;
    public int ConcurrentRequests { get; set; } = 8;
    public int ModelsPerIpPerDay { get; set; } = 100;
    public int AnonymousModelsPerIpPerDay { get; set; } = 10;
    public int ModelsPerUserPerDay { get; set; } = 50;
    public int ModelsPerUser { get; set; } = 200;
    public int MaximumModels { get; set; } = 10000;
    public int MaximumUsers { get; set; } = 10000;
    public long MaximumStoredBytes { get; set; } = 1_073_741_824;
    public long MaximumUserBytes { get; set; } = 67_108_864;
    public int StatisticsCacheSeconds { get; set; } = 60;
    public const int MaxRequestBytes = 8_388_608;
    public const int MaxPayloadBytes = 6_500_000;
    public const int MaxTextureCharacters = 3_000_000;

    public void Validate(bool development, JwtOptions jwt, string? connection, bool migrating)
    {
        static bool Weak(string value, int minimum) =>
            value.Length < minimum || new[] { "supersecret", "changeme", "example", "replace-me" }
                .Any(word => value.Contains(word, StringComparison.OrdinalIgnoreCase));
        if (Weak(jwt.SecretKey, 64) || Weak(FingerprintKey, 32) || jwt.SecretKey == FingerprintKey)
            throw new InvalidOperationException("Configure claves de seguridad independientes mediante secretos externos.");
        if (!Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var origin) || origin.AbsolutePath != "/"
            || (!development && origin.Scheme != "https") || origin.UserInfo.Length != 0 || origin.Query.Length != 0)
            throw new InvalidOperationException("Security:PublicOrigin debe ser un origen HTTPS válido.");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection.");
        var db = new NpgsqlConnectionStringBuilder(connection);
        if (!development && !migrating && string.Equals(db.Username, "postgres", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La API de producción requiere un rol PostgreSQL de ejecución dedicado.");
        if (jwt.AccessMinutes is < 1 or > 30 || jwt.RefreshDays is < 1 or > 30
            || string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience))
            throw new InvalidOperationException("Opciones JWT inválidas.");
        if (new[] { RequestsPerMinute, AuthRequestsPerMinute, RegistrationsPerHour, WritesPerMinute,
                ConcurrentRequests, ModelsPerIpPerDay, AnonymousModelsPerIpPerDay, ModelsPerUserPerDay,
                ModelsPerUser, MaximumModels, MaximumUsers, StatisticsCacheSeconds }.Any(v => v < 1)
            || ConcurrentRequests > 64 || MaximumUserBytes < MaxPayloadBytes || MaximumStoredBytes < MaximumUserBytes)
            throw new InvalidOperationException("Límites de recursos inválidos.");
        if (KnownProxies.Any(ip => !System.Net.IPAddress.TryParse(ip, out _)))
            throw new InvalidOperationException("Security:KnownProxies debe contener IPs explícitas.");
    }
}

public sealed class JwtOptions
{
    public string SecretKey { get; set; } = "";
    public string Issuer { get; set; } = "PhalloLab.DickifierApi";
    public string Audience { get; set; } = "PhalloLab.DickifierClient";
    public int AccessMinutes { get; set; } = 15;
    public int RefreshDays { get; set; } = 7;
}
