#!/bin/bash
# GCP Setup Script for Botty AI Assistant
# This script helps set up the initial GCP infrastructure

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}=== Botty AI Assistant - GCP Setup ===${NC}"
echo ""

# Check prerequisites
command -v gcloud >/dev/null 2>&1 || { echo -e "${RED}gcloud CLI is required but not installed. Please install it first.${NC}"; exit 1; }
command -v terraform >/dev/null 2>&1 || { echo -e "${RED}terraform is required but not installed. Please install it first.${NC}"; exit 1; }

# Get configuration
read -p "Enter your GCP Project ID: " PROJECT_ID
read -p "Enter the region [us-central1]: " REGION
REGION=${REGION:-us-central1}
read -p "Enter environment (development/staging/production) [development]: " ENVIRONMENT
ENVIRONMENT=${ENVIRONMENT:-development}

echo ""
echo -e "${YELLOW}Configuration:${NC}"
echo "  Project ID: $PROJECT_ID"
echo "  Region: $REGION"
echo "  Environment: $ENVIRONMENT"
echo ""

read -p "Continue? (y/n) " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
    exit 1
fi

# Set project
echo -e "${GREEN}Setting GCP project...${NC}"
gcloud config set project $PROJECT_ID

# Enable required APIs
echo -e "${GREEN}Enabling required APIs...${NC}"
gcloud services enable \
    run.googleapis.com \
    sqladmin.googleapis.com \
    secretmanager.googleapis.com \
    artifactregistry.googleapis.com \
    cloudbuild.googleapis.com \
    compute.googleapis.com \
    vpcaccess.googleapis.com \
    servicenetworking.googleapis.com \
    iam.googleapis.com

# Create Terraform state bucket
STATE_BUCKET="${PROJECT_ID}-terraform-state"
echo -e "${GREEN}Creating Terraform state bucket: $STATE_BUCKET${NC}"
gsutil mb -p $PROJECT_ID -l $REGION gs://$STATE_BUCKET 2>/dev/null || echo "Bucket already exists"
gsutil versioning set on gs://$STATE_BUCKET

# Create service account for GitHub Actions
SA_NAME="github-actions"
SA_EMAIL="${SA_NAME}@${PROJECT_ID}.iam.gserviceaccount.com"

echo -e "${GREEN}Creating service account for CI/CD...${NC}"
gcloud iam service-accounts create $SA_NAME \
    --display-name="GitHub Actions" \
    --description="Service account for GitHub Actions CI/CD" \
    2>/dev/null || echo "Service account already exists"

# Grant required roles
echo -e "${GREEN}Granting IAM roles...${NC}"
ROLES=(
    "roles/run.admin"
    "roles/storage.admin"
    "roles/artifactregistry.admin"
    "roles/secretmanager.admin"
    "roles/cloudsql.admin"
    "roles/compute.admin"
    "roles/iam.serviceAccountUser"
)

for ROLE in "${ROLES[@]}"; do
    gcloud projects add-iam-policy-binding $PROJECT_ID \
        --member="serviceAccount:$SA_EMAIL" \
        --role="$ROLE" \
        --quiet
done

# Set up Workload Identity Federation for GitHub Actions
echo -e "${GREEN}Setting up Workload Identity Federation...${NC}"
POOL_NAME="github-pool"
PROVIDER_NAME="github-provider"
GITHUB_ORG="your-github-org"  # Replace with your GitHub org/username

# Create Workload Identity Pool
gcloud iam workload-identity-pools create $POOL_NAME \
    --location="global" \
    --display-name="GitHub Actions Pool" \
    2>/dev/null || echo "Pool already exists"

# Create Workload Identity Provider
gcloud iam workload-identity-pools providers create-oidc $PROVIDER_NAME \
    --location="global" \
    --workload-identity-pool=$POOL_NAME \
    --display-name="GitHub Provider" \
    --attribute-mapping="google.subject=assertion.sub,attribute.actor=assertion.actor,attribute.repository=assertion.repository" \
    --issuer-uri="https://token.actions.githubusercontent.com" \
    2>/dev/null || echo "Provider already exists"

# Allow service account impersonation
gcloud iam service-accounts add-iam-policy-binding $SA_EMAIL \
    --role="roles/iam.workloadIdentityUser" \
    --member="principalSet://iam.googleapis.com/projects/$(gcloud projects describe $PROJECT_ID --format='value(projectNumber)')/locations/global/workloadIdentityPools/${POOL_NAME}/attribute.repository/${GITHUB_ORG}/botty" \
    --quiet 2>/dev/null || true

# Create secrets (placeholder values)
echo -e "${GREEN}Creating placeholder secrets...${NC}"
echo -n "your-anthropic-api-key" | gcloud secrets create botty-anthropic-api-key --data-file=- 2>/dev/null || echo "Secret already exists"

# Output values for GitHub secrets
echo ""
echo -e "${GREEN}=== Setup Complete ===${NC}"
echo ""
echo -e "${YELLOW}Add these values to your GitHub repository secrets:${NC}"
echo ""
echo "GCP_PROJECT_ID: $PROJECT_ID"
echo "TERRAFORM_STATE_BUCKET: $STATE_BUCKET"
echo "GCP_SERVICE_ACCOUNT: $SA_EMAIL"
echo "GCP_WORKLOAD_IDENTITY_PROVIDER: projects/$(gcloud projects describe $PROJECT_ID --format='value(projectNumber)')/locations/global/workloadIdentityPools/${POOL_NAME}/providers/${PROVIDER_NAME}"
echo ""
echo -e "${YELLOW}Update the Anthropic API key secret:${NC}"
echo "gcloud secrets versions add botty-anthropic-api-key --data-file=/path/to/key"
echo ""
echo -e "${YELLOW}Update terraform/environments/dev.tfvars with your project ID:${NC}"
echo "project_id = \"$PROJECT_ID\""
echo ""
echo -e "${GREEN}You can now run:${NC}"
echo "cd terraform && terraform init -backend-config=\"bucket=$STATE_BUCKET\" -backend-config=\"prefix=terraform/$ENVIRONMENT\""
echo "terraform plan -var-file=\"environments/$([ \"$ENVIRONMENT\" = \"production\" ] && echo \"prod\" || echo \"dev\").tfvars\" -var=\"project_id=$PROJECT_ID\""
