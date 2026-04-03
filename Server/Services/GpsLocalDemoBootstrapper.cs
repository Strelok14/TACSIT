using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StrikeballServer.Data;
using StrikeballServer.Models;

namespace StrikeballServer.Services;

public static class GpsLocalDemoBootstrapper
{
    public static async Task EnsureSeedDataAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = new PasswordHasher<User>();

        if (await db.Users.AnyAsync())
        {
            return;
        }

        var configuredUsers = configuration
            .GetSection("LocalDemo:BootstrapUsers")
            .Get<List<DemoBootstrapUserDto>>();

        configuredUsers ??=
        [
            new DemoBootstrapUserDto { Id = 1, Login = "admin", Password = "AdminDemo123!", Role = "admin", DisplayName = "Администратор" },
            new DemoBootstrapUserDto { Id = 2, Login = "observer", Password = "ObserverDemo123!", Role = "observer", DisplayName = "Наблюдатель" },
            new DemoBootstrapUserDto { Id = 3, Login = "player1", Password = "PlayerDemo123!", Role = "player", DisplayName = "Игрок 1" }
        ];

        foreach (var bootstrapUser in configuredUsers)
        {
            var user = new User
            {
                Id = bootstrapUser.Id,
                Login = bootstrapUser.Login,
                Role = bootstrapUser.Role,
                DisplayName = string.IsNullOrWhiteSpace(bootstrapUser.DisplayName) ? bootstrapUser.Login : bootstrapUser.DisplayName,
                CreatedAtUtc = DateTime.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, bootstrapUser.Password);
            db.Users.Add(user);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Seeded {count} local demo users", configuredUsers.Count);
    }
}