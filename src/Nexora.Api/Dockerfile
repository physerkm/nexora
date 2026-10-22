# Build context: repository root  (docker build -f src/Nexora.Api/Dockerfile .)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the layer is cached until a project file changes.
COPY Directory.Build.props Nexora.slnx ./
COPY src/Nexora.Domain/Nexora.Domain.csproj src/Nexora.Domain/
COPY src/Nexora.Application/Nexora.Application.csproj src/Nexora.Application/
COPY src/Nexora.Infrastructure/Nexora.Infrastructure.csproj src/Nexora.Infrastructure/
COPY src/Nexora.Api/Nexora.Api.csproj src/Nexora.Api/
RUN dotnet restore src/Nexora.Api/Nexora.Api.csproj

COPY src/ src/
RUN dotnet publish src/Nexora.Api/Nexora.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish .
# The base image ships a non-root "app" user.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nexora.Api.dll"]
