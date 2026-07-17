// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;
using NSubstitute;

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

        result.Should().BeOfType<ProjectCreationResult.NotCreated>()
            .Which.Reason.Should().Be("no Node project fingerprint found");
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

        result.Should().BeOfType<ProjectCreationResult.Created>();
    }

    [Fact]
    public async Task Foreign_python_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("requirements.txt", string.Empty);

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
    }

    [Fact]
    public async Task Foreign_go_directory_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("go.mod", "module example");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
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

        ProjectCreationResult.Created created = result.Should().BeOfType<ProjectCreationResult.Created>().Subject;
        created.Project.StackName.Should().Be("node");
        created.Project.StackDisplayName.Should().Be("Node.js");
        IFunctionsWorker worker = await ResolveWorkerAsync(created.Project);
        worker.WorkerRuntime.Should().Be("node");
        created.Reason.Should().NotBeNull();
    }

    [Theory]
    [InlineData("tsconfig.json", "TypeScript")]
    [InlineData("index.ts", "TypeScript")]
    [InlineData("package.json", "JavaScript")]
    [InlineData("index.js", "JavaScript")]
    [InlineData("index.mjs", "JavaScript")]
    [InlineData("index.cjs", "JavaScript")]
    public async Task Classifies_language_from_fingerprint(string fileName, string expectedLanguage)
    {
        WriteFile("host.json", "{}");
        WriteFile(fileName, "{}");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>().Which.Project.Language.Should().Be(expectedLanguage);
    }

    [Fact]
    public async Task Classifies_typescript_when_tsconfig_and_package_json_both_present()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{}");
        WriteFile("tsconfig.json", "{}");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>().Which.Project.Language.Should().Be("TypeScript");
    }

    [Fact]
    public async Task PackageJson_with_azure_functions_dep_uses_specific_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", """{ "dependencies": { "@azure/functions": "^4.0.0" } }""");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>()
            .Which.Reason.Should().Be("package.json declares @azure/functions");
    }

    [Fact]
    public async Task PackageJson_with_azure_functions_dev_dep_uses_specific_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", """{ "devDependencies": { "@azure/functions": "^4.0.0" } }""");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>()
            .Which.Reason.Should().Be("package.json declares @azure/functions");
    }

    [Fact]
    public async Task PackageJson_without_azure_functions_dep_uses_generic_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", """{ "dependencies": { "lodash": "^4.0.0" } }""");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>().Which.Reason.Should().Be("found package.json");
    }

    [Fact]
    public async Task Malformed_package_json_falls_back_to_generic_reason()
    {
        WriteFile("host.json", "{}");
        WriteFile("package.json", "{ this is not valid json");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.Created>().Which.Reason.Should().Be("found package.json");
    }

    [Fact]
    public async Task Host_json_only_does_not_match()
    {
        WriteFile("host.json", "{}");

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        result.Should().BeOfType<ProjectCreationResult.NotCreated>();
    }

    [Fact]
    public async Task MatchingDirectory_WhenWorkerNotResolved_WorkerReferenceReportsFailure()
    {
        WriteFile("package.json", "{}");
        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(
            new FunctionsWorkerId("node"),
            "missing node worker");
        _workerResolver.ResolveWorkerAsync(Arg.Any<FunctionsWorkerId>(), Arg.Any<CancellationToken>())
            .Returns(FunctionsWorkerResolutionResults.NotResolved(failure));

        ProjectCreationResult result = await new NodeProjectFactory().TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = result.Should().BeOfType<ProjectCreationResult.Created>().Subject;
        FunctionsWorkerResolutionResult workerResult = await created.Project.WorkerReference.ResolveWorkerAsync(
            new FunctionsWorkerResolutionContext(_workerResolver),
            default);
        workerResult.Should().BeOfType<FunctionsWorkerResolutionResult.NotResolved>()
            .Which.Failure.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task NullContext_throws()
    {
        await FluentActions.Awaiting(() => new NodeProjectFactory().TryCreateProjectAsync(null!, default)).Should().ThrowAsync<ArgumentNullException>();
    }

    private void WriteFile(string name, string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, name), contents);

    private ProjectCreationContext CreateContext(DirectoryInfo? directory = null)
        => new(WorkingDirectory.FromExplicit((directory ?? _projectDir).FullName));

    private async Task<IFunctionsWorker> ResolveWorkerAsync(FunctionsProject project)
    {
        FunctionsWorkerResolutionResult result = await project.WorkerReference.ResolveWorkerAsync(
            new FunctionsWorkerResolutionContext(_workerResolver),
            default);
        FunctionsWorkerResolutionResult.Resolved resolved = result.Should().BeOfType<FunctionsWorkerResolutionResult.Resolved>().Subject;
        return resolved.Worker;
    }

    private static IFunctionsWorker CreateWorker(string workerId, string workerRuntime)
        => new TestFunctionsWorker(new FunctionsWorkerId(workerId), workerRuntime, "worker.config.json", "1.0.0");

    private sealed record TestFunctionsWorker(
        FunctionsWorkerId Id,
        string WorkerRuntime,
        string WorkerConfigPath,
        string Version) : IFunctionsWorker;
}
