using Buildra.Application.Execution;
namespace Buildra.Infrastructure.Execution;

public sealed class DockerWorkspaceTools(WorkspaceFiles files, BoundedProcess process) : IWorkspaceTools
{
    public IReadOnlyList<string> ListFiles(string root) => files.List(root);
    public string ReadFile(string root, string path) => files.Read(root, path);
    public void WriteFile(string root, string path, string content) => files.Write(root, path, content);
    public void EditFile(string root, string path, string oldText, string newText) => files.Edit(root, path, oldText, newText);
    public async Task<string> PrepareAsync(string root, string image, string command, CancellationToken ct)
    {
        if (command.Trim() != "node --test") return "Custom test configuration selected. Run the configured tests before completing.";
        var probe = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root))!, "buildra-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(probe);
        try
        {
            files.Write(probe, "nested/runtime.test.cjs", "const test=require('node:test');const assert=require('node:assert/strict');test('Buildra runtime probe',()=>assert.equal(2+2,4));");
            var passing = await RunTestsAsync(probe, image, command, ct);
            if (!passing.Passed || !passing.Output.Contains("Buildra runtime probe")) throw new ExecutionException("Test environment preflight failed: the known passing test was not executed. Check Docker and the selected image.");
            files.Write(probe, "nested/runtime.test.cjs", "const test=require('node:test');const assert=require('node:assert/strict');test('Buildra intentional failure',()=>assert.equal(2+2,5));");
            var failing = await RunTestsAsync(probe, image, command, ct);
            if (failing.Passed || !failing.Output.Contains("Buildra intentional failure")) throw new ExecutionException("Test environment preflight failed: the intentional test failure was not detected.");
        }
        finally
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(root))! + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(probe).StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(probe).StartsWith("buildra-probe-")) throw new ExecutionException("Invalid preflight cleanup path.");
            if (Directory.Exists(probe)) Directory.Delete(probe, true);
        }
        var existing = files.List(root);
        if (existing.All(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path is "LICENSE" or ".gitignore"))
        {
            files.Write(root, "package.json", "{\n  \"name\": \"buildra-project\",\n  \"private\": true,\n  \"type\": \"commonjs\",\n  \"scripts\": { \"test\": \"node --test\" }\n}\n");
            files.Write(root, "test/README.md", "Use Node's built-in node:test and node:assert/strict modules. Name tests *.test.cjs. Add meaningful feature assertions; no external dependencies are installed.\n");
            return "Environment verified with passing and deliberately failing tests. Created a CommonJS Node scaffold; add feature tests in test/*.test.cjs.";
        }
        return "Environment verified with passing and deliberately failing tests. Existing project files preserved.";
    }
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
            var started = await process.RunAsync("docker", new[] { "run", "-d", "--pull", "never", "--name", name, "--network", "none", "--read-only", "--cap-drop", "ALL",
                "--security-opt", "no-new-privileges", "--cpus", "2", "--memory", "1g", "--pids-limit", "256", "--user", "1000:1000", "--tmpfs", "/tmp:rw,exec,size=536870912",
                image, "/bin/sh", "-c", "while :; do sleep 60; done" }, null, ct);
            if (started.ExitCode != 0) throw new ExecutionException("Docker could not start the configured test image. Ensure it is installed locally and the Linux engine is running.");
            // Transfer through Docker's API. Windows bind mounts can silently expose an empty directory.
            using var archive = new MemoryStream();
            System.Formats.Tar.TarFile.CreateFromDirectory(snapshot, archive, includeBaseDirectory: false);
            var copied = await process.RunAsync("docker", new[] { "exec", "-i", name, "/bin/sh", "-c", "mkdir /tmp/input && tar -xf - -C /tmp/input" }, null, ct, binaryInput: archive.ToArray());
            if (copied.ExitCode != 0) throw new ExecutionException("Docker could not receive the source snapshot. No tests were executed.");
            var result = await process.RunAsync("docker", new[] { "exec", name, "/bin/sh", "-c", "cp -R /tmp/input /tmp/project && cd /tmp/project && " + command }, null, ct);
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
