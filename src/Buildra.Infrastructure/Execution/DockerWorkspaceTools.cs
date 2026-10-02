using Buildra.Application.Execution;
namespace Buildra.Infrastructure.Execution;

public sealed class DockerWorkspaceTools(WorkspaceFiles files, BoundedProcess process) : IWorkspaceTools
{
    public IReadOnlyList<string> ListFiles(string root) => files.List(root);
    public string ReadFile(string root, string path) => files.Read(root, path);
    public void WriteFile(string root, string path, string content) => files.Write(root, path, content);
    public void DeleteFile(string root, string path) => files.Delete(root, path);
    public string SearchFiles(string root, string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200) throw new ExecutionException("Search query must contain 1–200 characters.");
        var matches = new List<string>();
        foreach (var path in files.List(root))
        {
            if (path.Contains(query, StringComparison.OrdinalIgnoreCase)) matches.Add(path);
            else try { if (files.Read(root, path).Contains(query, StringComparison.OrdinalIgnoreCase)) matches.Add(path); } catch (ExecutionException) { }
            if (matches.Count >= 50) break;
        }
        return string.Join('\n', matches);
    }
    public async Task<TestResult> RunTestsAsync(string root, string image, string command, CancellationToken ct)
    {
        var name = "buildra-test-" + Guid.NewGuid().ToString("N");
        var snapshot = Path.Combine(Path.GetDirectoryName(root)!, name); Directory.CreateDirectory(snapshot);
        try
        {
            long size = 0;
            foreach (var path in files.List(root))
            {
                var source = files.Resolve(root, path); size += new FileInfo(source).Length;
                if (size > 20_000_000) throw new ExecutionException("The source snapshot exceeds the 20 MB test limit.");
                var target = Path.Combine(snapshot, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
            }
            var result = await process.RunAsync("docker", new[] { "run", "--rm", "--name", name, "--network", "none", "--read-only", "--cap-drop", "ALL",
                "--security-opt", "no-new-privileges", "--cpus", "2", "--memory", "1g", "--pids-limit", "256", "--user", "1000:1000", "--tmpfs", "/tmp:rw,exec,size=536870912",
                "--mount", $"type=bind,source={snapshot},target=/input,readonly", image, "/bin/sh", "-c", "cp -R /input /tmp/project && cd /tmp/project && " + command }, null, ct);
            var output = (result.Output + "\n" + result.Error).Trim();
            var passed = result.ExitCode == 0 && !(command.Trim() == "node --test" && System.Text.RegularExpressions.Regex.IsMatch(output, @"\btests\s+0\b"));
            return new(passed, output);
        }
        finally
        {
            // Stop the named test container even when the CLI timed out or the worker was cancelled.
            try { await process.RunAsync("docker", new[] { "rm", "-f", name }, null, CancellationToken.None, timeoutSeconds: 15); } catch { }
            if (Directory.Exists(snapshot)) Directory.Delete(snapshot, true); // Generated sibling snapshot; never the repository.
        }
    }
}
