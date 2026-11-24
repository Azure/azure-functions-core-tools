// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.PostActions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class DotNetRestorePostActionProcessorTests
{
    private readonly IDotnetCliRunner _runner = Substitute.For<IDotnetCliRunner>();
    private readonly IFunctionsReporter _reporter = Substitute.For<IFunctionsReporter>();
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), "func restore tests");

    [Theory]
    [InlineData("dotnet", "C#", true)]
    [InlineData("DOTNET", "csharp", true)]
    [InlineData("dotnet", "F#", true)]
    [InlineData("dotnet", "FSHARP", true)]
    [InlineData(" dotnet ", " C# ", true)]
    [InlineData("dotnet-isolated", "C#", false)]
    [InlineData("node", "C#", false)]
    [InlineData("", "C#", false)]
    [InlineData("dotnet", "", false)]
    [InlineData("dotnet", "Visual Basic", false)]
    public void CanProcess_TemplateTags_SelectsDotNetLanguages(string stack, string language, bool expected)
    {
        CreateProcessor().CanProcess(new(stack, language)).Should().Be(expected);
    }

    [Fact]
    public async Task ProcessAsync_MixedPrimaryOutputs_RestoresEveryOutputInOrderAndStreamsOutput()
    {
        string csharp = Path.Combine("nested folder", "My App.csproj");
        string fsharp = Path.Combine("nested folder", "Library.FSPROJ");
        string[] outputs = ["Program.cs", csharp, Path.Combine("nested folder", ".", "My App.csproj"), fsharp, "host.json"];
        using CancellationTokenSource cancellation = new();
        List<string> restored = [];
        _runner.RunStreamingAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                IReadOnlyList<string> arguments = call.Arg<IReadOnlyList<string>>();
                arguments[0].Should().Be("restore");
                arguments.Should().HaveCount(4);
                arguments[2].Should().Be("-tlp:verbosity=quiet");
                arguments[3].Should().Be("--no-logo");
                restored.Add(arguments[1]);
                call.ArgAt<string>(1).Should().Be(_outputDirectory);
                call.Arg<CancellationToken>().Should().Be(cancellation.Token);
                call.ArgAt<Action<string>>(2)("Restored [project].");
                call.ArgAt<Action<string>>(3)("NU1301: source unavailable");
                return Task.CompletedTask;
            });

        bool result = await CreateProcessor().ProcessAsync(Context(outputs), cancellation.Token);

        result.Should().BeTrue();
        restored.Should().Equal(
            Path.Combine(_outputDirectory, "Program.cs"),
            Path.Combine(_outputDirectory, csharp),
            Path.Combine(_outputDirectory, csharp),
            Path.Combine(_outputDirectory, fsharp),
            Path.Combine(_outputDirectory, "host.json"));
        _reporter.Received(5).WriteLog("Restored [project].", FunctionsReportSeverity.Info);
        _reporter.Received(5).WriteLog("NU1301: source unavailable", FunctionsReportSeverity.Error);
        _reporter.Received(5).ReportStatus(Arg.Is<string>(message => message.Contains("Restoring dependencies")));
    }

    [Theory]
    [InlineData("*.csproj;*.fsproj")]
    [InlineData("\"*.csproj;*.fsproj\"")]
    [InlineData("[\"*.csproj\", null, 123, false, \"*.fsproj\"]")]
    [InlineData("[\"*.csproj\", /* comment */ \"*.fsproj\",]")]
    public async Task ProcessAsync_FilesArgument_RestoresMatchesInsteadOfPrimaryOutputs(string files)
    {
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths("*.csproj").Returns(["renamed.csproj", Path.Combine("nested", "other.csproj")]);
        matcher.MatchTargetPaths("*.fsproj").Returns(["renamed.fsproj"]);
        List<string> restored = RecordTargets();
        PostActionContext context = Context(["fallback.csproj"], new Dictionary<string, string> { ["files"] = files }, matcher);

        bool result = await CreateProcessor().ProcessAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        restored.Should().Equal(
            Path.Combine(_outputDirectory, "renamed.csproj"),
            Path.Combine(_outputDirectory, "nested", "other.csproj"),
            Path.Combine(_outputDirectory, "renamed.fsproj"));
        Received.InOrder(() =>
        {
            matcher.MatchTargetPaths("*.csproj");
            matcher.MatchTargetPaths("*.fsproj");
        });
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("missing.csproj", "missing.csproj")]
    [InlineData("[]", "[]")]
    [InlineData("{\"other\":true}", "{\"other\":true}")]
    [InlineData("[\"invalid\"", "[\"invalid\"")]
    public async Task ProcessAsync_NoConfiguredMatches_FallsBackToPrimaryOutputs(string files, string? expectedPattern)
    {
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths(Arg.Any<string>()).Returns([]);
        List<string> restored = RecordTargets();
        PostActionContext context = Context(["fallback.csproj"], new Dictionary<string, string> { ["files"] = files }, matcher);

        bool result = await CreateProcessor().ProcessAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        restored.Should().Equal(Path.Combine(_outputDirectory, "fallback.csproj"));
        if (expectedPattern is null)
        {
            matcher.DidNotReceiveWithAnyArgs().MatchTargetPaths(default!);
        }
        else
        {
            matcher.Received(1).MatchTargetPaths(expectedPattern);
        }
    }

    [Fact]
    public async Task ProcessAsync_JsonArray_DoesNotSplitElementsAndPreservesDuplicates()
    {
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths("*.csproj;*.fsproj").Returns(["shared.csproj"]);
        List<string> restored = RecordTargets();
        PostActionContext context = Context(
            ["fallback.csproj"],
            new Dictionary<string, string> { ["files"] = "[\"*.csproj;*.fsproj\", \"*.csproj;*.fsproj\"]" },
            matcher);

        bool result = await CreateProcessor().ProcessAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        restored.Should().Equal(
            Path.Combine(_outputDirectory, "shared.csproj"),
            Path.Combine(_outputDirectory, "shared.csproj"));
        matcher.Received(2).MatchTargetPaths("*.csproj;*.fsproj");
    }

    [Fact]
    public async Task ProcessAsync_SemicolonPatterns_PreserveWhitespaceAndSkipEmptyEntries()
    {
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths("*.csproj").Returns(["first.csproj"]);
        matcher.MatchTargetPaths(" *.fsproj").Returns(["second.fsproj"]);
        List<string> restored = RecordTargets();
        PostActionContext context = Context(
            [],
            new Dictionary<string, string> { ["files"] = "*.csproj;; *.fsproj;" },
            matcher);

        bool result = await CreateProcessor().ProcessAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        restored.Should().Equal(
            Path.Combine(_outputDirectory, "first.csproj"),
            Path.Combine(_outputDirectory, "second.fsproj"));
        Received.InOrder(() =>
        {
            matcher.MatchTargetPaths("*.csproj");
            matcher.MatchTargetPaths(" *.fsproj");
        });
    }

    [Fact]
    public async Task ProcessAsync_SourcePathsUnavailable_FallsBackToPrimaryOutputs()
    {
        List<string> restored = RecordTargets();
        PostActionContext context = Context(["fallback.csproj"], new Dictionary<string, string> { ["files"] = "*.csproj" });

        bool result = await CreateProcessor().ProcessAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        restored.Should().Equal(Path.Combine(_outputDirectory, "fallback.csproj"));
    }

    [Fact]
    public async Task ProcessAsync_NoTargets_SucceedsWithoutLaunching()
    {
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths("missing.csproj").Returns([]);
        PostActionContext context = Context([], new Dictionary<string, string> { ["files"] = "missing.csproj" }, matcher);

        bool result = await CreateProcessor().ProcessAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        _runner.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_AbsoluteAndParentPrimaryOutputs_ResolvesLikeDotnetNew()
    {
        string parent = Path.Combine("..", "Other.csproj");
        string rooted = Path.Combine(Path.GetTempPath(), "Rooted.csproj");
        List<string> restored = RecordTargets();

        bool result = await CreateProcessor().ProcessAsync(Context([parent, rooted]), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        restored.Should().Equal(Path.GetFullPath(parent, _outputDirectory), Path.GetFullPath(rooted, _outputDirectory));
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("start")]
    [InlineData("os")]
    public async Task ProcessAsync_ExpectedProcessFailure_ReportsFailureAndAttemptsRemainingTargets(string failure)
    {
        Exception exception = failure switch
        {
            "exit" => new DotnetCliException(1, "Restore failed", "", "restore"),
            "start" => new InvalidOperationException("Failed to start dotnet process."),
            _ => new Win32Exception(2),
        };
        int attempts = 0;
        _runner.RunStreamingAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns(_ =>
                ++attempts == 1 ? Task.FromException(exception) : Task.CompletedTask);

        bool result = await CreateProcessor().ProcessAsync(Context(["App.csproj", "Other.fsproj"]), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _runner.ReceivedCalls().Should().HaveCount(2);
        _reporter.Received().WriteLog(Arg.Is<string>(message => message.Contains(exception.Message)), FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_CancellationDuringRestore_PropagatesWithoutReportingFailure()
    {
        using CancellationTokenSource cancellation = new();
        FailRunner(new OperationCanceledException(cancellation.Token));

        Func<Task> act = () => CreateProcessor().ProcessAsync(Context(["App.csproj", "Other.fsproj"]), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _runner.ReceivedCalls().Should().ContainSingle();
        _reporter.DidNotReceive().WriteLog(Arg.Any<string>(), FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_AlreadyCancelled_DoesNotLaunch()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> act = () => CreateProcessor().ProcessAsync(Context(["App.csproj"]), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _runner.ReceivedCalls().Should().BeEmpty();
        _reporter.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedFailure_Propagates()
    {
        var exception = new IOException("Unexpected runner failure");
        FailRunner(exception);

        Func<Task> act = () => CreateProcessor().ProcessAsync(Context(["App.csproj"]), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        FluentActions.Invoking(() => new DotNetRestorePostActionProcessor(null!, new()))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new DotNetRestorePostActionProcessor(_runner, null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => CreateProcessor().CanProcess(null!))
            .Should().Throw<ArgumentNullException>();
        await FluentActions.Awaiting(() => CreateProcessor().ProcessAsync(null!, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    private DotNetRestorePostActionProcessor CreateProcessor() => new(_runner, new());

    private PostActionContext Context(
        IReadOnlyList<string> outputs,
        IReadOnlyDictionary<string, string>? arguments = null,
        IPostActionFileMatcher? matcher = null)
        => new(CreateProcessor().Id, arguments ?? new Dictionary<string, string>(), null, null, false, _outputDirectory, outputs, [],
            _reporter, matcher);

    private List<string> RecordTargets()
    {
        List<string> restored = [];
        _runner.RunStreamingAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                restored.Add(call.Arg<IReadOnlyList<string>>()[1]);
                return Task.CompletedTask;
            });
        return restored;
    }

    private void FailRunner(Exception exception)
        => _runner.RunStreamingAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).ThrowsAsync(exception);
}
