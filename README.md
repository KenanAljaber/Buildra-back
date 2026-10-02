# Buildra backend

.NET 10 modular monolith with PostgreSQL/EF Core, a local ASP.NET Core API, and a separate agent worker. `main` holds the base application; `dev` is the integration branch; `feature/github-task-workflow` adds GitHub verification and Developer/Reviewer execution on top of PM planning.

## Local startup

Requirements: .NET 10 SDK and Docker Desktop with its Linux engine running.

```powershell
docker compose up -d --wait
dotnet tool restore
dotnet run --project src/Buildra.Api --no-launch-profile -- --migrate
dotnet run --project src/Buildra.Api --no-launch-profile
```

API: http://127.0.0.1:5080. PostgreSQL: loopback port 55432. `/health` checks process health. The migration command applies schema changes and seeds a local organization and owner, then exits.

In another terminal, configure the worker's OpenAI key outside the repository and start it:

```powershell
dotnet user-secrets set 'OpenAI:ApiKey' '<your-api-key>' --project src/Buildra.Worker
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project src/Buildra.Worker --no-launch-profile
```

`OPENAI_API_KEY` in the worker environment is also supported. Never paste a key into chat or commit it to Git. The API does not need the model key. User secrets load in Development; environment variables work in either environment.

`OpenAI:StrongReasoningModel` defaults to `gpt-4.1-mini` and can be changed in worker configuration. The adapter uses OpenAI's Responses API with strict structured outputs and `store=false`: https://developers.openai.com/api/docs/guides/structured-outputs.

Optional `OpenAI:InputCostPerMillionTokens` and `OpenAI:OutputCostPerMillionTokens` allow an estimated cost in the same currency as those rates. Unconfigured cost is null, not zero. Estimates use full input/output token counts without cached-input discounts and describe the latest attempt, not an invoice. Configure current rates for your chosen model; rates are not hard-coded.

The checked-in PostgreSQL credentials are for local development. Override `ConnectionStrings__Buildra` on both API and worker together with Compose `POSTGRES_PASSWORD` if changing them. The API has a fixed local identity and binds to loopback; remote deployment requires authentication and trusted tenant resolution.

## API and behavior

- `GET/POST /api/projects`
- `GET/PUT/DELETE /api/projects/{id}`
- `GET /api/projects/{id}/planning/` — recent persisted messages, tasks, and runs
- `POST /api/projects/{id}/planning/requests` with `{ "content": "..." }` — returns HTTP 202 and queues the PM run
- `POST /api/projects/{id}/planning/runs/{runId}/retry` — retries a failed run
- `POST /api/projects/{id}/repository/verify` — checks GitHub write access and the configured base branch
- `POST /api/projects/{id}/tasks/{taskId}/execute` — queues a Ready task or retries a failed implementation
- `GET /api/projects/{id}/tasks/{taskId}` — task, execution job, runs, reviews, and tool activity

Project creation atomically saves PM/Developer/Reviewer definitions and assignments, project conversation, and creation event. Project deletion removes its data; agent definitions remain organization-owned.

The worker atomically claims PostgreSQL queue rows, with a five-minute lease and a two-minute HTTP timeout. Short claim transactions are serialized and only one request per project runs at a time. A crashed worker's lease is reclaimed; fencing tokens reject stale results. Task creation, PM response, run completion, and workflow event commit together. Results are at least once at the model-call boundary; interrupted model requests may incur usage again.

A valid plan creates one Ready task with measurable acceptance criteria. Clarification returns an Action Required message without inventing a task; the user responds by sending another request. Provider failures are persisted with safe messages and a retry action. Retrying reuses the request and run, preserving the user message; run usage reflects the latest returned response. The PM has no shell or repository tools and cannot claim to have inspected code.

## Validation

```powershell
dotnet build
dotnet test
$env:BUILDRA_TEST_POSTGRES = 'Host=localhost;Port=55432;Database=postgres;Username=buildra;Password=buildra_local'
$env:BUILDRA_TEST_DOCKER = '1'
dotnet test tests/Buildra.IntegrationTests
dotnet ef migrations has-pending-model-changes --project src/Buildra.Infrastructure --startup-project src/Buildra.Api
```

PostgreSQL tests create randomly named isolated databases and drop only those databases afterward. They exercise task persistence, clarification, failure/retry, queue concurrency, expired leases, stale-worker rejection, permission checks, and invalid model output. Without the test connection, PostgreSQL cases are explicitly skipped. HTTP tests use EF InMemory. OpenAI contract tests use fake HTTP responses; planning tests use a deterministic test provider, with no paid model calls.

## Code execution

Git and Docker must be installed on the worker machine. Sign into GitHub using Git Credential Manager for HTTPS Git operations; Buildra retrieves the cached credential in memory for GitHub REST calls. The account must have repository write access and permission to create pull requests. An empty repository needs an initial commit and the configured base branch before verification succeeds. No GitHub credentials go into project settings or model prompts.

1. Open the project and click **Verify repository**.
2. Send a specific request to the PM and respond to any clarification.
3. Click **Start implementation** on a Ready task. Optional **Automatically implement planned tasks** in project settings queues subsequent tasks after repository verification.
4. Inspect implementation history for runs, tests, review feedback, branch, and commit. Successful review publishes a task branch and opens a PR; merging remains a manual GitHub action.

Each task uses an isolated checkout under `%LOCALAPPDATA%\Buildra\workspaces` and a `buildra/task-{taskId}` branch. The Developer has constrained file/search/test tools; the Reviewer can only read, search, test, and submit a review. Agent file tools reject traversal, symlinks, Git metadata, GitHub workflows, environment files, and common credential files. Repository code never executes directly on the host.

Tests run in an offline Docker container with a read-only source snapshot, non-root user, resource limits, and no mounted credentials. The default is `node:24-alpine` with `node --test`, suited to dependency-free Node projects. Run `docker pull node:24-alpine` before first use. For other stacks, set a locally available image containing the required tools and dependencies and an appropriate test command in project settings. Images need `/bin/sh`, `cp`, and a writable `/tmp`; the container cannot download dependencies during tests. Passing zero tests with the default command is rejected. Both Developer completion and Reviewer approval require passing tests after the final edit.

Execution is bounded to 24 actions per agent run, three implementation/review rounds, two minutes per subprocess, and 45 minutes per job. Jobs have renewable three-minute leases, stale-worker fencing, and an exclusive workspace lock. A retry retains the workspace and existing branch; publishing reuses an existing open task PR. Project editing/deletion is blocked while code execution is queued or running. Configure `OpenAI:StrongCodingModel` to change the Developer/Reviewer model; it defaults to `gpt-4.1-mini`.

Workflow tests use deterministic model actions with real PostgreSQL, Git, and Docker. GitHub HTTP tests verify PR creation/reuse and rejection of changes after review using simulated responses; they do not create remote PRs or make paid model calls.

## Remaining work

Fuller agent configuration; direct and task chat UI; SignalR realtime updates; user cancellation and configurable spend budgets; dependency provisioning for arbitrary stacks. The current UI polls persisted state every two seconds. Real OpenAI calls require a valid local API key and model access; these are not verified by the deterministic tests.
