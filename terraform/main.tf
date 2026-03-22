# Botty AI Assistant - GCP Infrastructure
# This Terraform configuration deploys the complete Botty stack to GCP

terraform {
  required_version = ">= 1.5.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 5.0"
    }
    google-beta = {
      source  = "hashicorp/google-beta"
      version = "~> 5.0"
    }
  }

  backend "gcs" {}
}

provider "google" {
  project = var.project_id
  region  = var.region
}

provider "google-beta" {
  project = var.project_id
  region  = var.region
}

# Enable required APIs
resource "google_project_service" "required_apis" {
  for_each = toset([
    "cloudresourcemanager.googleapis.com",
    "run.googleapis.com",
    "sqladmin.googleapis.com",
    "secretmanager.googleapis.com",
    "artifactregistry.googleapis.com",
    "cloudbuild.googleapis.com",
    "compute.googleapis.com",
    "vpcaccess.googleapis.com",
    "servicenetworking.googleapis.com",
  ])

  project            = var.project_id
  service            = each.value
  disable_on_destroy = false
}

# Import blocks for pre-existing GCP resources
import {
  to = google_compute_network.vpc
  id = "projects/botty-projects/global/networks/botty-vpc"
}

import {
  to = google_compute_subnetwork.subnet
  id = "projects/botty-projects/regions/us-central1/subnetworks/botty-subnet"
}

import {
  to = google_compute_global_address.private_ip_range
  id = "projects/botty-projects/global/addresses/botty-private-ip"
}

import {
  to = google_service_networking_connection.private_vpc_connection
  id = "projects/botty-projects/global/networks/botty-vpc:servicenetworking.googleapis.com"
}

import {
  to = google_vpc_access_connector.connector
  id = "projects/botty-projects/locations/us-central1/connectors/botty-connector"
}

import {
  to = google_artifact_registry_repository.repo
  id = "projects/botty-projects/locations/us-central1/repositories/botty"
}

import {
  to = google_sql_database_instance.postgres
  id = "projects/botty-projects/instances/botty-db"
}

import {
  to = google_sql_database.botty
  id = "projects/botty-projects/instances/botty-db/databases/botty"
}

import {
  to = google_sql_user.botty
  id = "botty-projects/botty-db/botty"
}

import {
  to = google_secret_manager_secret.db_password
  id = "projects/278776623981/secrets/botty-db-password"
}

import {
  to = google_secret_manager_secret.anthropic_api_key
  id = "projects/278776623981/secrets/botty-anthropic-api-key"
}

import {
  to = google_secret_manager_secret.db_connection_string
  id = "projects/278776623981/secrets/botty-db-connection-string"
}

import {
  to = google_cloud_run_v2_service.api
  id = "projects/botty-projects/locations/us-central1/services/botty-api"
}

import {
  to = google_cloud_run_v2_service.whatsapp
  id = "projects/botty-projects/locations/us-central1/services/botty-whatsapp"
}

import {
  to = google_cloud_run_v2_service.admin
  id = "projects/botty-projects/locations/us-central1/services/botty-admin"
}

import {
  to = google_compute_region_network_endpoint_group.admin_neg
  id = "projects/botty-projects/regions/us-central1/networkEndpointGroups/botty-admin-neg"
}

import {
  to = google_compute_region_network_endpoint_group.api_neg
  id = "projects/botty-projects/regions/us-central1/networkEndpointGroups/botty-api-neg"
}

import {
  to = google_compute_backend_service.admin_backend
  id = "projects/botty-projects/global/backendServices/botty-admin-backend"
}

import {
  to = google_compute_backend_service.api_backend
  id = "projects/botty-projects/global/backendServices/botty-api-backend"
}

import {
  to = google_compute_url_map.botty_url_map
  id = "projects/botty-projects/global/urlMaps/botty-url-map"
}

import {
  to = google_compute_target_https_proxy.botty_proxy
  id = "projects/botty-projects/global/targetHttpsProxies/botty-https-proxy"
}

import {
  to = google_compute_global_forwarding_rule.botty_https
  id = "projects/botty-projects/global/forwardingRules/botty-https"
}

import {
  to = google_service_account.cloudrun
  id = "projects/botty-projects/serviceAccounts/botty-cloudrun@botty-projects.iam.gserviceaccount.com"
}

import {
  to = google_compute_managed_ssl_certificate.botty_cert
  id = "projects/botty-projects/global/sslCertificates/botty-cert"
}

import {
  to = google_cloud_scheduler_job.health_check
  id = "projects/botty-projects/locations/us-central1/jobs/botty-health-check"
}

# VPC Network for private connections
resource "google_compute_network" "vpc" {
  name                    = "${var.app_name}-vpc"
  auto_create_subnetworks = false
  depends_on              = [google_project_service.required_apis]
}

resource "google_compute_subnetwork" "subnet" {
  name          = "${var.app_name}-subnet"
  ip_cidr_range = "10.0.0.0/24"
  region        = var.region
  network       = google_compute_network.vpc.id

  private_ip_google_access = true
}

# Private IP range for Cloud SQL
resource "google_compute_global_address" "private_ip_range" {
  name          = "${var.app_name}-private-ip"
  purpose       = "VPC_PEERING"
  address_type  = "INTERNAL"
  prefix_length = 16
  network       = google_compute_network.vpc.id
}

resource "google_service_networking_connection" "private_vpc_connection" {
  network                 = google_compute_network.vpc.id
  service                 = "servicenetworking.googleapis.com"
  reserved_peering_ranges = [google_compute_global_address.private_ip_range.name]
}

# VPC Access Connector for Cloud Run
resource "google_vpc_access_connector" "connector" {
  name          = "${var.app_name}-connector"
  region        = var.region
  network       = google_compute_network.vpc.name
  ip_cidr_range = "10.8.0.0/28"

  depends_on = [google_project_service.required_apis]
}

# Artifact Registry for container images
resource "google_artifact_registry_repository" "repo" {
  location      = var.region
  repository_id = var.app_name
  format        = "DOCKER"
  description   = "Docker repository for Botty AI Assistant"

  depends_on = [google_project_service.required_apis]
}

# Cloud SQL PostgreSQL instance
resource "google_sql_database_instance" "postgres" {
  name             = "${var.app_name}-db"
  database_version = "POSTGRES_16"
  region           = var.region

  settings {
    tier              = var.db_tier
    availability_type = "ZONAL"
    disk_size         = var.db_disk_size
    disk_type         = "PD_SSD"

    ip_configuration {
      ipv4_enabled    = false
      private_network = google_compute_network.vpc.id
    }

    backup_configuration {
      enabled                        = true
      point_in_time_recovery_enabled = false
      start_time                     = "03:00"
    }

    maintenance_window {
      day  = 7 # Sunday
      hour = 4
    }
  }

  deletion_protection = true

  lifecycle {
    prevent_destroy = true
    ignore_changes  = all
  }

  depends_on = [google_service_networking_connection.private_vpc_connection]
}

resource "google_sql_database" "botty" {
  name     = "botty"
  instance = google_sql_database_instance.postgres.name

  lifecycle {
    ignore_changes = all
  }
}

resource "google_sql_user" "botty" {
  name     = "botty"
  instance = google_sql_database_instance.postgres.name
  password = random_password.db_password.result

  lifecycle {
    ignore_changes = [password]
  }
}

resource "random_password" "db_password" {
  length  = 32
  special = false

  lifecycle {
    ignore_changes = [result]
  }
}

# Secret Manager secrets
resource "google_secret_manager_secret" "db_password" {
  secret_id = "${var.app_name}-db-password"

  replication {
    auto {}
  }

  depends_on = [google_project_service.required_apis]
}

# Secret versions are managed outside Terraform (already populated)
# Adding new versions here would overwrite existing credentials

resource "google_secret_manager_secret" "anthropic_api_key" {
  secret_id = "${var.app_name}-anthropic-api-key"

  replication {
    auto {}
  }

  depends_on = [google_project_service.required_apis]
}

# Service Account for Cloud Run
resource "google_service_account" "cloudrun" {
  account_id   = "${var.app_name}-cloudrun"
  display_name = "Botty Cloud Run Service Account"
}

# Grant permissions to service account
resource "google_project_iam_member" "cloudrun_secretmanager" {
  project = var.project_id
  role    = "roles/secretmanager.secretAccessor"
  member  = "serviceAccount:${google_service_account.cloudrun.email}"
}

resource "google_project_iam_member" "cloudrun_cloudsql" {
  project = var.project_id
  role    = "roles/cloudsql.client"
  member  = "serviceAccount:${google_service_account.cloudrun.email}"
}

# Cloud Run - API Service
resource "google_cloud_run_v2_service" "api" {
  name     = "${var.app_name}-api"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_ALL"

  template {
    service_account = google_service_account.cloudrun.email

    scaling {
      min_instance_count = var.environment == "production" ? 1 : 0
      max_instance_count = var.api_max_instances
    }

    vpc_access {
      connector = google_vpc_access_connector.connector.id
      egress    = "ALL_TRAFFIC"
    }

    containers {
      image = "${var.region}-docker.pkg.dev/${var.project_id}/${var.app_name}/api:${var.image_tag}"

      resources {
        limits = {
          cpu    = var.api_cpu
          memory = var.api_memory
        }
      }

      env {
        name  = "ASPNETCORE_ENVIRONMENT"
        value = var.environment == "production" ? "Production" : "Development"
      }

      env {
        name = "ConnectionStrings__DefaultConnection"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.db_connection_string.secret_id
            version = "latest"
          }
        }
      }

      env {
        name = "Claude__ApiKey"
        value_source {
          secret_key_ref {
            secret  = google_secret_manager_secret.anthropic_api_key.secret_id
            version = "latest"
          }
        }
      }

      env {
        name  = "GCP__ProjectId"
        value = var.project_id
      }

      env {
        name  = "WhatsAppBridge__BaseUrl"
        value = google_cloud_run_v2_service.whatsapp.uri
      }

      env {
        name  = "Channels__WhatsApp__Enabled"
        value = "true"
      }

      env {
        name  = "Channels__WhatsApp__BridgeUrl"
        value = google_cloud_run_v2_service.whatsapp.uri
      }

      startup_probe {
        http_get {
          path = "/health"
        }
        initial_delay_seconds = 10
        period_seconds        = 10
        failure_threshold     = 3
      }

      liveness_probe {
        http_get {
          path = "/health"
        }
        period_seconds = 30
      }
    }
  }

  depends_on = [
    google_project_service.required_apis,
    google_sql_database.botty,
  ]
}

# Database connection string secret
resource "google_secret_manager_secret" "db_connection_string" {
  secret_id = "${var.app_name}-db-connection-string"

  replication {
    auto {}
  }

  depends_on = [google_project_service.required_apis]
}

# Connection string secret version is managed outside Terraform (already populated)

# Cloud Run - WhatsApp Bridge Service
resource "google_cloud_run_v2_service" "whatsapp" {
  name     = "${var.app_name}-whatsapp"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_INTERNAL_LOAD_BALANCER"

  template {
    service_account = google_service_account.cloudrun.email

    scaling {
      min_instance_count = 1  # WhatsApp needs persistent connection
      max_instance_count = 1
    }

    vpc_access {
      connector = google_vpc_access_connector.connector.id
      egress    = "ALL_TRAFFIC"
    }

    containers {
      image = "${var.region}-docker.pkg.dev/${var.project_id}/${var.app_name}/whatsapp-bridge:${var.image_tag}"

      resources {
        limits = {
          cpu    = "1"
          memory = "2Gi"
        }
      }

      env {
        name  = "BOTTY_API_URL"
        value = google_cloud_run_v2_service.api.uri
      }

      env {
        name  = "HEADLESS"
        value = "true"
      }

      env {
        name  = "AUTO_CREATE_TASKS"
        value = "true"
      }

    }
  }

  depends_on = [google_cloud_run_v2_service.api]
}

# Cloud Run - Admin UI Service
resource "google_cloud_run_v2_service" "admin" {
  name     = "${var.app_name}-admin"
  location = var.region
  ingress  = "INGRESS_TRAFFIC_ALL"

  template {
    service_account = google_service_account.cloudrun.email

    scaling {
      min_instance_count = var.environment == "production" ? 1 : 0
      max_instance_count = var.admin_max_instances
    }

    containers {
      image = "${var.region}-docker.pkg.dev/${var.project_id}/${var.app_name}/admin-ui:${var.image_tag}"

      resources {
        limits = {
          cpu    = "1"
          memory = "512Mi"
        }
      }

      env {
        name  = "NEXT_PUBLIC_API_URL"
        value = "${google_cloud_run_v2_service.api.uri}/api"
      }

      env {
        name  = "NEXT_PUBLIC_WS_URL"
        value = replace(google_cloud_run_v2_service.whatsapp.uri, "https://", "wss://")
      }
    }
  }

  depends_on = [google_cloud_run_v2_service.api]
}

# Allow unauthenticated access to public services
resource "google_cloud_run_v2_service_iam_member" "api_public" {
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.api.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}

resource "google_cloud_run_v2_service_iam_member" "admin_public" {
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.admin.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}

resource "google_cloud_run_v2_service_iam_member" "whatsapp_internal" {
  project  = var.project_id
  location = var.region
  name     = google_cloud_run_v2_service.whatsapp.name
  role     = "roles/run.invoker"
  member   = "allUsers"
}

# Cloud Scheduler for cron jobs (optional - can use internal scheduler)
resource "google_cloud_scheduler_job" "health_check" {
  name        = "${var.app_name}-health-check"
  description = "Periodic health check to keep services warm"
  schedule    = "*/5 * * * *"
  region      = var.region

  http_target {
    http_method = "GET"
    uri         = "${google_cloud_run_v2_service.api.uri}/health"
  }

  depends_on = [google_project_service.required_apis]
}

# --- Global HTTPS Load Balancer for custom domains ---

resource "google_compute_region_network_endpoint_group" "admin_neg" {
  name                  = "${var.app_name}-admin-neg"
  network_endpoint_type = "SERVERLESS"
  region                = var.region

  cloud_run {
    service = google_cloud_run_v2_service.admin.name
  }
}

resource "google_compute_region_network_endpoint_group" "api_neg" {
  name                  = "${var.app_name}-api-neg"
  network_endpoint_type = "SERVERLESS"
  region                = var.region

  cloud_run {
    service = google_cloud_run_v2_service.api.name
  }
}

resource "google_compute_backend_service" "admin_backend" {
  name                  = "${var.app_name}-admin-backend"
  load_balancing_scheme = "EXTERNAL_MANAGED"

  backend {
    group = google_compute_region_network_endpoint_group.admin_neg.id
  }
}

resource "google_compute_backend_service" "api_backend" {
  name                  = "${var.app_name}-api-backend"
  load_balancing_scheme = "EXTERNAL_MANAGED"

  backend {
    group = google_compute_region_network_endpoint_group.api_neg.id
  }
}

resource "google_compute_url_map" "botty_url_map" {
  name            = "${var.app_name}-url-map"
  default_service = google_compute_backend_service.admin_backend.id

  host_rule {
    hosts        = ["bot.phill.ie"]
    path_matcher = "admin"
  }

  host_rule {
    hosts        = ["bot-api.phill.ie"]
    path_matcher = "api"
  }

  path_matcher {
    name            = "admin"
    default_service = google_compute_backend_service.admin_backend.id
  }

  path_matcher {
    name            = "api"
    default_service = google_compute_backend_service.api_backend.id
  }
}

resource "google_compute_managed_ssl_certificate" "botty_cert" {
  name = "${var.app_name}-cert"

  managed {
    domains = ["bot.phill.ie", "bot-api.phill.ie"]
  }
}

resource "google_compute_target_https_proxy" "botty_proxy" {
  name             = "${var.app_name}-https-proxy"
  url_map          = google_compute_url_map.botty_url_map.id
  ssl_certificates = [google_compute_managed_ssl_certificate.botty_cert.id]
}

resource "google_compute_global_forwarding_rule" "botty_https" {
  name                  = "${var.app_name}-https"
  load_balancing_scheme = "EXTERNAL_MANAGED"
  target                = google_compute_target_https_proxy.botty_proxy.id
  port_range            = "443"
}
