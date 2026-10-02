using Buildra.Application.SourceControl;
using Buildra.Infrastructure.Execution;
namespace Buildra.Infrastructure.SourceControl;
public interface IGitHubCredentialSource { Task<string> GetAsync(CancellationToken ct); }
public sealed class GitHubCredentialSource(BoundedProcess process) : IGitHubCredentialSource
{
    public async Task<string> GetAsync(CancellationToken ct)
    {
        var credential = await process.RunAsync("git", new[] { "credential", "fill" }, null, ct, "protocol=https\nhost=github.com\n\n", 30);
        var token = credential.Output.Split('\n').FirstOrDefault(x => x.StartsWith("password="))?[9..].Trim();
        if (credential.ExitCode != 0 || string.IsNullOrWhiteSpace(token)) throw new SourceControlException("Sign in to GitHub through Git Credential Manager before connecting this repository.");
        return token;
    }
}
