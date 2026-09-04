FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/PocketLedger.Domain/PocketLedger.Domain.csproj src/PocketLedger.Domain/
COPY src/PocketLedger.Application/PocketLedger.Application.csproj src/PocketLedger.Application/
COPY src/PocketLedger.Contracts/PocketLedger.Contracts.csproj src/PocketLedger.Contracts/
COPY src/PocketLedger.Infrastructure/PocketLedger.Infrastructure.csproj src/PocketLedger.Infrastructure/
COPY src/PocketLedger.Web/PocketLedger.Web.csproj src/PocketLedger.Web/
COPY src/PocketLedger.Api/PocketLedger.Api.csproj src/PocketLedger.Api/
COPY src/PocketLedger.Identity/PocketLedger.Identity.csproj src/PocketLedger.Identity/
RUN dotnet restore src/PocketLedger.Web/PocketLedger.Web.csproj \
    && dotnet restore src/PocketLedger.Api/PocketLedger.Api.csproj \
    && dotnet restore src/PocketLedger.Identity/PocketLedger.Identity.csproj

COPY src/ src/
RUN dotnet publish src/PocketLedger.Web/PocketLedger.Web.csproj -c Release -o /app/web --no-restore /p:UseAppHost=false
RUN dotnet publish src/PocketLedger.Api/PocketLedger.Api.csproj -c Release -o /app/api --no-restore /p:UseAppHost=false
RUN dotnet publish src/PocketLedger.Identity/PocketLedger.Identity.csproj -c Release -o /app/identity --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:5050
EXPOSE 5050
COPY --from=build /app/web .
USER $APP_UID
ENTRYPOINT ["dotnet", "PocketLedger.Web.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:5051
EXPOSE 5051
COPY --from=build /app/api .
USER $APP_UID
ENTRYPOINT ["dotnet", "PocketLedger.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS identity
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:5052
EXPOSE 5052
COPY --from=build /app/identity .
USER $APP_UID
ENTRYPOINT ["dotnet", "PocketLedger.Identity.dll"]
