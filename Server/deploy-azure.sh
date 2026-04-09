#!/bin/bash
set -e

# Configuration
RG="pizzajam-rg"
ACR="pizzajamacr"
APP="pizzajam-server"
LOCATION="swedencentral"
IMAGE="pizzajam-server:latest"

echo "=== Azure Container Instance Deployment ==="
echo "Region: $LOCATION"
echo ""

# Check Azure CLI
if ! command -v az &> /dev/null; then
    echo "ERROR: Azure CLI not installed. Run: brew install azure-cli"
    exit 1
fi

# Check login
if ! az account show &> /dev/null 2>&1; then
    echo "Not logged in. Opening browser..."
    az login
fi

echo "Logged in as: $(az account show --query user.name -o tsv)"
echo ""

# 1. Register required providers
echo ">>> Registering Azure providers (if needed)..."
az provider register --namespace Microsoft.ContainerRegistry --wait 2>/dev/null || true
az provider register --namespace Microsoft.ContainerInstance --wait 2>/dev/null || true
echo "OK"

# 2. Resource Group
echo ">>> Creating resource group..."
az group create --name $RG --location $LOCATION --output none
echo "OK"

# 3. Container Registry
echo ">>> Creating container registry..."
az acr create --name $ACR --resource-group $RG --sku Basic --admin-enabled true --output none 2>/dev/null || \
echo "(already exists)"
echo "OK"

# 4. Build & push image locally
echo ">>> Building Docker image locally..."
cd "$(dirname "$0")"
docker build -t ${ACR}.azurecr.io/$IMAGE .
echo ">>> Pushing image to registry..."
az acr login --name $ACR
docker push ${ACR}.azurecr.io/$IMAGE
echo "OK"

# 5. Deploy via YAML (needed for mixed TCP + UDP ports)
echo ">>> Deploying container instance..."
ACR_PASSWORD=$(az acr credential show --name $ACR --query "passwords[0].value" -o tsv)

DEPLOY_YAML=$(mktemp /tmp/deploy-XXXXX.yaml)
cat > "$DEPLOY_YAML" <<EOF
apiVersion: 2021-09-01
location: $LOCATION
name: $APP
type: Microsoft.ContainerInstance/containerGroups
properties:
  containers:
    - name: $APP
      properties:
        image: ${ACR}.azurecr.io/$IMAGE
        resources:
          requests:
            cpu: 1.0
            memoryInGb: 2.0
        ports:
          - port: 8080
            protocol: TCP
          - port: 9050
            protocol: UDP
  osType: Linux
  ipAddress:
    type: Public
    dnsNameLabel: $APP
    ports:
      - port: 8080
        protocol: TCP
      - port: 9050
        protocol: UDP
  imageRegistryCredentials:
    - server: ${ACR}.azurecr.io
      username: $ACR
      password: $ACR_PASSWORD
EOF

az container create --resource-group $RG --file "$DEPLOY_YAML" --output none
rm -f "$DEPLOY_YAML"
echo "OK"

# 6. Get IP and DNS
echo ""
echo "=== Deployment Complete ==="
IP=$(az container show --resource-group $RG --name $APP --query "ipAddress.ip" -o tsv)
FQDN=$(az container show --resource-group $RG --name $APP --query "ipAddress.fqdn" -o tsv)
echo "Public IP:    $IP"
echo "DNS:          $FQDN"
echo "HTTP:         http://$FQDN:8080"
echo "SignalR Hub:  http://$FQDN:8080/gamehub"
echo "UDP (game):   $FQDN:9050"
