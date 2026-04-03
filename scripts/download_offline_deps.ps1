$ErrorActionPreference = 'Stop'

$RootDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$OfflineDir = Join-Path $RootDir 'offline_deps'

$downloads = @(
    @{ Name = 'dotnet-sdk-win-x64.zip'; Url = 'https://dotnetcli.azureedge.net/dotnet/Sdk/8.0.408/dotnet-sdk-8.0.408-win-x64.zip'; Target = (Join-Path $OfflineDir 'dotnet\archives\dotnet-sdk-8.0.408-win-x64.zip') },
    @{ Name = 'dotnet-runtime-linux-x64.tar.gz'; Url = 'https://dotnetcli.azureedge.net/dotnet/Runtime/8.0.15/dotnet-runtime-8.0.15-linux-x64.tar.gz'; Target = (Join-Path $OfflineDir 'dotnet\archives\dotnet-runtime-8.0.15-linux-x64.tar.gz') },
    @{ Name = 'aspnetcore-runtime-linux-x64.tar.gz'; Url = 'https://dotnetcli.azureedge.net/dotnet/aspnetcore/Runtime/8.0.15/aspnetcore-runtime-8.0.15-linux-x64.tar.gz'; Target = (Join-Path $OfflineDir 'dotnet\archives\aspnetcore-runtime-8.0.15-linux-x64.tar.gz') },
    @{ Name = 'postgres-win-x64-binaries.zip'; Url = 'https://get.enterprisedb.com/postgresql/postgresql-16.4-1-windows-x64-binaries.zip'; Target = (Join-Path $OfflineDir 'postgres\archives\postgresql-16.4-1-windows-x64-binaries.zip') },
    @{ Name = 'postgres-linux-x64-binaries.tar.gz'; Url = 'https://get.enterprisedb.com/postgresql/postgresql-16.4-1-linux-x64-binaries.tar.gz'; Target = (Join-Path $OfflineDir 'postgres\archives\postgresql-16.4-1-linux-x64-binaries.tar.gz') },
    @{ Name = 'postgres-linux-source.tar.gz'; Url = 'https://ftp.postgresql.org/pub/source/v16.4/postgresql-16.4.tar.gz'; Target = (Join-Path $OfflineDir 'postgres\archives\postgresql-16.4-source.tar.gz') },
    @{ Name = 'redis-win-x64.zip'; Url = 'https://github.com/tporadowski/redis/releases/download/v5.0.14.1/Redis-x64-5.0.14.1.zip'; Target = (Join-Path $OfflineDir 'redis\archives\Redis-x64-5.0.14.1.zip') },
    @{ Name = 'redis-linux-source.tar.gz'; Url = 'https://download.redis.io/releases/redis-7.2.5.tar.gz'; Target = (Join-Path $OfflineDir 'redis\archives\redis-7.2.5.tar.gz') }
)

function Download-File {
    param(
        [string]$Url,
        [string]$Target
    )

    $dir = Split-Path -Parent $Target
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    if (Test-Path $Target) {
        Write-Host "Skip existing: $Target"
        return
    }

    Write-Host "Downloading: $Url"
    Invoke-WebRequest -Uri $Url -OutFile $Target -UseBasicParsing
}

function Expand-IfExists {
    param(
        [string]$Archive,
        [string]$Destination
    )

    if (-not (Test-Path $Archive)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    if ($Archive.EndsWith('.zip')) {
        Expand-Archive -Path $Archive -DestinationPath $Destination -Force
        return
    }

    tar -xf $Archive -C $Destination
}

foreach ($item in $downloads) {
    try {
        Download-File -Url $item.Url -Target $item.Target
    }
    catch {
        Write-Warning "Failed download for $($item.Name): $($_.Exception.Message)"
    }
}

$dotnetWinArchive = Join-Path $OfflineDir 'dotnet\archives\dotnet-sdk-8.0.408-win-x64.zip'
$dotnetWinDir = Join-Path $OfflineDir 'dotnet\win-x64'
if (Test-Path $dotnetWinArchive) {
    Expand-IfExists -Archive $dotnetWinArchive -Destination $dotnetWinDir
}

$dotnetLinuxRuntimeArchive = Join-Path $OfflineDir 'dotnet\archives\dotnet-runtime-8.0.15-linux-x64.tar.gz'
$dotnetLinuxAspArchive = Join-Path $OfflineDir 'dotnet\archives\aspnetcore-runtime-8.0.15-linux-x64.tar.gz'
$dotnetLinuxDir = Join-Path $OfflineDir 'dotnet\linux-x64'
if (Test-Path $dotnetLinuxRuntimeArchive) {
    Expand-IfExists -Archive $dotnetLinuxRuntimeArchive -Destination $dotnetLinuxDir
}
if (Test-Path $dotnetLinuxAspArchive) {
    Expand-IfExists -Archive $dotnetLinuxAspArchive -Destination $dotnetLinuxDir
}

$postgresWinArchive = Join-Path $OfflineDir 'postgres\archives\postgresql-16.4-1-windows-x64-binaries.zip'
$postgresWinDir = Join-Path $OfflineDir 'postgres\win-x64'
if (Test-Path $postgresWinArchive) {
    Expand-IfExists -Archive $postgresWinArchive -Destination $postgresWinDir
}

# Remove optional heavy GUI components from Windows PostgreSQL bundle.
$pgAdminPath = Join-Path $postgresWinDir 'pgsql\pgAdmin 4'
if (Test-Path $pgAdminPath) {
    Remove-Item -Recurse -Force $pgAdminPath
}

$stackBuilderPath = Join-Path $postgresWinDir 'pgsql\stackbuilder.exe'
if (Test-Path $stackBuilderPath) {
    Remove-Item -Force $stackBuilderPath
}

$postgresLinuxArchive = Join-Path $OfflineDir 'postgres\archives\postgresql-16.4-1-linux-x64-binaries.tar.gz'
$postgresLinuxDir = Join-Path $OfflineDir 'postgres\linux-x64'
if (Test-Path $postgresLinuxArchive) {
    Expand-IfExists -Archive $postgresLinuxArchive -Destination $postgresLinuxDir
}

$postgresLinuxSourceArchive = Join-Path $OfflineDir 'postgres\archives\postgresql-16.4-source.tar.gz'
$postgresLinuxSourceDir = Join-Path $OfflineDir 'postgres\linux-x64-source'
if (Test-Path $postgresLinuxSourceArchive) {
    Expand-IfExists -Archive $postgresLinuxSourceArchive -Destination $postgresLinuxSourceDir
}

$redisWinArchive = Join-Path $OfflineDir 'redis\archives\Redis-x64-5.0.14.1.zip'
$redisWinDir = Join-Path $OfflineDir 'redis\win-x64'
if (Test-Path $redisWinArchive) {
    Expand-IfExists -Archive $redisWinArchive -Destination $redisWinDir
}

$redisLinuxArchive = Join-Path $OfflineDir 'redis\archives\redis-7.2.5.tar.gz'
$redisLinuxDir = Join-Path $OfflineDir 'redis\linux-x64-source'
if (Test-Path $redisLinuxArchive) {
    Expand-IfExists -Archive $redisLinuxArchive -Destination $redisLinuxDir
}

Write-Host 'Offline downloads completed. Review warnings above and verify offline_deps contents.'
