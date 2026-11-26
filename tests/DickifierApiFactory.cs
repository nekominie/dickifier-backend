using System.Net.Http.Headers;
using backend_dickifier.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace backend_dickifier.Tests;

public sealed class DickifierApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string PublicOrigin = "https://dickifier.nekomisystems.com";
    private const string DefaultMigratorConn =
        "Host=127.0.0.1;Port=15432;Database=dickifier_test;Username=dickifier_migrator;Password=test_migrator_password_only";
    private const string DefaultAppConn =
        "Host=127.0.0.1;Port=15432;Database=dickifier_test;Username=dickifier_app;Password=test_app_password_only";

    private readonly string _migratorConn =
        Environment.GetEnvironmentVariable("DICKIFIER_TEST_MIGRATOR_CONN") ?? DefaultMigratorConn;
    private readonly string _appConn =
        Environment.GetEnvironmentVariable("DICKIFIER_TEST_APP_CONN") ?? DefaultAppConn;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _appConn);
        Environment.SetEnvironmentVariable("Jwt__SecretKey", "test_only_jwt_secret_key_with_more_than_64_characters_1234567890abcdef");
        Environment.SetEnvironmentVariable("Security__FingerprintKey", "test_only_fingerprint_key_with_more_than_32_characters_abcdef");
        Environment.SetEnvironmentVariable("Security__PublicOrigin", PublicOrigin);
        Environment.SetEnvironmentVariable("Security__KnownProxies__0", "10.255.255.254");
        Environment.SetEnvironmentVariable("Security__RegistrationsPerHour", "50");
        Environment.SetEnvironmentVariable("Security__AnonymousPublishingEnabled", "true");
        Environment.SetEnvironmentVariable("AllowedHosts", "dickifier.nekomisystems.com;localhost;127.0.0.1");

        var options = new DbContextOptionsBuilder<DickifierDbContext>()
            .UseNpgsql(_migratorConn)
            .Options;

        await using var db = new DickifierDbContext(options);
        await db.Database.MigrateAsync();
    }

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _appConn,
                ["Jwt:SecretKey"] = "test_only_jwt_secret_key_with_more_than_64_characters_1234567890abcdef",
                ["Security:FingerprintKey"] = "test_only_fingerprint_key_with_more_than_32_characters_abcdef",
                ["Security:PublicOrigin"] = PublicOrigin,
                ["Security:KnownProxies:0"] = "127.0.0.1",
                ["AllowedHosts"] = "dickifier.nekomisystems.com;localhost;127.0.0.1"
            });
        });
    }

    public HttpClient CreateBrowserClient(bool withOrigin = true, bool handleCookies = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(PublicOrigin),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies
        });
        if (withOrigin)
        {
            client.DefaultRequestHeaders.Add("Origin", PublicOrigin);
        }
        return client;
    }

    public static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
