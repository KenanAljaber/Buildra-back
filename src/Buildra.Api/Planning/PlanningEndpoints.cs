using Buildra.Application.Planning;
namespace Buildra.Api.Planning;

public static class PlanningEndpoints
{
    public record SubmitRequest(string Content);
    public static void MapPlanningEndpoints(this WebApplication app, Guid organizationId)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/planning");
        group.MapGet("/", async (Guid projectId, PlanningUseCases useCases, CancellationToken ct) =>
            await useCases.GetAsync(organizationId, projectId, ct) is { } workspace ? Results.Ok(workspace) : Results.NotFound());
        group.MapPost("/requests", async (Guid projectId, SubmitRequest input, PlanningUseCases useCases, CancellationToken ct) =>
        {
            try
            {
                var run = await useCases.EnqueueAsync(organizationId, projectId, Guid.Parse("22222222-2222-2222-2222-222222222222"), input.Content, ct);
                return run is null ? Results.NotFound() : Results.Accepted($"/api/projects/{projectId}/planning", run);
            }
            catch (ArgumentException e) { return Results.Problem(e.Message, statusCode: 400); }
            catch (InvalidOperationException) { return Results.Problem("Assign an enabled Product Manager with task-creation permission first.", statusCode: 409); }
        });
        group.MapPost("/runs/{runId:guid}/retry", async (Guid projectId, Guid runId, PlanningUseCases useCases, CancellationToken ct) =>
            await useCases.RetryAsync(organizationId, projectId, runId, ct) ? Results.Accepted() : Results.NotFound());
    }
}
