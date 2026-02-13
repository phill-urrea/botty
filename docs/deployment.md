# Botty AI Assistant - Deployment Guide

This guide covers deploying Botty to Google Cloud Platform (GCP).

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Google Cloud Platform                        │
│                                                                      │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────────────────┐ │
│  │  Cloud Run   │   │  Cloud Run   │   │      Cloud Run           │ │
│  │  Admin UI    │   │  API         │   │   WhatsApp Bridge        │ │
│  │  (Next.js)   │   │  (.NET 10)   │   │   (Node.js + Puppeteer)  │ │
│  └──────┬───────┘   └──────┬───────┘   └──────────┬───────────────┘ │
│         │                  │                      │                  │
│         └──────────────────┼──────────────────────┘                  │
│                            │                                         │
│                  ┌─────────▼─────────┐                               │
│                  │    VPC Network    │                               │
│                  └─────────┬─────────┘                               │
│                            │                                         │
│  ┌─────────────────────────▼─────────────────────────────────────┐  │
│  │                    Cloud SQL (PostgreSQL 16)                   │  │
│  │                    + pgvector extension                        │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  ┌───────────────┐   ┌───────────────┐   ┌───────────────────────┐  │
│  │    Secret     │   │   Artifact    │   │   Cloud Scheduler     │  │
│  │    Manager    │   │   Registry    │   │   (Health Checks)     │  │
│  └───────────────┘   └───────────────┘   └───────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

## Prerequisites

1. **GCP Account** with billing enabled
2. **gcloud CLI** installed and authenticated
3. **Terraform** >= 1.5.0 installed
4. **Docker** installed (for local builds)
5. **GitHub repository** (for CI/CD)

## Quick Start

### 1. Run the Setup Script

```bash
chmod +x scripts/gcp-setup.sh
./scripts/gcp-setup.sh
```

This script will:
- Enable required GCP APIs
- Create a Terraform state bucket
- Set up service accounts and IAM roles
- Configure Workload Identity Federation for GitHub Actions

### 2. Configure GitHub Secrets

Add these secrets to your GitHub repository:

| Secret | Description |
|--------|-------------|
| `GCP_PROJECT_ID` | Your GCP project ID |
| `TERRAFORM_STATE_BUCKET` | Terraform state bucket name |
| `GCP_SERVICE_ACCOUNT` | Service account email |
| `GCP_WORKLOAD_IDENTITY_PROVIDER` | Workload Identity provider path |

### 3. Update Terraform Variables

Edit `terraform/environments/dev.tfvars`:

```hcl
project_id = "your-project-id"
region     = "us-central1"
```

### 4. Deploy Infrastructure

```bash
cd terraform

# Initialize
terraform init \
  -backend-config="bucket=YOUR_STATE_BUCKET" \
  -backend-config="prefix=terraform/development"

# Plan
terraform plan -var-file="environments/dev.tfvars"

# Apply
terraform apply -var-file="environments/dev.tfvars"
```

## Manual Deployment Steps

### Building Images Locally

```bash
# Build all images
./scripts/local-build.sh

# Or build individually
docker build -t botty-api -f botty/docker/Dockerfile.api botty/
docker build -t botty-whatsapp -f whatsapp-bridge/Dockerfile whatsapp-bridge/
docker build -t botty-admin -f admin-ui/Dockerfile admin-ui/
```

### Pushing to Artifact Registry

```bash
REGION=us-central1
PROJECT_ID=your-project-id
REPO=$REGION-docker.pkg.dev/$PROJECT_ID/botty

# Configure Docker
gcloud auth configure-docker $REGION-docker.pkg.dev

# Tag and push
docker tag botty-api:latest $REPO/api:latest
docker push $REPO/api:latest

docker tag botty-whatsapp:latest $REPO/whatsapp-bridge:latest
docker push $REPO/whatsapp-bridge:latest

docker tag botty-admin:latest $REPO/admin-ui:latest
docker push $REPO/admin-ui:latest
```

### Deploying to Cloud Run

```bash
# Deploy API
gcloud run deploy botty-api \
  --image=$REPO/api:latest \
  --region=$REGION \
  --platform=managed \
  --allow-unauthenticated

# Deploy Admin UI
gcloud run deploy botty-admin \
  --image=$REPO/admin-ui:latest \
  --region=$REGION \
  --platform=managed \
  --allow-unauthenticated
```

## CI/CD Pipeline

The GitHub Actions workflow automatically:

1. **On PR**: Runs tests, builds Docker images, validates Terraform
2. **On merge to main**: Deploys to production
3. **On tag (v*)**: Deploys tagged version to production

### Workflow Files

- `.github/workflows/ci.yml` - Continuous integration
- `.github/workflows/deploy.yml` - Deployment pipeline
- `.github/workflows/terraform-plan.yml` - Terraform plan on PRs

## Environment Configuration

### Development

```hcl
db_tier           = "db-f1-micro"
api_max_instances = 3
```

### Production

```hcl
db_tier           = "db-custom-2-4096"
api_max_instances = 20
```

## Secrets Management

Secrets are stored in GCP Secret Manager:

| Secret | Description |
|--------|-------------|
| `botty-db-password` | PostgreSQL password |
| `botty-db-connection-string` | Full connection string |
| `botty-anthropic-api-key` | Claude API key |

### Adding Secrets

```bash
# Add a new secret
echo -n "secret-value" | gcloud secrets create SECRET_NAME --data-file=-

# Update a secret
echo -n "new-value" | gcloud secrets versions add SECRET_NAME --data-file=-
```

## Monitoring

### Cloud Run Metrics

- Request count and latency
- Instance count
- Memory/CPU utilization

### Cloud SQL Metrics

- Connections
- Query performance
- Storage usage

### Logging

```bash
# View API logs
gcloud logging read "resource.type=cloud_run_revision AND resource.labels.service_name=botty-api"

# View error logs
gcloud logging read "severity>=ERROR"
```

## Scaling Configuration

### API Service

```hcl
scaling {
  min_instance_count = 1  # Always warm in production
  max_instance_count = 20
}
```

### WhatsApp Bridge

```hcl
scaling {
  min_instance_count = 1  # Must stay running for persistent connection
  max_instance_count = 1  # Single instance only
}
```

## Cost Optimization

1. **Development**: Use `min_instance_count = 0` for scale-to-zero
2. **Database**: Start with `db-f1-micro`, scale as needed
3. **Health checks**: Keep services warm only in production

## Troubleshooting

### Common Issues

**Connection Refused to Database**
- Ensure VPC connector is configured
- Check Cloud SQL private IP is accessible

**WhatsApp Session Lost**
- Session stored in memory, persists through restarts
- Re-scan QR code if session expires

**Build Failures**
- Check Artifact Registry permissions
- Verify Docker build context

### Useful Commands

```bash
# Check service status
gcloud run services describe botty-api --region=$REGION

# View recent revisions
gcloud run revisions list --service=botty-api --region=$REGION

# Check Cloud SQL status
gcloud sql instances describe botty-db

# Access database
gcloud sql connect botty-db --database=botty --user=botty
```

## Rollback

```bash
# List revisions
gcloud run revisions list --service=botty-api --region=$REGION

# Rollback to specific revision
gcloud run services update-traffic botty-api \
  --to-revisions=botty-api-00001-abc=100 \
  --region=$REGION
```

## Security Best Practices

1. **No public database access** - Cloud SQL uses private IP only
2. **Service account principle of least privilege**
3. **Secrets in Secret Manager** - Never in environment variables or code
4. **VPC isolation** - Services communicate over private network
5. **IAM authentication** - For service-to-service calls
