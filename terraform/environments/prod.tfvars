# Production Environment Configuration

project_id  = "your-gcp-project-id"  # Replace with your GCP project ID
region      = "us-central1"
app_name    = "botty"
environment = "production"
image_tag   = "v1.0.0"  # Use specific version tags in production

# Use production-ready instances
db_tier      = "db-custom-2-4096"  # 2 vCPUs, 4GB RAM
db_disk_size = 50

api_cpu           = "2"
api_memory        = "1Gi"
api_max_instances = 20

admin_max_instances = 10
