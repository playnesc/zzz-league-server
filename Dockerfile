# Multi-stage build: SDK image compiles/publishes, runtime image (much smaller, no build tools)
# actually runs. Only ZenlessLeague.Api is containerized -- Migrator/Tests aren't part of the
# runtime image.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/ZenlessLeague.Api/ZenlessLeague.Api.csproj src/ZenlessLeague.Api/
RUN dotnet restore src/ZenlessLeague.Api/ZenlessLeague.Api.csproj

COPY src/ZenlessLeague.Api/ src/ZenlessLeague.Api/
RUN dotnet publish src/ZenlessLeague.Api/ZenlessLeague.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Cloud Run sets $PORT at runtime (defaults to 8080) and expects the container to listen on it --
# ASP.NET Core's default Kestrel binding (ASPNETCORE_URLS) needs to be pointed at it explicitly,
# since appsettings.json's launch profiles (5158/7034) are dev-only and don't apply in a container.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ZenlessLeague.Api.dll"]
