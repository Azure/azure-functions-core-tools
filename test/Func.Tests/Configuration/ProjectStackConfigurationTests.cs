// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Tests.Configuration;

public sealed class ProjectStackConfigurationTests : IDisposable
{
    private readonly TempDirectory _directory = new PhysicalFileSystem().CreateTempDirectory();
    private readonly ProjectStackConfiguration _config = new(new PhysicalFileSystem(), new LocalSettingsProvider());
    private readonly IProjectStack _stack = new TestProjectStack("go", "Go", "native", new LanguageDescriptor("go", "Go"));
    private DirectoryInfo Directory => new(_directory.Path);
    public void Dispose() => _directory.Dispose();

    [Theory]
    [InlineData("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":123}}""")]
    [InlineData("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"native","functions_worker_runtime":"different"}}""")]
    [InlineData("invalid json")]
    public void Read_MalformedHostRuntimeDoesNotBecomeAnAbsentSignal(string json)
    {
        File.WriteAllText(Path.Combine(_directory.Path, "local.settings.json"), json);

        FluentActions.Invoking(() => _config.Read(Directory)).Should().Throw<ProjectStackConfigurationException>()
            .WithMessage("*FUNCTIONS_WORKER_RUNTIME in local.settings.json*");
        File.Exists(CliConfigurationPathsOptions.GetProjectConfigPath(Directory)).Should().BeFalse();
    }

    [Fact]
    public async Task Read_AfterCreateReturnsPersistedStackInsteadOfCachedLocalRuntime()
    {
        File.WriteAllText(Path.Combine(_directory.Path, "local.settings.json"), """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"native"}}""");
        _config.Read(Directory).Should().Be(new ProjectStackSettings(WorkerRuntime: "native"));

        await _config.CreateAsync(Directory, _stack, _stack.SupportedLanguages[0], TestContext.Current.CancellationToken);

        _config.Read(Directory).Should().Be(new ProjectStackSettings(HasProjectConfig: true, StackIdentifier: "go"));
    }

    [Fact]
    public async Task Create_DoesNotReplaceAConfigCreatedSinceRead()
    {
        _config.Read(Directory).HasProjectConfig.Should().BeFalse();
        string path = CliConfigurationPathsOptions.GetProjectConfigPath(Directory);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string concurrent = """{"stack":{"runtime":"comet"},"keep":true}""";
        File.WriteAllText(path, concurrent);

        var write = () => _config.CreateAsync(Directory, _stack, null, TestContext.Current.CancellationToken);

        await write.Should().ThrowAsync<ProjectStackConfigurationException>();
        File.ReadAllText(path).Should().Be(concurrent);
        System.IO.Directory.GetFiles(Path.GetDirectoryName(path)!).Should().ContainSingle();
    }

    [Fact]
    public async Task Create_RejectsForeignLanguageEvenWhenSingleLanguageWouldBeOmitted()
    {
        var write = () => _config.CreateAsync(Directory, _stack, new LanguageDescriptor("other", "Other"), TestContext.Current.CancellationToken);

        await write.Should().ThrowAsync<ProjectStackConfigurationException>();
        File.Exists(CliConfigurationPathsOptions.GetProjectConfigPath(Directory)).Should().BeFalse();
    }
}
