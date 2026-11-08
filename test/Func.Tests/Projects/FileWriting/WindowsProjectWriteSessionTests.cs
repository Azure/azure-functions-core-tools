// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Projects.FileWriting;
using Azure.Functions.Cli.Tests.Common;
using Microsoft.Win32.SafeHandles;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Projects.FileWriting;

public sealed class WindowsProjectWriteSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LaneFProjectWriterTests", Guid.NewGuid().ToString("N"));

    public WindowsProjectWriteSessionTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [WindowsX64ProjectWriterFact]
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

    [WindowsX64ProjectWriterFact]
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

    [WindowsX64ProjectWriterFact]
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

    [WindowsX64ProjectWriterFact]
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

    [WindowsX64ProjectWriterFact]
    public async Task CreateFileAsync_FileArrivesAfterPreflight_DoesNotOverwriteIt()
    {
        using var session = new WindowsProjectWriteSession(_root, CancellationToken.None);
        session.CheckFileAbsent("file");
        File.WriteAllText(Path.Combine(_root, "file"), "original");

        Func<Task> action = () => session.CreateFileAsync("file", [9], CancellationToken.None);

        await action.Should().ThrowAsync<IOException>();
        File.ReadAllText(Path.Combine(_root, "file")).Should().Be("original");
    }

    [WindowsX64ProjectWriterFact]
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

    [WindowsX64ProjectWriterFact]
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

    [WindowsX64ProjectWriterFact]
    public async Task CreateFileAsync_CleanupFails_ReportsPrimaryFailureAndUncertainRemainingFileSeparately()
    {
        using var session = new FailingSession(_root, failCleanup: true);

        ProjectWriteEffect result = await session.CreateFileAsync("file", [1, 2], CancellationToken.None);

        result.Status.Should().Be(ProjectWriteStatus.Uncertain);
        result.Error.Should().BeOfType<IOException>().Which.Message.Should().Be("injected write failure");
        result.CleanupError.Should().BeOfType<IOException>().Which.Message.Should().Be("injected cleanup failure");
        File.ReadAllBytes(Path.Combine(_root, "file")).Should().Equal(1);
    }

    [WindowsX64ProjectWriterFact]
    public async Task CreateFileAsync_CanceledDuringWrite_RemovesIncompleteFileAndReportsCancellation()
    {
        using CancellationTokenSource cancellation = new();
        using var session = new CancelingSession(_root, cancellation);

        ProjectWriteEffect result = await session.CreateFileAsync("file", [1, 2], cancellation.Token);

        result.Status.Should().Be(ProjectWriteStatus.RemovedAfterFailure);
        result.Error.Should().BeOfType<OperationCanceledException>();
        File.Exists(Path.Combine(_root, "file")).Should().BeFalse();
    }

    [WindowsX64ProjectWriterTheory]
    [InlineData(true, 1, 0)]
    [InlineData(true, 1, 1)]
    [InlineData(false, 1, 0)]
    [InlineData(false, 1, 1)]
    [InlineData(false, 2, 0)]
    [InlineData(false, 2, 1)]
    [InlineData(false, 2, 2)]
    public async Task WriteAsync_UnexpectedAfterDirectoryCreation_ReportsCleanupAndPartialEffects(
        bool missingDestination, int failureOrdinal, int cleanupMode)
    {
        string destination = missingDestination ? Path.Combine(_root, "project") : _root;
        using var session = new UnexpectedDirectoryValidationSession(destination, failureOrdinal, cleanupMode);
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        fileSystem.OpenSession(destination, Arg.Any<CancellationToken>()).Returns(session);
        var writer = new ProjectFileWriter(fileSystem);

        Exception? thrown = await Record.ExceptionAsync(() =>
            writer.WriteAsync(destination, Files("alpha/file"), ["alpha", "beta", "gamma"], CancellationToken.None));

        thrown.Should().BeSameAs(session.Failure);
        thrown!.StackTrace.Should().Contain("UnexpectedDirectoryValidationSession.CheckDirectory");
        session.CleanupAttempted.Should().BeTrue();
        session.CreatedHandle.Should().NotBeNull();
        session.CreatedHandle!.IsClosed.Should().BeTrue();
        ProjectWriteEffect effect = thrown.Data[typeof(ProjectWriteEffect)].Should().BeOfType<ProjectWriteEffect>().Subject;
        ProjectWriteResult result = thrown.Data[typeof(ProjectWriteResult)].Should().BeOfType<ProjectWriteResult>().Subject;
        string failedPath = missingDestination ? destination : failureOrdinal == 1 ? "alpha" : "beta";
        effect.Path.Should().Be(failedPath);
        effect.IsDirectory.Should().BeTrue();
        effect.Error.Should().BeSameAs(session.Failure);
        effect.CompletionError.Should().BeNull();
        effect.CleanupRequested.Should().Be(cleanupMode == 0);
        effect.Status.Should().Be(cleanupMode == 0 ? ProjectWriteStatus.RemovedAfterFailure : ProjectWriteStatus.Uncertain);
        effect.CleanupError.Should().BeSameAs(cleanupMode switch
        {
            1 => session.CleanupFailure,
            2 => session.UnexpectedCleanupFailure,
            _ => null,
        });
        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeFalse();
        result.Effects.Single(item => item.Path == failedPath).Should().BeSameAs(effect);
        if (!missingDestination)
        {
            result.Effects[0].Status.Should().Be(ProjectWriteStatus.Preserved);
        }

        string[] directories = ["alpha", "beta", "gamma"];
        for (int index = 0; index < directories.Length; index++)
        {
            ProjectWriteStatus expected = missingDestination || index >= failureOrdinal
                ? ProjectWriteStatus.NotAttempted
                : index == failureOrdinal - 1 ? effect.Status : ProjectWriteStatus.Created;
            ProjectWriteEffect directoryEffect = result.Effects.Single(item => item.Path == directories[index]);
            directoryEffect.Status.Should().Be(expected);
            if (expected == ProjectWriteStatus.NotAttempted)
            {
                directoryEffect.Error.Should().BeNull();
            }

            bool exists = !missingDestination && (index < failureOrdinal - 1 || (index == failureOrdinal - 1 && cleanupMode != 0));
            Directory.Exists(Path.Combine(destination, directories[index])).Should().Be(exists);
        }

        ProjectWriteEffect file = result.Effects.Single(item => item.Path == "alpha\\file");
        file.Status.Should().Be(ProjectWriteStatus.NotAttempted);
        file.Error.Should().BeNull();
        File.Exists(Path.Combine(destination, "alpha", "file")).Should().BeFalse();
        if (missingDestination)
        {
            Directory.Exists(destination).Should().Be(cleanupMode != 0);
        }
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

    private sealed class UnexpectedDirectoryValidationSession(string destination, int failureOrdinal, int cleanupMode)
        : WindowsProjectWriteSession(destination, CancellationToken.None)
    {
        private int _createdDirectories;

        public InvalidOperationException Failure { get; } = new("injected unexpected directory validation failure");

        public IOException CleanupFailure { get; } = new("injected directory cleanup failure");

        public InvalidOperationException UnexpectedCleanupFailure { get; } = new("injected unexpected directory cleanup failure");

        public bool CleanupAttempted { get; private set; }

        public SafeFileHandle? CreatedHandle { get; private set; }

        protected override SafeFileHandle CreateEntry(SafeFileHandle parent, string name, bool isDirectory)
        {
            SafeFileHandle handle = base.CreateEntry(parent, name, isDirectory);
            if (isDirectory && ++_createdDirectories == failureOrdinal)
            {
                CreatedHandle = handle;
            }

            return handle;
        }

        protected override void CheckDirectory(SafeFileHandle handle, string path, string? expectedName)
        {
            if (ReferenceEquals(handle, CreatedHandle))
            {
                throw Failure;
            }

            base.CheckDirectory(handle, path, expectedName);
        }

        protected override void DeleteCreatedEntry(SafeFileHandle handle)
        {
            CleanupAttempted = true;
            if (cleanupMode == 1)
            {
                throw CleanupFailure;
            }

            if (cleanupMode == 2)
            {
                throw UnexpectedCleanupFailure;
            }

            base.DeleteCreatedEntry(handle);
        }
    }

    private sealed class WindowsX64ProjectWriterFactAttribute : FactAttribute
    {
        public WindowsX64ProjectWriterFactAttribute()
        {
            if (!TemplateSourceTestPlatform.IsSupported)
            {
                Skip = "Project writing is qualified only on Windows x64.";
            }
        }
    }

    private sealed class WindowsX64ProjectWriterTheoryAttribute : TheoryAttribute
    {
        public WindowsX64ProjectWriterTheoryAttribute()
        {
            if (!TemplateSourceTestPlatform.IsSupported)
            {
                Skip = "Project writing is qualified only on Windows x64.";
            }
        }
    }
}
