using System.Text;
using backend_dickifier.Data;
using backend_dickifier.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Configurar puerto por defecto (5195)
builder.WebHost.UseUrls("http://localhost:5195");

// 2. Base de datos PostgreSQL con EF Core
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=dickifier_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<DickifierDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// 3. Inyección de dependencias de servicios
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();

// 4. Autenticación con JWT Bearer
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]
    ?? "SuperSecretKeyForPhalloLabDickifierApp2026!WithSufficientLengthForHmacSha256";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "PhalloLab.DickifierApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "PhalloLab.DickifierClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization();

// 5. CORS para Vite (localhost:5173 y afines)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowVite", policy =>
    {
        policy.WithOrigins(
                  "http://localhost:5173",
                  "http://127.0.0.1:5173",
                  "http://localhost:5174",
                  "http://127.0.0.1:5174",
                  "http://localhost:5198",
                  "http://127.0.0.1:5198"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 6. Controladores y Swagger con soporte para JWT
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Dickifier Studio API - PhalloLab",
        Version = "v1",
        Description = "Backend oficial del Repositorio Global Comunitario y estadísticas anatómicas de Dickifier Studio."
    });

    // Definición de seguridad Bearer JWT en Swagger UI
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Cabecera de autorización JWT usando el esquema Bearer. Ejemplo: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// 7. Migración automática de base de datos en inicio si está disponible
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = services.GetRequiredService<DickifierDbContext>();
        db.Database.Migrate();
        logger.LogInformation("Base de datos de Dickifier migrada correctamente.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "No se pudo ejecutar Migrate() automáticamente al iniciar. Verifica la conexión a PostgreSQL.");
    }
}

// 8. Pipeline HTTP
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Dickifier Studio API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowVite");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    status = "running",
    name = "Dickifier Studio (PhalloLab) - Backend API",
    swagger = "/swagger",
    endpoints = new[] { "/api/auth", "/api/models", "/api/stats" },
    timestamp = DateTime.UtcNow
}));

app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();

// Permite visibilidad para WebApplicationFactory en proyectos de pruebas
public partial class Program { }

