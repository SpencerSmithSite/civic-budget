#!/usr/bin/env bash
# One-time local setup: generates a SQL Server password, writes .env for docker compose,
# and stores the matching connection string in .NET user-secrets (never in the repo).
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ -f .env ]]; then
  # shellcheck disable=SC1091
  source .env
  echo "Using existing .env"
else
  # Random 24-char password satisfying SQL Server complexity rules.
  # (cut consumes all of its input, so this pipeline is safe under `pipefail`.)
  MSSQL_SA_PASSWORD="Cb_$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | cut -c1-20)1!"
  printf 'MSSQL_SA_PASSWORD=%s\n' "$MSSQL_SA_PASSWORD" > .env
  echo "Wrote .env"
fi

CONN="Server=localhost,1433;Database=CivicBudget;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True;Encrypt=True"
dotnet user-secrets set "ConnectionStrings:CivicBudget" "$CONN" --project src/CivicBudget.Web >/dev/null
echo "Stored ConnectionStrings:CivicBudget in user-secrets for src/CivicBudget.Web"
echo
echo "Next:  docker compose up -d && dotnet run --project src/CivicBudget.Web"
