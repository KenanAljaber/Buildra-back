# Buildra backend

.NET 10 modular monolith with PostgreSQL/EF Core, a local ASP.NET Core API, and a separate PM worker. `main` holds the base application; `dev` is the integration branch; `feature/pm-request-to-task` adds the first agent workflow.

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

Project creation atomically saves PM/Developer/Reviewer definitions and assignments, project conversation, and creation event. Project deletion removes its data; agent definitions remain organization-owned.

The worker atomically claims PostgreSQL queue rows, with a five-minute lease and a two-minute HTTP timeout. Short claim transactions are serialized and only one request per project runs at a time. A crashed worker's lease is reclaimed; fencing tokens reject stale results. Task creation, PM response, run completion, and workflow event commit together. Results are at least once at the model-call boundary; interrupted model requests may incur usage again.

A valid plan creates one Ready task with measurable acceptance criteria. Clarification returns an Action Required message without inventing a task; the user responds by sending another request. Provider failures are persisted with safe messages and a retry action. Retrying reuses the request and run, preserving the user message; run usage reflects the latest returned response. The PM has no shell or repository tools and cannot claim to have inspected code.

## Validation

```powershell
dotnet build
dotnet test
$env:BUILDRA_TEST_POSTGRES = 'Host=localhost;Port=55432;Database=postgres;Username=buildra;Password=buildra_local'
dotnet test tests/Buildra.IntegrationTests
dotnet ef migrations has-pending-model-changes --project src/Buildra.Infrastructure --startup-project src/Buildra.Api
```

PostgreSQL tests create randomly named isolated databases and drop only those databases afterward. They exercise task persistence, clarification, failure/retry, queue concurrency, expired leases, stale-worker rejection, permission checks, and invalid model output. Without the test connection, PostgreSQL cases are explicitly skipped. HTTP tests use EF InMemory. OpenAI contract tests use fake HTTP responses; planning tests use a deterministic test provider, with no paid model calls.

## Remaining work

GitHub verification and credentials; repository/worktree adapter; Developer/Reviewer execution; PR creation; fuller agent configuration; direct and task chat UI; SignalR realtime updates; cancellation and budgets. The current UI polls persisted state every two seconds. Real OpenAI calls require a valid local API key and model access; these are not verified by the deterministic tests.
