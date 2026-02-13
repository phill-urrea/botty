# Botty - Personal AI Assistant

A personal AI assistant with persistent memory, approval workflows, and extensible skills.

## Features

- **Memory System**: Postgres + pgvector for semantic memory storage and retrieval
- **Kanban Workflow**: Task management with approval gates for all side effects
- **Scheduled Tasks**: Cron-based scheduling for proactive assistant behavior
- **Skills Framework**: Extensible skills with Gmail, Google Calendar, and shell access
- **WhatsApp Integration**: Send and receive messages via WhatsApp Web
- **Admin UI**: Next.js dashboard for configuration and approvals
- **Secrets Management**: Secure storage for sensitive configuration

## Architecture

```
botty/
├── src/
│   ├── Botty.Api/              # ASP.NET Core Web API
│   ├── Botty.Core/             # Domain models, interfaces
│   ├── Botty.Memory/           # Memory system implementation
│   ├── Botty.LLM/              # LLM abstraction + Claude impl
│   ├── Botty.Skills/           # Skills framework + Gmail/GCal
│   ├── Botty.Messaging/        # WhatsApp integration
│   ├── Botty.Workflow/         # Kanban tasks & approval system
│   ├── Botty.Scheduler/        # Cron jobs & scheduled tasks
│   ├── Botty.Secrets/          # Secret store abstraction
│   └── Botty.Infrastructure/   # EF Core, repositories
├── config/
│   └── Soul.md                 # Assistant personality config
├── whatsapp-bridge/            # Node.js WhatsApp sidecar
├── admin-ui/                   # Next.js admin application
├── docker/                     # Dockerfiles, compose
└── deploy/                     # GCP Terraform/scripts
```

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- Docker & Docker Compose
- PostgreSQL 16 with pgvector extension

## Quick Start

### 1. Start the database

```bash
cd docker
docker compose up -d postgres
```

### 2. Run the API

```bash
cd src/Botty.Api
dotnet run
```

### 3. Configure secrets

Create a `.env.local` file with your secrets:

```bash
BOTTY_SECRET_ANTHROPIC_API_KEY=sk-ant-...
```

## Configuration

### Soul.md

The `config/Soul.md` file defines the assistant's personality, directives, and boundaries. Edit this file to customize your assistant's behavior.

### Skills

Skills are configured via the Admin UI at `/skills`. Each skill has:
- Non-sensitive config stored in the database
- Sensitive config (API keys, OAuth tokens) stored in the secret store

### Google OAuth Account Linking (Gmail + Calendar)

Botty now supports an in-app OAuth web flow for linking multiple Google accounts.

1. Open Admin UI at `/settings`.
2. Configure Google provider credentials:
   - `Client ID`
   - `Client Secret`
   - `Redirect URI` (for local Docker: `http://localhost:5001/api/oauth/providers/google/callback`)
3. Click **Connect Google Account** and complete Google consent.
4. Linked accounts become available to both Gmail and Calendar skills automatically.
5. Set default Gmail/Calendar accounts in Settings if needed.

Environment/secret keys for local development:

```bash
BOTTY_SECRET_OAUTH_PROVIDERS_GOOGLE_CLIENT_ID=...
BOTTY_SECRET_OAUTH_PROVIDERS_GOOGLE_CLIENT_SECRET=...
BOTTY_SECRET_OAUTH_PROVIDERS_GOOGLE_REDIRECT_URI=http://localhost:5001/api/oauth/providers/google/callback
BOTTY_SECRET_OAUTH_PROVIDERS_GOOGLE_SCOPES="openid email profile https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.send https://www.googleapis.com/auth/gmail.modify https://www.googleapis.com/auth/calendar https://www.googleapis.com/auth/calendar.events"
```

Legacy `accounts` JSON config in Gmail/Calendar skills is still supported as a fallback and is imported into linked accounts at startup.

### Interactive Brokers (Client Portal Gateway)

The `interactive_brokers` skill is read-only in v1 and uses the IBKR Client Portal
Gateway (CPGW) running locally.

1. Download and run CPGW from IBKR docs, then authenticate in browser.
2. Configure the skill in Admin UI (`/skills`) with:
   - `gateway_base_url`: usually `https://host.docker.internal:5000` when API runs in Docker.
   - `default_account_id`: optional account fallback.
   - `request_timeout_seconds`: optional timeout.
   - `use_insecure_local_tls`: set to `true` for local self-signed gateway certs.
3. Use tools:
   - `ib_list_accounts`
   - `ib_get_portfolio_summary`
   - `ib_list_positions`
   - `ib_get_account_balances`
   - `ib_get_unrealized_pnl`

If the assistant reports an unauthenticated IB session, log in to Client Portal Gateway
in browser on the same machine and retry.

## Development

### Build the solution

```bash
dotnet build
```

### Run tests

```bash
dotnet test
```

### Docker development

```bash
cd docker
docker compose up
```

## Deployment

Botty is designed to run on Google Cloud Platform using Cloud Run, Cloud SQL, and Secret Manager.

### Quick Deploy

```bash
# Run the setup script
chmod +x scripts/gcp-setup.sh
./scripts/gcp-setup.sh

# Deploy with Terraform
cd terraform
terraform init
terraform apply -var-file="environments/dev.tfvars"
```

### CI/CD

The repository includes GitHub Actions workflows for:
- **CI**: Builds, tests, and validates on every PR
- **Deploy**: Automatically deploys on merge to main
- **Terraform Plan**: Shows infrastructure changes on PRs

See [docs/deployment.md](docs/deployment.md) for detailed deployment instructions.

### Infrastructure

| Component | Service | Description |
|-----------|---------|-------------|
| API | Cloud Run | .NET 10 Web API |
| Admin UI | Cloud Run | Next.js frontend |
| WhatsApp | Cloud Run | Node.js bridge |
| Database | Cloud SQL | PostgreSQL 16 + pgvector |
| Secrets | Secret Manager | API keys, OAuth tokens |
| Images | Artifact Registry | Docker container images |

## Project Structure

```
botty/
├── botty/                      # .NET backend
│   ├── src/
│   │   ├── Botty.Api/          # Web API
│   │   ├── Botty.Core/         # Domain models
│   │   ├── Botty.Memory/       # Memory system
│   │   ├── Botty.LLM/          # LLM providers
│   │   ├── Botty.Skills/       # Skills framework
│   │   └── ...
│   └── docker/                 # Docker configs
├── admin-ui/                   # Next.js admin app
├── whatsapp-bridge/            # Node.js WhatsApp sidecar
├── terraform/                  # GCP infrastructure
├── scripts/                    # Setup scripts
└── docs/                       # Documentation
```

## License

Private - All rights reserved.
