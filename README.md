# Buildra backend

Foundation milestone: .NET 10 modular monolith with Domain → Application boundaries, PostgreSQL/EF Core infrastructure, an ASP.NET Core API, and a separate worker host.

## Local startup

Requirements: .NET 10 SDK and Docker Desktop with its Linux engine running.

```powershell
docker compose up -d --wait
dotnet tool restore
dotnet run --project src/Buildra.Api --no-launch-profile -- --migrate
dotnet run --project src/Buildra.Api --no-launch-profile
```

API: http://127.0.0.1:5080. Health endpoint: `/health` (process health only). The frontend runs separately from Buildra-front.

`--migrate` applies committed EF migrations and seeds one local organization and local owner, then exits. The checked-in database password is exclusively for local development. Override `ConnectionStrings__Buildra` and the Compose `POSTGRES_PASSWORD` together if changing it.

## API

- `GET /api/projects`
- `POST /api/projects`
- `GET /api/projects/{id}` — project and assigned team
- `PUT /api/projects/{id}`
- `DELETE /api/projects/{id}`

Project input: name, description, repositoryUrl, defaultBranch, instructions. Only HTTPS GitHub repository URLs are accepted. Creating a project atomically persists three agent definitions, assignments, a project conversation, and a creation event. Agent definitions remain organization-owned after project deletion.

The API binds to loopback with a fixed local identity. Authentication and trusted tenant resolution must be implemented before network deployment. The repository URL is metadata only; GitHub access is not yet verified.

## Validation

```powershell
dotnet build
dotnet test
dotnet ef migrations has-pending-model-changes --project src/Buildra.Infrastructure --startup-project src/Buildra.Api
```

Tests cover domain lifecycle, role permissions, repository input validation, architecture dependencies, and HTTP project CRUD with tenant scoping. API tests use EF InMemory; real PostgreSQL verification is still required. Domain permissions are defined and tested, but tool dispatch does not exist yet.

## Current milestone and next work

Delivered: project CRUD, organization/user basics, core entity schema, initial migration, three agent templates, provider contract, worker host, and React integration.

Next: model provider implementation and runtime; GitHub credentials and repository/worktree adapter; durable worker jobs; PM → Developer → Reviewer orchestration; persisted chat and SignalR; retries, cancellation, tool sandboxing, and PostgreSQL/Git integration tests.

The worker host is intentionally idle until durable execution is implemented. No agent execution, model calls, automatic PR creation, production authentication, or SignalR hub is implemented in this milestone.
