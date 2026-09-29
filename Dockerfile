# syntax=docker/dockerfile:1

# ---- сборка ----
# SDK-этап всегда на платформе сборщика: publish даёт переносимые dll, поэтому образ под другую архитектуру
# (например, linux/amd64 с Mac на arm64) собирается без эмуляции и быстро.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Сначала проект целиком для restore: слой с пакетами кэшируется, пока не меняются зависимости.
COPY src/SupplierQuotationApi/SupplierQuotationApi.csproj src/SupplierQuotationApi/
RUN dotnet restore src/SupplierQuotationApi/SupplierQuotationApi.csproj

COPY src/SupplierQuotationApi/ src/SupplierQuotationApi/
RUN dotnet publish src/SupplierQuotationApi/SupplierQuotationApi.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- запуск ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

# Секреты в образ не попадают: .env исключён в .dockerignore, значения передаются при запуске
# (docker compose env_file или монтирование файла .env в /app/.env).
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

# В образе aspnet нет curl: проверка живости — обычным TCP-запросом к /health средствами bash.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -n1 <&3 | grep -q " 200 "' || exit 1

# Не root: пользователь app (UID 1654) уже есть в образах .NET 8.
USER $APP_UID

ENTRYPOINT ["dotnet", "SupplierQuotationApi.dll"]
