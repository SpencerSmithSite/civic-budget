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
# Publish restores again on purpose. The restore above saw only project files, and the SDK's
# static web assets pipeline decides at restore time whether a project needs _framework/blazor.web.js
# (it looks for Razor components); with --no-restore the script was left out of the image and every
# interactive page went dead. The packages are already cached, so the second restore takes seconds.
#
# Published for the one platform the image runs on (framework-dependent, so the runtime still comes
# from the base image): without a runtime identifier the output carries native libraries for Windows,
# macOS, and the other Linux architecture, tens of megabytes the container never loads.
ARG TARGETARCH
RUN case "$TARGETARCH" in \
        amd64|"") rid=linux-x64 ;; \
        arm64) rid=linux-arm64 ;; \
        *) echo "Unsupported architecture: $TARGETARCH" && exit 1 ;; \
    esac \
    && dotnet publish src/CivicBudget.Web/CivicBudget.Web.csproj -c Release -r "$rid" --self-contained false -o /app/publish \
    && test -f /app/publish/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The base image already runs Kestrel on 8080 and sets ASPNETCORE_HTTP_PORTS; run as the
# non-root user it ships with. TLS terminates at the load balancer, so forwarded headers are on.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0
USER app
EXPOSE 8080

COPY --from=build /app/publish .

# No HEALTHCHECK: the runtime image has neither curl nor wget, so one always reported "unhealthy".
# Azure's startup probe, the AWS load balancer, and compose all check /health from outside instead.

ENTRYPOINT ["dotnet", "CivicBudget.Web.dll"]
