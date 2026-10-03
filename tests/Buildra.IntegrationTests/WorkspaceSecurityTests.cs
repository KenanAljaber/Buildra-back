using Buildra.Application.Execution;
using Buildra.Infrastructure.Execution;
namespace Buildra.IntegrationTests;

public sealed class WorkspaceSecurityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "buildra-path-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceFiles files = new();
    public WorkspaceSecurityTests() => Directory.CreateDirectory(root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    [Theory]
    [InlineData("../escape.js")]
    [InlineData("/outside.js")]
    [InlineData("C:/outside.js")]
    [InlineData("nested\\..\\escape.js")]
    [InlineData(".git/config")]
    [InlineData(".env.local")]
    [InlineData(".github/workflows/build.yml")]
    [InlineData("id.key")]
    [InlineData("file.js:secret")]
    [InlineData("CON.js")]
    public void RejectsWorkspaceEscapeAndSensitivePaths(string path) => Assert.Throws<ExecutionException>(() => files.Write(root, path, "blocked"));
    [Fact]
    public void InvalidPackageJsonIsRejectedWithoutReplacingExistingConfiguration()
    {
        files.Write(root, "package.json", "{\"type\":\"module\"}");
        Assert.Throws<ExecutionException>(() => files.Write(root, "package.json", "{\"type\":\"module\"}\n// explanatory text"));
        Assert.Equal("{\"type\":\"module\"}", files.Read(root, "package.json"));
    }
    [Fact]
    public void ReadWriteSearchOnlyUseRegularSourceFiles()
    {
        files.Write(root, "src/app.js", "export const ready = true;");
        Assert.Contains("ready", files.Read(root, "src/app.js")); Assert.Contains("src/app.js", files.List(root));
        Assert.Throws<ExecutionException>(() => files.Delete(root, "src"));
        files.Delete(root, "src/app.js"); Assert.Empty(files.List(root));
    }
}
