using Buildra.Application.Execution;
using Buildra.Application.SourceControl;
namespace Buildra.Api.Execution;

public static class ExecutionEndpoints
{
    public static void MapExecutionEndpoints(this WebApplication app, Guid org)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}");
        group.MapPost("/repository/verify", async (Guid projectId, ExecutionUseCases cases, CancellationToken ct) =>
        {
            try { return await cases.VerifyAsync(org, projectId, ct) is { } metadata ? Results.Ok(metadata) : Results.NotFound(); }
            catch (SourceControlException e) { return Results.Problem(e.Message, statusCode: 409); }
        });
        group.MapPost("/tasks/{taskId:guid}/execute", async (Guid projectId, Guid taskId, ExecutionUseCases cases, CancellationToken ct) =>
        {
            try { return await cases.StartAsync(org, projectId, taskId, ct) ? Results.Accepted() : Results.NotFound(); }
            catch (ExecutionException e) { return Results.Problem(e.Message, statusCode: 409); }
        });
        group.MapGet("/tasks/{taskId:guid}", async (Guid projectId, Guid taskId, ExecutionUseCases cases, CancellationToken ct) =>
            await cases.GetAsync(org, projectId, taskId, ct) is { } result ? Results.Ok(result) : Results.NotFound());
    }
}
