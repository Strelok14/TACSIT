$ErrorActionPreference = 'Stop'

$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$OfflineDir = Join-Path $RootDir 'offline_deps'
$ServerProject = Join-Path $RootDir 'Server\StrikeballServer.csproj'
$EnvFile = if ($env:TACID_ENV_FILE) { $env:TACID_ENV_FILE } else { Join-Path $RootDir 'server.local.env' }

function Require-Path([string]$PathValue) {
    if (-not (Test-Path $PathValue)) {
        throw "Missing required offline dependency: $PathValue"
    }
}

Require-Path (Join-Path $OfflineDir 'dotnet')
Require-Path (Join-Path $OfflineDir 'nuget')
Require-Path (Join-Path $OfflineDir 'postgres')
Require-Path (Join-Path $OfflineDir 'redis')

$env:DOTNET_ROOT = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Join-Path $OfflineDir 'dotnet' }
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:ASPNETCORE_ENVIRONMENT = 'Local'
$env:ASPNETCORE_URLS = if ($env:ASPNETCORE_URLS) { $env:ASPNETCORE_URLS } else { 'http://0.0.0.0:5001' }
$env:TACID_ALLOW_INSECURE_HTTP = 'true'
$env:ConnectionStrings__PostgreSQL = if ($env:ConnectionStrings__PostgreSQL) { $env:ConnectionStrings__PostgreSQL } else { 'Host=127.0.0.1;Port=5432;Database=tacid_local_demo;Username=tacid;Password=tacid-demo' }
$env:Redis__ConnectionString = if ($env:Redis__ConnectionString) { $env:Redis__ConnectionString } else { '127.0.0.1:6379,abortConnect=false' }
$env:Security__SecretStoreDirectory = if ($env:Security__SecretStoreDirectory) { $env:Security__SecretStoreDirectory } else { Join-Path $RootDir 'App_Data\keys' }

New-Item -ItemType Directory -Force -Path (Join-Path $RootDir 'App_Data\keys') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $RootDir 'App_Data\postgres') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $RootDir 'App_Data\logs') | Out-Null

@"
ASPNETCORE_ENVIRONMENT=Local
ASPNETCORE_URLS=$($env:ASPNETCORE_URLS)
TACID_ALLOW_INSECURE_HTTP=true
ConnectionStrings__PostgreSQL=$($env:ConnectionStrings__PostgreSQL)
Redis__ConnectionString=$($env:Redis__ConnectionString)
Security__SecretStoreDirectory=$($env:Security__SecretStoreDirectory)
"@ | Set-Content -Encoding UTF8 $EnvFile

$redisExe = Join-Path $OfflineDir 'redis\redis-server.exe'
if (Test-Path $redisExe) {
    Start-Process -FilePath $redisExe -ArgumentList '--port 6379 --save "" --appendonly no' -WindowStyle Hidden
}

$pgCtl = Join-Path $OfflineDir 'postgres\bin\pg_ctl.exe'
$initDb = Join-Path $OfflineDir 'postgres\bin\initdb.exe'
$pgData = Join-Path $RootDir 'App_Data\postgres'
if ((Test-Path $initDb) -and -not (Test-Path (Join-Path $pgData 'PG_VERSION'))) {
    & $initDb -D $pgData | Out-Host
}
if (Test-Path $pgCtl) {
    & $pgCtl -D $pgData -l (Join-Path $RootDir 'App_Data\logs\postgres.log') start | Out-Host
}

& (Join-Path $env:DOTNET_ROOT 'dotnet.exe') restore $ServerProject --packages (Join-Path $OfflineDir 'nuget')
& (Join-Path $env:DOTNET_ROOT 'dotnet.exe') build $ServerProject --no-restore

$taskCommand = "`"$env:DOTNET_ROOT\dotnet.exe`" `"$RootDir\Server\bin\Debug\net8.0\StrikeballServer.dll`""
schtasks /Create /F /SC ONLOGON /TN 'TACID GPS Local Demo' /TR $taskCommand | Out-Host

Write-Host "Offline setup completed. Auto-start task: TACID GPS Local Demo"