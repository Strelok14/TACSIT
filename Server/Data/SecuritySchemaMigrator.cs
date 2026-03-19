using Microsoft.EntityFrameworkCore;

namespace StrikeballServer.Data;

/// <summary>
/// Лёгкий runtime-мигратор для security-схемы.
/// Нужен для существующих БД, где таблицы были созданы через EnsureCreated.
/// </summary>
public static class SecuritySchemaMigrator
{
    public static async Task ApplyAsync(ApplicationDbContext dbContext, ILogger logger)
    {
        var provider = dbContext.Database.ProviderName ?? string.Empty;

        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await ApplyPostgresAsync(dbContext, logger);
            return;
        }

        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await ApplySqliteAsync(dbContext, logger);
            return;
        }

        logger.LogWarning("Security schema migrator: unknown provider {provider}, migration skipped", provider);
    }

    private static async Task ApplyPostgresAsync(ApplicationDbContext dbContext, ILogger logger)
    {
        // Добавляем новые security-колонки и таблицы идемпотентно.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name='Beacons' AND column_name='KeyVersion'
                ) THEN
                    ALTER TABLE "Beacons" ADD COLUMN "KeyVersion" integer NOT NULL DEFAULT 1;
                END IF;
            END $$;
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name='Measurements' AND column_name='PacketSequence'
                ) THEN
                    ALTER TABLE "Measurements" ADD COLUMN "PacketSequence" bigint NULL;
                END IF;
            END $$;
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "BeaconSecrets" (
                "Id" bigserial PRIMARY KEY,
                "BeaconId" integer NOT NULL,
                "KeyVersion" integer NOT NULL,
                "Ciphertext" bytea NOT NULL,
                "Nonce" bytea NOT NULL,
                "Tag" bytea NOT NULL,
                "IsActive" boolean NOT NULL DEFAULT false,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "ValidUntilUtc" timestamp with time zone NULL,
                CONSTRAINT "FK_BeaconSecrets_Beacons_BeaconId" FOREIGN KEY ("BeaconId") REFERENCES "Beacons" ("Id") ON DELETE CASCADE
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BeaconSecrets_BeaconId_KeyVersion"
                ON "BeaconSecrets" ("BeaconId", "KeyVersion");
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_BeaconSecrets_BeaconId_IsActive"
                ON "BeaconSecrets" ("BeaconId", "IsActive");
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Measurements_BeaconId_PacketSequence"
                ON "Measurements" ("BeaconId", "PacketSequence")
                WHERE "PacketSequence" IS NOT NULL;
            """);

        logger.LogInformation("Security schema migration for PostgreSQL applied");
    }

    private static async Task ApplySqliteAsync(ApplicationDbContext dbContext, ILogger logger)
    {
        var hasBeaconKeyVersion = await ColumnExistsSqlite(dbContext, "Beacons", "KeyVersion");
        if (!hasBeaconKeyVersion)
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Beacons ADD COLUMN KeyVersion INTEGER NOT NULL DEFAULT 1;");
        }

        var hasPacketSequence = await ColumnExistsSqlite(dbContext, "Measurements", "PacketSequence");
        if (!hasPacketSequence)
        {
            await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Measurements ADD COLUMN PacketSequence INTEGER NULL;");
        }

        await dbContext.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS BeaconSecrets (
    Id INTEGER NOT NULL CONSTRAINT PK_BeaconSecrets PRIMARY KEY AUTOINCREMENT,
    BeaconId INTEGER NOT NULL,
    KeyVersion INTEGER NOT NULL,
    Ciphertext BLOB NOT NULL,
    Nonce BLOB NOT NULL,
    Tag BLOB NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc TEXT NOT NULL,
    ValidUntilUtc TEXT NULL,
    FOREIGN KEY (BeaconId) REFERENCES Beacons (Id) ON DELETE CASCADE
);");

        await dbContext.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_BeaconSecrets_BeaconId_KeyVersion ON BeaconSecrets (BeaconId, KeyVersion);");
        await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_BeaconSecrets_BeaconId_IsActive ON BeaconSecrets (BeaconId, IsActive);");
        await dbContext.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Measurements_BeaconId_PacketSequence ON Measurements (BeaconId, PacketSequence);");

        logger.LogInformation("Security schema migration for SQLite applied");
    }

    private static async Task<bool> ColumnExistsSqlite(ApplicationDbContext dbContext, string tableName, string columnName)
    {
        await using var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader[1]?.ToString();
            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
