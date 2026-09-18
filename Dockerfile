# syntax=docker/dockerfile:1.7
# Multi-stage build for CivicBudget.Web. Stage 1 restores and publishes with the SDK; stage 2 is
# the small ASP.NET runtime image with only the published output. Restore is its own layer keyed
# on the project files, so a code-only change does not re-download packages.
#
#   docker build -t civicbudget .
#   docker compose -f docker-compose.full.yml up --build     # app + SQL Server together

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first: copy only what restore reads, so this layer survives source edits. The
# .editorconfig comes along because it exempts generated migrations from the analyzers.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/CivicBudget.Domain/CivicBudget.Domain.csproj src/CivicBudget.Domain/
COPY src/CivicBudget.Application/CivicBudget.Application.csproj src/CivicBudget.Application/
COPY src/CivicBudget.Infrastructure/CivicBudget.Infrastructure.csproj src/CivicBudget.Infrastructure/
COPY src/CivicBudget.Web/CivicBudget.Web.csproj src/CivicBudget.Web/
RUN dotnet restore src/CivicBudget.Web/CivicBudget.Web.csproj

COPY src/ src/
RUN dotnet publish src/CivicBudget.Web/CivicBudget.Web.csproj --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The base image already runs Kestrel on 8080 and sets ASPNETCORE_HTTP_PORTS; run as the
# non-root user it ships with. TLS terminates at the load balancer, so forwarded headers are on.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0
USER app
EXPOSE 8080

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "CivicBudget.Web.dll"]
