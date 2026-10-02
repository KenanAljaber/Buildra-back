using System.Diagnostics;
using System.Text;
using Buildra.Application.Execution;
namespace Buildra.Infrastructure.Execution;

public record ProcessResult(int ExitCode, string Output, string Error);
public sealed class BoundedProcess
{
    public async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, string? directory, CancellationToken ct,
        string? input = null, int timeoutSeconds = 120, IReadOnlyDictionary<string, string>? extraEnvironment = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (directory is not null) start.WorkingDirectory = directory;
        var allowed = new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "ProgramFiles", "ProgramFiles(x86)", "HOME" };
        start.Environment.Clear();
        foreach (var key in allowed) if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        start.Environment["GIT_TERMINAL_PROMPT"] = "0"; start.Environment["GCM_INTERACTIVE"] = "never";
        if (extraEnvironment is not null) foreach (var (key, value) in extraEnvironment) start.Environment[key] = value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = start };
        try { process.Start(); } catch { throw new ExecutionException($"Could not start {executable}. Check the worker's local installation."); }
        var output = DrainAsync(process.StandardOutput, timeout.Token); var error = DrainAsync(process.StandardError, timeout.Token);
        try
        {
            if (input is not null) { await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token); process.StandardInput.Close(); }
            await process.WaitForExitAsync(timeout.Token);
            return new(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            try { await Task.WhenAll(output, error); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new ExecutionException($"{executable} exceeded its execution time limit.");
        }
    }
    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[4096]; var text = new StringBuilder(); int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
            if (text.Length < 32000) text.Append(buffer, 0, Math.Min(count, 32000 - text.Length));
        return text.ToString();
    }
}
