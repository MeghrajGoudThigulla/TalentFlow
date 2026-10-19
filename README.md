# TalentFlow: Applicant Tracking System (ATS) & Recruitment Pipeline

[![TalentFlow CI](https://github.com/MeghrajGoudThigulla/TalentFlow/actions/workflows/ci.yml/badge.svg)](https://github.com/MeghrajGoudThigulla/TalentFlow/actions/workflows/ci.yml)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

An enterprise-grade Applicant Tracking System (ATS) and recruitment workflow engine built with **C#**, **ASP.NET Core 8**, **Clean Architecture**, **CQRS via MediatR**, **EF Core**, **SignalR**, and **Docker**.

---

## 🌟 Key Architecture & Engineering Highlights

- **Clean Architecture with CQRS**: Decoupled domain entities and business rules with zero external dependencies. Command/Query segregation implemented via **MediatR** with pipeline behaviors for automated validation (`FluentValidation`) and structured diagnostic logging.
- **Transactional Outbox Pattern**: Candidate email dispatches and workflow transitions occur within an atomic database transaction. An asynchronous background worker (`OutboxProcessor : BackgroundService`) guarantees reliable delivery with exponential backoff and max retry limits.
- **Optimistic Concurrency Protection (`RowVersion`)**: Concurrency token tracking on pipeline cards prevents race conditions and overwrites when multiple recruiters review or move candidates simultaneously (`409 Conflict` resolution).
- **Real-Time Pipeline Synchronization (`SignalR`)**: Kanban board changes, candidate submissions, and interview updates broadcast in real-time over WebSocket groups (`job-{id}`) to keep all recruiter dashboards synced without manual page refreshes.
- **Resource-Based Authorization & IDOR Hardening**: Hiring managers are strictly restricted to reading and evaluating jobs and candidates specifically assigned to them.
- **Upload Hardening & Security**: Candidate resumes are inspected for extension allowlists, size caps (5 MB), and magic-byte signatures (`%PDF-`, `PK..`) before storage with randomized GUID paths outside the web root.

---

## 🏗 Solution Layout (Clean Architecture)

```
TalentFlow/
├── src/
│   ├── TalentFlow.Domain/            # Pure entities, domain events, enums, business rule validators
│   ├── TalentFlow.Application/       # CQRS commands/queries, MediatR pipeline behaviors, DTOs, interfaces
│   ├── TalentFlow.Infrastructure/    # EF Core DbContext, Identity/JWT, SignalR hub, file storage, outbox worker
│   └── TalentFlow.Api/               # Controllers, RFC 7807 ProblemDetails middleware, Swagger, Static Kanban UI
├── tests/
│   ├── TalentFlow.Domain.Tests/      # Pure business rule & stage transition invariant unit tests
│   └── TalentFlow.Application.Tests/ # Command handler integration & concurrency conflict tests
├── docker-compose.yml                # One-click container orchestration
├── Dockerfile                        # Multi-stage optimized production image
└── .github/workflows/ci.yml          # Automated CI pipeline
```

---

## 🚀 Quick Start (Local Development)

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: [Docker](https://www.docker.com/)

### 1. Clone & Run
```bash
git clone https://github.com/MeghrajGoudThigulla/TalentFlow.git
cd TalentFlow

# Run the API and Dashboard
dotnet run --project src/TalentFlow.Api/TalentFlow.Api.csproj
```

The application will start at:
- **Interactive Kanban Dashboard & Careers Portal:** `http://localhost:5000`
- **Swagger API Documentation:** `http://localhost:5000/swagger`
- **Health Probes:** `http://localhost:5000/health/ready`

### 2. Pre-Seeded Demo Credentials

The database automatically initializes with roles and demo users upon first startup:

| Role | Email | Password | Permissions |
|---|---|---|---|
| **Admin** | `admin@talentflow.local` | `Admin@123456` | Full system access, audit logs, stage configuration |
| **Recruiter** | `recruiter@talentflow.local` | `Recruiter@123456` | Create/publish jobs, move pipeline cards, schedule interviews |
| **Hiring Manager** | `hiringmanager@talentflow.local` | `Manager@123456` | Review assigned candidates, submit feedback, approve offers |

---

## 🧪 Running Automated Tests

Run the full xUnit test suite covering domain transition rules, stage skipping logic, optimistic concurrency, and duplicate application checks:

```bash
dotnet test
```

---

## 📡 API Overview

### Authentication & Users
- `POST /api/auth/login` - Authenticate and retrieve JWT token + refresh token
- `POST /api/auth/refresh` - Rotate refresh token
- `POST /api/auth/logout` - Revoke refresh token
- `GET /api/users` - Admin user management

### Jobs & Public Careers
- `GET /api/public/jobs` - Public open job postings (anonymous)
- `POST /api/public/jobs/{id}/apply` - Candidate submission with resume file upload
- `GET /api/jobs` - Recruiter/HM job listings with search & filtering
- `POST /api/jobs` - Create draft job
- `POST /api/jobs/{id}/publish` - Publish job to careers page

### Pipeline & Applications
- `GET /api/jobs/{id}/applications` - Kanban board data grouped by pipeline stage
- `GET /api/applications/{id}` - Full candidate drawer (notes, timeline, feedback)
- `POST /api/applications/{id}/move` - Move stage with concurrency validation & notification
- `POST /api/applications/{id}/notes` - Add internal evaluation notes
- `GET /api/applications/{id}/resume` - Secure resume download

### Interviews & Feedback
- `POST /api/applications/{id}/interviews` - Schedule interview with overlap collision detection
- `POST /api/interviews/{id}/feedback` - Submit 1-5 rating & recommendation
- `POST /api/applications/{id}/offer/approve` - Hiring manager offer approval

---

## 📄 License
Licensed under the [MIT License](LICENSE).
