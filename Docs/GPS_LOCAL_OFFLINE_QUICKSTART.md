# GPS Local Demo: Offline Quickstart

Эта инструкция для ветки `gps-local-demo`. Цель: поднять сервер в локальной сети без интернета.

## 1. Что подготовить на онлайн-машине

1. Перейти в корень проекта `StrikeballServer`.
2. Запустить загрузку офлайн-зависимостей:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\download_offline_deps.ps1`
3. Заполнить NuGet-кэш:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\prepare_offline_deps.ps1`

После этого папка `offline_deps` должна содержать минимум:

- `dotnet/win-x64/dotnet.exe`
- `dotnet/linux-x64/dotnet`
- `nuget/` (кэш пакетов)
- `postgres/win-x64/pgsql/bin/initdb.exe`
- `postgres/win-x64/pgsql/bin/pg_ctl.exe`
- `redis/win-x64/redis-server.exe`
- `redis/linux-x64-source/` (если нет готовых Linux binaries)
- `osm_tiles/` (опционально, офлайн-карта)

## 2. Что копировать на флешку

Скопировать целиком каталог `StrikeballServer` (или минимум):

- `Server/`
- `ServerManager/`
- `scripts/`
- `offline_deps/`
- `setup.ps1`, `setup.sh`, `run.ps1`, `run.sh`
- `README.md`, `Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md`

Не требуется интернет на целевой машине.

## 3. Развёртывание на Windows (офлайн)

1. Скопировать папку проекта на сервер, например `D:\TACID`.
2. Открыть PowerShell от администратора.
3. Выполнить:
   - `Set-Location D:\TACID\StrikeballServer`
   - `.\setup.ps1`
4. Запустить сервер:
   - `.\run.ps1`

Сервер поднимется в режиме `Local`, HTTP без TLS (LAN).

## 4. Развёртывание на Linux (офлайн)

1. Скопировать проект, например в `/opt/tacid/StrikeballServer`.
2. Установить PostgreSQL (требуется один раз):

   **Вариант A — если есть доступ к интернету или локальному зеркалу:**
   ```bash
   apt install -y postgresql-16
   ```

   **Вариант B — полный офлайн (скачать .deb на Windows, перенести флешкой):**
   ```bash
   # На Windows — скачать пакеты (открыть в браузере):
   # https://apt.postgresql.org/pub/repos/apt/pool/main/p/postgresql-16/
   # Нужны: postgresql-16_16.x_amd64.deb, postgresql-client-16, libpq5, postgresql-common
   # Скопировать .deb-файлы на Linux, затем:
   dpkg -i *.deb
   ```

3. Выполнить:
   ```bash
   cd /opt/tacid/StrikeballServer
   chmod +x setup.sh run.sh
   ./setup.sh
   ```
   > Redis компилируется из исходников автоматически (нужны `gcc` и `make` — есть по умолчанию).  
   > Если `gcc`/`make` нет: `apt install -y build-essential`

4. Запустить сервер:
   ```bash
   ./run.sh
   ```

## 5. Проверка после старта

1. Проверить доступность API:
   - `GET http://<server-ip>:5001/api/auth/me` → ожидаемо `401` без токена.
2. Войти через `POST /api/auth/login`.
3. Открыть Web UI:
   - `http://<server-ip>:5001/`
4. Убедиться, что Android-клиент отправляет:
   - `POST /api/gps`
   - `POST /api/detections`

## 6. Безопасность и ключи

- JWT signing key и master key генерируются при первом старте и хранятся в `App_Data/keys`.
- В офлайн-LAN режиме используется HTTP, но пакеты защищены `JWT + HMAC + replay + rate limit`.
