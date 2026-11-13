# Build context is the repository root:
#   docker build -f src/PermitDesk.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY src/PermitDesk.Api/PermitDesk.Api.csproj src/PermitDesk.Api/
RUN dotnet restore src/PermitDesk.Api/PermitDesk.Api.csproj

COPY src/PermitDesk.Api/ src/PermitDesk.Api/
RUN dotnet publish src/PermitDesk.Api/PermitDesk.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
COPY db/migrations db/migrations

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "PermitDesk.Api.dll"]
