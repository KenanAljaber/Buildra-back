using System.Text.Json.Serialization;
using Buildra.Application.Projects;
using Buildra.Infrastructure.Persistence;
using Buildra.Infrastructure.Projects;
using Microsoft.EntityFrameworkCore;
using Buildra.Api.Planning;
using Buildra.Application.Planning;
using Buildra.Infrastructure.Planning;
using Buildra.Infrastructure.Execution;
using Buildra.Api.Execution;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<BuildraDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Buildra")));
builder.Services.AddScoped<IProjectStore, EfProjectStore>();
builder.Services.AddScoped<ProjectUseCases>();
builder.Services.AddScoped<IPlanningStore, EfPlanningStore>();
builder.Services.AddScoped<PlanningUseCases>();
builder.Services.AddBuildraExecution();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseExceptionHandler();
app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", milestone = "github-task-workflow" }));
app.MapGet("/api/status", async (BuildraDbContext db, CancellationToken ct) => {
    var pulse = await db.WorkerHeartbeats.AsNoTracking().SingleOrDefaultAsync(p => p.Id == "agents", ct);
    return Results.Ok(new { workerOnline = pulse is not null && pulse.UpdatedAt > DateTimeOffset.UtcNow.AddSeconds(-20), lastHeartbeat = pulse?.UpdatedAt, serverTime = DateTimeOffset.UtcNow });
});
// Local-only identity. Remote deployment requires authentication and tenant resolution.
var organizationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
app.MapPlanningEndpoints(organizationId);
app.MapExecutionEndpoints(organizationId);
app.MapGet("/api/projects", (ProjectUseCases useCases, CancellationToken ct) => useCases.ListAsync(organizationId, ct));
app.MapGet("/api/projects/{id:guid}", async (Guid id, ProjectUseCases useCases, CancellationToken ct) =>
    await useCases.GetAsync(organizationId, id, ct) is { } project ? Results.Ok(project) : Results.NotFound());
app.MapPost("/api/projects", async (ProjectInput input, ProjectUseCases useCases, CancellationToken ct) =>
{
    try { var project = await useCases.CreateAsync(organizationId, input, ct); return Results.Created($"/api/projects/{project.Id}", project); }
    catch (ArgumentException e) { return Results.Problem(e.Message, statusCode: 400); }
});
app.MapPut("/api/projects/{id:guid}", async (Guid id, ProjectInput input, ProjectUseCases useCases, CancellationToken ct) =>
{
    try { return await useCases.UpdateAsync(organizationId, id, input, ct) is { } project ? Results.Ok(project) : Results.NotFound(); }
    catch (ArgumentException e) { return Results.Problem(e.Message, statusCode: 400); }
});
app.MapDelete("/api/projects/{id:guid}", async (Guid id, ProjectUseCases useCases, CancellationToken ct) =>
{
    try { return await useCases.DeleteAsync(organizationId, id, ct) ? Results.NoContent() : Results.NotFound(); }
    catch (ArgumentException e) { return Results.Problem(e.Message, statusCode: 409); }
});
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BuildraDbContext>();
    await db.Database.MigrateAsync();
    if (!await db.Organizations.AnyAsync(x => x.Id == organizationId))
    {
        db.Organizations.Add(new() { Id = organizationId, Name = "My company" });
        db.Users.Add(new() { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), OrganizationId = organizationId, Name = "Local owner", Email = "owner@localhost" });
        await db.SaveChangesAsync();
    }
    return;
}
app.Run();
public partial class Program;
