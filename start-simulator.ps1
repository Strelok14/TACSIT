# 🧪 Скрипт запуска симулятора маяков

Write-Host "🧪 Запуск симулятора маяков..." -ForegroundColor Green
Write-Host "⚠️  Убедитесь, что сервер запущен на http://localhost:5000`n" -ForegroundColor Yellow

# Переходим в папку тестов
Set-Location -Path "$PSScriptRoot\Tests"

# Восстанавливаем зависимости
Write-Host "📦 Восстановление зависимостей..." -ForegroundColor Cyan
dotnet restore

# Запускаем симулятор
Write-Host "`n✅ Запуск симулятора...`n" -ForegroundColor Green
dotnet run
