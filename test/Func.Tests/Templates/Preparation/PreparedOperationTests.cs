// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Preparation;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Preparation;

public sealed class PreparedOperationTests
{
    [Fact]
    public async Task Preview_RepeatedCalls_ReadsSamePlanWithoutFilesystemAccessOrConsumingCommit()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        PreparedOperationPlan plan = PreparedOperationTestFileSystem.Plan(new("item.bin", PreparedOutputKind.File, [0, 255]));
        await using PreparedOperation operation = new(plan, fileSystem.FileSystem);
        PreparedOperationCompletion before = operation.Completion;

        operation.Preview().Should().BeSameAs(plan);
        operation.Preview().Should().BeSameAs(plan);

        fileSystem.FileSystem.ReceivedCalls().Should().BeEmpty();
        operation.State.Should().Be(PreparedOperationState.Prepared);
        await operation.CommitAsync(CancellationToken.None);
        before.Status.Should().Be(PreparedCommitStatus.NotStarted);
        before.CompletedEffects.Should().BeEmpty();
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("item.bin")].Should().Equal(0, 255);
    }

    [Fact]
    public async Task CommitAsync_OutputAndConfiguration_PublishesExactRetainedBytesAndSeparateActualEffects()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        byte[] fileBytes = [0, 255, 13, 10];
        byte[] configBytes = [123, 13, 10, 125];
        PreparedConfiguration configuration = new(".", configBytes);
        PreparedOperationPlan plan = new(PreparedOperationTestFileSystem.Target, PreparedOperationTestFileSystem.Identity,
            [new("src/item.bin", PreparedOutputKind.File, fileBytes), new("empty", PreparedOutputKind.Directory),
                new("host.json", PreparedOutputKind.File, [123, 125])], configuration);
        await using PreparedOperation operation = new(plan, fileSystem.FileSystem);
        fileBytes[0] = 42;
        configBytes[0] = 42;

        PreparedOperationCompletion result = await operation.CommitAsync(CancellationToken.None);

        result.Status.Should().Be(PreparedCommitStatus.Succeeded);
        result.Stage.Should().Be(PreparedCommitStage.Complete);
        result.PendingEffect.Should().BeNull();
        result.ConfigurationCompleted.Should().BeTrue();
        result.CleanupStatus.Should().Be(PreparedCleanupStatus.Pending);
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("src/item.bin")].Should().Equal(0, 255, 13, 10);
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("host.json")].Should().Equal(123, 125);
        fileSystem.Files[PreparedOperationTestFileSystem.Destination(".func/config.json")].Should().Equal(123, 13, 10, 125);
        result.CompletedEffects.Where(effect => effect.Kind == PreparedOutputKind.Directory).Select(effect => effect.Path)
            .Should().BeEquivalentTo(PreparedOperationTestFileSystem.Destination(".func"),
                PreparedOperationTestFileSystem.Destination("empty"), PreparedOperationTestFileSystem.Destination("src"));
        result.CompletedEffects.Last().IsConfiguration.Should().BeTrue();
        ((IList<PreparedEffect>)result.CompletedEffects).Invoking(effects => effects.Clear()).Should().Throw<NotSupportedException>();
        operation.Preview().Should().BeSameAs(plan);
        fileSystem.FileSystem.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        await fileSystem.FileSystem.DidNotReceive().WriteAllTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await operation.DisposeAsync();
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Succeeded);
        operation.Completion.CompletedEffects.Should().Equal(result.CompletedEffects);
        result.CleanupStatus.Should().Be(PreparedCleanupStatus.Pending);
        fileSystem.CleanupAttempts.Should().HaveCount(3);
    }

    [Fact]
    public async Task CommitAsync_MissingTargetAncestors_RecordsOnlyDirectoriesActuallyCreated()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        string target = PreparedOperationTestFileSystem.Destination("parent/project");
        PreparedOperationPlan plan = new(target, PreparedOperationTestFileSystem.Identity, [new("item", PreparedOutputKind.File, [7])]);
        await using PreparedOperation operation = new(plan, fileSystem.FileSystem);

        PreparedOperationCompletion result = await operation.CommitAsync(CancellationToken.None);

        result.CompletedEffects.Where(effect => effect.Kind == PreparedOutputKind.Directory).Select(effect => effect.Path)
            .Should().Equal(PreparedOperationTestFileSystem.Destination("parent"), target);
        fileSystem.Files[Path.Combine(target, "item")].Should().Equal(7);
    }

    [Fact]
    public async Task CommitAsync_ExistingImplicitParent_ReusesItWithoutClaimingCreation()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        fileSystem.SetDirectory(PreparedOperationTestFileSystem.Destination("src"));
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("src/item", PreparedOutputKind.File, [1])), fileSystem.FileSystem);

        PreparedOperationCompletion result = await operation.CommitAsync(CancellationToken.None);

        result.CompletedEffects.Should().ContainSingle().Which.Kind.Should().Be(PreparedOutputKind.File);
    }

    [Fact]
    public async Task CommitAsync_SecondAttemptAfterSuccess_RejectsWithoutChangingCompletion()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem);
        PreparedOperationCompletion first = await operation.CommitAsync(CancellationToken.None);

        Func<Task> second = () => operation.CommitAsync(CancellationToken.None);

        await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already consumed*");
        operation.Completion.CompletedEffects.Should().Equal(first.CompletedEffects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_DestinationOccupiedBeforeCommit_RefusesWithoutMutationAndCannotRetry(bool directory)
    {
        PreparedOperationTestFileSystem fileSystem = new();
        string destination = PreparedOperationTestFileSystem.Destination("item");
        PreparedOperationPlan plan = PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1]));
        if (directory)
        {
            fileSystem.SetDirectory(destination);
        }
        else
        {
            fileSystem.SetFile(destination, 99);
        }

        await using PreparedOperation operation = new(plan, fileSystem.FileSystem);
        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>().WithMessage("*already exists*");
        fileSystem.Stages.Should().BeEmpty();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Failed);
        operation.Completion.Stage.Should().Be(PreparedCommitStage.Preflight);
        operation.Completion.CompletedEffects.Should().BeEmpty();
        operation.Completion.PendingEffect!.Path.Should().Be(destination);
        operation.Completion.CommitError.Should().BeOfType<IOException>();
        await commit.Should().ThrowAsync<InvalidOperationException>();
        if (!directory)
        {
            fileSystem.Files[destination].Should().Equal(99);
        }
    }

    [Fact]
    public async Task CommitAsync_CapturedDirectoryAlreadyExists_RefusesInsteadOfReusingAnOutputDestination()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        fileSystem.SetDirectory(PreparedOperationTestFileSystem.Destination("empty"));
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("empty", PreparedOutputKind.Directory)), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>();
        operation.Completion.CompletedEffects.Should().BeEmpty();
        fileSystem.Stages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_AncestorIsFileOrLink_RefusesBeforeMutation(bool link)
    {
        PreparedOperationTestFileSystem fileSystem = new();
        string ancestor = PreparedOperationTestFileSystem.Destination("src");
        if (link)
        {
            fileSystem.SetLink(ancestor);
        }
        else
        {
            fileSystem.SetFile(ancestor, 99);
        }

        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("src/item", PreparedOutputKind.File)), fileSystem.FileSystem);
        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>();
        operation.Completion.CompletedEffects.Should().BeEmpty();
        fileSystem.Stages.Should().BeEmpty();
    }

    [Fact]
    public async Task CommitAsync_TargetRootIsLink_RefusesBeforeMutation()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        fileSystem.SetLink(PreparedOperationTestFileSystem.Target);
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>();
        fileSystem.Stages.Should().BeEmpty();
    }

    [Fact]
    public async Task CommitAsync_AttributeReadDenied_PropagatesWithoutTreatingThePathAsAbsent()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        fileSystem.FileSystem.GetAttributesIfExists(PreparedOperationTestFileSystem.Destination("item"))
            .Returns(_ => throw new UnauthorizedAccessException("controlled denial"));
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("controlled denial");
        fileSystem.Stages.Should().BeEmpty();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Failed);
    }

    [Fact]
    public async Task CommitAsync_CollisionAfterStaging_RechecksBeforePublication()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        string destination = PreparedOperationTestFileSystem.Destination("item");
        fileSystem.AfterStagingWrite = _ => fileSystem.SetFile(destination, 99);
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1])), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>();
        operation.Completion.CompletedEffects.Should().BeEmpty();
        fileSystem.Files[destination].Should().Equal(99);
        fileSystem.FileSystem.DidNotReceive().PublishFileNoReplace(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CommitAsync_CollisionAtAtomicFilePublication_NoReplacePrimitiveRefuses()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        string destination = PreparedOperationTestFileSystem.Destination("item");
        fileSystem.BeforePublishFile = path => fileSystem.SetFile(path, 99);
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1])), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>();
        operation.Completion.CompletedEffects.Should().BeEmpty();
        fileSystem.Files[destination].Should().Equal(99);
        fileSystem.FileSystem.Received(1).PublishFileNoReplace(Arg.Any<string>(), destination);
    }

    [Fact]
    public async Task CommitAsync_CollisionAtExclusiveDirectoryCreation_RecordsNoUnownedDirectory()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        fileSystem.BeforeCreateDirectory = fileSystem.SetDirectory;
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("empty", PreparedOutputKind.Directory)), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>();
        operation.Completion.CompletedEffects.Should().BeEmpty();
        operation.Completion.PendingEffect!.Path.Should().Be(PreparedOperationTestFileSystem.Destination("empty"));
    }

    [Fact]
    public async Task CommitAsync_AncestorBecomesLinkedAfterStaging_RefusesLatePublication()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        string parent = PreparedOperationTestFileSystem.Destination("src");
        fileSystem.SetDirectory(parent);
        fileSystem.AfterStagingWrite = _ => fileSystem.SetLink(parent);
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("src/item", PreparedOutputKind.File)), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>().WithMessage("*ancestor*");
        operation.Completion.CompletedEffects.Should().BeEmpty();
        fileSystem.FileSystem.DidNotReceive().PublishFileNoReplace(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CommitAsync_LaterFileFails_PreservesEarlierEffectAndImmutablePlan()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        PreparedOperationPlan plan = PreparedOperationTestFileSystem.Plan(
            new("b", PreparedOutputKind.File, [2]), new("a", PreparedOutputKind.File, [1]));
        fileSystem.AfterPublishFile = _ => fileSystem.SetFile(PreparedOperationTestFileSystem.Destination("b"), 99);
        await using PreparedOperation operation = new(plan, fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>();
        operation.Completion.CompletedEffects.Should().ContainSingle().Which.Path.Should().Be(PreparedOperationTestFileSystem.Destination("a"));
        operation.Completion.PendingEffect!.Path.Should().Be(PreparedOperationTestFileSystem.Destination("b"));
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("a")].Should().Equal(1);
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("b")].Should().Equal(99);
        operation.Preview().Should().BeSameAs(plan);
        plan.Entries.Select(entry => entry.RelativePath).Should().Equal("a", "b");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_ConfigurationCollision_ReportsWhetherOutputWasAlreadyPublished(bool late)
    {
        PreparedOperationTestFileSystem fileSystem = new();
        string configPath = PreparedOperationTestFileSystem.Destination(".func/config.json");
        fileSystem.SetDirectory(PreparedOperationTestFileSystem.Destination(".func"));
        if (late)
        {
            fileSystem.AfterPublishFile = _ => fileSystem.SetFile(configPath, 99);
        }
        else
        {
            fileSystem.SetFile(configPath, 99);
        }

        PreparedOperationPlan plan = new(PreparedOperationTestFileSystem.Target, PreparedOperationTestFileSystem.Identity,
            [new("host.json", PreparedOutputKind.File, [123, 125])], new(".", [123, 125]));
        await using PreparedOperation operation = new(plan, fileSystem.FileSystem);
        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<IOException>();
        operation.Completion.CompletedEffects.Should().HaveCount(late ? 1 : 0);
        operation.Completion.PendingEffect!.IsConfiguration.Should().BeTrue();
        operation.Completion.ConfigurationCompleted.Should().BeFalse();
        fileSystem.Files[configPath].Should().Equal(99);
    }

    [Fact]
    public async Task CommitAsync_AlreadyCanceled_ConsumesAttemptWithoutFilesystemAccess()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        using CancellationTokenSource source = new();
        source.Cancel();
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem);
        Func<Task> commit = () => operation.CommitAsync(source.Token);

        await commit.Should().ThrowAsync<OperationCanceledException>();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Canceled);
        operation.Completion.Stage.Should().Be(PreparedCommitStage.NotStarted);
        operation.Completion.PendingEffect.Should().BeNull();
        operation.Completion.CompletedEffects.Should().BeEmpty();
        fileSystem.FileSystem.ReceivedCalls().Should().BeEmpty();
        await commit.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CommitAsync_CanceledAfterFirstFile_RecordsPartialOutputAndNextPendingEffect()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        using CancellationTokenSource source = new();
        fileSystem.AfterPublishFile = _ => source.Cancel();
        await using PreparedOperation operation = new(PreparedOperationTestFileSystem.Plan(
            new("a", PreparedOutputKind.File, [1]), new("b", PreparedOutputKind.File, [2])), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(source.Token);

        await commit.Should().ThrowAsync<OperationCanceledException>();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Canceled);
        operation.Completion.CompletedEffects.Should().ContainSingle().Which.Path.Should().Be(PreparedOperationTestFileSystem.Destination("a"));
        operation.Completion.PendingEffect!.Path.Should().Be(PreparedOperationTestFileSystem.Destination("b"));
        operation.Completion.Stage.Should().Be(PreparedCommitStage.Publication);
        fileSystem.Files.Should().NotContainKey(PreparedOperationTestFileSystem.Destination("b"));
    }

    [Fact]
    public async Task CommitAsync_CanceledAfterDirectoryCreation_RecordsDirectoryWithoutClaimingFile()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        using CancellationTokenSource source = new();
        fileSystem.AfterCreateDirectory = _ => source.Cancel();
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("src/item", PreparedOutputKind.File)), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(source.Token);

        await commit.Should().ThrowAsync<OperationCanceledException>();
        operation.Completion.CompletedEffects.Should().ContainSingle().Which.Should()
            .Be(new PreparedEffect(PreparedOperationTestFileSystem.Destination("src"), PreparedOutputKind.Directory));
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Canceled);
    }

    [Fact]
    public async Task CommitAsync_CanceledDuringStaging_ForwardsTokenAndPublishesNoFile()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        using CancellationTokenSource source = new();
        fileSystem.BeforeWriteAsync = token =>
        {
            token.Should().Be(source.Token);
            source.Cancel();
            return Task.FromCanceled(token);
        };
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1])), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(source.Token);

        await commit.Should().ThrowAsync<OperationCanceledException>();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Canceled);
        operation.Completion.CompletedEffects.Should().BeEmpty();
        fileSystem.Files.Should().NotContainKey(PreparedOperationTestFileSystem.Destination("item"));
    }

    [Fact]
    public async Task CommitAsync_CanceledAfterStagingBeforePublication_DoesNotPublishTheStagedFile()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        using CancellationTokenSource source = new();
        fileSystem.AfterStagingWrite = _ => source.Cancel();
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1])), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(source.Token);

        await commit.Should().ThrowAsync<OperationCanceledException>();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Canceled);
        operation.Completion.CompletedEffects.Should().BeEmpty();
        fileSystem.FileSystem.DidNotReceive().PublishFileNoReplace(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CommitAsync_CancellationAfterFinalPublication_ReportsCompletedOutput()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        using CancellationTokenSource source = new();
        fileSystem.AfterPublishFile = _ => source.Cancel();
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1])), fileSystem.FileSystem);

        PreparedOperationCompletion completion = await operation.CommitAsync(source.Token);

        source.IsCancellationRequested.Should().BeTrue();
        completion.Status.Should().Be(PreparedCommitStatus.Succeeded);
        completion.CompletedEffects.Should().ContainSingle();
    }

    [Fact]
    public async Task CommitAsync_UnexpectedCollaboratorFailure_PropagatesAndStillMarksAttemptFailed()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        InvalidOperationException failure = new("controlled unexpected failure");
        fileSystem.BeforeWriteAsync = _ => Task.FromException(failure);
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem);

        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);

        await commit.Should().ThrowAsync<InvalidOperationException>().WithMessage(failure.Message);
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Failed);
        operation.Completion.CommitError.Should().BeSameAs(failure);
        operation.Completion.CompletedEffects.Should().BeEmpty();
    }

    [Theory]
    [InlineData("write", false)]
    [InlineData("write", true)]
    [InlineData("flush", false)]
    [InlineData("flush", true)]
    [InlineData("cancel-write", false)]
    [InlineData("cancel-write", true)]
    [InlineData("cancel-flush", false)]
    [InlineData("cancel-flush", true)]
    public async Task CommitAsync_StagingIoAndCloseBothFail_PreservesBothCausesAndPriorPublishedEffect(string phase, bool synchronousClose)
    {
        PreparedOperationTestFileSystem fileSystem = new();
        using CancellationTokenSource cancellation = new();
        Exception primary = phase.StartsWith("cancel", StringComparison.Ordinal)
            ? new OperationCanceledException("controlled staging cancellation", cancellation.Token)
            : new IOException("controlled staging " + phase + " failure");
        InvalidOperationException closeFailure = new("controlled staging close failure");
        Stream stream = CreateFailingStagingStream(cancellation, phase, primary, closeFailure, synchronousClose);
        fileSystem.AfterPublishFile = _ => fileSystem.StagingStreamFactory = _ => stream;
        await using PreparedOperation operation = new(PreparedOperationTestFileSystem.Plan(
            new("a", PreparedOutputKind.File, [1]), new("b", PreparedOutputKind.File, [2])), fileSystem.FileSystem);

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() => operation.CommitAsync(cancellation.Token));

        failure.InnerExceptions.Should().Equal(primary, closeFailure);
        operation.Completion.CommitError.Should().BeSameAs(failure);
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Failed);
        operation.Completion.Stage.Should().Be(PreparedCommitStage.Publication);
        operation.Completion.PendingEffect!.Path.Should().Be(PreparedOperationTestFileSystem.Destination("b"));
        operation.Completion.CompletedEffects.Should().ContainSingle().Which.Path.Should().Be(PreparedOperationTestFileSystem.Destination("a"));
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("a")].Should().Equal(1);
        fileSystem.Files.Should().NotContainKey(PreparedOperationTestFileSystem.Destination("b"));
        fileSystem.FileSystem.DidNotReceive().PublishFileNoReplace(Arg.Any<string>(), PreparedOperationTestFileSystem.Destination("b"));
        stream.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(Stream.DisposeAsync)).Should().Be(1);
        cancellation.IsCancellationRequested.Should().Be(phase.StartsWith("cancel", StringComparison.Ordinal));

        await operation.DisposeAsync();
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Succeeded);
        operation.Completion.CompletedEffects.Should().ContainSingle();
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("a")].Should().Equal(1);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("cancel-write")]
    [InlineData("cancel-flush")]
    [InlineData("close")]
    public async Task CommitAsync_OnlyOneStagingFailure_PropagatesOriginalCauseWithoutAggregation(string phase)
    {
        PreparedOperationTestFileSystem fileSystem = new();
        using CancellationTokenSource cancellation = new();
        Exception primary = phase.StartsWith("cancel", StringComparison.Ordinal)
            ? new OperationCanceledException("controlled staging cancellation", cancellation.Token)
            : new IOException("controlled staging failure");
        Stream stream = CreateFailingStagingStream(cancellation, phase, primary, phase == "close" ? primary : null, false);
        fileSystem.StagingStreamFactory = _ => stream;
        await using PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1])), fileSystem.FileSystem);

        Exception? failure = await Record.ExceptionAsync(() => operation.CommitAsync(cancellation.Token));

        failure.Should().BeSameAs(primary);
        operation.Completion.Status.Should().Be(cancellation.IsCancellationRequested ? PreparedCommitStatus.Canceled : PreparedCommitStatus.Failed);
        operation.Completion.CompletedEffects.Should().BeEmpty();
        fileSystem.FileSystem.DidNotReceive().PublishFileNoReplace(Arg.Any<string>(), Arg.Any<string>());
        stream.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(Stream.DisposeAsync)).Should().Be(1);
    }

    [Fact]
    public async Task CommitAsync_PublishedDestinationThenStagingCleanupFails_DoesNotUndoPublicationInCompletion()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [0, 255])), fileSystem.FileSystem);

        PreparedOperationCompletion committed = await operation.CommitAsync(CancellationToken.None);
        string stagedFile = Path.Combine(fileSystem.Stages.Single(), "content");
        fileSystem.Files[stagedFile].Should().Equal(0, 255);
        fileSystem.DeleteFailure = _ => new IOException("controlled retained staging cleanup failure");
        await Assert.ThrowsAsync<AggregateException>(() => operation.DisposeAsync().AsTask());

        committed.Status.Should().Be(PreparedCommitStatus.Succeeded);
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Succeeded);
        operation.Completion.CompletedEffects.Should().Equal(committed.CompletedEffects);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Failed);
        operation.Completion.CleanupErrors.Should().ContainSingle();
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("item")].Should().Equal(0, 255);
    }

    [Fact]
    public async Task DisposeAsync_AfterPreviewWithoutCommit_ReleasesResourceOnceAndRejectsNewUse()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        IAsyncDisposable resource = Substitute.For<IAsyncDisposable>();
        PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem, resource);
        operation.Preview();

        await operation.DisposeAsync();
        await operation.DisposeAsync();

        await resource.Received(1).DisposeAsync();
        operation.State.Should().Be(PreparedOperationState.Disposed);
        operation.Completion.Status.Should().Be(PreparedCommitStatus.NotStarted);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Succeeded);
        operation.Invoking(instance => instance.Preview()).Should().Throw<ObjectDisposedException>();
        Func<Task> commit = () => operation.CommitAsync(CancellationToken.None);
        await commit.Should().ThrowAsync<ObjectDisposedException>();
        fileSystem.FileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_SeveralCleanupFailures_AttemptsAllAndRetainsEveryErrorAcrossRepeatedDisposal()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        IAsyncDisposable resource = Substitute.For<IAsyncDisposable>();
        resource.DisposeAsync().Returns(ValueTask.FromException(new InvalidOperationException("retained resource cleanup")));
        PreparedOperation operation = new(PreparedOperationTestFileSystem.Plan(
            new("a", PreparedOutputKind.File), new("b", PreparedOutputKind.File)), fileSystem.FileSystem, resource);
        await operation.CommitAsync(CancellationToken.None);
        fileSystem.DeleteFailure = path => new IOException("staging cleanup: " + path);

        Task firstDisposal = operation.DisposeAsync().AsTask();
        Func<Task> dispose = () => firstDisposal;

        await dispose.Should().ThrowAsync<Exception>();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Succeeded);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Failed);
        operation.Completion.CleanupErrors.Should().HaveCount(3);
        operation.Completion.CleanupErrors.Select(error => error.Message).Should().Contain("retained resource cleanup");
        fileSystem.CleanupAttempts.Should().HaveCount(2);
        Task repeatedDisposal = operation.DisposeAsync().AsTask();
        repeatedDisposal.Should().BeSameAs(firstDisposal);
        await ((Func<Task>)(() => repeatedDisposal)).Should().ThrowAsync<Exception>();
        resource.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task DisposeAsync_UnexpectedStagingFailure_StillAwaitsRetainedCleanupAndReportsBothFailures()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        IAsyncDisposable resource = Substitute.For<IAsyncDisposable>();
        TaskCompletionSource releaseResource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        resource.DisposeAsync().Returns(new ValueTask(releaseResource.Task));
        PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem, resource);
        await operation.CommitAsync(CancellationToken.None);
        fileSystem.DeleteFailure = _ => new InvalidOperationException("unexpected staging failure");

        Task disposal = operation.DisposeAsync().AsTask();
        disposal.IsCompleted.Should().BeFalse();
        releaseResource.SetException(new IOException("retained cleanup failure"));

        await ((Func<Task>)(() => disposal)).Should().ThrowAsync<Exception>();
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Failed);
        operation.Completion.CleanupErrors.Select(error => error.Message)
            .Should().BeEquivalentTo("unexpected staging failure", "retained cleanup failure");
    }

    [Fact]
    public async Task DisposeAsync_ExpectedStagingAccessDenied_ReportsFailureWithoutChangingPublishedOutput()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1])), fileSystem.FileSystem);
        await operation.CommitAsync(CancellationToken.None);
        fileSystem.DeleteFailure = _ => new UnauthorizedAccessException("staging access denied");

        Func<Task> disposal = () => operation.DisposeAsync().AsTask();

        await disposal.Should().ThrowAsync<AggregateException>();
        operation.Completion.CleanupErrors.Should().ContainSingle().Which.Should().BeOfType<UnauthorizedAccessException>();
        operation.Completion.CompletedEffects.Should().ContainSingle();
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("item")].Should().Equal(1);
    }

    [Fact]
    public async Task DisposeAsync_RetainedCleanupIsCanceled_ReportsFailedCleanupWithoutTargetMutation()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        using CancellationTokenSource source = new();
        source.Cancel();
        IAsyncDisposable resource = Substitute.For<IAsyncDisposable>();
        resource.DisposeAsync().Returns(new ValueTask(Task.FromCanceled(source.Token)));
        PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem, resource);
        operation.Preview();

        Func<Task> dispose = () => operation.DisposeAsync().AsTask();

        await dispose.Should().ThrowAsync<OperationCanceledException>();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.NotStarted);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Failed);
        operation.Completion.CleanupErrors.Should().ContainSingle().Which.Should().BeOfType<TaskCanceledException>();
        fileSystem.FileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_AfterFailedCommit_CleansOwnedStagesWithoutRollingBackOutput()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        IAsyncDisposable resource = Substitute.For<IAsyncDisposable>();
        fileSystem.AfterPublishFile = _ => fileSystem.SetFile(PreparedOperationTestFileSystem.Destination("b"), 99);
        PreparedOperation operation = new(PreparedOperationTestFileSystem.Plan(
            new("a", PreparedOutputKind.File, [1]), new("b", PreparedOutputKind.File)), fileSystem.FileSystem, resource);
        await ((Func<Task>)(() => operation.CommitAsync(CancellationToken.None))).Should().ThrowAsync<IOException>();

        await operation.DisposeAsync();

        operation.Completion.Status.Should().Be(PreparedCommitStatus.Failed);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Succeeded);
        operation.Completion.CompletedEffects.Should().ContainSingle();
        fileSystem.CleanupAttempts.Should().ContainSingle();
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("a")].Should().Equal(1);
        fileSystem.Files[PreparedOperationTestFileSystem.Destination("b")].Should().Equal(99);
        await resource.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentRepeatedCalls_ShareOnePendingCleanup()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        IAsyncDisposable resource = Substitute.For<IAsyncDisposable>();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        resource.DisposeAsync().Returns(new ValueTask(release.Task));
        PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem, resource);

        Task first = operation.DisposeAsync().AsTask();
        Task second = operation.DisposeAsync().AsTask();

        first.Should().BeSameAs(second);
        first.IsCompleted.Should().BeFalse();
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.InProgress);
        release.SetResult();
        await Task.WhenAll(first, second);
        resource.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task DisposeAsync_RetainedResourceReadsOwner_SeesDisposalBeforeItsCallbackRuns()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        IAsyncDisposable resource = Substitute.For<IAsyncDisposable>();
        PreparedOperation? operation = null;
        Task? reentrantDisposal = null;
        resource.DisposeAsync().Returns(_ =>
        {
            operation!.State.Should().Be(PreparedOperationState.Disposing);
            operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.InProgress);
            operation.Invoking(instance => instance.Preview()).Should().Throw<ObjectDisposedException>();
            reentrantDisposal = operation.DisposeAsync().AsTask();
            return ValueTask.CompletedTask;
        });
        operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), fileSystem.FileSystem, resource);

        Task disposal = operation.DisposeAsync().AsTask();
        await disposal;

        reentrantDisposal.Should().BeSameAs(disposal);
        operation.State.Should().Be(PreparedOperationState.Disposed);
        resource.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task CommitAsync_ConcurrentCommitAndPreviews_OnlyOneAttemptPublishesAndPreviewsRemainStable()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fileSystem.BeforeWriteAsync = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        PreparedOperationPlan plan = PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1]));
        await using PreparedOperation operation = new(plan, fileSystem.FileSystem);
        Task<PreparedOperationCompletion> first = operation.CommitAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            operation.State.Should().Be(PreparedOperationState.Committing);
            Func<Task> second = () => Task.Run(() => operation.CommitAsync(CancellationToken.None));
            await second.Should().ThrowAsync<InvalidOperationException>();
            PreparedOperationPlan[] previews = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(operation.Preview)));
            previews.Should().OnlyContain(preview => ReferenceEquals(preview, plan));
        }
        finally
        {
            release.TrySetResult();
        }

        (await first).Status.Should().Be(PreparedCommitStatus.Succeeded);
        operation.Completion.CompletedEffects.Should().ContainSingle();
        fileSystem.FileSystem.Received(1).PublishFileNoReplace(Arg.Any<string>(), PreparedOperationTestFileSystem.Destination("item"));
    }

    [Fact]
    public async Task DisposeAsync_DuringActiveCommit_RejectsNewUseAndWaitsBeforeReleasingResources()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        IAsyncDisposable resource = Substitute.For<IAsyncDisposable>();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fileSystem.BeforeWriteAsync = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        PreparedOperation operation = new(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File, [1])), fileSystem.FileSystem, resource);
        Task<PreparedOperationCompletion> commit = operation.CommitAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Task dispose = operation.DisposeAsync().AsTask();
        try
        {
            dispose.IsCompleted.Should().BeFalse();
            operation.State.Should().Be(PreparedOperationState.Disposing);
            operation.Invoking(instance => instance.Preview()).Should().Throw<ObjectDisposedException>();
            await resource.DidNotReceive().DisposeAsync();
            fileSystem.CleanupAttempts.Should().BeEmpty();
            Func<Task> nextCommit = () => operation.CommitAsync(CancellationToken.None);
            await nextCommit.Should().ThrowAsync<ObjectDisposedException>();
        }
        finally
        {
            release.TrySetResult();
        }

        (await commit).Status.Should().Be(PreparedCommitStatus.Succeeded);
        await dispose;
        await resource.Received(1).DisposeAsync();
        operation.State.Should().Be(PreparedOperationState.Disposed);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Succeeded);
    }

    [Fact]
    public void Constructor_NullRequiredDependencies_Rejects()
    {
        PreparedOperationTestFileSystem fileSystem = new();
        Action nullPlan = () => _ = new PreparedOperation(null!, fileSystem.FileSystem);
        Action nullFileSystem = () => _ = new PreparedOperation(
            PreparedOperationTestFileSystem.Plan(new("item", PreparedOutputKind.File)), null!);

        nullPlan.Should().Throw<ArgumentNullException>();
        nullFileSystem.Should().Throw<ArgumentNullException>();
    }

    private static Stream CreateFailingStagingStream(
        CancellationTokenSource cancellation, string phase, Exception primary, Exception? closeFailure, bool synchronousClose)
    {
        Stream stream = Substitute.For<Stream>();
        stream.WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<CancellationToken>().Should().Be(cancellation.Token);
            if (phase is "write" or "cancel-write")
            {
                if (phase == "cancel-write")
                {
                    cancellation.Cancel();
                }

                return ValueTask.FromException(primary);
            }

            return ValueTask.CompletedTask;
        });
        stream.FlushAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<CancellationToken>().Should().Be(cancellation.Token);
            if (phase is "flush" or "cancel-flush")
            {
                if (phase == "cancel-flush")
                {
                    cancellation.Cancel();
                }

                return Task.FromException(primary);
            }

            return Task.CompletedTask;
        });
        stream.DisposeAsync().Returns(_ =>
        {
            if (closeFailure is null)
            {
                return ValueTask.CompletedTask;
            }

            if (synchronousClose)
            {
                throw closeFailure;
            }

            return ValueTask.FromException(closeFailure);
        });
        return stream;
    }
}
