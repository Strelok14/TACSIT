# GPS Local Demo: Field Checklist

Короткий чеклист для развёртывания и запуска на полигоне без интернета.

## A. Подготовка на онлайн-машине

1. Обновить репозиторий и перейти в `StrikeballServer`.
2. Скачать офлайн-зависимости:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\download_offline_deps.ps1`
3. Подготовить NuGet-кэш:
   - `powershell -ExecutionPolicy Bypass -File .\scripts\prepare_offline_deps.ps1`
4. Проверить наличие:
   - `offline_deps/dotnet/win-x64/dotnet.exe`
   - `offline_deps/dotnet/linux-x64/dotnet`
   - `offline_deps/postgres/win-x64/pgsql/bin/initdb.exe`
   - `offline_deps/postgres/linux-x64/bin/pg_ctl`
   - `offline_deps/redis/win-x64/redis-server.exe`
   - `offline_deps/redis/linux-x64-source/`
   - `offline_deps/nuget/`

## B. Что копировать на флешку

1. Папка `StrikeballServer` целиком.
2. Или минимум:
   - `Server/`
   - `ServerManager/`
   - `offline_deps/`
   - `scripts/`
   - `setup.ps1`, `setup.sh`, `run.ps1`, `run.sh`
   - `README.md`, `Docs/GPS_LOCAL_OFFLINE_QUICKSTART.md`, `Docs/GPS_FIELD_CHECKLIST.md`

Для уменьшения размера флешки не брать `offline_deps/*/archives/`: в работу идут только уже распакованные каталоги.

## C. Развёртывание на целевой машине

### Windows

1. `Set-Location <путь>\StrikeballServer`
2. `./setup.ps1`
3. `./run.ps1`

### Linux

1. `cd <путь>/StrikeballServer`
2. `chmod +x setup.sh run.sh`
3. `./setup.sh`
4. `./run.sh`

Примечание: `setup.sh` стартует bundled PostgreSQL из `offline_deps/postgres/linux-x64` и собирает Redis из исходников. Если на Linux нет `gcc` и `make`, нужно установить `build-essential`.

## D. Быстрая проверка

1. API без токена: `GET http://<server-ip>:5001/api/auth/me` → `401`.
2. Логин: `POST /api/auth/login` (observer/admin).
3. Web UI: `http://<server-ip>:5001/`.
4. Данные: `GET /api/gps/current` и `GET /api/detections/recent`.

## E. Перед стартом игры

1. Сервер и телефоны в одной Wi-Fi сети.
2. Время на сервере и телефонах синхронизировано.
3. На телефонах выданы права GPS/камера/сеть.
4. На сервере есть свободное место под БД и логи.
5. Проверен логин минимум одного observer и одного player.
