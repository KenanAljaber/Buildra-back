using Buildra.Domain.Projects;
using Buildra.Application.Projects;
namespace Buildra.ArchitectureTests;
public sealed class DependencyTests
{
    [Fact]
    public void DomainDoesNotReferenceExternalConcerns()
    {
        var references = typeof(Project).Assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();
        Assert.DoesNotContain(references, name => name.StartsWith("Buildra.") || name.StartsWith("Microsoft.EntityFrameworkCore") || name.StartsWith("Microsoft.AspNetCore") || name.StartsWith("Npgsql"));
    }
    [Fact]
    public void ApplicationDoesNotReferenceInfrastructureOrAspNet()
    {
        var references = typeof(ProjectUseCases).Assembly.GetReferencedAssemblies().Select(x => x.Name!).ToArray();
        Assert.DoesNotContain(references, name => name == "Buildra.Infrastructure" || name.StartsWith("Microsoft.AspNetCore") || name.StartsWith("Microsoft.EntityFrameworkCore"));
    }
}
