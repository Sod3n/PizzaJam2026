#!/bin/bash
set -e

# Configuration
VPS_HOST="193.168.49.169"
VPS_USER="root"
IMAGE_NAME="pizzajam-server"
CONTAINER_NAME="pizzajam-server"

echo "=== VPS Deployment ==="
echo "Host: $VPS_USER@$VPS_HOST"
echo ""

# Check Docker is available locally
if ! command -v docker &> /dev/null; then
    echo "ERROR: Docker not installed locally."
    exit 1
fi

# SSH helper — uses key auth if available, otherwise prompts for password
SSH_OPTS="-o StrictHostKeyChecking=accept-new -o ConnectTimeout=10"
ssh_cmd() {
    ssh $SSH_OPTS "$VPS_USER@$VPS_HOST" "$@"
}

# Test SSH connection
echo ">>> Testing SSH connection..."
if ! ssh_cmd "echo ok" &> /dev/null; then
    echo "ERROR: Cannot connect to $VPS_USER@$VPS_HOST"
    echo "Make sure you can SSH in (password will be prompted if no key is set up)."
    echo ""
    echo "To set up key auth (recommended):"
    echo "  ssh-copy-id $VPS_USER@$VPS_HOST"
    exit 1
fi
echo "OK"

# Build image locally
echo ">>> Building Docker image..."
cd "$(dirname "$0")"
docker build -t "$IMAGE_NAME:latest" .
echo "OK"

# Transfer image to VPS
echo ">>> Transferring image to VPS (this may take a minute)..."
docker save "$IMAGE_NAME:latest" | gzip | ssh $SSH_OPTS "$VPS_USER@$VPS_HOST" "docker load"
echo "OK"

# Deploy on VPS
echo ">>> Deploying container..."
ssh_cmd bash -s <<'REMOTE'
    CONTAINER_NAME="pizzajam-server"
    IMAGE_NAME="pizzajam-server:latest"

    # Stop & remove old container if running
    if docker ps -a --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
        echo "  Stopping old container..."
        docker stop "$CONTAINER_NAME" 2>/dev/null || true
        docker rm "$CONTAINER_NAME" 2>/dev/null || true
    fi

    # Run new container
    echo "  Starting new container..."
    docker run -d \
        --name "$CONTAINER_NAME" \
        --restart unless-stopped \
        -p 8080:8080 \
        -p 9050:9050/udp \
        "$IMAGE_NAME"

    # Prune old dangling images
    docker image prune -f > /dev/null 2>&1 || true
REMOTE
echo "OK"

# Verify
echo ""
echo "=== Deployment Complete ==="
echo "Public IP:    $VPS_HOST"
echo "HTTP:         http://$VPS_HOST:8080"
echo "SignalR Hub:  http://$VPS_HOST:8080/gamehub"
echo "UDP (game):   $VPS_HOST:9050"
echo ""
echo "Useful commands:"
echo "  ssh $VPS_USER@$VPS_HOST docker logs -f $CONTAINER_NAME"
echo "  ssh $VPS_USER@$VPS_HOST docker restart $CONTAINER_NAME"
