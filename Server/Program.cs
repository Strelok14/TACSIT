using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using StrikeballServer.Data;
using StrikeballServer.Hubs;
using StrikeballServer.Middleware;
using StrikeballServer.Services;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var allowInsecureHttp = builder.Configuration.GetValue<bool>("Security:AllowInsecureHttp")
    || string.Equals(
        Environment.GetEnvironmentVariable("TACID_ALLOW_INSECURE_HTTP"),
        "true",
        StringComparison.OrdinalIgnoreCase);
var requireHttps = builder.Environment.IsProduction() && !allowInsecureHttp;

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Strikeball Positioning API",
        Version = "v1.1",
        Description = "Защищённый API для системы позиционирования игроков в страйкбол"
    });
});

// Database configuration - выбираем БД в зависимости от окружения
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (builder.Environment.IsProduction())
    {
        var postgresConnection = builder.Configuration.GetConnectionString("PostgreSQL") ?? connectionString;
        options.UseNpgsql(postgresConnection);
        options.EnableSensitiveDataLogging(false);
    }
    else
    {
        options.UseSqlite(connectionString);
        options.EnableSensitiveDataLogging(false);
    }
});

builder.Services.AddSignalR(options =>
{
    // Ограничение размера входящих сообщений Hub для снижения риска abuse.
    options.MaximumReceiveMessageSize = 64 * 1024;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("StrictCors", policy =>
    {
        var origins = builder.Configuration.GetSection("Security:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        if (origins.Length == 0)
        {
            // Для тестовых стендов без origin-конфига сохраняем совместимость.
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
        }
    });
});

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? Environment.GetEnvironmentVariable("TACID_JWT_SIGNING_KEY")
    ?? throw new InvalidOperationException("JWT signing key is required (TACID_JWT_SIGNING_KEY)");

var issuer = builder.Configuration["Jwt:Issuer"] ?? "tacid-server";
var audience = builder.Configuration["Jwt:Audience"] ?? "tacid-clients";
var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // HTTPS обязателен только в production; в dev/testing отключаем.
        options.RequireHttpsMetadata = requireHttps;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = securityKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };

        options.Events = new JwtBearerEvents
        {
            // Для SignalR принимаем access_token из query string.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrWhiteSpace(accessToken)
                    && path.StartsWithSegments("/hubs/positioning", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },

            // Проверяем JTI в denylist после успешной валидации подписи.
            OnTokenValidated = async context =>
            {
                var jti = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
                if (!string.IsNullOrEmpty(jti))
                {
                    var denylist = context.HttpContext.RequestServices
                        .GetRequiredService<IJwtDenylistService>();
                    if (await denylist.IsDeniedAsync(jti, context.HttpContext.RequestAborted))
                    {
                        context.Fail("Token has been revoked");
                    }
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ObserverPolicy", p => p.RequireRole("observer", "admin"));
    options.AddPolicy("PlayerPolicy", p => p.RequireRole("player", "admin"));
    options.AddPolicy("AdminPolicy", p => p.RequireRole("admin"));
});

builder.Services.AddScoped<IPositioningService, PositioningService>();
builder.Services.AddScoped<IFilteringService, FilteringService>();
builder.Services.AddSingleton<ITelemetryService, TelemetryService>();
builder.Services.AddScoped<IBeaconKeyStore, BeaconKeyStore>();
builder.Services.AddScoped<ISecurityEventLogger, SecurityEventLogger>();
builder.Services.AddSingleton<IReplayProtectionService, RedisReplayProtectionService>();
builder.Services.AddSingleton<ITelemetryRateLimiter, RedisTelemetryRateLimiter>();
builder.Services.AddSingleton<IJwtDenylistService, RedisJwtDenylistService>();
builder.Services.AddHostedService<BeaconKeyRotationHostedService>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddDebug();
}

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
    });
});

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Strikeball Positioning API v1.0");
        c.RoutePrefix = string.Empty;
    });
}
else
{
    if (requireHttps)
    {
        app.UseHsts();
    }
}

if (requireHttps)
{
    app.UseHttpsRedirection();
}

// Жёсткий запрет незащищённого HTTP — только в production.
if (requireHttps)
{
    app.Use(async (context, next) =>
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        var isLocal = remoteIp != null && System.Net.IPAddress.IsLoopback(remoteIp);

        if (!context.Request.IsHttps && !isLocal)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "HTTPS is required" });
            return;
        }

        await next();
    });
}

// Инициализация БД + security-миграция.
await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    app.Logger.LogInformation("Инициализация базы данных...");
    await dbContext.Database.EnsureCreatedAsync();
    if (!app.Environment.IsEnvironment("Testing"))
    {
        await SecuritySchemaMigrator.ApplyAsync(dbContext, app.Logger);
    }
    app.Logger.LogInformation("База данных готова");
}

app.UseCors("StrictCors");
app.UseAuthentication();

// Криптографический входной фильтр телеметрии выполняется перед авторизацией.
app.UseMiddleware<TelemetrySecurityMiddleware>();

app.UseAuthorization();
app.MapControllers();
app.MapHub<PositioningHub>("/hubs/positioning");

var environment = app.Environment.EnvironmentName;
app.Logger.LogInformation("Strikeball Positioning Server started [{environment}]", environment);
app.Logger.LogInformation("SignalR Hub endpoint: /hubs/positioning");
app.Logger.LogInformation("HTTPS enforcement: {mode}", requireHttps ? "enabled" : "disabled (LAN/VPN mode)");
app.Logger.LogInformation("Database provider: {provider}", builder.Environment.IsProduction() ? "PostgreSQL" : "SQLite");

app.Run();

// Делаем Program доступным для WebApplicationFactory в интеграционных тестах.
public partial class Program { }
