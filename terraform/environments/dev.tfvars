# Development Environment Configuration

project_id  = "botty-projects"
region      = "us-central1"
app_name    = "botty"
environment = "development"
image_tag   = "latest"

# Use smaller instances for development
db_tier      = "db-f1-micro"
db_disk_size = 10

api_cpu           = "1"
api_memory        = "512Mi"
api_max_instances = 3

admin_max_instances = 2
