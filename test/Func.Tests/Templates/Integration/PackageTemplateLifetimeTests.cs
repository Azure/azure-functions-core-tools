// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Tests.Templates.PackageIntake;
using Azure.Functions.Cli.Tests.Templates.Preparation;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Integration;

public sealed class PackageTemplateLifetimeTests
{
    [Fact]
    public async Task DisposeAsync_PreparedOperation_RetainsAndReleasesPackageAndNativeSessionOnce()
    {
        var fixture = new PackageTemplateFixture();
        fixture.SetCandidates(fixture.Candidate());
        var operation = await fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Active);
        fixture.Session.DisposeCalls.Should().Be(0);
        await operation.CommitAsync(CancellationToken.None);
        fixture.Session.DisposeCalls.Should().Be(0);

        await operation.DisposeAsync();
        await operation.DisposeAsync();

        fixture.Session.DisposeCalls.Should().Be(1);
        fixture.Lease.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Succeeded);
    }

    [Fact]
    public async Task PrepareAsync_ExplicitArchiveInvalid_DoesNotOpenAdapterOrResolveAnotherSource()
    {
        var fixture = new PackageTemplateFixture { ArchiveBytes = [0, 1, 2] };

        var action = () => fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidTemplatePackageException>();
        fixture.Adapter.ReceivedCalls().Should().BeEmpty();
        fixture.ProjectResolver.ReceivedCalls().Should().BeEmpty();
        fixture.Storage.FileSystem.Received(1).OpenReadIfExists(TemplatePackageTestData.ArchivePath);
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"constraints":[]}""")]
    [InlineData("""{"constraints":{"same":{"type":"fixture"},"same":{"type":"fixture"}}}""")]
    public async Task PrepareAsync_FixtureAdapterRejectsRawConstraintMetadata_PropagatesBeforeDescriptorProjection(string rawMetadata)
    {
        var fixture = new PackageTemplateFixture
        {
            ArchiveBytes = TemplatePackageTestData.Archive(
                [new("content/.template.config/template.json", rawMetadata)],
                TemplatePackageTestData.Manifest(types: ["Template", "IntegrationFixture"])),
        };
        var descriptorProjected = false;
        fixture.Open = async (lease, _, token) =>
        {
            using var retained = lease.OpenRead();
            using var archive = new ZipArchive(retained, ZipArchiveMode.Read);
            using var reader = new StreamReader(archive.GetEntry("content/.template.config/template.json")!.Open());
            var original = await reader.ReadToEndAsync(token);
            TemplateConstraintDeclarationReader.Read(original);
            descriptorProjected = true;
            return new FixtureSession([fixture.Candidate()]);
        };

        var action = () => fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidTemplateMetadataException>();
        descriptorProjected.Should().BeFalse();
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_UnsupportedNativeDeclaration_PreservesFailureWithoutFallback()
    {
        var fixture = new PackageTemplateFixture();
        var failure = new NotSupportedException("Fixture native declaration is not accepted.");
        fixture.Open = (_, _, _) => Task.FromException<PackageTemplateSession>(failure);

        var action = () => fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<NotSupportedException>()).Which.Should().BeSameAs(failure);
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_RenderFails_PreservesFailureAndReleasesResources()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate();
        var failure = new IOException("Fixture capture failed.");
        candidate.DuringRender = _ => Task.FromException(failure);
        fixture.SetCandidates(candidate);

        var action = () => fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
        fixture.Session.DisposeCalls.Should().Be(1);
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_RenderAndCleanupFail_PreservesBothFailuresAndReleasesPackage()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate();
        var renderFailure = new IOException("Fixture rendering failed.");
        var cleanupFailure = new UnauthorizedAccessException("Fixture cleanup failed.");
        candidate.DuringRender = _ => Task.FromException(renderFailure);
        fixture.SetCandidates(candidate);
        fixture.Session.CleanupFailure = cleanupFailure;

        var action = () => fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<AggregateException>()).Which;
        failure.InnerExceptions.Should().Equal(renderFailure, cleanupFailure);
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareAsync_IndependentlyCanceledCleanup_PreservesPrimaryFailureAndBothCancellationTokens(bool preparationCanceled)
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate();
        using var preparationCancellation = new CancellationTokenSource();
        using var cleanupCancellation = new CancellationTokenSource();
        preparationCancellation.Cancel();
        cleanupCancellation.Cancel();
        Exception primary = preparationCanceled
            ? new OperationCanceledException("Preparation independently canceled.", preparationCancellation.Token)
            : new IOException("Rendering failed before cleanup.");
        candidate.DuringRender = _ => Task.FromException(primary);
        fixture.SetCandidates(candidate);
        fixture.Session.CleanupFailure = new OperationCanceledException("Cleanup independently canceled.", cleanupCancellation.Token);

        var action = () => fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);

        var failure = (await action.Should().ThrowExactlyAsync<AggregateException>()).Which;
        failure.InnerExceptions.Should().HaveCount(2);
        if (preparationCanceled)
        {
            var canceled = failure.InnerExceptions[0].Should().BeOfType<TaskCanceledException>().Which;
            canceled.CancellationToken.Should().Be(preparationCancellation.Token);
            canceled.Task.Should().NotBeNull();
            canceled.Task!.IsCanceled.Should().BeTrue();
            canceled.Task.Exception.Should().BeNull();
        }
        else
        {
            failure.InnerExceptions[0].Should().BeSameAs(primary);
        }

        var cleanup = failure.InnerExceptions[1].Should().BeOfType<TaskCanceledException>().Which;
        cleanup.CancellationToken.Should().Be(cleanupCancellation.Token).And.NotBe(preparationCancellation.Token);
        cleanup.Task.Should().NotBeNull();
        cleanup.Task!.IsCanceled.Should().BeTrue();
        cleanup.Task.Exception.Should().BeNull();
        fixture.Session.DisposeCalls.Should().Be(1);
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
        var read = () => fixture.Lease.OpenRead();
        read.Should().Throw<ObjectDisposedException>();
        fixture.Storage.Files.Should().BeEmpty();
        fixture.Storage.FileSystem.DidNotReceive().PublishFileNoReplace(Arg.Any<string>(), Arg.Any<string>());
        fixture.Storage.FileSystem.DidNotReceive().CreateNewDirectory(Arg.Any<string>());
    }

    [Fact]
    public async Task DisposeAsync_CleanupCancellationAlone_RemainsObservableAndReleasesPackage()
    {
        var fixture = new PackageTemplateFixture();
        fixture.SetCandidates(fixture.Candidate());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Session.CleanupFailure = new OperationCanceledException("Native cleanup canceled.", cancellation.Token);
        var operation = await fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);

        var action = () => operation.DisposeAsync().AsTask();

        (await action.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        operation.Completion.Status.Should().Be(PreparedCommitStatus.NotStarted);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Failed);
        operation.Completion.CleanupErrors.Should().ContainSingle()
            .Which.Should().BeOfType<TaskCanceledException>().Which.CancellationToken.Should().Be(cancellation.Token);
        fixture.Session.DisposeCalls.Should().Be(1);
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("before-intake")]
    [InlineData("adapter")]
    [InlineData("project-resolution")]
    [InlineData("render")]
    public async Task PrepareAsync_CanceledAtJoin_DoesNotReturnSuccessOrPublish(string stage)
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(TemplateType.Item);
        fixture.SetCandidates(candidate);
        using var cancellation = new CancellationTokenSource();
        if (stage == "before-intake")
        {
            await cancellation.CancelAsync();
        }

        fixture.Open = (_, _, _) =>
        {
            if (stage == "adapter")
            {
                cancellation.Cancel();
            }

            return Task.FromResult<PackageTemplateSession>(fixture.Session);
        };
        fixture.ProjectResolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (stage == "project-resolution")
            {
                cancellation.Cancel();
            }

            return Task.FromResult<ProjectResolutionResult>(new ProjectResolutionResult.Resolved(fixture.Project, "Fixture project."));
        });
        candidate.DuringRender = _ => stage == "render" ? cancellation.CancelAsync() : Task.CompletedTask;

        var action = () => fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Storage.Files.Should().BeEmpty();
        if (stage == "before-intake")
        {
            fixture.Lease.Should().BeNull();
            fixture.Storage.FileSystem.ReceivedCalls().Should().BeEmpty();
        }
        else if (stage == "project-resolution")
        {
            fixture.Adapter.ReceivedCalls().Should().BeEmpty();
            fixture.Session.DisposeCalls.Should().Be(0);
        }
        else
        {
            fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
            fixture.Session.DisposeCalls.Should().Be(1);
        }
    }

    [Fact]
    public async Task PrepareAsync_CancellationAndCleanupFail_PreservesCancellationAlongsideCleanupFailure()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate();
        fixture.SetCandidates(candidate);
        using var cancellation = new CancellationTokenSource();
        candidate.DuringRender = _ => cancellation.CancelAsync();
        var cleanupFailure = new IOException("Fixture cleanup failed.");
        fixture.Session.CleanupFailure = cleanupFailure;

        var action = () => fixture.Application.PrepareAsync(fixture.Request(), cancellation.Token);

        var failure = (await action.Should().ThrowAsync<AggregateException>()).Which;
        failure.InnerExceptions.Should().ContainSingle(exception => exception is OperationCanceledException);
        failure.InnerExceptions.Should().Contain(cleanupFailure);
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
    }

    [Fact]
    public async Task CommitAsync_PublicationConflict_PreservesExistingFileAndCompletion()
    {
        var fixture = new PackageTemplateFixture();
        fixture.SetCandidates(fixture.Candidate());
        await using var operation = await fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);
        var occupied = PreparedOperationTestFileSystem.Destination("app.bin");
        fixture.Storage.SetFile(occupied, [42]);

        var action = () => operation.CommitAsync(CancellationToken.None);

        await action.Should().ThrowAsync<IOException>();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Failed);
        operation.Completion.CompletedEffects.Should().BeEmpty();
        operation.Completion.CommitError.Should().BeOfType<IOException>();
        fixture.Storage.Files[occupied].Should().Equal([42]);
    }

    [Fact]
    public async Task CommitAsync_PublicationFailsAfterOneFile_ReportsOnlyActualCompletedEffects()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(TemplateType.Item);
        candidate.Capture = new([new("a.txt", PreparedOutputKind.File, "first"u8), new("b.txt", PreparedOutputKind.File, "second"u8)]);
        fixture.SetCandidates(candidate);
        await using var operation = await fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);
        var failure = new IOException("Fixture second publication failed.");
        fixture.Storage.BeforePublishFile = path =>
        {
            if (path.EndsWith("b.txt", StringComparison.Ordinal))
            {
                throw failure;
            }
        };

        var action = () => operation.CommitAsync(CancellationToken.None);

        (await action.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(failure);
        operation.Completion.CompletedEffects.Should().Contain(effect => effect.Path.EndsWith("a.txt", StringComparison.Ordinal));
        operation.Completion.CompletedEffects.Should().NotContain(effect => effect.Path.EndsWith("b.txt", StringComparison.Ordinal));
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Failed);
    }

    [Fact]
    public async Task CommitAsync_CanceledBeforePublication_ConsumesTheOnlyAttempt()
    {
        var fixture = new PackageTemplateFixture();
        fixture.SetCandidates(fixture.Candidate(TemplateType.Item));
        await using var operation = await fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var first = () => operation.CommitAsync(cancellation.Token);
        await first.Should().ThrowAsync<OperationCanceledException>();
        var second = () => operation.CommitAsync(CancellationToken.None);

        await second.Should().ThrowAsync<InvalidOperationException>();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.Canceled);
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_NativeCleanupFails_LeavesFailureObservableInPreparedCompletion()
    {
        var fixture = new PackageTemplateFixture();
        fixture.SetCandidates(fixture.Candidate());
        var operation = await fixture.Application.PrepareAsync(fixture.Request(), CancellationToken.None);
        var cleanupFailure = new IOException("Fixture native cleanup failed.");
        fixture.Session.CleanupFailure = cleanupFailure;

        var action = async () => await operation.DisposeAsync();

        (await action.Should().ThrowAsync<IOException>()).Which.Should().BeSameAs(cleanupFailure);
        operation.Completion.CleanupStatus.Should().Be(PreparedCleanupStatus.Failed);
        operation.Completion.CleanupErrors.Should().Contain(cleanupFailure);
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
    }

    [Theory]
    [InlineData("project-without-config")]
    [InlineData("item-with-config")]
    public async Task PrepareAsync_CaptureHasWrongConfigurationKind_FailsBeforePublication(string scenario)
    {
        var fixture = new PackageTemplateFixture();
        var type = scenario == "item-with-config" ? TemplateType.Item : TemplateType.Project;
        var candidate = fixture.Candidate(type);
        candidate.Capture = new([new("host.json", PreparedOutputKind.File, "{}"u8)],
            type == TemplateType.Item ? new(".", "{}"u8) : null);
        fixture.SetCandidates(candidate);

        var action = () => fixture.Application.PrepareAsync(fixture.Request(type), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
        fixture.Storage.Files.Should().BeEmpty();
        fixture.Lease!.CleanupOutcome.Should().Be(TemplatePackageCleanupOutcome.Released);
    }
}
