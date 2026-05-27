// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public class DotNetProjectFactoryTests : IDisposable
{
    private readonly DirectoryInfo _projectDir;
    private readonly IDotnetCliRunner _dotnetCli = Substitute.For<IDotnetCliRunner>();

    public DotNetProjectFactoryTests()
    {
        _projectDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "func-dotnet-resolver-" + Guid.NewGuid().ToString("N")));
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
        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("no .NET project file or build output found", notCreated.Reason);
    }

    [Theory]
    [InlineData("MyApp.csproj")]
    [InlineData("MyApp.fsproj")]
    public async Task Single_project_file_creates_source_project(string fileName)
    {
        WriteFile(fileName, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        DotNetSourceProject sourceProject = Assert.IsType<DotNetSourceProject>(created.Project);
        Assert.Equal($"found {fileName}", created.Reason);
        Assert.Equal("dotnet", sourceProject.StackName);
        Assert.Equal(".NET", sourceProject.StackDisplayName);
        Assert.False(sourceProject.SupportsExtensionBundles);
        Assert.Equal(Path.Combine(_projectDir.FullName, fileName), sourceProject.ProjectFilePath);
    }

    [Theory]
    [InlineData("extensions.csproj")]
    [InlineData("extensions.fsproj")]
    public async Task Extensions_project_file_still_matches(string fileName)
    {
        WriteFile(fileName, "<Project></Project>");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.Equal($"found {fileName}", created.Reason);
    }

    [Fact]
    public async Task Multiple_project_files_does_not_match()
    {
        WriteFile("App1.csproj", "<Project></Project>");
        WriteFile("App2.csproj", "<Project></Project>");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("multiple .NET project files found; cannot determine which to use", notCreated.Reason);
    }

    [Fact]
    public async Task Nonexistent_directory_does_not_match()
    {
        var nonexistent = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "func-dotnet-missing-" + Guid.NewGuid().ToString("N")));
        var context = new ProjectCreationContext(WorkingDirectory.FromExplicit(nonexistent.FullName));

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(context, default);

        ProjectCreationResult.NotCreated notCreated = Assert.IsType<ProjectCreationResult.NotCreated>(result);
        Assert.Equal("directory does not exist", notCreated.Reason);
    }

    [Fact]
    public async Task NullContext_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(null!, default));
    }

    [Fact]
    public async Task Non_dotnet_project_files_are_ignored()
    {
        // .vbproj should not be matched as source or output
        WriteFile("MyApp.vbproj", "<Project></Project>");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Output_directory_creates_output_project()
    {
        WriteFile("host.json", "{}");
        WriteFile("worker.config.json", "{}");
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, ".azurefunctions"));

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.IsType<DotNetOutputProject>(created.Project);
        Assert.Equal("found .NET build output (host.json, worker.config.json, .azurefunctions)", created.Reason);
    }

    [Fact]
    public async Task Output_directory_missing_azurefunctions_does_not_match()
    {
        WriteFile("host.json", "{}");
        WriteFile("worker.config.json", "{}");

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Output_directory_missing_worker_config_does_not_match()
    {
        WriteFile("host.json", "{}");
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, ".azurefunctions"));

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        Assert.IsType<ProjectCreationResult.NotCreated>(result);
    }

    [Fact]
    public async Task Source_project_takes_priority_over_output_signals()
    {
        // Directory has both a project file and output artifacts
        WriteFile("MyApp.csproj", "<Project></Project>");
        WriteFile("host.json", "{}");
        WriteFile("worker.config.json", "{}");
        Directory.CreateDirectory(Path.Combine(_projectDir.FullName, ".azurefunctions"));

        ProjectCreationResult result = await new DotNetProjectFactory(_dotnetCli).TryCreateProjectAsync(CreateContext(), default);

        ProjectCreationResult.Created created = Assert.IsType<ProjectCreationResult.Created>(result);
        Assert.IsType<DotNetSourceProject>(created.Project);
    }

    private void WriteFile(string name, string contents)
        => File.WriteAllText(Path.Combine(_projectDir.FullName, name), contents);

    private ProjectCreationContext CreateContext()
        => new(WorkingDirectory.FromExplicit(_projectDir.FullName));
}
