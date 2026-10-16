// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NSubstitute;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class DotNetTemplateProjectPreparationTests
{
    private readonly IDotnetCliRunner _runner = Substitute.For<IDotnetCliRunner>();
    private readonly string _project = Path.GetFullPath(Path.Combine("explicit project", "App.fsproj"));

    [Fact]
    public async Task ReadAsync_EvaluatedProject_UsesExactPathAndPreservesPropertiesAndCancellation()
    {
        SetupOutput("""{"Properties":{"TargetFramework":"net10.0","RootNamespace":"Actual.Imported.Namespace"}}""");
        DotNetTemplateProjectPreparation preparation = new(_runner);
        using CancellationTokenSource cancellation = new();

        DotNetTemplateProjectContext result = await preparation.ReadAsync(_project, cancellation.Token);

        result.Should().Be(new DotNetTemplateProjectContext(_project, "net10.0", "Actual.Imported.Namespace"));
        await _runner.Received(1).RunStreamingAsync(Arg.Is<IReadOnlyList<string>>(args =>
            args.SequenceEqual(new[] { "msbuild", _project, "-nologo", "-getProperty:TargetFramework,RootNamespace" })),
            Path.GetDirectoryName(_project), Arg.Any<Action<string>>(), Arg.Is<Action<string>?>(callback => callback == null), cancellation.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"Properties":{"TargetFramework":10,"RootNamespace":"Example"}}""")]
    [InlineData("""{"Properties":{"TargetFramework":"net10.0"}}""")]
    [InlineData("""{"Properties":{"TargetFramework":"net10.0","RootNamespace":""}}""")]
    [InlineData("""{"Properties":{"TargetFramework":"net8.0","RootNamespace":"Example"}}""")]
    [InlineData("""{"Properties":{"TargetFramework":"net48","RootNamespace":"Example"}}""")]
    [InlineData("""{"Properties":{"TargetFramework":"","RootNamespace":"Example"}}""")]
    public async Task ReadAsync_MalformedOrUnsupportedProject_RefusesFallback(string output)
    {
        SetupOutput(output);
        DotNetTemplateProjectPreparation preparation = new(_runner);
        Func<Task> action = () => preparation.ReadAsync(_project, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReadAsync_EvaluationFails_PreservesFailure()
    {
        DotnetCliException failure = new(1, "invalid project", "", "msbuild");
        _runner.RunStreamingAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<Action<string>>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure));
        DotNetTemplateProjectPreparation preparation = new(_runner);
        Func<Task> action = () => preparation.ReadAsync(_project, CancellationToken.None);

        (await action.Should().ThrowAsync<DotnetCliException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public async Task ReadAsync_PreCanceled_RefusesBeforeRunner()
    {
        DotNetTemplateProjectPreparation preparation = new(_runner);
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        Func<Task> action = () => preparation.ReadAsync(_project, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ReadAsync_RelativeTarget_RefusesBeforeRunner()
    {
        DotNetTemplateProjectPreparation preparation = new(_runner);
        Func<Task> action = () => preparation.ReadAsync("App.csproj", CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullRunner_Throws()
    {
        Action action = () => new DotNetTemplateProjectPreparation(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("dotnetCli");
    }

    private void SetupOutput(string output)
    {
        _runner.RunStreamingAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>(), Arg.Any<Action<string>>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.ArgAt<Action<string>>(2)(output);
                return Task.CompletedTask;
            });
    }
}
