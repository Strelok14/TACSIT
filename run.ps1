$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:DOTNET_ROOT = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Join-Path $RootDir 'offline_deps\dotnet' }
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:ASPNETCORE_ENVIRONMENT = if ($env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT } else { 'Local' }
$env:ASPNETCORE_URLS = if ($env:ASPNETCORE_URLS) { $env:ASPNETCORE_URLS } else { 'http://0.0.0.0:5001' }
$env:TACID_ALLOW_INSECURE_HTTP = 'true'

& (Join-Path $env:DOTNET_ROOT 'dotnet.exe') (Join-Path $RootDir 'Server\bin\Debug\net8.0\StrikeballServer.dll')