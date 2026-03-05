using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Services;
using StrikeballServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "Strikeball Positioning API", 
        Version = "v1.0",
        Description = "API для системы позиционирования игроков в страйкбол"
    });
});

// Database configuration - выбираем БД в зависимости от окружения
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (builder.Environment.IsProduction())
    {
        // Production: PostgreSQL
        var postgresConnection = builder.Configuration.GetConnectionString("PostgreSQL") ?? connectionString;
        options.UseNpgsql(postgresConnection);
        options.EnableSensitiveDataLogging(false); // Не логируем параметры в production
    }
    else
    {
        // Development: SQLite (по умолчанию из DefaultConnection)
        options.UseSqlite(connectionString);
        options.EnableSensitiveDataLogging(true); // Логируем данные в разработке
    }
});

// SignalR for real-time communication
builder.Services.AddSignalR();

// CORS policy for Android/Web clients
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register application services
builder.Services.AddScoped<IPositioningService, PositioningService>();
builder.Services.AddScoped<IFilteringService, FilteringService>();
builder.Services.AddSingleton<ITelemetryService, TelemetryService>();

// Logging
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}
else
{
    // Production: только console (переходит в systemd journal)
    builder.Logging.AddConsole();
}

var app = builder.Build();

// 🔧 Middleware для логирования запросов
app.Use(async (context, next) =>
{
    var startTime = DateTime.UtcNow;
    var request = context.Request;
    
    try
    {
        await next();
        
        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        app.Logger.LogInformation(
            $"✅ {request.Method} {request.Path} → {context.Response.StatusCode} ({duration:F0}ms)");
    }
    catch (Exception ex)
    {
        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
        app.Logger.LogError(ex, $"❌ {request.Method} {request.Path} → Exception ({duration:F0}ms)");
        
        // Отправляем error response
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { 
                error = "Internal Server Error",
                message = app.Environment.IsDevelopment() ? ex.Message : "See server logs for details"
            });
        }
    }
});

// 🔧 Middleware для обработки исключений
app.UseExceptionHandler((IApplicationBuilder errorApp) =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;

        app.Logger.LogError(exception, "Необработанное исключение");

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { 
            error = "Internal Server Error",
            message = app.Environment.IsDevelopment() ? exception?.Message : "An error occurred"
        });
    });
});

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Strikeball Positioning API v1.0");
        c.RoutePrefix = string.Empty; // Swagger UI at root
    });
}
else
{
    // HTTPS in production
    app.UseHttpsRedirection();
}

// Initialize database
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        app.Logger.LogInformation("📊 Инициализация базы данных...");
        await dbContext.Database.EnsureCreatedAsync();
        app.Logger.LogInformation("✅ База данных готова");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "❌ Ошибка при инициализации БД");
        throw;
    }
}

app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

// Map SignalR hub
app.MapHub<PositioningHub>("/hubs/positioning");

// Startup info
var environment = app.Environment.EnvironmentName;
app.Logger.LogInformation($"🚀 Strikeball Positioning Server started [{environment}]");
app.Logger.LogInformation($"📡 WebSocket Hub: ws://+:5000/hubs/positioning");
app.Logger.LogInformation($"📖 Swagger UI: http://+:5000 (development only)");
app.Logger.LogInformation($"📊 Database: {(builder.Environment.IsProduction() ? "PostgreSQL" : "SQLite")}");

app.Run();
