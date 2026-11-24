// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.PostActions;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.PostActions;

public sealed class RestorePostActionTests
{
    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), "func-restore-base");

    [Fact]
    public async Task ProcessAsync_ConfiguredFiles_RestoresEveryTargetAndAggregatesFailure()
    {
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths("*.project").Returns(["renamed/App.project", "other/Lib.project", "renamed/App.project"]);
        PostActionContext context = Context(["ignored.project"], new Dictionary<string, string> { ["files"] = "*.project" }, matcher);
        using CancellationTokenSource cancellation = new();
        List<string> targets = [];
        var action = new RecordingRestoreAction((receivedContext, path, token) =>
        {
            receivedContext.Should().BeSameAs(context);
            token.Should().Be(cancellation.Token);
            targets.Add(path);
            return Task.FromResult(targets.Count != 1);
        });

        bool result = await action.ProcessAsync(context, cancellation.Token);

        action.Id.Should().Be(new Guid("210D431B-A78B-4D2F-B762-4ED3E3EA9025"));
        result.Should().BeFalse();
        targets.Should().Equal(
            Path.Combine(_outputDirectory, "renamed", "App.project"),
            Path.Combine(_outputDirectory, "other", "Lib.project"),
            Path.Combine(_outputDirectory, "renamed", "App.project"));
    }

    [Fact]
    public async Task ProcessAsync_NoConfiguredMatches_RestoresPrimaryOutputs()
    {
        IPostActionFileMatcher matcher = Substitute.For<IPostActionFileMatcher>();
        matcher.MatchTargetPaths("missing.project").Returns([]);
        PostActionContext context = Context(["first.project", "second.project"],
            new Dictionary<string, string> { ["files"] = "missing.project" }, matcher);
        List<string> targets = [];
        var action = new RecordingRestoreAction((_, path, _) =>
        {
            targets.Add(path);
            return Task.FromResult(true);
        });

        bool result = await action.ProcessAsync(context, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        targets.Should().Equal(
            Path.Combine(_outputDirectory, "first.project"),
            Path.Combine(_outputDirectory, "second.project"));
    }

    [Fact]
    public async Task ProcessAsync_NoTargets_SucceedsWithoutCallingRestore()
    {
        var action = new RecordingRestoreAction((_, _, _) => throw new InvalidOperationException("Unexpected restore."));

        bool result = await action.ProcessAsync(Context([]), TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessAsync_RestoreCancels_PropagatesCancellationAndStops()
    {
        using CancellationTokenSource cancellation = new();
        int calls = 0;
        var action = new RecordingRestoreAction((_, _, token) =>
        {
            token.Should().Be(cancellation.Token);
            calls++;
            throw new OperationCanceledException(token);
        });

        Func<Task> act = () => action.ProcessAsync(Context(["first", "second"]), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_AlreadyCancelled_DoesNotCallRestore()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        var action = new RecordingRestoreAction((_, _, _) => throw new InvalidOperationException("Unexpected restore."));

        Func<Task> act = () => action.ProcessAsync(Context([]), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProcessAsync_FinalRestoreCancels_PropagatesCancellation()
    {
        using CancellationTokenSource cancellation = new();
        var action = new RecordingRestoreAction((_, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(true);
        });

        Func<Task> act = () => action.ProcessAsync(Context(["project"]), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private PostActionContext Context(
        IReadOnlyList<string> outputs,
        IReadOnlyDictionary<string, string>? arguments = null,
        IPostActionFileMatcher? matcher = null)
        => new(Guid.Empty, arguments ?? new Dictionary<string, string>(), null, null, false, _outputDirectory, outputs, [],
            fileMatcher: matcher);

    private sealed class RecordingRestoreAction(Func<PostActionContext, string, CancellationToken, Task<bool>> restore) : RestorePostAction
    {
        private readonly Func<PostActionContext, string, CancellationToken, Task<bool>> _restore =
            restore ?? throw new ArgumentNullException(nameof(restore));

        public override bool CanProcess(PostActionSelectionContext context) => true;

        protected override Task<bool> RestoreAsync(PostActionContext context, string targetPath, CancellationToken cancellationToken)
            => _restore(context, targetPath, cancellationToken);
    }
}
