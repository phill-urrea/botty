# Production Environment Configuration

project_id  = "botty-projects"
region      = "us-central1"
app_name    = "botty"
environment = "production"
image_tag   = "v1.0.0" # Use specific version tags in production

# Match current Cloud SQL instance (upgrade later if needed)
db_tier      = "db-f1-micro"
db_disk_size = 10

api_cpu           = "1"
api_memory        = "512Mi"
api_max_instances = 10

admin_max_instances = 5
