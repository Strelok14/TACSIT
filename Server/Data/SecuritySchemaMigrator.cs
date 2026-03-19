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
            DROP INDEX IF EXISTS "IX_Measurements_BeaconId_PacketSequence";
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Measurements_BeaconId_PacketSequence"
                ON "Measurements" ("BeaconId", "PacketSequence", "AnchorId")
                WHERE "PacketSequence" IS NOT NULL;
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "RefreshTokens" (
                "Id" bigserial PRIMARY KEY,
                "UserId" varchar(100) NOT NULL,
                "Token" varchar(256) NOT NULL,
                "ExpiryUtc" timestamp with time zone NOT NULL,
                "IsRevoked" boolean NOT NULL DEFAULT false,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_RefreshTokens_Token" ON "RefreshTokens" ("Token");
            """);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_RefreshTokens_UserId_IsRevoked" ON "RefreshTokens" ("UserId", "IsRevoked");
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

        // В legacy-схемах таблица может называться Measurement (singular).
        string? measurementTable = null;
        if (await TableExistsSqlite(dbContext, "Measurements")) measurementTable = "Measurements";
        else if (await TableExistsSqlite(dbContext, "Measurement")) measurementTable = "Measurement";

        if (!string.IsNullOrEmpty(measurementTable))
        {
            var hasPacketSequence = await ColumnExistsSqlite(dbContext, measurementTable, "PacketSequence");
            if (!hasPacketSequence)
            {
                if (string.Equals(measurementTable, "Measurements", StringComparison.OrdinalIgnoreCase))
                {
                    await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Measurements ADD COLUMN PacketSequence INTEGER NULL;");
                }
                else
                {
                    await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE Measurement ADD COLUMN PacketSequence INTEGER NULL;");
                }
            }
        }
        else
        {
            logger.LogWarning("Security schema migration (SQLite): measurement table not found, PacketSequence migration skipped");
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
        if (!string.IsNullOrEmpty(measurementTable))
        {
            if (string.Equals(measurementTable, "Measurements", StringComparison.OrdinalIgnoreCase))
            {
                await dbContext.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_Measurements_BeaconId_PacketSequence;");
                await dbContext.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Measurements_BeaconId_PacketSequence ON Measurements (BeaconId, PacketSequence, AnchorId);");
            }
            else
            {
                await dbContext.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_Measurement_BeaconId_PacketSequence;");
                await dbContext.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Measurement_BeaconId_PacketSequence ON Measurement (BeaconId, PacketSequence, AnchorId);");
            }
        }

        await dbContext.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS RefreshTokens (
    Id INTEGER NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY AUTOINCREMENT,
    UserId TEXT NOT NULL,
    Token TEXT NOT NULL,
    ExpiryUtc TEXT NOT NULL,
    IsRevoked INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc TEXT NOT NULL
);");
        await dbContext.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_RefreshTokens_Token ON RefreshTokens (Token);");
        await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_RefreshTokens_UserId_IsRevoked ON RefreshTokens (UserId, IsRevoked);");

        logger.LogInformation("Security schema migration for SQLite applied");
    }

    private static async Task<bool> TableExistsSqlite(ApplicationDbContext dbContext, string tableName)
    {
        await using var conn = dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name = $name LIMIT 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$name";
        p.Value = tableName;
        cmd.Parameters.Add(p);

        var result = await cmd.ExecuteScalarAsync();
        return result != null && result != DBNull.Value;
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
