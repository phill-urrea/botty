#!/bin/bash
# Local Build Script for Botty AI Assistant
# Builds all Docker images for local testing

set -e

# Colors
GREEN='\033[0;32m'
NC='\033[0m'

echo -e "${GREEN}=== Building Botty Docker Images ===${NC}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

# Build API
echo -e "${GREEN}Building API image...${NC}"
docker build -t botty-api:latest \
    -f "$ROOT_DIR/botty/docker/Dockerfile.api" \
    "$ROOT_DIR/botty"

# Build WhatsApp Bridge
echo -e "${GREEN}Building WhatsApp Bridge image...${NC}"
docker build -t botty-whatsapp:latest \
    -f "$ROOT_DIR/whatsapp-bridge/Dockerfile" \
    "$ROOT_DIR/whatsapp-bridge"

# Build Admin UI
echo -e "${GREEN}Building Admin UI image...${NC}"
docker build -t botty-admin:latest \
    -f "$ROOT_DIR/admin-ui/Dockerfile" \
    "$ROOT_DIR/admin-ui"

echo ""
echo -e "${GREEN}=== All images built successfully ===${NC}"
echo ""
echo "Images:"
echo "  - botty-api:latest"
echo "  - botty-whatsapp:latest"
echo "  - botty-admin:latest"
echo ""
echo "Run with: docker compose -f botty/docker/docker-compose.yml up"
