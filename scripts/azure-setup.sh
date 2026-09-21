#!/usr/bin/env bash
# One-time setup for the free Azure showcase (ADR-0030). Run once from the repo root after
# `az login`; safe to rerun. It creates the resource group and everything in infra/azure/main.bicep,
# then wires GitHub Actions to deploy over OIDC by creating an app registration with a federated
# credential for this repository's main branch and storing the three ids as repository secrets.
#
#   brew install azure-cli gh
#   az login
#   ./scripts/azure-setup.sh
#
# Before running it: the deploy-azure workflow must have pushed an image to ghcr.io at least once
# (it does on every push to main), and that package must be public (GitHub -> Packages ->
# civic-budget -> Package settings -> Change visibility), because the Container App pulls it
# anonymously.
#
# Optional environment: LOCATION (default eastus2), RESOURCE_GROUP (default civicbudget-rg),
# DEMO_PASSWORD (default: generated and printed; put it in the README's demo logins section).
set -euo pipefail

LOCATION="${LOCATION:-eastus2}"
RESOURCE_GROUP="${RESOURCE_GROUP:-civicbudget-rg}"
APP_REG_NAME="civicbudget-github-deploy"
REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
IMAGE="ghcr.io/$(echo "$REPO" | tr '[:upper:]' '[:lower:]'):latest"

for tool in az gh openssl; do
  command -v "$tool" >/dev/null || { echo "Install $tool first (brew install azure-cli gh)."; exit 1; }
done

SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
TENANT_ID="$(az account show --query tenantId -o tsv)"
echo "Subscription $SUBSCRIPTION_ID, resource group $RESOURCE_GROUP in $LOCATION, image $IMAGE"

# Passwords: the SQL admin password is kept only in the Container App's secrets; the demo password
# is meant to be published. Both must satisfy Azure SQL's complexity rules, hence the shape below.
SQL_PASSWORD="$(openssl rand -base64 24 | tr -d '/+=' | cut -c1-20)Aa1!"
DEMO_PASSWORD="${DEMO_PASSWORD:-Demo-$(openssl rand -base64 12 | tr -d '/+=' | cut -c1-10)-1!}"

az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

echo "Deploying infra/azure/main.bicep (five to ten minutes; the SQL server is the slow part)..."
az deployment group create \
  --resource-group "$RESOURCE_GROUP" \
  --template-file infra/azure/main.bicep \
  --parameters location="$LOCATION" containerImage="$IMAGE" sqlAdminPassword="$SQL_PASSWORD" demoPassword="$DEMO_PASSWORD" \
  --output none

APP_URL="$(az deployment group show --resource-group "$RESOURCE_GROUP" --name main --query properties.outputs.appUrl.value -o tsv)"

# GitHub Actions signs in as this app registration through OIDC: no client secret anywhere.
APP_ID="$(az ad app list --display-name "$APP_REG_NAME" --query '[0].appId' -o tsv)"
if [ -z "$APP_ID" ]; then
  APP_ID="$(az ad app create --display-name "$APP_REG_NAME" --query appId -o tsv)"
  az ad sp create --id "$APP_ID" --output none
fi
SP_ID="$(az ad sp show --id "$APP_ID" --query id -o tsv)"
az role assignment create --assignee-object-id "$SP_ID" --assignee-principal-type ServicePrincipal \
  --role Contributor --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP" --output none 2>/dev/null || true

if ! az ad app federated-credential list --id "$APP_ID" --query "[?name=='github-main']" -o tsv | grep -q .; then
  az ad app federated-credential create --id "$APP_ID" --parameters "{
    \"name\": \"github-main\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"repo:$REPO:ref:refs/heads/main\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }" --output none
  az ad app federated-credential create --id "$APP_ID" --parameters "{
    \"name\": \"github-environment\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"repo:$REPO:environment:azure-demo\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }" --output none
fi

gh secret set AZURE_CLIENT_ID --body "$APP_ID"
gh secret set AZURE_TENANT_ID --body "$TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID --body "$SUBSCRIPTION_ID"
gh variable set AZURE_DEPLOY --body "true"

cat <<DONE

Done.
  Site:           $APP_URL  (first start takes a minute or two while the image pulls and migrations run)
  Demo password:  $DEMO_PASSWORD   <- put this in README.md under "Live demo"
  Nightly reset:  Container Apps job civicbudget-reset at 08:00 UTC

From now on every push to main deploys (deploy-azure workflow). To run the reset by hand:
  az containerapp job start -n civicbudget-reset -g $RESOURCE_GROUP
DONE
