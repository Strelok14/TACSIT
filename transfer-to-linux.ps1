#!/usr/bin/env pwsh
# PowerShell скрипт для переноса проекта на Linux сервер через SCP

param(
    [string]$Server = "",
    [string]$User = "root",
    [string]$TargetPath = "/opt"
)

# Проверка параметров
if ([string]::IsNullOrEmpty($Server)) {
    Write-Host "❌ Необходимо указать IP или адрес сервера" -ForegroundColor Red
    Write-Host ""
    Write-Host "Использование: .\transfer-to-linux.ps1 -Server <ip_or_hostname> -User <username>" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Примеры:"
    Write-Host "  .\transfer-to-linux.ps1 -Server 192.168.1.100"
    Write-Host "  .\transfer-to-linux.ps1 -Server server.example.com -User ubuntu"
    exit 1
}

$ProjectPath = "c:\Я\ICIDS\serv_1\StrikeballServer"
$RemotePath = "$User@$Server`:$TargetPath/StrikeballServer"

Write-Host "════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "📦 Перенос проекта на Linux сервер" -ForegroundColor Green
Write-Host "════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "📁 Источник: $ProjectPath"
Write-Host "🌐 Сервер: $RemotePath"
Write-Host "👤 Пользователь: $User"
Write-Host ""

# Проверка что папка существует
if (-not (Test-Path $ProjectPath)) {
    Write-Host "❌ Папка не найдена: $ProjectPath" -ForegroundColor Red
    exit 1
}

# Проверка наличия scp
try {
    $null = scp 2>&1
    Write-Host "✅ SCP найден" -ForegroundColor Green
} catch {
    Write-Host "❌ SCP не установлен. Установите Git Bash или WSL." -ForegroundColor Red
    exit 1
}

# Подтверждение
Write-Host ""
Write-Host "⚠️  Это скопирует весь проект на сервер."
Write-Host "Убедитесь что сервер доступен через SSH!"
Write-Host ""
$confirmation = Read-Host "Продолжить? (y/n)"

if ($confirmation -ne "y") {
    Write-Host "❌ Отменено" -ForegroundColor Red
    exit 0
}

Write-Host ""
Write-Host "📤 Начинается перенос..."
Write-Host ""

# Копирование через SCP
scp -r "$ProjectPath" "$RemotePath"

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "✅ Перенос успешен!" -ForegroundColor Green
    Write-Host "════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Host "🚀 Теперь на сервере запустите:"
    Write-Host ""
    Write-Host "  ssh $User@$Server" -ForegroundColor Yellow
    Write-Host "  cd $TargetPath/StrikeballServer" -ForegroundColor Yellow
    Write-Host "  chmod +x deploy.sh" -ForegroundColor Yellow
    Write-Host "  sudo ./deploy.sh" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "📖 Подробная инструкция в:"
    Write-Host "  LINUX_DEPLOYMENT_STEP_BY_STEP.sh"
}
else {
    Write-Host ""
    Write-Host "❌ Ошибка при передаче файлов" -ForegroundColor Red
    Write-Host "Проверьте:"
    Write-Host "  ☐ SSH доступ к серверу (попробуйте 'ssh $User@$Server')"
    Write-Host "  ☐ Права доступа"
    Write-Host "  ☐ Наличие места на диске на сервере"
}
