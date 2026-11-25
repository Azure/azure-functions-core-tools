// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.PostActions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Azure.Functions.Cli.Workloads.DotNet.Tests;

public sealed class DotNetAddReferencePostActionProcessorTests
{
    private readonly IDotnetCliRunner _runner = Substitute.For<IDotnetCliRunner>();
    private readonly IProjectFileLocator _locator = Substitute.For<IProjectFileLocator>();
    private readonly IFunctionsReporter _reporter = Substitute.For<IFunctionsReporter>();
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), "func add tests");

    [Theory]
    [InlineData("dotnet", "C#", true)]
    [InlineData("dotnet", "F#", true)]
    [InlineData("node", "C#", false)]
    public void CanProcess_TemplateTags_SelectsDotNetLanguages(string stack, string language, bool expected)
    {
        CreateProcessor().CanProcess(new(stack, language)).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("4.1.0")]
    public async Task ProcessAsync_PackageReference_RunsDotnetAddPackage(string? version)
    {
        string project = Path.Combine(_outputDirectory, "App.csproj");
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>()).Returns([project]);
        using CancellationTokenSource cancellation = new();
        List<IReadOnlyList<string>> calls = RecordCalls(cancellation.Token);

        bool result = await CreateProcessor().ProcessAsync(Context(Package("Pkg", version)), cancellation.Token);

        result.Should().BeTrue();
        string[] expected = version is null
            ? ["add", project, "package", "Pkg"]
            : ["add", project, "package", "Pkg", "--version", version];
        calls.Should().ContainSingle().Which.Should().Equal(expected);
        _reporter.Received(1).ReportStatus(version is null
            ? "Adding package reference 'Pkg' to 'App.csproj'."
            : $"Adding package reference 'Pkg' version '{version}' to 'App.csproj'.");
        _reporter.Received(1).WriteLog("info line", FunctionsReportSeverity.Info);
        _reporter.Received(1).WriteLog("error line", FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_ProjectReference_RunsDotnetAddReference()
    {
        string project = Path.Combine(_outputDirectory, "App.csproj");
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>()).Returns([project]);
        List<IReadOnlyList<string>> calls = RecordCalls(TestContext.Current.CancellationToken);
        Dictionary<string, string> arguments = new()
        {
            ["reference"] = "Lib/Lib.csproj",
            ["referenceType"] = "project",
        };

        bool result = await CreateProcessor().ProcessAsync(Context(arguments), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        calls.Should().ContainSingle().Which.Should().Equal(
            "add", project, "reference", Path.GetFullPath(Path.Combine(_outputDirectory, "Lib", "Lib.csproj")));
        _reporter.Received(1).ReportStatus("Adding project reference 'Lib/Lib.csproj' to 'App.csproj'.");
    }

    [Theory]
    [InlineData("project", 0)]
    [InlineData("PROJECT", 1)]
    [InlineData("project", 2)]
    public async Task ProcessAsync_ProjectReference_UsesRenameOnlyWhenExactlyOneMatch(
        string referenceType, int matchCount)
    {
        string target = Path.Combine(_outputDirectory, "App.csproj");
        string source = Path.Combine("Template.Lib", "Template.Lib.csproj");
        string renamed = Path.Combine("MyApp.Lib", "MyApp.Lib.csproj");
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths(source).Returns(matchCount switch
        {
            1 => [renamed],
            2 => [renamed, "Other.csproj"],
            _ => [],
        });
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>()).Returns([target]);
        List<IReadOnlyList<string>> calls = RecordCalls(TestContext.Current.CancellationToken);
        Dictionary<string, string> arguments = new() { ["reference"] = source, ["referenceType"] = referenceType };

        bool result = await CreateProcessor().ProcessAsync(
            Context(arguments, matcher), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        calls.Should().ContainSingle().Which.Should().Equal(
            "add", target, "reference", Path.Combine(_outputDirectory, matchCount == 1 ? renamed : source));
    }

    [Fact]
    public async Task ProcessAsync_ProjectReferenceInParent_ResolvesAgainstOutputDirectory()
    {
        string target = Path.Combine(_outputDirectory, "App.csproj");
        string reference = Path.Combine("..", "Shared", "Shared.csproj");
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>()).Returns([target]);
        List<IReadOnlyList<string>> calls = RecordCalls(TestContext.Current.CancellationToken);
        Dictionary<string, string> arguments = new() { ["reference"] = reference, ["referenceType"] = "project" };

        bool result = await CreateProcessor().ProcessAsync(Context(arguments), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        calls.Should().ContainSingle().Which.Should().Equal(
            "add", target, "reference", Path.GetFullPath(reference, _outputDirectory));
    }

    [Theory]
    [InlineData("framework")]
    [InlineData("custom")]
    public async Task ProcessAsync_UnregisteredReferenceType_ReportsUnsupported(string referenceType)
    {
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>())
            .Returns([Path.Combine(_outputDirectory, "App.csproj")]);
        Dictionary<string, string> arguments = new() { ["reference"] = "Value", ["referenceType"] = referenceType };

        bool result = await CreateProcessor().ProcessAsync(Context(arguments), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
        _reporter.Received(1).WriteLog(
            $"Reference type '{referenceType}' is not supported for this template.", FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_ProjectFileExtensions_PassesLiteralSetToLocator()
    {
        IReadOnlySet<string>? extensions = null;
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Do<IReadOnlySet<string>>(set => extensions = set))
            .Returns([Path.Combine(_outputDirectory, "App.csproj")]);
        RecordCalls(TestContext.Current.CancellationToken);

        bool result = await CreateProcessor().ProcessAsync(
            Context(Package("Pkg", null, ("projectFileExtensions", ".csproj; .FSPROJ;"))),
            TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        extensions.Should().HaveCount(2);
        extensions!.Contains(".CSPROJ").Should().BeTrue();
        extensions.Contains(".fsproj").Should().BeTrue();
    }

    [Theory]
    [InlineData("*.csproj")]
    [InlineData("src/.csproj")]
    [InlineData("src\\.csproj")]
    public async Task ProcessAsync_NonLiteralProjectFileExtensions_ReportsMisconfiguration(string extensions)
    {
        bool result = await CreateProcessor().ProcessAsync(
            Context(Package("Pkg", null, ("projectFileExtensions", extensions))),
            TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _locator.ReceivedCalls().Should().BeEmpty();
        _runner.ReceivedCalls().Should().BeEmpty();
        _reporter.Received(1).WriteLog(
            Arg.Is<string>(message => message.Contains("projectFileExtensions")), FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_MultipleProjectsFound_ReportsAmbiguity()
    {
        string first = Path.Combine(_outputDirectory, "A.csproj");
        string second = Path.Combine(_outputDirectory, "B.csproj");
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>()).Returns([first, second]);

        bool result = await CreateProcessor().ProcessAsync(
            Context(Package("Pkg")), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
        _reporter.Received(1).WriteLog(
            Arg.Is<string>(message => message.Contains("targetFiles")
                && message.Contains("\t" + first) && message.Contains("\t" + second)),
            FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_NoProjectsFound_FallsBackToExistingTargetFiles()
    {
        string existing = Path.Combine(_outputDirectory, "Existing.csproj");
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>()).Returns([]);
        _locator.FileExists(existing).Returns(true);
        List<IReadOnlyList<string>> calls = RecordCalls(TestContext.Current.CancellationToken);

        bool result = await CreateProcessor().ProcessAsync(
            Context(Package("Pkg", null, ("targetFiles", "Existing.csproj;Missing.csproj"))),
            TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        calls.Should().ContainSingle().Which[1].Should().Be(existing);
    }

    [Fact]
    public async Task ProcessAsync_NothingFound_ReportsMissingTarget()
    {
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>()).Returns([]);

        bool result = await CreateProcessor().ProcessAsync(
            Context(Package("Pkg")), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _runner.ReceivedCalls().Should().BeEmpty();
        _reporter.Received(1).WriteLog(
            Arg.Is<string>(message => message.Contains("Could not find a target")), FunctionsReportSeverity.Error);
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("resolve")]
    [InlineData("start")]
    [InlineData("os")]
    public async Task ProcessAsync_ExpectedProcessFailure_ReportsAndStops(string failure)
    {
        Exception exception = failure switch
        {
            "exit" => new DotnetCliException(1, "Add failed", "", "add"),
            "resolve" => new GracefulException("Could not locate the dotnet host."),
            "start" => new InvalidOperationException("Failed to start dotnet process."),
            _ => new Win32Exception(2),
        };
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths("*.csproj").Returns(["A.csproj", "B.csproj"]);
        FailRunner(exception);

        bool result = await CreateProcessor().ProcessAsync(
            Context(Package("Pkg", null, ("targetFiles", "*.csproj")), matcher), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _runner.ReceivedCalls().Should().ContainSingle();
        _reporter.Received(1).WriteLog(
            Arg.Is<string>(message => message.Contains(exception.Message)), FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_Cancellation_Propagates()
    {
        using CancellationTokenSource cancellation = new();
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>())
            .Returns([Path.Combine(_outputDirectory, "App.csproj")]);
        FailRunner(new OperationCanceledException(cancellation.Token));

        Func<Task> act = () => CreateProcessor().ProcessAsync(Context(Package("Pkg")), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _reporter.DidNotReceive().WriteLog(Arg.Any<string>(), FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedFailure_Propagates()
    {
        IOException exception = new("Unexpected runner failure");
        _locator.FindProjectFilesAtOrAbove(_outputDirectory, Arg.Any<IReadOnlySet<string>>())
            .Returns([Path.Combine(_outputDirectory, "App.csproj")]);
        FailRunner(exception);

        Func<Task> act = () => CreateProcessor().ProcessAsync(
            Context(Package("Pkg")), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(exception);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        FluentActions.Invoking(() => new DotNetAddReferencePostActionProcessor(null!, new(), _locator))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new DotNetAddReferencePostActionProcessor(_runner, null!, _locator))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new DotNetAddReferencePostActionProcessor(_runner, new(), null!))
            .Should().Throw<ArgumentNullException>();
    }

    private static Dictionary<string, string> Package(
        string id, string? version = null, params (string Key, string Value)[] extra)
    {
        Dictionary<string, string> arguments = new() { ["reference"] = id, ["referenceType"] = "package" };
        if (version is not null)
        {
            arguments["version"] = version;
        }

        foreach ((string key, string value) in extra)
        {
            arguments[key] = value;
        }

        return arguments;
    }

    private DotNetAddReferencePostActionProcessor CreateProcessor() => new(_runner, new(), _locator);

    private PostActionContext Context(
        IReadOnlyDictionary<string, string> arguments, IPostActionFileMatcher? matcher = null)
        => new(CreateProcessor().Id, arguments, null, null, false, _outputDirectory, [], [], _reporter, matcher);

    private List<IReadOnlyList<string>> RecordCalls(CancellationToken expectedToken)
    {
        List<IReadOnlyList<string>> calls = [];
        _runner.RunStreamingAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                calls.Add([.. call.Arg<IReadOnlyList<string>>()]);
                call.ArgAt<string>(1).Should().Be(_outputDirectory);
                call.Arg<CancellationToken>().Should().Be(expectedToken);
                call.ArgAt<Action<string>>(2)("info line");
                call.ArgAt<Action<string>>(3)("error line");
                return Task.CompletedTask;
            });
        return calls;
    }

    private void FailRunner(Exception exception)
        => _runner.RunStreamingAsync(
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>(), Arg.Any<CancellationToken>()).ThrowsAsync(exception);
}
