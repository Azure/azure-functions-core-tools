// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Xunit;

namespace Azure.Functions.Cli.Workload.Node.Tests;

public class NodeProjectResolverTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;

    public NodeProjectResolverTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-node-resolver-" + Guid.NewGuid().ToString("N")));
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
        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
        Assert.Equal("no host.json", result.Reason);
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("tsconfig.json")]
    [InlineData("index.js")]
    [InlineData("index.ts")]
    public async Task Fingerprint_without_host_json_does_not_match(string fileName)
    {
        WriteFile(fileName, "{}");

        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Foreign_python_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("requirements.txt", string.Empty);

        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Foreign_go_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("go.mod", "module example");

        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Theory]
    [InlineData("package.json", "{}")]
    [InlineData("tsconfig.json", "{}")]
    [InlineData("index.js", "module.exports = {};")]
    [InlineData("index.mjs", "export default {};")]
    [InlineData("index.cjs", "module.exports = {};")]
    [InlineData("index.ts", "export const x = 1;")]
    public async Task Match_for_each_fingerprint(string fileName, string contents)
    {
        WriteFile("host.json", "{}");
        WriteFile(fileName, contents);

        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("node", result.WorkerRuntime);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task PackageJson_with_azure_functions_dep_uses_specific_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", """{ "dependencies": { "@azure/functions": "^4.0.0" } }""");

        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("node", result.WorkerRuntime);
        Assert.Equal("package.json declares @azure/functions", result.Reason);
    }

    [Fact]
    public async Task PackageJson_with_azure_functions_dev_dep_uses_specific_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", """{ "devDependencies": { "@azure/functions": "^4.0.0" } }""");

        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("package.json declares @azure/functions", result.Reason);
    }

    [Fact]
    public async Task PackageJson_without_azure_functions_dep_uses_generic_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", """{ "dependencies": { "lodash": "^4.0.0" } }""");

        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("found package.json", result.Reason);
    }

    [Fact]
    public async Task Malformed_package_json_falls_back_to_generic_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{ this is not valid json");

        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.True(result.IsMatch);
        Assert.Equal("found package.json", result.Reason);
    }

    [Fact]
    public async Task Host_json_only_does_not_match()
    {
        WriteFile("host.json", "{}");

        EvaluationResult result = await new NodeProjectResolver().EvaluateAsync(_projectDir, default);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task NullDirectory_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new NodeProjectResolver().EvaluateAsync(null!, default));
    }

    private void WriteFile(string name, string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, name), contents);
}
