using System.Net;
using System.Net.Http.Json;
using Buildra.Application.Projects;
using Buildra.Domain.Identity;
using Buildra.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Buildra.IntegrationTests;

public sealed class ProjectApiTests
{
    [Fact]
    public async Task ProjectCrudAssignsTeamAndScopesOwnership()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<BuildraDbContext>();
            services.RemoveAll<DbContextOptions<BuildraDbContext>>();
            services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<BuildraDbContext>>();
            services.AddDbContext<BuildraDbContext>(o => o.UseInMemoryDatabase(databaseName));
        }));
        using var client = factory.CreateClient();
        var input = new ProjectInput("Buildra", "AI team", "https://github.com/owner/repo", "main", "Use tests");
        var response = await client.PostAsJsonAsync("/api/projects", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<Buildra.Domain.Projects.Project>();
        Assert.NotNull(project);
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var details = await client.GetFromJsonAsync<ProjectDetails>($"/api/projects/{project.Id}", options);
        Assert.Equal(3, details!.Team.Count);
        Assert.Equal(3, details.Team.Select(a => a.Role).Distinct().Count());
        var update = await client.PutAsJsonAsync($"/api/projects/{project.Id}", input with { Name = "Updated" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Updated", (await client.GetFromJsonAsync<ProjectDetails>($"/api/projects/{project.Id}", options))!.Project.Name);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BuildraDbContext>();
            var foreign = new Buildra.Domain.Projects.Project { OrganizationId = Guid.NewGuid(), Name = "Foreign" };
            db.Projects.Add(foreign);
            await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{foreign.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/projects/{foreign.Id}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/projects", input with { Name = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/projects/{project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{project.Id}")).StatusCode);
    }
}
