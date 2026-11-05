// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers.Binary;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Azure.Functions.Cli.Projects.FileWriting;
using Microsoft.Win32.SafeHandles;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Projects.FileWriting;

public sealed partial class WindowsProjectWriteSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LaneBFileWritingTests", Guid.NewGuid().ToString("N"));

    public WindowsProjectWriteSessionTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [WindowsFact]
    public async Task WriteAsync_ValidOutput_CreatesExactBytesEmptyFilesAndDirectories()
    {
        var writer = new ProjectFileWriter(new());
        Dictionary<string, ImmutableArray<byte>> files = new()
        {
            ["src/function/run.cs"] = [0, 1, 2, 255],
            [".gitignore"] = [],
        };
        string destination = Path.Combine(_root, "project");

        ProjectWriteResult result = await writer.WriteAsync(destination, files, ["empty", "src"], CancellationToken.None);

        result.Succeeded.Should().BeTrue(string.Join(Environment.NewLine, result.Effects.Select(effect => effect.Error)));
        result.Effects.Where(effect => effect.Status == ProjectWriteStatus.Created && effect.IsDirectory)
            .Select(effect => effect.Path).Should().BeEquivalentTo(destination, "empty", "src", "src\\function");
        File.ReadAllBytes(Path.Combine(destination, "src", "function", "run.cs")).Should().Equal(0, 1, 2, 255);
        File.ReadAllBytes(Path.Combine(destination, ".gitignore")).Should().BeEmpty();
        Directory.Exists(Path.Combine(destination, "empty")).Should().BeTrue();
    }

    [WindowsFact]
    public async Task WriteAsync_ExistingDirectory_PreservesUnrelatedContentAndGitPointerFile()
    {
        var writer = new ProjectFileWriter(new());
        File.WriteAllText(Path.Combine(_root, "existing.txt"), "preserve");
        File.WriteAllText(Path.Combine(_root, ".git"), "gitdir: elsewhere");

        ProjectWriteResult result = await writer.WriteAsync(_root, Files("new.txt"), [], CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Effects[0].Status.Should().Be(ProjectWriteStatus.Preserved);
        File.ReadAllText(Path.Combine(_root, "existing.txt")).Should().Be("preserve");
        File.ReadAllText(Path.Combine(_root, ".git")).Should().Be("gitdir: elsewhere");
    }

    [WindowsFact]
    public async Task WriteAsync_MidPlanWriteFailure_ReportsActualFilesDirectoriesCleanupAndUnattemptedWork()
    {
        using var session = new SecondWriteFailsSession(_root);
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        fileSystem.OpenSession(_root, Arg.Any<CancellationToken>()).Returns(session);
        var writer = new ProjectFileWriter(fileSystem);

        ProjectWriteResult result = await writer.WriteAsync(_root, Files("src/one", "src/two", "src/three"), ["empty"], CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Effects.Where(effect => effect.IsDirectory && effect.Status == ProjectWriteStatus.Created)
            .Select(effect => effect.Path).Should().BeEquivalentTo("empty", "src");
        result.Effects.Single(effect => effect.Path == "src\\one").Status.Should().Be(ProjectWriteStatus.Created);
        result.Effects.Single(effect => effect.Path == "src\\two").Status.Should().Be(ProjectWriteStatus.RemovedAfterFailure);
        result.Effects.Single(effect => effect.Path == "src\\three").Status.Should().Be(ProjectWriteStatus.NotAttempted);
        File.ReadAllBytes(Path.Combine(_root, "src", "one")).Should().Equal(1, 2, 3);
        File.Exists(Path.Combine(_root, "src", "two")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "src", "three")).Should().BeFalse();
        Directory.Exists(Path.Combine(_root, "empty")).Should().BeTrue();
    }

    [WindowsFact]
    public async Task WriteAsync_ExistingFileCollision_FailsPreflightWithoutCreatingDirectoriesOrOtherFiles()
    {
        var writer = new ProjectFileWriter(new());
        File.WriteAllText(Path.Combine(_root, "existing"), "original");

        ProjectWriteResult result = await writer.WriteAsync(_root, Files("new", "existing"), ["empty"], CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Effects.Should().Contain(effect => effect.Path == "existing" && effect.Status == ProjectWriteStatus.Failed);
        File.ReadAllText(Path.Combine(_root, "existing")).Should().Be("original");
        File.Exists(Path.Combine(_root, "new")).Should().BeFalse();
        Directory.Exists(Path.Combine(_root, "empty")).Should().BeFalse();
    }

    [WindowsFact]
    public async Task WriteAsync_ExistingFileAtPlannedDirectory_FailsWithoutEffects()
    {
        var writer = new ProjectFileWriter(new());
        File.WriteAllText(Path.Combine(_root, "src"), "original");

        ProjectWriteResult result = await writer.WriteAsync(_root, Files("src/file"), [], CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Effects.Should().NotContain(effect => effect.Status == ProjectWriteStatus.Created);
        File.ReadAllText(Path.Combine(_root, "src")).Should().Be("original");
    }

    [WindowsFact]
    public async Task WriteAsync_ExistingDirectoryAtPlannedFile_FailsWithoutEffects()
    {
        var writer = new ProjectFileWriter(new());
        Directory.CreateDirectory(Path.Combine(_root, "file"));

        ProjectWriteResult result = await writer.WriteAsync(_root, Files("file"), [], CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        Directory.Exists(Path.Combine(_root, "file")).Should().BeTrue();
    }

    [WindowsFact]
    public void CreateDirectory_DirectoryArrivesAfterPreflight_DoesNotAdoptOrReplaceIt()
    {
        using var session = new WindowsProjectWriteSession(_root, CancellationToken.None);
        session.DirectoryExists("nested").Should().BeFalse();
        string nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "original"), "keep");

        Action action = () => session.CreateDirectory("nested");

        action.Should().Throw<IOException>();
        File.ReadAllText(Path.Combine(nested, "original")).Should().Be("keep");
    }

    [WindowsFact]
    public async Task CreateFileAsync_FileArrivesAfterPreflight_DoesNotOverwriteIt()
    {
        using var session = new WindowsProjectWriteSession(_root, CancellationToken.None);
        session.CheckFileAbsent("file");
        File.WriteAllText(Path.Combine(_root, "file"), "original");

        Func<Task> action = () => session.CreateFileAsync("file", [9], CancellationToken.None);

        await action.Should().ThrowAsync<IOException>();
        File.ReadAllText(Path.Combine(_root, "file")).Should().Be("original");
    }

    [WindowsFact]
    public async Task CreateFileAsync_PinnedDestinationAndAncestors_CannotBeRenamedDuringWrite()
    {
        string ancestor = Path.Combine(_root, "ancestor");
        string destination = Path.Combine(ancestor, "project");
        Directory.CreateDirectory(destination);
        using (var session = new WindowsProjectWriteSession(destination, CancellationToken.None))
        {
            Action renameAncestor = () => Directory.Move(ancestor, ancestor + "-moved");
            Action renameDestination = () => Directory.Move(destination, destination + "-moved");

            renameAncestor.Should().Throw<IOException>();
            renameDestination.Should().Throw<IOException>();
            ProjectWriteEffect result = await session.CreateFileAsync("file", [7], CancellationToken.None);

            result.Status.Should().Be(ProjectWriteStatus.Created);
            File.ReadAllBytes(Path.Combine(destination, "file")).Should().Equal(7);
        }

        Directory.Move(ancestor, ancestor + "-moved");
        Directory.Exists(ancestor + "-moved").Should().BeTrue();
    }

    [WindowsFact]
    public void DirectoryExists_PinnedPlannedDirectory_CannotBeRenamed()
    {
        string nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        using var session = new WindowsProjectWriteSession(_root, CancellationToken.None);
        session.DirectoryExists("nested").Should().BeTrue();

        Action action = () => Directory.Move(nested, nested + "-moved");

        action.Should().Throw<IOException>();
    }

    [WindowsFact]
    public async Task CreateFileAsync_WriteFails_RemovesOnlyTheNewFileAndReportsFailure()
    {
        using var session = new FailingSession(_root, failCleanup: false);
        File.WriteAllText(Path.Combine(_root, "existing"), "keep");

        ProjectWriteEffect result = await session.CreateFileAsync("file", [1, 2], CancellationToken.None);

        result.Status.Should().Be(ProjectWriteStatus.RemovedAfterFailure);
        result.Error.Should().BeOfType<IOException>().Which.Message.Should().Be("injected write failure");
        result.CleanupError.Should().BeNull();
        File.Exists(Path.Combine(_root, "file")).Should().BeFalse();
        File.ReadAllText(Path.Combine(_root, "existing")).Should().Be("keep");
    }

    [WindowsFact]
    public async Task CreateFileAsync_CleanupFails_ReportsPrimaryFailureAndUncertainRemainingFileSeparately()
    {
        using var session = new FailingSession(_root, failCleanup: true);

        ProjectWriteEffect result = await session.CreateFileAsync("file", [1, 2], CancellationToken.None);

        result.Status.Should().Be(ProjectWriteStatus.Uncertain);
        result.Error.Should().BeOfType<IOException>().Which.Message.Should().Be("injected write failure");
        result.CleanupError.Should().BeOfType<IOException>().Which.Message.Should().Be("injected cleanup failure");
        File.ReadAllBytes(Path.Combine(_root, "file")).Should().Equal(1);
    }

    [WindowsFact]
    public async Task CreateFileAsync_CanceledDuringWrite_RemovesIncompleteFileAndReportsCancellation()
    {
        using CancellationTokenSource cancellation = new();
        using var session = new CancelingSession(_root, cancellation);

        ProjectWriteEffect result = await session.CreateFileAsync("file", [1, 2], cancellation.Token);

        result.Status.Should().Be(ProjectWriteStatus.RemovedAfterFailure);
        result.Error.Should().BeOfType<OperationCanceledException>();
        File.Exists(Path.Combine(_root, "file")).Should().BeFalse();
    }

    [WindowsTheory]
    [InlineData(@"\\server\share\project")]
    [InlineData(@"\\?\C:\project")]
    [InlineData("relative")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\parent\.git\project")]
    [InlineData(@"C:\parent\..\project")]
    [InlineData(@"C:\PARENT~1\project")]
    public void Constructor_UnsupportedDestination_RejectsBeforeCreation(string destination)
    {
        Action action = () => _ = new WindowsProjectWriteSession(destination, CancellationToken.None);

        action.Should().Throw<Exception>().Where(exception => exception is ArgumentException || exception is NotSupportedException);
    }

    [WindowsFact]
    public async Task WriteAsync_MissingDestinationParent_RejectsWithoutCreatingIt()
    {
        var writer = new ProjectFileWriter(new());
        string destination = Path.Combine(_root, "missing", "project");

        ProjectWriteResult result = await writer.WriteAsync(destination, Files("file"), [], CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Effects[0].Error.Should().BeOfType<DirectoryNotFoundException>();
        Directory.Exists(Path.Combine(_root, "missing")).Should().BeFalse();
    }

    [WindowsFact]
    public async Task WriteAsync_GitDirectoryDestinationAncestry_RejectsWithoutCreatingFiles()
    {
        string destination = Path.Combine(_root, ".git", "objects");
        Directory.CreateDirectory(destination);
        var writer = new ProjectFileWriter(new());

        Func<Task> action = () => writer.WriteAsync(destination, Files("file"), [], CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        File.Exists(Path.Combine(destination, "file")).Should().BeFalse();
    }

    [WindowsTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteAsync_JunctionInDestinationAncestryOrPlannedDirectory_RejectsWithoutWritingOutside(bool inAncestry)
    {
        string outside = Path.Combine(_root, "outside");
        string junction = Path.Combine(_root, "junction");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(junction);
        var mutation = new DirectoryMutation();
        try
        {
            mutation.SetJunction(junction, outside);
            var writer = new ProjectFileWriter(new());
            string destination = inAncestry ? Path.Combine(junction, "project") : _root;
            string file = inAncestry ? "file" : "junction/file";

            ProjectWriteResult result = await writer.WriteAsync(destination, Files(file), [], CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Effects.Should().NotContain(effect => effect.Status == ProjectWriteStatus.Created);
            Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [WindowsFact]
    public async Task CreateFileAsync_JunctionArrivesAfterPreflight_RejectsWithoutFollowingIt()
    {
        string outside = Path.Combine(_root, "outside");
        string junction = Path.Combine(_root, "file");
        Directory.CreateDirectory(outside);
        var mutation = new DirectoryMutation();
        try
        {
            using (var session = new WindowsProjectWriteSession(_root, CancellationToken.None))
            {
                session.CheckFileAbsent("file");
                Directory.CreateDirectory(junction);
                mutation.SetJunction(junction, outside);

                Func<Task> action = () => session.CreateFileAsync("file", [9], CancellationToken.None);

                await action.Should().ThrowAsync<IOException>();
                Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
            }
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [WindowsTheory]
    [InlineData(0x40000000u)]
    [InlineData(0x100u)]
    [InlineData(0x80u)]
    public async Task CreateFileAsync_PinnedDirectoryMetadataMutation_DoesNotFollowReparsePoint(uint access)
    {
        string outside = Path.Combine(_root, "outside");
        string nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(nested);
        var mutation = new DirectoryMutation();
        try
        {
            using var session = new WindowsProjectWriteSession(_root, CancellationToken.None);
            session.DirectoryExists("nested").Should().BeTrue();

            Exception? error = Record.Exception(() => mutation.SetJunction(nested, outside, access));

            if (error is null)
            {
                Func<Task> action = () => session.CreateFileAsync("nested\\file", [9], CancellationToken.None);
                await action.Should().ThrowAsync<NotSupportedException>();
            }
            else
            {
                error.Should().BeOfType<IOException>();
                File.GetAttributes(nested).Should().NotHaveFlag(FileAttributes.ReparsePoint);
            }

            Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(nested);
        }
    }

    [WindowsFact]
    public async Task WriteAsync_ExistingWritableAncestorHandle_FailsWithoutEffectsAndReleasesOtherHandles()
    {
        string ancestor = Path.Combine(_root, "ancestor");
        string destination = Path.Combine(ancestor, "project");
        Directory.CreateDirectory(destination);
        var mutation = new DirectoryMutation();
        var writer = new ProjectFileWriter(new());
        using (SafeFileHandle writable = mutation.OpenWrite(ancestor))
        {
            ProjectWriteResult result = await writer.WriteAsync(destination, Files("file"), [], CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Effects.Should().NotContain(effect => effect.Status == ProjectWriteStatus.Created);
            File.Exists(Path.Combine(destination, "file")).Should().BeFalse();
        }

        ProjectWriteResult retry = await writer.WriteAsync(destination, Files("file"), [], CancellationToken.None);
        retry.Succeeded.Should().BeTrue();
        Directory.Move(ancestor, ancestor + "-moved");
    }

    [WindowsTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateEntry_ParentRetaggedAfterSafetyCheck_NeverCreatesOutsidePinnedDirectory(bool directory)
    {
        string outside = Path.Combine(_root, "outside");
        string nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(nested);
        try
        {
            using var session = new RetaggingSession(_root, nested, outside);
            session.DirectoryExists("nested").Should().BeTrue();
            ProjectWriteEffect? effect = null;

            Exception? error = await Record.ExceptionAsync(async () =>
            {
                effect = directory
                    ? session.CreateDirectory("nested\\child")
                    : await session.CreateFileAsync("nested\\child", [9], CancellationToken.None);
            });

            session.Retagged.Should().BeTrue();
            session.CreatedOutside.Should().BeFalse("parent-relative no-reparse creation must not resolve the junction target");
            if (error is null)
            {
                effect!.Status.Should().Be(ProjectWriteStatus.RemovedAfterFailure);
            }
            else
            {
                error.Should().BeOfType<IOException>();
            }

            Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(nested);
        }
    }

    [WindowsFact]
    public async Task CreateFileAsync_ReparseMutationAttemptDuringWrite_CleansUpOriginalFileWithoutFollowingJunction()
    {
        string destination = Path.Combine(_root, "project");
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(outside);
        try
        {
            using (var session = new RetagDuringWriteSession(destination, outside))
            {
                ProjectWriteEffect effect = await session.CreateFileAsync("file", [1, 2, 3], CancellationToken.None);

                effect.Status.Should().Be(ProjectWriteStatus.RemovedAfterFailure);
                if (session.Retagged)
                {
                    effect.Error.Should().BeOfType<NotSupportedException>();
                }
                else
                {
                    effect.Error.Should().BeOfType<IOException>().Which.InnerException.Should().BeOfType<Win32Exception>();
                }

                effect.CleanupError.Should().BeNull();
                Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
            }
        }
        finally
        {
            Directory.Delete(destination);
        }
    }

    [WindowsFact]
    public void DirectoryExists_DisposedSession_RejectsFurtherUse()
    {
        var session = new WindowsProjectWriteSession(_root, CancellationToken.None);
        session.Dispose();

        Action action = () => session.DirectoryExists("nested");

        action.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(@"\??\C:\private")]
    [InlineData(@"\??\Q:")]
    [InlineData(@"\Device\HarddiskVolume1\private")]
    [InlineData(@"\Device\Mup\server\share")]
    [InlineData(@"\Device\HarddiskVolume")]
    public void ValidateDriveTarget_RedirectedOrSubstitutedMapping_RejectsWithoutChangingDriveMappings(string target)
    {
        Action action = () => WindowsProjectWriteSession.ValidateDriveTarget(target);

        action.Should().Throw<NotSupportedException>().WithMessage("*Redirected or substituted*");
    }

    [Fact]
    public void ValidateDriveTarget_OrdinaryPhysicalVolume_AcceptsMapping()
    {
        Action action = () => WindowsProjectWriteSession.ValidateDriveTarget(@"\Device\HarddiskVolume123");

        action.Should().NotThrow();
    }

    [WindowsFact]
    public void Constructor_RedirectedRootHandle_RejectsPhysicalSubdirectoryAndReleasesHandle()
    {
        Action action = () => _ = new RedirectedRootSession(_root);

        action.Should().Throw<NotSupportedException>().WithMessage("*physical volume root*");
        string probe = Path.Combine(_root, "probe");
        Directory.CreateDirectory(probe);
        Directory.Move(probe, probe + "-moved");
    }

    [WindowsFact]
    public async Task CreateFileAsync_DriveMappingChangesBeforeEffect_RejectsWithoutWriting()
    {
        using var session = new ChangingMappingSession(_root);
        session.CheckFileAbsent("file");
        session.MappingChanged = true;

        Func<Task> action = () => session.CreateFileAsync("file", [1], CancellationToken.None);

        await action.Should().ThrowAsync<NotSupportedException>().WithMessage("*drive mapping changed*");
        File.Exists(Path.Combine(_root, "file")).Should().BeFalse();
    }

    [WindowsTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateEntry_DriveMappingChangesAtCreation_CleansUpOnlyPinnedVolumeEntry(bool directory)
    {
        using var session = new ChangingMappingSession(_root) { ChangeDuringCreate = true };

        ProjectWriteEffect effect = directory
            ? session.CreateDirectory("child")
            : await session.CreateFileAsync("child", [1], CancellationToken.None);

        effect.Status.Should().Be(ProjectWriteStatus.RemovedAfterFailure);
        effect.Error.Should().BeOfType<NotSupportedException>().Which.Message.Should().Contain("drive mapping changed");
        Path.Exists(Path.Combine(_root, "child")).Should().BeFalse();
    }

    [WindowsTheory]
    [InlineData("construction")]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("disposal")]
    [InlineData("early-disposal")]
    public async Task CreateFileAsync_PostCreationStageFailure_CleansUpWithIndependentHandle(string stage)
    {
        using var session = new StageFailureSession(_root, stage);

        ProjectWriteEffect effect = await session.CreateFileAsync("file", [1, 2, 3], CancellationToken.None);

        effect.Status.Should().Be(ProjectWriteStatus.RemovedAfterFailure);
        effect.Error.Should().BeOfType<IOException>().Which.Message.Should().Be($"injected {stage} failure");
        effect.CleanupError.Should().BeNull();
        File.Exists(Path.Combine(_root, "file")).Should().BeFalse();
        using FileStream retry = File.Open(Path.Combine(_root, "file"), FileMode.CreateNew);
    }

    [WindowsFact]
    public async Task CreateFileAsync_WriteAndDisposalFailures_PreservesBothErrorsAndCleansUp()
    {
        using var session = new StageFailureSession(_root, "write+disposal");

        ProjectWriteEffect effect = await session.CreateFileAsync("file", [1], CancellationToken.None);

        effect.Status.Should().Be(ProjectWriteStatus.RemovedAfterFailure);
        effect.Error.Should().BeOfType<IOException>().Which.Message.Should().Be("injected write failure");
        effect.CompletionError.Should().BeOfType<IOException>().Which.Message.Should().Be("injected disposal failure");
        effect.CleanupError.Should().BeNull();
        File.Exists(Path.Combine(_root, "file")).Should().BeFalse();
    }

    [WindowsTheory]
    [InlineData("construction", false)]
    [InlineData("write", false)]
    [InlineData("flush", false)]
    [InlineData("disposal", false)]
    [InlineData("early-disposal", false)]
    [InlineData("validation", false)]
    [InlineData("write+disposal", false)]
    [InlineData("write-unexpected+disposal", false)]
    [InlineData("write", true)]
    public async Task WriteAsync_UnexpectedAfterNativeCreation_CleansUpAndPreservesPartialEffects(string stage, bool failCleanup)
    {
        using var session = new UnexpectedAfterCreationSession(_root, stage, failCleanup: failCleanup);
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        fileSystem.OpenSession(_root, Arg.Any<CancellationToken>()).Returns(session);
        var writer = new ProjectFileWriter(fileSystem);

        Exception? thrown = await Record.ExceptionAsync(() =>
            writer.WriteAsync(_root, Files("src/one", "src/two", "src/three"), [], CancellationToken.None));

        thrown.Should().BeSameAs(session.Failure);
        thrown!.StackTrace.Should().NotBeNullOrWhiteSpace();
        session.CleanupAttempted.Should().BeTrue();
        ProjectWriteEffect effect = thrown.Data[typeof(ProjectWriteEffect)].Should().BeOfType<ProjectWriteEffect>().Subject;
        ProjectWriteResult result = thrown.Data[typeof(ProjectWriteResult)].Should().BeOfType<ProjectWriteResult>().Subject;
        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeFalse();
        result.Effects.Single(item => item.Path == "src").Status.Should().Be(ProjectWriteStatus.Created);
        result.Effects.Single(item => item.Path == "src\\one").Status.Should().Be(ProjectWriteStatus.Created);
        result.Effects.Single(item => item.Path == "src\\two").Should().Be(effect);
        result.Effects.Single(item => item.Path == "src\\three").Status.Should().Be(ProjectWriteStatus.NotAttempted);
        result.Effects.Single(item => item.Path == "src\\three").Error.Should().BeNull();
        if (stage == "write+disposal")
        {
            effect.Error.Should().BeOfType<IOException>().Which.Message.Should().Be("injected write failure");
            effect.CompletionError.Should().BeSameAs(session.Failure);
        }
        else
        {
            effect.Error.Should().BeSameAs(session.Failure);
            if (stage == "write-unexpected+disposal")
            {
                effect.CompletionError.Should().BeSameAs(session.CompletionFailure);
            }
            else
            {
                effect.CompletionError.Should().BeNull();
            }
        }

        effect.CleanupRequested.Should().Be(!failCleanup);
        effect.Status.Should().Be(failCleanup ? ProjectWriteStatus.Uncertain : ProjectWriteStatus.RemovedAfterFailure);
        if (failCleanup)
        {
            effect.CleanupError.Should().BeSameAs(session.CleanupFailure);
        }
        else
        {
            effect.CleanupError.Should().BeNull();
        }

        string directory = Path.Combine(_root, "src");
        string failedFile = Path.Combine(directory, "two");
        File.ReadAllBytes(Path.Combine(directory, "one")).Should().Equal(1, 2, 3);
        File.Exists(failedFile).Should().Be(failCleanup);
        File.Exists(Path.Combine(directory, "three")).Should().BeFalse();
        using (FileStream check = File.Open(failedFile, failCleanup ? FileMode.Open : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            check.CanWrite.Should().BeTrue("the writer must release both file handles");
        }

        Directory.Move(directory, directory + "-moved");
    }

    [WindowsFact]
    public async Task WriteAsync_UnexpectedAfterNativeCreation_ObservationFailureReportsUncertainOrPendingCleanup()
    {
        using var session = new UnexpectedAfterCreationSession(_root, "write", retainHandle: true);
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        fileSystem.OpenSession(_root, Arg.Any<CancellationToken>()).Returns(session);
        var writer = new ProjectFileWriter(fileSystem);
        string failedFile = Path.Combine(_root, "two");
        try
        {
            Exception? thrown = await Record.ExceptionAsync(() =>
                writer.WriteAsync(_root, Files("one", "two", "three"), [], CancellationToken.None));

            thrown.Should().BeSameAs(session.Failure);
            session.CleanupAttempted.Should().BeTrue();
            ProjectWriteEffect effect = thrown!.Data[typeof(ProjectWriteEffect)].Should().BeOfType<ProjectWriteEffect>().Subject;
            ProjectWriteResult result = thrown.Data[typeof(ProjectWriteResult)].Should().BeOfType<ProjectWriteResult>().Subject;
            effect.Error.Should().BeSameAs(session.Failure);
            effect.CleanupRequested.Should().BeTrue();
            effect.Status.Should().BeOneOf(ProjectWriteStatus.CleanupPending, ProjectWriteStatus.Uncertain);
            if (effect.Status == ProjectWriteStatus.Uncertain)
            {
                effect.CleanupError.Should().BeOfType<IOException>().Which.InnerException
                    .Should().BeOfType<Win32Exception>().Which.NativeErrorCode.Should().Be(5);
            }
            else
            {
                effect.CleanupError.Should().BeNull();
            }

            result.Effects.Single(item => item.Path == "one").Status.Should().Be(ProjectWriteStatus.Created);
            result.Effects.Single(item => item.Path == "two").Should().Be(effect);
            result.Effects.Single(item => item.Path == "three").Status.Should().Be(ProjectWriteStatus.NotAttempted);
            Action recreate = () => File.Open(failedFile, FileMode.CreateNew).Dispose();
            recreate.Should().Throw<UnauthorizedAccessException>();
        }
        finally
        {
            session.RetainedHandle?.Dispose();
        }

        File.Exists(failedFile).Should().BeFalse();
        using FileStream check = File.Open(failedFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    [WindowsTheory]
    [InlineData("expected-write")]
    [InlineData("write")]
    public async Task WriteAsync_UnexpectedAfterNativeCreation_CleanupBugPreservesBothExceptions(string stage)
    {
        using var session = new UnexpectedAfterCreationSession(_root, stage, unexpectedCleanup: true);
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        fileSystem.OpenSession(_root, Arg.Any<CancellationToken>()).Returns(session);
        var writer = new ProjectFileWriter(fileSystem);

        Exception? thrown = await Record.ExceptionAsync(() =>
            writer.WriteAsync(_root, Files("one", "two", "three"), [], CancellationToken.None));

        thrown.Should().BeSameAs(stage == "write" ? session.Failure : session.UnexpectedCleanupFailure);
        session.CleanupAttempted.Should().BeTrue();
        ProjectWriteEffect effect = thrown!.Data[typeof(ProjectWriteEffect)].Should().BeOfType<ProjectWriteEffect>().Subject;
        ProjectWriteResult result = thrown.Data[typeof(ProjectWriteResult)].Should().BeOfType<ProjectWriteResult>().Subject;
        effect.Status.Should().Be(ProjectWriteStatus.Uncertain);
        effect.CleanupRequested.Should().BeFalse();
        if (stage == "write")
        {
            effect.Error.Should().BeSameAs(session.Failure);
        }
        else
        {
            effect.Error.Should().BeOfType<IOException>().Which.Message.Should().Be("injected write failure");
        }

        effect.CleanupError.Should().BeSameAs(session.UnexpectedCleanupFailure);
        result.Effects.Single(item => item.Path == "one").Status.Should().Be(ProjectWriteStatus.Created);
        result.Effects.Single(item => item.Path == "two").Should().Be(effect);
        result.Effects.Single(item => item.Path == "three").Status.Should().Be(ProjectWriteStatus.NotAttempted);
        using FileStream check = File.Open(Path.Combine(_root, "two"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [WindowsTheory]
    [InlineData("construction")]
    [InlineData("flush")]
    [InlineData("disposal")]
    public async Task CreateFileAsync_PostCreationFailureAndCleanupFailure_ReportsBothWithoutClaimingRemoval(string stage)
    {
        using var session = new StageFailureSession(_root, stage, failCleanup: true);

        ProjectWriteEffect effect = await session.CreateFileAsync("file", [1], CancellationToken.None);

        effect.Status.Should().Be(ProjectWriteStatus.Uncertain);
        effect.Error.Should().BeOfType<IOException>().Which.Message.Should().Be($"injected {stage} failure");
        effect.CleanupError.Should().BeOfType<IOException>().Which.Message.Should().Be("injected cleanup failure");
        File.Exists(Path.Combine(_root, "file")).Should().BeTrue();
    }

    [WindowsFact]
    public async Task CreateFileAsync_AnotherHandleDelaysDeletion_DoesNotClaimRemovalUntilThatHandleCloses()
    {
        using var session = new StageFailureSession(_root, "write", retainHandle: true);
        string path = Path.Combine(_root, "file");
        try
        {
            ProjectWriteEffect effect = await session.CreateFileAsync("file", [1], CancellationToken.None);

            effect.Status.Should().BeOneOf(ProjectWriteStatus.CleanupPending, ProjectWriteStatus.Uncertain);
            effect.CleanupRequested.Should().BeTrue();
            effect.Error.Should().BeOfType<IOException>().Which.Message.Should().Be("injected write failure");
            if (effect.Status == ProjectWriteStatus.Uncertain)
            {
                effect.CleanupError.Should().BeOfType<IOException>().Which.InnerException
                    .Should().BeOfType<Win32Exception>().Which.NativeErrorCode.Should().Be(5);
            }
            else
            {
                effect.CleanupError.Should().BeNull();
            }
            Action recreate = () => File.Open(path, FileMode.CreateNew).Dispose();
            recreate.Should().Throw<UnauthorizedAccessException>("the remaining handle keeps deletion pending");
        }
        finally
        {
            session.RetainedHandle?.Dispose();
        }

        File.Exists(path).Should().BeFalse();
        using FileStream retry = File.Open(path, FileMode.CreateNew);
    }

    [WindowsFact]
    public async Task WriteAsync_CanceledAfterLastCompletedNativeWrite_PreservesFileAndReturnsCanceled()
    {
        using CancellationTokenSource cancellation = new();
        using var session = new CancelAfterCompleteSession(_root, cancellation);
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        fileSystem.OpenSession(_root, cancellation.Token).Returns(session);
        var writer = new ProjectFileWriter(fileSystem);

        ProjectWriteResult result = await writer.WriteAsync(_root, Files("file"), [], cancellation.Token);

        result.Canceled.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.Effects.Single(effect => !effect.IsDirectory).Status.Should().Be(ProjectWriteStatus.Created);
        File.ReadAllBytes(Path.Combine(_root, "file")).Should().Equal(1, 2, 3);
    }

    [WindowsFact]
    public async Task CreateDirectory_ManySiblings_ValidatesConstantRelevantAncestryAndIgnoresUnrelatedMutation()
    {
        using var session = new CountingSession(_root);
        int firstCost = 0;
        for (int index = 0; index < 128; index++)
        {
            int before = session.CheckCount;
            ProjectWriteEffect effect = session.CreateDirectory($"sibling{index:D3}");
            effect.Status.Should().Be(ProjectWriteStatus.Created);
            int cost = session.CheckCount - before;
            if (index == 0)
            {
                firstCost = cost;
            }

            cost.Should().Be(firstCost, "unrelated retained siblings must not be revalidated");
        }

        string unrelated = Path.Combine(_root, "sibling000");
        string outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        try
        {
            new DirectoryMutation().SetJunction(unrelated, outside, 0x100);

            ProjectWriteEffect effect = await session.CreateFileAsync("sibling127\\file", [1], CancellationToken.None);

            effect.Status.Should().Be(ProjectWriteStatus.Created);
            Func<Task> relevant = () => session.CreateFileAsync("sibling000\\file", [1], CancellationToken.None);
            await relevant.Should().ThrowAsync<NotSupportedException>();
            Directory.EnumerateFileSystemEntries(outside).Should().BeEmpty();
        }
        finally
        {
            session.Dispose();
            Directory.Delete(unrelated);
        }
    }

    [WindowsFact]
    public async Task WriteAsync_MaximumBoundedDepth_CreatesFileAndExplicitParents()
    {
        var writer = new ProjectFileWriter(new());
        string path = string.Join('/', Enumerable.Repeat("nested", ProjectFileWriter.MaximumPathDepth - 1)) + "/file";

        ProjectWriteResult result = await writer.WriteAsync(_root, Files(path), [], CancellationToken.None);

        result.Succeeded.Should().BeTrue(string.Join(Environment.NewLine, result.Effects.Select(effect => effect.Error)));
        File.ReadAllBytes(Path.Combine(_root, path.Replace('/', '\\'))).Should().Equal(1, 2, 3);
    }

    private static Dictionary<string, ImmutableArray<byte>> Files(params string[] paths) =>
        paths.ToDictionary(path => path, _ => ImmutableArray.Create<byte>(1, 2, 3));

    private sealed class FailingSession(string destination, bool failCleanup) : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        protected override async Task WriteContentAsync(Stream stream, ImmutableArray<byte> content, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(content.AsMemory()[..1], cancellationToken);
            throw new IOException("injected write failure");
        }

        protected override void DeleteCreatedEntry(SafeFileHandle handle)
        {
            if (failCleanup)
            {
                throw new IOException("injected cleanup failure");
            }

            base.DeleteCreatedEntry(handle);
        }
    }

    private sealed class CancelingSession(string destination, CancellationTokenSource cancellation)
        : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        protected override async Task WriteContentAsync(Stream stream, ImmutableArray<byte> content, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(content.AsMemory()[..1], cancellationToken);
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private sealed class SecondWriteFailsSession(string destination) : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        private int _writes;

        protected override Task WriteContentAsync(Stream stream, ImmutableArray<byte> content, CancellationToken cancellationToken)
        {
            if (++_writes == 2)
            {
                throw new IOException("injected second write failure");
            }

            return base.WriteContentAsync(stream, content, cancellationToken);
        }
    }

    private sealed class RetaggingSession(string destination, string nested, string outside)
        : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        public bool Retagged { get; private set; }

        public bool CreatedOutside { get; private set; }

        protected override SafeFileHandle CreateEntry(SafeFileHandle parent, string name, bool isDirectory)
        {
            new DirectoryMutation().SetJunction(nested, outside, 0x100);
            Retagged = true;
            SafeFileHandle handle = base.CreateEntry(parent, name, isDirectory);
            CreatedOutside = Path.Exists(Path.Combine(outside, name));
            return handle;
        }
    }

    private sealed class RetagDuringWriteSession(string destination, string outside)
        : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        private readonly string _destination = destination;

        public bool Retagged { get; private set; }

        protected override async Task WriteContentAsync(Stream stream, ImmutableArray<byte> content, CancellationToken cancellationToken)
        {
            await base.WriteContentAsync(stream, content, cancellationToken);
            new DirectoryMutation().SetJunction(_destination, outside, 0x100);
            Retagged = true;
        }
    }

    private sealed class RedirectedRootSession(string destination) : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        protected override SafeFileHandle OpenRoot(string path) => base.OpenRoot(Path.GetTempPath());
    }

    private sealed class ChangingMappingSession(string destination) : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        public bool MappingChanged { get; set; }

        public bool ChangeDuringCreate { get; set; }

        protected override string ReadDriveTarget(string root) =>
            base.ReadDriveTarget(root) + (MappingChanged ? "\\redirected" : string.Empty);

        protected override SafeFileHandle CreateEntry(SafeFileHandle parent, string name, bool isDirectory)
        {
            if (ChangeDuringCreate)
            {
                MappingChanged = true;
            }

            return base.CreateEntry(parent, name, isDirectory);
        }
    }

    private sealed class CountingSession(string destination) : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        public int CheckCount { get; private set; }

        protected override void CheckDirectory(SafeFileHandle handle, string path, string? expectedName)
        {
            CheckCount++;
            base.CheckDirectory(handle, path, expectedName);
        }
    }

    private sealed class StageFailureSession(string destination, string stage, bool failCleanup = false, bool retainHandle = false)
        : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        public SafeFileHandle? RetainedHandle { get; private set; }

        protected override Stream CreateStream(SafeFileHandle handle)
        {
            if (stage == "construction")
            {
                throw new IOException("injected construction failure");
            }

            if (retainHandle)
            {
                RetainedHandle = DuplicateFileHandle(handle);
            }

            return new StageFailureStream(base.CreateStream(handle), stage);
        }

        protected override void DeleteCreatedEntry(SafeFileHandle handle)
        {
            if (failCleanup)
            {
                throw new IOException("injected cleanup failure");
            }

            base.DeleteCreatedEntry(handle);
        }
    }

    private sealed class UnexpectedAfterCreationSession(
        string destination, string stage, bool failCleanup = false, bool unexpectedCleanup = false, bool retainHandle = false)
        : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        private int _filesCreated;
        private bool _streamDisposed;

        public InvalidOperationException Failure { get; } = new($"injected unexpected {stage} failure");

        public InvalidOperationException CompletionFailure { get; } = new("injected unexpected disposal failure");

        public IOException CleanupFailure { get; } = new("injected cleanup failure");

        public InvalidOperationException UnexpectedCleanupFailure { get; } = new("injected unexpected cleanup failure");

        public bool CleanupAttempted { get; private set; }

        public SafeFileHandle? RetainedHandle { get; private set; }

        protected override SafeFileHandle CreateEntry(SafeFileHandle parent, string name, bool isDirectory)
        {
            SafeFileHandle handle = base.CreateEntry(parent, name, isDirectory);
            if (!isDirectory)
            {
                _filesCreated++;
            }

            return handle;
        }

        protected override Stream CreateStream(SafeFileHandle handle)
        {
            if (_filesCreated != 2)
            {
                return base.CreateStream(handle);
            }

            if (stage == "construction")
            {
                throw Failure;
            }

            if (retainHandle)
            {
                RetainedHandle = DuplicateFileHandle(handle);
            }

            return new StageFailureStream(base.CreateStream(handle), stage, Failure,
                stage == "write-unexpected+disposal" ? CompletionFailure : null, () => _streamDisposed = true);
        }

        protected override void CheckDirectory(SafeFileHandle handle, string path, string? expectedName)
        {
            if (stage == "validation" && _filesCreated == 2 && _streamDisposed)
            {
                throw Failure;
            }

            base.CheckDirectory(handle, path, expectedName);
        }

        protected override void DeleteCreatedEntry(SafeFileHandle handle)
        {
            CleanupAttempted = true;
            if (failCleanup)
            {
                throw CleanupFailure;
            }

            if (unexpectedCleanup)
            {
                throw UnexpectedCleanupFailure;
            }

            base.DeleteCreatedEntry(handle);
        }
    }

    private sealed class StageFailureStream(
        Stream inner, string stage, Exception? failure = null, Exception? completionFailure = null, Action? onDisposed = null) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            if (stage.StartsWith("write", StringComparison.Ordinal) || stage == "expected-write")
            {
                throw stage is "write+disposal" or "expected-write" ? new IOException("injected write failure")
                    : failure ?? new IOException("injected write failure");
            }
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            if (stage == "flush")
            {
                throw failure ?? new IOException("injected flush failure");
            }

            return inner.FlushAsync(cancellationToken);
        }

        public override async ValueTask DisposeAsync()
        {
            if (stage == "early-disposal")
            {
                throw failure ?? new IOException("injected early-disposal failure");
            }

            await inner.DisposeAsync();
            onDisposed?.Invoke();
            GC.SuppressFinalize(this);
            if (stage.EndsWith("disposal", StringComparison.Ordinal))
            {
                throw completionFailure ?? failure ?? new IOException("injected disposal failure");
            }
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    private sealed class CancelAfterCompleteSession(string destination, CancellationTokenSource cancellation)
        : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        public override async Task<ProjectWriteEffect> CreateFileAsync(string path, ImmutableArray<byte> content, CancellationToken cancellationToken)
        {
            ProjectWriteEffect effect = await base.CreateFileAsync(path, content, cancellationToken);
            cancellation.Cancel();
            return effect;
        }
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            Skip = OperatingSystem.IsWindows() ? null : "Requires Windows filesystem qualification.";
        }
    }

    private sealed class WindowsTheoryAttribute : TheoryAttribute
    {
        public WindowsTheoryAttribute()
        {
            Skip = OperatingSystem.IsWindows() ? null : "Requires Windows filesystem qualification.";
        }
    }

    private sealed partial class DirectoryMutation
    {
        public SafeFileHandle OpenWrite(string path, uint access = 0x40000000)
        {
            SafeFileHandle handle = CreateFile(path, access, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new IOException("Cannot open the disposable test directory for mutation.", new Win32Exception(error));
            }

            return handle;
        }

        public unsafe void SetJunction(string path, string target, uint access = 0x40000000)
        {
            using SafeFileHandle handle = OpenWrite(path, access);
            string substitute = @"\??\" + target;
            byte[] names = Encoding.Unicode.GetBytes(substitute + '\0' + target + '\0');
            byte[] reparse = new byte[16 + names.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(reparse, 0xa0000003);
            BinaryPrimitives.WriteUInt16LittleEndian(reparse.AsSpan(4), checked((ushort)(8 + names.Length)));
            BinaryPrimitives.WriteUInt16LittleEndian(reparse.AsSpan(10), checked((ushort)(substitute.Length * 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(reparse.AsSpan(12), checked((ushort)((substitute.Length + 1) * 2)));
            BinaryPrimitives.WriteUInt16LittleEndian(reparse.AsSpan(14), checked((ushort)(target.Length * 2)));
            names.CopyTo(reparse, 16);
            fixed (byte* buffer = reparse)
            {
                if (!DeviceIoControl(handle, 0x000900a4, buffer, (uint)reparse.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
                {
                    throw new IOException("Cannot create the disposable test junction.", new Win32Exception(Marshal.GetLastPInvokeError()));
                }
            }
        }

        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        private static partial SafeFileHandle CreateFile(
            string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool DeviceIoControl(
            SafeFileHandle handle, uint code, void* input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
    }
}
