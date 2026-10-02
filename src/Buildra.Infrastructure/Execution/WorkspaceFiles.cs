using Buildra.Application.Execution;
namespace Buildra.Infrastructure.Execution;

public sealed class WorkspaceFiles
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".github", "node_modules", "bin", "obj", ".vs", ".ssh", "secrets.json", ".npmrc", ".netrc", "credentials", "appsettings.Local.json" };
    public static bool AllowedName(string name) => !Forbidden.Contains(name) && !name.StartsWith(".env", StringComparison.OrdinalIgnoreCase) &&
        !name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".key", StringComparison.OrdinalIgnoreCase);
    public string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 500 || relative.Contains('\\') || relative.Contains(':') || Path.IsPathRooted(relative))
            throw new ExecutionException("Use a relative repository path with forward slashes.");
        var parts = relative.Split('/');
        if (parts.Any(p => p is "" or "." or ".." || !AllowedName(p) || p.EndsWith(' ') || p.EndsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ExecutionException("The requested repository path is not permitted.");
        if (parts.Any(p => System.Text.RegularExpressions.Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new ExecutionException("Device names are not valid repository paths.");
        var fullRoot = Path.GetFullPath(root);
        var current = fullRoot;
        if (!Directory.Exists(fullRoot) || File.GetAttributes(fullRoot).HasFlag(FileAttributes.ReparsePoint)) throw new ExecutionException("Invalid task workspace.");
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new ExecutionException("Symbolic links are not accessible to agents.");
        }
        var path = Path.GetFullPath(current);
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ExecutionException("The requested path escapes the workspace.");
        return path;
    }
    public IReadOnlyList<string> List(string root)
    {
        if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) throw new ExecutionException("Invalid task workspace.");
        var files = new List<string>(); var pending = new Stack<string>(); pending.Push(root); var visited = 0;
        while (pending.Count > 0 && files.Count < 1000 && visited++ < 2000)
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()).Order())
            {
                if (!AllowedName(Path.GetFileName(entry)) || File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)) continue;
                if (Directory.Exists(entry)) pending.Push(entry);
                else { files.Add(Path.GetRelativePath(root, entry).Replace('\\', '/')); if (files.Count >= 1000) break; }
            }
        return files;
    }
    public string Read(string root, string path)
    {
        var full = Resolve(root, path);
        if (!File.Exists(full)) throw new ExecutionException("The requested file does not exist.");
        if (new FileInfo(full).Length > 256000) throw new ExecutionException("The file exceeds the agent read limit.");
        var text = File.ReadAllText(full); return text.Length <= 20000 ? text : text[..20000] + "\n[truncated]";
    }
    public void Write(string root, string path, string content)
    {
        if (content.Length > 100000 || content.Contains('\0')) throw new ExecutionException("File content exceeds the allowed text limits.");
        var full = Resolve(root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, content);
    }
    public void Delete(string root, string path)
    {
        var full = Resolve(root, path); if (Directory.Exists(full)) throw new ExecutionException("Only individual files can be deleted."); File.Delete(full);
    }
}
