-- Runtime SQL migration for security hardening.
-- Применяется программно через SecuritySchemaMigrator.

-- PostgreSQL
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name='Beacons' AND column_name='KeyVersion'
    ) THEN
        ALTER TABLE "Beacons" ADD COLUMN "KeyVersion" integer NOT NULL DEFAULT 1;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name='Measurements' AND column_name='PacketSequence'
    ) THEN
        ALTER TABLE "Measurements" ADD COLUMN "PacketSequence" bigint NULL;
    END IF;
END $$;

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

CREATE UNIQUE INDEX IF NOT EXISTS "IX_BeaconSecrets_BeaconId_KeyVersion"
    ON "BeaconSecrets" ("BeaconId", "KeyVersion");

CREATE INDEX IF NOT EXISTS "IX_BeaconSecrets_BeaconId_IsActive"
    ON "BeaconSecrets" ("BeaconId", "IsActive");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Measurements_BeaconId_PacketSequence"
    ON "Measurements" ("BeaconId", "PacketSequence")
    WHERE "PacketSequence" IS NOT NULL;
