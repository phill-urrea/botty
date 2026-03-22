#!/bin/bash
# Botty AI Assistant - Build, Push, and Deploy Script
# Usage: ./scripts/deploy.sh [environment] [image_tag]
#   environment: dev (default) or prod
#   image_tag:   Docker image tag (default: latest)

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

ENVIRONMENT="${1:-dev}"
IMAGE_TAG="${2:-latest}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

command -v gcloud >/dev/null 2>&1 || { echo -e "${RED}gcloud CLI is required${NC}"; exit 1; }
command -v docker >/dev/null 2>&1 || { echo -e "${RED}docker is required${NC}"; exit 1; }
command -v terraform >/dev/null 2>&1 || { echo -e "${RED}terraform is required${NC}"; exit 1; }

PROJECT_ID=$(gcloud config get-value project 2>/dev/null)
if [[ -z "$PROJECT_ID" || "$PROJECT_ID" == "(unset)" ]]; then
  echo -e "${RED}No GCP project set. Run: gcloud config set project YOUR_PROJECT_ID${NC}"
  exit 1
fi

REGION=$(cd "$ROOT_DIR/terraform" && terraform output -raw 2>/dev/null <<< "" || echo "us-central1")
REGION="${REGION:-us-central1}"
REGISTRY="${REGION}-docker.pkg.dev/${PROJECT_ID}/botty"
TFVARS_FILE="environments/$( [ "$ENVIRONMENT" = "prod" ] && echo "prod" || echo "dev" ).tfvars"

echo -e "${GREEN}=== Botty Deploy ===${NC}"
echo "  Project:     $PROJECT_ID"
echo "  Region:      $REGION"
echo "  Environment: $ENVIRONMENT"
echo "  Image Tag:   $IMAGE_TAG"
echo "  Registry:    $REGISTRY"
echo ""

# Authenticate Docker with Artifact Registry
echo -e "${GREEN}[1/5] Configuring Docker authentication...${NC}"
gcloud auth configure-docker "${REGION}-docker.pkg.dev" --quiet

# Build images
echo -e "${GREEN}[2/5] Building Docker images...${NC}"

echo "  Building API..."
docker build -t "${REGISTRY}/api:${IMAGE_TAG}" \
  -f "$ROOT_DIR/docker/Dockerfile.api" \
  "$ROOT_DIR"

echo "  Building WhatsApp Bridge..."
docker build -t "${REGISTRY}/whatsapp-bridge:${IMAGE_TAG}" \
  -f "$ROOT_DIR/whatsapp-bridge/Dockerfile" \
  "$ROOT_DIR/whatsapp-bridge"

echo "  Building Admin UI..."
docker build -t "${REGISTRY}/admin-ui:${IMAGE_TAG}" \
  --build-arg NEXT_PUBLIC_API_URL=https://bot-api.phill.ie/api \
  -f "$ROOT_DIR/admin-ui/Dockerfile" \
  "$ROOT_DIR/admin-ui"

# Push images
echo -e "${GREEN}[3/5] Pushing images to Artifact Registry...${NC}"
docker push "${REGISTRY}/api:${IMAGE_TAG}"
docker push "${REGISTRY}/whatsapp-bridge:${IMAGE_TAG}"
docker push "${REGISTRY}/admin-ui:${IMAGE_TAG}"

# Terraform apply
echo -e "${GREEN}[4/5] Applying Terraform...${NC}"
cd "$ROOT_DIR/terraform"

terraform init -input=false \
  -backend-config="bucket=${PROJECT_ID}-terraform-state" \
  -backend-config="prefix=terraform/${ENVIRONMENT}"

terraform apply -auto-approve \
  -var-file="$TFVARS_FILE" \
  -var="project_id=${PROJECT_ID}" \
  -var="image_tag=${IMAGE_TAG}"

# Health check
echo -e "${GREEN}[5/5] Running health checks...${NC}"
API_URL=$(terraform output -raw api_url)

for i in {1..10}; do
  HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" "${API_URL}/health" 2>/dev/null || echo "000")
  if [[ "$HTTP_CODE" == "200" ]]; then
    echo -e "${GREEN}Health check passed!${NC}"
    break
  fi
  if [[ $i -eq 10 ]]; then
    echo -e "${RED}Health check failed after 10 attempts${NC}"
    exit 1
  fi
  echo -e "${YELLOW}  Attempt $i: HTTP $HTTP_CODE, retrying in 10s...${NC}"
  sleep 10
done

LB_IP=$(terraform output -raw lb_ip 2>/dev/null || echo "n/a")
echo ""
echo -e "${GREEN}=== Deploy Complete ===${NC}"
echo "  API:        ${API_URL}"
echo "  Admin UI:   $(terraform output -raw admin_url)"
echo "  LB IP:      ${LB_IP}"
echo ""
echo "  bot.phill.ie     -> ${LB_IP}"
echo "  bot-api.phill.ie -> ${LB_IP}"
