// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public class NodeProjectFactoryTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;
    private readonly IFunctionsWorkerResolver _workerResolver = Substitute.For<IFunctionsWorkerResolver>();

    public NodeProjectFactoryTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-node-resolver-" + Guid.NewGuid().ToString("N")));
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.Resolved(CreateWorker("node", "node")));
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
        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("no Node project fingerprint found", notCreated.Reason);
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("tsconfig.json")]
    [InlineData("index.js")]
    [InlineData("index.ts")]
    public async Task Fingerprint_without_host_json_creates_project(string fileName)
    {
        WriteFile(fileName, "{}");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.Created>(result);
    }

    [Fact]
    public async Task Foreign_python_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("requirements.txt", string.Empty);

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Foreign_go_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("go.mod", "module example");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
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

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("node", created.Project.StackName);
        Assert.Equal("Node.js", created.Project.StackDisplayName);
        Assert.Equal("node", created.Project.Worker.WorkerRuntime);
        Assert.NotNull(created.Reason);
    }

    [Fact]
    public async Task PackageJson_with_azure_functions_dep_uses_specific_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", """{ "dependencies": { "@azure/functions": "^4.0.0" } }""");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("package.json declares @azure/functions", created.Reason);
    }

    [Fact]
    public async Task PackageJson_with_azure_functions_dev_dep_uses_specific_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", """{ "devDependencies": { "@azure/functions": "^4.0.0" } }""");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("package.json declares @azure/functions", created.Reason);
    }

    [Fact]
    public async Task PackageJson_without_azure_functions_dep_uses_generic_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", """{ "dependencies": { "lodash": "^4.0.0" } }""");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("found package.json", created.Reason);
    }

    [Fact]
    public async Task Malformed_package_json_falls_back_to_generic_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{ this is not valid json");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal("found package.json", created.Reason);
    }

    [Fact]
    public async Task Host_json_only_does_not_match()
    {
        WriteFile("host.json", "{}");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task MatchingDirectory_WhenWorkerNotResolved_Fails()
    {
        WriteFile("package.json", "{}");
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(
            new FunctionsWorkerId("node"),
            "missing node worker");
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.NotResolved(failure));

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Failed failed = Assert.IsType<ProjectCreationResult.Failed>(result);
        ProjectCreationFailure.WorkerNotResolved workerFailure =
            Assert.IsType<ProjectCreationFailure.WorkerNotResolved>(failed.Failure);
        Assert.Same(failure, workerFailure.WorkerFailure);
    }

    [Fact]
    public async Task NullContext_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new NodeProjectFactory().TryCreateProjectAsync(null!, default));
    }

    private void WriteFile(string name, string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, name), contents);

    private ProjectCreationContext CreateContext(DirectoryInfo? directory = null)
        => new(WorkingDirectory.FromExplicit((directory ?? _projectDir).FullName), _workerResolver);

    private static IFunctionsWorker CreateWorker(string workerId, string workerRuntime)
        => new TestFunctionsWorker(new FunctionsWorkerId(workerId), workerRuntime, "worker.config.json", "1.0.0");

    private sealed record TestFunctionsWorker(
        FunctionsWorkerId Id,
        string WorkerRuntime,
        string WorkerConfigPath,
        string Version) : IFunctionsWorker;
}
