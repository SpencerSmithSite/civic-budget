#!/usr/bin/env bash
# Connects the Azure demo's assistant and portal question box to an AI model, or disconnects them
# (ADR-0047, ADR-0049). Run from the repo root after `az login`:
#
#   ./scripts/azure-assistant.sh          # use the key, provider, and model in the web project's user-secrets
#   ./scripts/azure-assistant.sh --off    # remove the key: the AI features disappear from the demo
#
# The key is read from user-secrets (Assistant:ApiKey) or ASSISTANT_API_KEY and handed straight to
# the Container App as a secret; it is never printed. The provider and model come from the same
# user-secrets (Assistant:Provider, Assistant:Model), else Ollama and glm-5.3-flash. Each
# government's portal answers PORTAL_QUESTIONS_PER_MONTH questions a month (default 200).
# Both commands start a new revision; the site keeps serving while it rolls out.
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-civicbudget-rg}"
APP_NAME="civicbudget-app"
PORTAL_QUESTIONS_PER_MONTH="${PORTAL_QUESTIONS_PER_MONTH:-200}"

if [ "${1:-}" = "--off" ]; then
  az containerapp update --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
    --remove-env-vars Assistant__ApiKey Assistant__Provider Assistant__Model Assistant__PortalQuestionsPerMonth --output none
  az containerapp secret remove --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --secret-names assistant-api-key --output none 2>/dev/null || true
  echo "The demo's assistant is disconnected. The switches stay in Government settings with nothing behind them."
  exit 0
fi

secret() { dotnet user-secrets list --project src/CivicBudget.Web 2>/dev/null | sed -n "s/^$1 = //p"; }
KEY="${ASSISTANT_API_KEY:-$(secret 'Assistant:ApiKey')}"
PROVIDER="$(secret 'Assistant:Provider')"; PROVIDER="${PROVIDER:-Ollama}"
MODEL="$(secret 'Assistant:Model')"; MODEL="${MODEL:-glm-5.3-flash}"
[ -n "$KEY" ] || { echo "No key: set Assistant:ApiKey in the web project's user-secrets, or ASSISTANT_API_KEY."; exit 1; }

az containerapp secret set --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --secrets "assistant-api-key=$KEY" --output none
az containerapp update --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --output none --set-env-vars \
  "Assistant__ApiKey=secretref:assistant-api-key" "Assistant__Provider=$PROVIDER" "Assistant__Model=$MODEL" \
  "Assistant__PortalQuestionsPerMonth=$PORTAL_QUESTIONS_PER_MONTH"

echo "The demo's assistant uses $PROVIDER ($MODEL); each portal takes $PORTAL_QUESTIONS_PER_MONTH questions a month."
echo "Maple Ridge has both switches on after every nightly reset; Pine Hollow has them off."
