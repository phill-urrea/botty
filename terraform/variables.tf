# Botty AI Assistant - Terraform Variables

variable "project_id" {
  description = "GCP project ID"
  type        = string
}

variable "region" {
  description = "GCP region"
  type        = string
  default     = "us-central1"
}

variable "app_name" {
  description = "Application name"
  type        = string
  default     = "botty"
}

variable "environment" {
  description = "Environment (development, staging, production)"
  type        = string
  default     = "development"

  validation {
    condition     = contains(["development", "staging", "production"], var.environment)
    error_message = "Environment must be development, staging, or production."
  }
}

variable "image_tag" {
  description = "Docker image tag to deploy"
  type        = string
  default     = "latest"
}

# Database settings
variable "db_tier" {
  description = "Cloud SQL instance tier"
  type        = string
  default     = "db-f1-micro"
}

variable "db_disk_size" {
  description = "Cloud SQL disk size in GB"
  type        = number
  default     = 10
}

# API Service settings
variable "api_cpu" {
  description = "API service CPU limit"
  type        = string
  default     = "1"
}

variable "api_memory" {
  description = "API service memory limit"
  type        = string
  default     = "512Mi"
}

variable "api_max_instances" {
  description = "API service max instances"
  type        = number
  default     = 10
}

# Admin UI settings
variable "admin_max_instances" {
  description = "Admin UI max instances"
  type        = number
  default     = 5
}

# Authentication settings
variable "admin_allowed_email" {
  description = "Email address allowed to access the admin UI (leave empty to allow any Google user)"
  type        = string
  default     = ""
  sensitive   = true
}

variable "auth_google_client_id" {
  description = "Google OAuth Client ID for admin UI authentication"
  type        = string
  default     = ""
  sensitive   = true
}

variable "auth_google_client_secret" {
  description = "Google OAuth Client Secret for admin UI authentication"
  type        = string
  default     = ""
  sensitive   = true
}
