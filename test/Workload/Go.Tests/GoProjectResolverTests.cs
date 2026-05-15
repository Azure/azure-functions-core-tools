// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Workload.Go.Tests;

public class GoProjectResolverTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public GoProjectResolverTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-go-resolver-" + Guid.NewGuid().ToString("N")));
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
        }
    }

    [Fact]
    public async Task Empty_directory_does_not_match()
    {
        EvaluationResult result = await new GoProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
        Assert.Equal("no host.json", result.Reason);
    }

    [Theory]
    [InlineData("go.mod", "module example")]
    [InlineData("main.go", "package main")]
    public async Task Fingerprint_without_host_json_does_not_match(string fileName, string contents)
    {
        WriteFile(fileName, contents);

        EvaluationResult result = await new GoProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Foreign_python_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("requirements.txt", string.Empty);

        EvaluationResult result = await new GoProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Foreign_node_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{}");

        EvaluationResult result = await new GoProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Theory]
    [InlineData("go.mod", "module example")]
    [InlineData("main.go", "package main")]
    public async Task Match_for_each_fingerprint(string fileName, string contents)
    {
        WriteFile("host.json", "{}");
        WriteFile(fileName, contents);

        EvaluationResult result = await new GoProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("go", result.WorkerRuntime);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task GoMod_takes_precedence_over_loose_go_files()
    {
        WriteFile("host.json", "{}");
        WriteFile("go.mod", "module example");
        WriteFile("main.go", "package main");

        EvaluationResult result = await new GoProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("found go.mod", result.Reason);
    }

    [Fact]
    public async Task Host_json_only_does_not_match()
    {
        WriteFile("host.json", "{}");

        EvaluationResult result = await new GoProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task NullDirectory_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new GoProjectResolver().EvaluateAsync(null!, default));
    }

    private void WriteFile(string name, string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, name), contents);
}
