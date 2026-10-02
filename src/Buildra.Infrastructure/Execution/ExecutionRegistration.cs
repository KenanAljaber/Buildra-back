using Buildra.Application.Execution;
using Buildra.Application.SourceControl;
using Buildra.Infrastructure.SourceControl;
using Microsoft.Extensions.DependencyInjection;
namespace Buildra.Infrastructure.Execution;

public static class ExecutionRegistration
{
    public static IServiceCollection AddBuildraExecution(this IServiceCollection services)
    {
        services.AddOptions<GitHubOptions>(); services.AddSingleton<BoundedProcess>(); services.AddSingleton<WorkspaceFiles>();
        services.AddScoped<IGitHubCredentialSource, GitHubCredentialSource>();
        services.AddScoped<GitWorkspace>(); services.AddScoped<IExecutionStore, EfExecutionStore>();
        services.AddScoped<IWorkspaceTools, DockerWorkspaceTools>(); services.AddScoped<ExecutionUseCases>();
        services.AddHttpClient<ISourceControlProvider, GitHubSourceControlProvider>(http => http.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
