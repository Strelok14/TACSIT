# Этап 1: Сборка
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Копируем файлы проекта
COPY ["Server/StrikeballServer.csproj", "Server/"]
RUN dotnet restore "Server/StrikeballServer.csproj"

COPY . .
RUN dotnet build "Server/StrikeballServer.csproj" -c Release -o /app/build

# Этап 2: Публикация
FROM build AS publish
RUN dotnet publish "Server/StrikeballServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Этап 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Создаем пользователя для безопасности
RUN useradd -m -u 1000 strikeball && \
    chown -R strikeball:strikeball /app
USER strikeball

# Порты
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "StrikeballServer.dll"]
