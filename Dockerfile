FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/PocketLedger.Security/PocketLedger.Security.csproj src/PocketLedger.Security/
COPY src/PocketLedger.Landing/PocketLedger.Landing.csproj src/PocketLedger.Landing/
COPY src/PocketLedger.Domain/PocketLedger.Domain.csproj src/PocketLedger.Domain/
COPY src/PocketLedger.Application/PocketLedger.Application.csproj src/PocketLedger.Application/
COPY src/PocketLedger.Contracts/PocketLedger.Contracts.csproj src/PocketLedger.Contracts/
COPY src/PocketLedger.Infrastructure/PocketLedger.Infrastructure.csproj src/PocketLedger.Infrastructure/
COPY src/PocketLedger.Web/PocketLedger.Web.csproj src/PocketLedger.Web/
COPY src/PocketLedger.Api/PocketLedger.Api.csproj src/PocketLedger.Api/
COPY src/PocketLedger.Identity/PocketLedger.Identity.csproj src/PocketLedger.Identity/
COPY tools/PocketLedger.Security.Cli/PocketLedger.Security.Cli.csproj tools/PocketLedger.Security.Cli/
RUN dotnet restore src/PocketLedger.Landing/PocketLedger.Landing.csproj \
    && dotnet restore src/PocketLedger.Web/PocketLedger.Web.csproj \
    && dotnet restore src/PocketLedger.Api/PocketLedger.Api.csproj \
    && dotnet restore src/PocketLedger.Identity/PocketLedger.Identity.csproj \
    && dotnet restore tools/PocketLedger.Security.Cli/PocketLedger.Security.Cli.csproj

COPY src/ src/
COPY tools/PocketLedger.Security.Cli/ tools/PocketLedger.Security.Cli/
RUN dotnet publish src/PocketLedger.Landing/PocketLedger.Landing.csproj -c Release -o /app/landing --no-restore /p:UseAppHost=false
RUN dotnet publish src/PocketLedger.Web/PocketLedger.Web.csproj -c Release -o /app/web --no-restore /p:UseAppHost=false
RUN dotnet publish src/PocketLedger.Api/PocketLedger.Api.csproj -c Release -o /app/api --no-restore /p:UseAppHost=false
RUN dotnet publish src/PocketLedger.Identity/PocketLedger.Identity.csproj -c Release -o /app/identity --no-restore /p:UseAppHost=false
RUN dotnet publish tools/PocketLedger.Security.Cli/PocketLedger.Security.Cli.csproj -c Release -o /app/security-cli --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS web
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:5050
EXPOSE 5050
COPY --from=build /app/web .
COPY --from=build /app/security-cli /app/security-cli
USER $APP_UID
ENTRYPOINT ["dotnet", "PocketLedger.Web.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=https://+:5051
EXPOSE 5051
COPY --from=build /app/api .
COPY --from=build /app/security-cli /app/security-cli
USER $APP_UID
ENTRYPOINT ["dotnet", "PocketLedger.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS identity
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:5052
EXPOSE 5052
COPY --from=build /app/identity .
COPY --from=build /app/security-cli /app/security-cli
USER $APP_UID
ENTRYPOINT ["dotnet", "PocketLedger.Identity.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS landing
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://+:5053
EXPOSE 5053
COPY --from=build /app/landing .
USER $APP_UID
ENTRYPOINT ["dotnet", "PocketLedger.Landing.dll"]
