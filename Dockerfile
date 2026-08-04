FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/PocketLedger/PocketLedger.csproj src/PocketLedger/
RUN dotnet restore src/PocketLedger/PocketLedger.csproj

COPY src/PocketLedger/ src/PocketLedger/
RUN dotnet publish src/PocketLedger/PocketLedger.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5050
EXPOSE 5050

COPY --from=build /app/publish .

RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R $APP_UID:$APP_UID /home/app/.aspnet

USER $APP_UID
ENTRYPOINT ["dotnet", "PocketLedger.dll"]
