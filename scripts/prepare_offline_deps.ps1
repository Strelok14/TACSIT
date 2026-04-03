$RootDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$TargetDir = Join-Path $RootDir 'offline_deps\nuget'
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
dotnet restore (Join-Path $RootDir 'Server\StrikeballServer.csproj') --packages $TargetDir
Write-Host 'Скопируйте в offline_deps: .NET 8 runtime, PostgreSQL, Redis и osm_tiles, затем перенесите папку на офлайн-машину.'