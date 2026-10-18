# См. статью по ссылке https://aka.ms/customizecontainer, чтобы узнать как настроить контейнер отладки и как Visual Studio использует этот Dockerfile для создания образов для ускорения отладки.

# Этот этап используется при запуске из VS в быстром режиме (по умолчанию для конфигурации отладки)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
RUN mkdir -p /app/data && chown $APP_UID /app/data
USER $APP_UID
EXPOSE 8080
EXPOSE 8081


# Этот этап используется для сборки проекта службы
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["GeoProcessingService.Core.Application/GeoProcessingService.Core.Application.csproj", "GeoProcessingService.Core.Application/"]
COPY ["GeoProcessingService.Core.Domain/GeoProcessingService.Core.Domain.csproj", "GeoProcessingService.Core.Domain/"]
COPY ["GeoProcessingService.Core.Infrastructure/GeoProcessingService.Core.Infrastructure.csproj", "GeoProcessingService.Core.Infrastructure/"]
RUN dotnet restore "./GeoProcessingService.Core.Application/GeoProcessingService.Core.Application.csproj"
COPY . .
WORKDIR "/src/GeoProcessingService.Core.Application"
RUN dotnet build "./GeoProcessingService.Core.Application.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Этот этап используется для публикации проекта службы, который будет скопирован на последний этап
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./GeoProcessingService.Core.Application.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Этот этап используется в рабочей среде или при запуске из VS в обычном режиме (по умолчанию, когда конфигурация отладки не используется)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GeoProcessingService.Core.Application.dll"]
