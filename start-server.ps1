# 🚀 Скрипт быстрого запуска сервера

Write-Host "🚀 Запуск Strikeball Positioning Server..." -ForegroundColor Green

# Переходим в папку сервера
Set-Location -Path "$PSScriptRoot\Server"

# Восстанавливаем зависимости
Write-Host "`n📦 Восстановление зависимостей..." -ForegroundColor Cyan
dotnet restore

# Собираем проект
Write-Host "`n🔨 Сборка проекта..." -ForegroundColor Cyan
dotnet build

# Запускаем сервер
Write-Host "`n✅ Запуск сервера..." -ForegroundColor Green
Write-Host "📖 Swagger UI: http://localhost:5000" -ForegroundColor Yellow
Write-Host "📡 WebSocket Hub: ws://localhost:5000/hubs/positioning" -ForegroundColor Yellow
Write-Host "`nНажмите Ctrl+C для остановки сервера`n" -ForegroundColor Gray

dotnet run
