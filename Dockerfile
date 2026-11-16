# syntax=docker/dockerfile:1

# SDK con el código y dotnet-ef: lo usan las migraciones y el provisioning de desarrollo.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS tools
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    PATH="$PATH:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --version 10.0.12
COPY . .
RUN dotnet restore AulaVirtual.sln

FROM tools AS publish
RUN dotnet publish src/AulaVirtual.Api/AulaVirtual.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=publish /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "AulaVirtual.Api.dll"]
