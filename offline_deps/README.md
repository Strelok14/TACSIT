# offline_deps

Эта папка предназначена для полностью офлайн-развёртываемого стенда gps-local-demo.

Ожидаемая структура:

- dotnet/win-x64/
- dotnet/linux-x64/
- nuget/
- postgres/win-x64/
- postgres/linux-x64/
- redis/win-x64/
- redis/linux-x64-source/
- osm_tiles/
- node_modules_cache/

Эти бинарники и кэши подготавливаются заранее на машине с интернетом и затем переносятся на флешке.

Актуально для Linux:

- PostgreSQL используется из portable-пакета в `postgres/linux-x64/`.
- Redis собирается на целевой Linux-машине из `redis/linux-x64-source/`.
- Папки `archives/` нужны только на этапе подготовки. После распаковки их можно удалить, если нужно уменьшить размер флешки.

Автоматическая подготовка на Windows:

- `powershell -ExecutionPolicy Bypass -File .\scripts\download_offline_deps.ps1`
- `powershell -ExecutionPolicy Bypass -File .\scripts\prepare_offline_deps.ps1`