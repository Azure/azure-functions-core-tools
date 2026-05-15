// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Workload.Python.Tests;

public class PythonProjectResolverTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public PythonProjectResolverTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-python-resolver-" + Guid.NewGuid().ToString("N")));
    }

    public void Dispose()
    {
        try
        {
            if (_projectDir.Exists)
            {
                _projectDir.Delete(recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; CI runners may hold file handles briefly.
        }
    }

    [Fact]
    public async Task Empty_directory_does_not_match()
    {
        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
        Assert.Equal("no host.json", result.Reason);
    }

    [Fact]
    public async Task NonExistent_directory_does_not_match()
    {
        var missing = new DirectoryInfo(Path.Combine(_projectDir.FullName, "does-not-exist"));

        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(missing, default);

        Assert.False(result.IsMatch);
    }

    [Theory]
    [InlineData("requirements.txt")]
    [InlineData("pyproject.toml")]
    [InlineData("function_app.py")]
    [InlineData("uv.lock")]
    [InlineData("poetry.lock")]
    public async Task Fingerprint_without_host_json_does_not_match(string fileName)
    {
        WriteFile(fileName, string.Empty);

        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Foreign_stack_directory_does_not_match()
    {
        // host.json + Node fingerprint should not be claimed by the Python resolver.
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{}");

        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Foreign_go_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("go.mod", "module example");

        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Theory]
    [InlineData("function_app.py")]
    [InlineData("requirements.txt")]
    [InlineData("pyproject.toml")]
    [InlineData("uv.lock")]
    [InlineData("poetry.lock")]
    public async Task Match_for_each_fingerprint(string fileName)
    {
        WriteFile("host.json", "{}");
        WriteFile(fileName, string.Empty);

        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("python", result.WorkerRuntime);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task Uv_managed_project_matches_via_pyproject_and_lock()
    {
        // Reproduces the uv layout from issue #4676 / #4705: no requirements.txt,
        // pyproject.toml + uv.lock instead.
        WriteFile("host.json", "{}");
        WriteFile("pyproject.toml", "[project]\nname = \"x\"\n");
        WriteFile("uv.lock", string.Empty);

        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("python", result.WorkerRuntime);
    }

    [Fact]
    public async Task Poetry_managed_project_matches_via_pyproject_and_lock()
    {
        WriteFile("host.json", "{}");
        WriteFile("pyproject.toml", "[tool.poetry]\nname = \"x\"\n");
        WriteFile("poetry.lock", string.Empty);

        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("python", result.WorkerRuntime);
    }

    [Fact]
    public async Task Bare_py_file_with_host_json_matches()
    {
        WriteFile("host.json", "{}");
        WriteFile("loose.py", "print('hi')");

        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("python", result.WorkerRuntime);
        Assert.Contains("*.py", result.Reason);
    }

    [Fact]
    public async Task Host_json_only_does_not_match()
    {
        WriteFile("host.json", "{}");

        EvaluationResult result = await new PythonProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task NullDirectory_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new PythonProjectResolver().EvaluateAsync(null!, default));
    }

    private void WriteFile(string name, string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, name), contents);
}
