# Botty AI Assistant - Terraform Outputs

output "api_url" {
  description = "URL of the Botty API service"
  value       = google_cloud_run_v2_service.api.uri
}

output "admin_url" {
  description = "URL of the Admin UI"
  value       = google_cloud_run_v2_service.admin.uri
}

output "whatsapp_url" {
  description = "URL of the WhatsApp bridge service (internal)"
  value       = google_cloud_run_v2_service.whatsapp.uri
}

output "database_instance" {
  description = "Cloud SQL instance name"
  value       = google_sql_database_instance.postgres.name
}

output "database_private_ip" {
  description = "Cloud SQL private IP address"
  value       = google_sql_database_instance.postgres.private_ip_address
}

output "artifact_registry_url" {
  description = "Artifact Registry URL for Docker images"
  value       = "${var.region}-docker.pkg.dev/${var.project_id}/${google_artifact_registry_repository.repo.repository_id}"
}

output "service_account_email" {
  description = "Cloud Run service account email"
  value       = google_service_account.cloudrun.email
}

output "vpc_connector_id" {
  description = "VPC Access Connector ID"
  value       = google_vpc_access_connector.connector.id
}
