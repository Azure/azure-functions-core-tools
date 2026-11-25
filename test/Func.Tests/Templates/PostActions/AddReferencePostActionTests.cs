// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.PostActions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.PostActions;

public sealed class AddReferencePostActionTests
{
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), "func-add-reference-base");
    private readonly IFunctionsReporter _reporter = Substitute.For<IFunctionsReporter>();

    [Fact]
    public async Task ProcessAsync_TargetFilesMatch_AddsPackageToEachTargetWithoutDiscovery()
    {
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths("*.proj").Returns(["App.proj", Path.Combine("nested", "Lib.proj")]);
        RecordingAction action = new();
        using CancellationTokenSource cancellation = new();

        bool result = await action.ProcessAsync(
            Context(Package("Pkg", "1.2.3", ("targetFiles", "*.proj")), matcher), cancellation.Token);

        action.Id.Should().Be(new Guid("B17581D1-C5C9-4489-8F0A-004BE667B814"));
        result.Should().BeTrue();
        action.DiscoveryCalls.Should().Be(0);
        action.Calls.Should().Equal(
            $"package|{Path.Combine(_outputDirectory, "App.proj")}|Pkg|1.2.3",
            $"package|{Path.Combine(_outputDirectory, "nested", "Lib.proj")}|Pkg|1.2.3");
        action.Tokens.Should().AllSatisfy(token => token.Should().Be(cancellation.Token));
    }

    [Fact]
    public async Task ProcessAsync_NoTargetFilesMatch_UsesDiscoveredTargets()
    {
        RecordingAction action = new() { Discovered = [Path.Combine(_outputDirectory, "Found.proj")] };

        bool result = await action.ProcessAsync(Context(Package("Pkg", " ")), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        action.DiscoveryCalls.Should().Be(1);
        action.Calls.Should().Equal($"package|{Path.Combine(_outputDirectory, "Found.proj")}|Pkg|");
    }

    [Fact]
    public async Task ProcessAsync_DiscoveryFails_ReturnsFalseWithoutAdding()
    {
        RecordingAction action = new() { DiscoverySucceeds = false };

        bool result = await action.ProcessAsync(Context(Package("Pkg")), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        action.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_DefaultDiscoveryFindsNothing_ReportsMissingTarget()
    {
        UnsupportedAction action = new();

        bool result = await action.ProcessAsync(Context(Package("Pkg")), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _reporter.Received(1).WriteLog(
            Arg.Is<string>(message => message.Contains("targetFiles")), FunctionsReportSeverity.Error);
    }

    [Theory]
    [InlineData(null, "package", 1)]
    [InlineData("Pkg", null, 1)]
    [InlineData(" ", "package", 1)]
    [InlineData("Pkg", "", 1)]
    [InlineData(null, null, 2)]
    public async Task ProcessAsync_MissingRequiredArgument_ReportsMisconfiguration(
        string? reference, string? referenceType, int expectedReports)
    {
        Dictionary<string, string> arguments = [];
        if (reference is not null)
        {
            arguments["reference"] = reference;
        }

        if (referenceType is not null)
        {
            arguments["referenceType"] = referenceType;
        }

        RecordingAction action = new() { Discovered = [Path.Combine(_outputDirectory, "App.proj")] };

        bool result = await action.ProcessAsync(Context(arguments), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        action.DiscoveryCalls.Should().Be(0);
        action.Calls.Should().BeEmpty();
        _reporter.Received(expectedReports).WriteLog(
            Arg.Is<string>(message => message.Contains("misconfigured")), FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_TargetFails_StopsBeforeRemainingTargets()
    {
        RecordingAction action = new()
        {
            Discovered = [Path.Combine(_outputDirectory, "First.proj"), Path.Combine(_outputDirectory, "Second.proj")],
            Succeeds = false,
        };

        bool result = await action.ProcessAsync(Context(Package("Pkg")), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        action.Calls.Should().ContainSingle();
    }

    [Theory]
    [InlineData("custom")]
    [InlineData("CUSTOM")]
    [InlineData(" custom ")]
    [InlineData("framework")]
    public async Task ProcessAsync_RegisteredReferenceType_InvokesItsHandler(string referenceType)
    {
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths("Template.Lib/Template.Lib.csproj").Returns(["renamed.csproj"]);
        RecordingAction action = new() { Discovered = [Path.Combine(_outputDirectory, "App.proj")] };
        Dictionary<string, string> arguments = new()
        {
            ["reference"] = "Template.Lib/Template.Lib.csproj",
            ["referenceType"] = referenceType,
        };

        bool result = await action.ProcessAsync(Context(arguments, matcher), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        action.Calls.Should().Equal(
            $"custom|{Path.Combine(_outputDirectory, "App.proj")}|Template.Lib/Template.Lib.csproj|");
        matcher.DidNotReceiveWithAnyArgs().MatchTargetPaths(default!);
    }

    [Fact]
    public async Task ProcessAsync_PopulatesWorkloadSpecificReferenceProperties()
    {
        RecordingAction action = new() { Discovered = [Path.Combine(_outputDirectory, "App.proj")] };

        bool result = await action.ProcessAsync(
            Context(Package("Pkg", extra: ("registry", "https://packages.example.test"))),
            TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        action.References.Should().ContainSingle()
            .Which.Registry.Should().Be("https://packages.example.test");
    }

    [Fact]
    public async Task ProcessAsync_UsesCustomReferenceCreation()
    {
        RecordingAction action = new(customizeReference: true)
        {
            Discovered = [Path.Combine(_outputDirectory, "App.proj")],
        };

        bool result = await action.ProcessAsync(
            Context(Package("plain-reference")), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        action.References.Should().ContainSingle()
            .Which.Registry.Should().Be("custom-registry");
    }

    [Theory]
    [InlineData("project")]
    [InlineData("tool")]
    public async Task ProcessAsync_UnregisteredReferenceType_ReportsError(string referenceType)
    {
        RecordingAction action = new() { Discovered = [Path.Combine(_outputDirectory, "App.proj")] };
        Dictionary<string, string> arguments = new() { ["reference"] = "Value", ["referenceType"] = referenceType };

        bool result = await action.ProcessAsync(Context(arguments), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        action.Calls.Should().BeEmpty();
        _reporter.Received(1).WriteLog(
            $"Reference type '{referenceType}' is not supported for this template.", FunctionsReportSeverity.Error);
    }

    [Theory]
    [InlineData("package")]
    [InlineData("project")]
    public async Task ProcessAsync_NoHandlersRegistered_ReportsUnsupported(string referenceType)
    {
        UnsupportedAction action = new() { Targets = [Path.Combine(_outputDirectory, "App.proj")] };
        Dictionary<string, string> arguments = new() { ["reference"] = "Value", ["referenceType"] = referenceType };

        bool result = await action.ProcessAsync(Context(arguments), TestContext.Current.CancellationToken);

        result.Should().BeFalse();
        _reporter.Received(1).WriteLog(
            $"Reference type '{referenceType}' is not supported for this template.", FunctionsReportSeverity.Error);
    }

    [Fact]
    public async Task ProcessAsync_AddCancels_PropagatesAndStops()
    {
        using CancellationTokenSource cancellation = new();
        RecordingAction action = new()
        {
            Discovered = [Path.Combine(_outputDirectory, "First.proj"), Path.Combine(_outputDirectory, "Second.proj")],
            OnAdd = cancellation.Cancel,
        };

        Func<Task> act = () => action.ProcessAsync(Context(Package("Pkg")), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        action.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessAsync_AlreadyCancelled_DoesNothing()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        RecordingAction action = new() { Discovered = [Path.Combine(_outputDirectory, "App.proj")] };

        Func<Task> act = () => action.ProcessAsync(Context(Package("Pkg")), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        action.DiscoveryCalls.Should().Be(0);
        _reporter.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_NullContext_Throws()
    {
        await FluentActions.Awaiting(
            () => new RecordingAction().ProcessAsync(null!, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RegisterReferenceHandlers_BlankReferenceType_Throws(string? referenceType)
    {
        FluentActions.Invoking(() => new RecordingAction(referenceType!)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("package")]
    [InlineData("PACKAGE")]
    [InlineData(" package ")]
    public void RegisterReferenceHandlers_DuplicateReferenceType_Throws(string referenceType)
    {
        FluentActions.Invoking(() => new RecordingAction(referenceType)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterReferenceHandlers_NullHandler_Throws()
    {
        FluentActions.Invoking(() => new RecordingAction(nullHandler: true)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterReferenceHandlers_NullList_Throws()
    {
        FluentActions.Invoking(() => new RecordingAction(nullList: true)).Should().Throw<ArgumentNullException>();
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

    private PostActionContext Context(
        IReadOnlyDictionary<string, string> arguments, IPostActionFileMatcher? matcher = null)
        => new(Guid.Empty, arguments, null, null, false, _outputDirectory, [], [], _reporter, matcher);

    private sealed class RecordingAction : AddReferencePostAction<RecordingReference>
    {
        private readonly bool _customizeReference;

        public RecordingAction(
            string referenceType = "custom",
            bool nullHandler = false,
            bool nullList = false,
            bool customizeReference = false)
        {
            _customizeReference = customizeReference;
            RegisterReferenceHandlers(("package", AddPackageAsync));
            RegisterReferenceHandlers(nullList ? null! : [(referenceType, nullHandler ? null! : AddCustomAsync)]);
            RegisterReferenceHandlers(("framework", AddCustomAsync));
        }

        public IReadOnlyList<string> Discovered { get; init; } = [];

        public bool DiscoverySucceeds { get; init; } = true;

        public bool Succeeds { get; init; } = true;

        public Action? OnAdd { get; init; }

        public int DiscoveryCalls { get; private set; }

        public List<string> Calls { get; } = [];

        public List<RecordingReference> References { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public override bool CanProcess(PostActionSelectionContext context) => true;

        protected override bool TryGetReference(
            PostActionContext context,
            string referenceType,
            string referenceValue,
            [NotNullWhen(true)] out RecordingReference? reference)
        {
            if (!_customizeReference)
            {
                return base.TryGetReference(context, referenceType, referenceValue, out reference);
            }

            reference = new()
            {
                ReferenceType = referenceType,
                Reference = referenceValue,
                Registry = "custom-registry",
            };
            return true;
        }

        protected override bool TryFindTargets(PostActionContext context, out IReadOnlyList<string> targets)
        {
            DiscoveryCalls++;
            targets = Discovered;
            return DiscoverySucceeds;
        }

        private Task<bool> AddPackageAsync(
            PostActionContext context,
            string targetPath,
            RecordingReference reference,
            CancellationToken cancellationToken)
            => Record($"package|{targetPath}|{reference.Reference}|{reference.Version}", reference, cancellationToken);

        private Task<bool> AddCustomAsync(
            PostActionContext context,
            string targetPath,
            RecordingReference reference,
            CancellationToken cancellationToken)
            => Record($"custom|{targetPath}|{reference.Reference}|{reference.Version}", reference, cancellationToken);

        private Task<bool> Record(string call, RecordingReference reference, CancellationToken cancellationToken)
        {
            Calls.Add(call);
            References.Add(reference);
            Tokens.Add(cancellationToken);
            OnAdd?.Invoke();
            return Task.FromResult(Succeeds);
        }
    }

    private sealed record RecordingReference : PostActionReference
    {
        public string? Registry { get; init; }
    }

    private sealed class UnsupportedAction : AddReferencePostAction<PostActionReference>
    {
        public IReadOnlyList<string>? Targets { get; init; }

        public override bool CanProcess(PostActionSelectionContext context) => true;

        protected override bool TryFindTargets(PostActionContext context, out IReadOnlyList<string> targets)
        {
            if (Targets is null)
            {
                return base.TryFindTargets(context, out targets);
            }

            targets = Targets;
            return true;
        }
    }
}
