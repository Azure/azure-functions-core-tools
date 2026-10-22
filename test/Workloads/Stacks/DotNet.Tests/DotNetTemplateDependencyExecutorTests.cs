// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class DotNetTemplateDependencyExecutorTests
{
    private readonly IDotnetCliRunner _runner = Substitute.For<IDotnetCliRunner>();
    private readonly string _project = Path.GetFullPath(Path.Combine("explicit project", "App.csproj"));

    [Fact]
    public async Task AddPackageReferenceAsync_ExactNativeReference_PreservesImplicitRestoreAndCancellation()
    {
        DotNetTemplateDependencyExecutor executor = new(_runner);
        DotNetTemplatePackageReference reference = new("Microsoft.Azure.Functions.Worker.Extensions.Kusto", "1.0.13-preview", ".csproj");
        using CancellationTokenSource cancellation = new();

        await executor.AddPackageReferenceAsync(_project, reference, cancellation.Token);

        await _runner.Received(1).RunAsync(Arg.Is<IReadOnlyList<string>>(args =>
            args.SequenceEqual(new[] { "add", _project, "package", reference.PackageId, "--version", "1.0.13-preview" })),
            Path.GetDirectoryName(_project), cancellation.Token);
    }

    [Theory]
    [InlineData(".csproj")]
    [InlineData(".fsproj")]
    public async Task RestoreAsync_ExplicitProject_UsesExactTarget(string extension)
    {
        DotNetTemplateDependencyExecutor executor = new(_runner);
        string project = Path.ChangeExtension(_project, extension);
        using CancellationTokenSource cancellation = new();

        await executor.RestoreAsync(project, cancellation.Token);

        await _runner.Received(1).RunAsync(Arg.Is<IReadOnlyList<string>>(args =>
            args.SequenceEqual(new[] { "restore", project })), Path.GetDirectoryName(project), cancellation.Token);
    }

    [Fact]
    public void Constructor_NullRunner_Throws()
    {
        Action action = () => new DotNetTemplateDependencyExecutor(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("dotnetCli");
    }

    [Theory]
    [InlineData(null, "1.0", ".csproj")]
    [InlineData("", "1.0", ".csproj")]
    [InlineData("--source", "1.0", ".csproj")]
    [InlineData("Package;Command", "1.0", ".csproj")]
    [InlineData("Package", null, ".csproj")]
    [InlineData("Package", "[1.0,2.0)", ".csproj")]
    [InlineData("Package", "--interactive", ".csproj")]
    [InlineData("Package", "1.0", null)]
    [InlineData("Package", "1.0", ".vbproj")]
    [InlineData("Package", "1.0", ".csproj;.fsproj")]
    public void PackageReference_InvalidNativeArguments_Throws(string? id, string? version, string? extension)
    {
        Action action = () => new DotNetTemplatePackageReference(id!, version!, extension!);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("App.csproj")]
    [InlineData("script.cmd")]
    public async Task DependencyActions_InvalidTarget_RefuseBeforeCallingRunner(string? path)
    {
        DotNetTemplateDependencyExecutor executor = new(_runner);
        DotNetTemplatePackageReference reference = new("Package", "1.0.0", ".csproj");
        Func<Task> add = () => executor.AddPackageReferenceAsync(path!, reference, CancellationToken.None);
        Func<Task> restore = () => executor.RestoreAsync(path!, CancellationToken.None);

        await add.Should().ThrowAsync<ArgumentException>();
        await restore.Should().ThrowAsync<ArgumentException>();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task AddPackageReferenceAsync_MismatchedProjectExtension_RefusesBeforeCallingRunner()
    {
        DotNetTemplateDependencyExecutor executor = new(_runner);
        DotNetTemplatePackageReference reference = new("Package", "1.0.0", ".fsproj");
        Func<Task> action = () => executor.AddPackageReferenceAsync(_project, reference, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("projectFilePath");
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task AddPackageReferenceAsync_NullReference_Throws()
    {
        DotNetTemplateDependencyExecutor executor = new(_runner);
        Func<Task> action = () => executor.AddPackageReferenceAsync(_project, null!, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentNullException>().WithParameterName("reference");
    }

    [Fact]
    public async Task DependencyActions_PreCanceled_RefuseBeforeCallingRunner()
    {
        DotNetTemplateDependencyExecutor executor = new(_runner);
        DotNetTemplatePackageReference reference = new("Package", "1.0.0", ".csproj");
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        Func<Task> add = () => executor.AddPackageReferenceAsync(_project, reference, cancellation.Token);
        Func<Task> restore = () => executor.RestoreAsync(_project, cancellation.Token);

        await add.Should().ThrowAsync<OperationCanceledException>();
        await restore.Should().ThrowAsync<OperationCanceledException>();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyActions_ProcessFailure_RemainsVisible(bool restore)
    {
        DotNetTemplateDependencyExecutor executor = new(_runner);
        DotnetCliException failure = new(1, "restore refused", "", "dotnet");
        _runner.RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        Func<Task> action = () => restore ? executor.RestoreAsync(_project, CancellationToken.None)
            : executor.AddPackageReferenceAsync(_project, new("Package", "1.0.0", ".csproj"), CancellationToken.None);

        (await action.Should().ThrowAsync<DotnetCliException>()).Which.Should().BeSameAs(failure);
    }
}
