// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Projects.FileWriting;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Projects.FileWriting;

public sealed class ProjectFileWriterTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/rooted")]
    [InlineData(@"\rooted")]
    [InlineData(@"C:\rooted")]
    [InlineData("C:relative")]
    [InlineData(@"\\server\share\file")]
    [InlineData(@"\\?\C:\file")]
    [InlineData("../file")]
    [InlineData(@"src\..\file")]
    [InlineData("src/./file")]
    [InlineData("src//file")]
    [InlineData("src/")]
    [InlineData("src./file")]
    [InlineData("src /file")]
    [InlineData("file:stream")]
    [InlineData("file?")]
    [InlineData("file*")]
    [InlineData("file|")]
    [InlineData("file<")]
    [InlineData("file>")]
    [InlineData("file\"")]
    [InlineData("file\0")]
    [InlineData("file\n")]
    [InlineData("GIT~1/config")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("AUX/file")]
    [InlineData("PRN.txt")]
    [InlineData("COM1.txt")]
    [InlineData("LPT9.txt")]
    [InlineData("COM\u00b9.txt")]
    [InlineData("CON .txt")]
    [InlineData("CONIN$")]
    [InlineData("CONOUT$")]
    [InlineData(".git")]
    [InlineData(".Git/config")]
    [InlineData(@"nested\.GIT\HEAD")]
    public async Task WriteAsync_UnsafePlannedPath_RejectsFilesAndDirectoriesBeforeOpeningDestination(string path)
    {
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        var writer = new ProjectFileWriter(fileSystem);

        Func<Task> fileAction = () => writer.WriteAsync(@"C:\project", Files(path), [], CancellationToken.None);
        Func<Task> directoryAction = () => writer.WriteAsync(@"C:\project", Files(), [path], CancellationToken.None);

        await fileAction.Should().ThrowAsync<ArgumentException>();
        await directoryAction.Should().ThrowAsync<ArgumentException>();
        fileSystem.DidNotReceiveWithAnyArgs().OpenSession(default!, default);
    }

    [Theory]
    [InlineData("src/file", @"src\file")]
    [InlineData("file", "FILE")]
    public async Task WriteAsync_DuplicateNormalizedFiles_RejectsBeforeOpeningDestination(string first, string second)
    {
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        var writer = new ProjectFileWriter(fileSystem);

        Func<Task> action = () => writer.WriteAsync(@"C:\project", Files(first, second), [], CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*duplicated*");
        fileSystem.DidNotReceiveWithAnyArgs().OpenSession(default!, default);
    }

    [Fact]
    public async Task WriteAsync_DuplicateExplicitDirectories_RejectsBeforeOpeningDestination()
    {
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        var writer = new ProjectFileWriter(fileSystem);

        Func<Task> action = () => writer.WriteAsync(@"C:\project", Files(), ["src/nested", @"SRC\nested"], CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*duplicated*");
        fileSystem.DidNotReceiveWithAnyArgs().OpenSession(default!, default);
    }

    [Theory]
    [InlineData("src", "src/file")]
    [InlineData("src/file", "src")]
    public async Task WriteAsync_FileIsAnotherFilesParent_RejectsEitherOrder(string first, string second)
    {
        var writer = new ProjectFileWriter(new());

        Func<Task> action = () => writer.WriteAsync(@"C:\project", Files(first, second), [], CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*both a file and a directory*");
    }

    [Theory]
    [InlineData("src")]
    [InlineData("src/nested")]
    public async Task WriteAsync_FileConflictsWithExplicitDirectory_RejectsPlan(string directory)
    {
        var writer = new ProjectFileWriter(new());

        Func<Task> action = () => writer.WriteAsync(@"C:\project", Files("src"), [directory], CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*both a file and a directory*");
    }

    [Fact]
    public async Task WriteAsync_DefaultBytes_RejectsUninitializedContent()
    {
        var writer = new ProjectFileWriter(new());
        Dictionary<string, ImmutableArray<byte>> files = new() { ["file"] = default };

        Func<Task> action = () => writer.WriteAsync(@"C:\project", files, [], CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*uninitialized*");
    }

    [Fact]
    public async Task WriteAsync_PreCanceled_ReportsNoEffectsAndDoesNotOpenDestination()
    {
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        var writer = new ProjectFileWriter(fileSystem);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("file"), ["empty"], cancellation.Token);

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeTrue();
        result.Cancellation!.CancellationToken.Should().Be(cancellation.Token);
        result.Effects.Should().OnlyContain(effect => effect.Status == ProjectWriteStatus.NotAttempted && effect.Error == null);
        fileSystem.DidNotReceiveWithAnyArgs().OpenSession(default!, default);
    }

    [Fact]
    public async Task WriteAsync_PreflightConflicts_ReportsAllConflictsWithoutCreatingAnything()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        session.DirectoryExists(string.Empty).Returns(true);
        session.When(value => value.CheckFileAbsent(Arg.Any<string>())).Do(_ => throw new IOException("collision"));
        var writer = CreateWriter(session);

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("one", "two"), ["empty"], CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Effects.Should().Contain(effect => effect.Path == "one" && effect.Status == ProjectWriteStatus.Failed);
        result.Effects.Should().Contain(effect => effect.Path == "two" && effect.Status == ProjectWriteStatus.Failed);
        session.DidNotReceiveWithAnyArgs().CreateDirectory(default!, default);
        await session.DidNotReceiveWithAnyArgs().CreateFileAsync(default!, default, default);
    }

    [Fact]
    public async Task WriteAsync_CanceledAfterFirstFile_ReportsCreatedAndUnattemptedFiles()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        session.DirectoryExists(string.Empty).Returns(true);
        using CancellationTokenSource cancellation = new();
        session.CreateFileAsync("one", Arg.Any<ImmutableArray<byte>>(), cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return new ProjectWriteEffect("one", false, ProjectWriteStatus.Created);
        });
        var writer = CreateWriter(session);

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("one", "two", "three"), [], cancellation.Token);

        result.Succeeded.Should().BeFalse();
        result.Effects[1].Status.Should().Be(ProjectWriteStatus.Created);
        result.Canceled.Should().BeTrue();
        result.Effects[2].Status.Should().Be(ProjectWriteStatus.NotAttempted);
        result.Effects[2].Error.Should().BeNull();
        result.Effects[3].Status.Should().Be(ProjectWriteStatus.NotAttempted);
        await session.DidNotReceive().CreateFileAsync("two", Arg.Any<ImmutableArray<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_CanceledAfterDirectoryCreation_ReportsCreatedDirectoryWithoutAttemptingFile()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        session.DirectoryExists(string.Empty).Returns(true);
        using CancellationTokenSource cancellation = new();
        session.When(value => value.CreateDirectory("src", Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        var writer = CreateWriter(session);

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("src/file"), [], cancellation.Token);

        result.Succeeded.Should().BeFalse();
        result.Effects[1].Status.Should().Be(ProjectWriteStatus.Created);
        result.Canceled.Should().BeTrue();
        result.Effects[2].Status.Should().Be(ProjectWriteStatus.NotAttempted);
        result.Effects[2].Error.Should().BeNull();
        await session.DidNotReceiveWithAnyArgs().CreateFileAsync(default!, default, default);
    }

    [Fact]
    public async Task WriteAsync_FailedDirectoryCreation_PreservesEarlierEffectsAndDoesNotAttemptFiles()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        session.DirectoryExists(string.Empty).Returns(true);
        session.When(value => value.CreateDirectory("src\\nested", Arg.Any<CancellationToken>())).Do(_ => throw new IOException("injected failure"));
        var writer = CreateWriter(session);

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("src/nested/file"), [], CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Effects[1].Should().Be(new ProjectWriteEffect("src", true, ProjectWriteStatus.Created));
        result.Effects[2].Status.Should().Be(ProjectWriteStatus.Failed);
        result.Effects[3].Status.Should().Be(ProjectWriteStatus.NotAttempted);
    }

    [Fact]
    public async Task WriteAsync_FileCleanupFailure_RetainsPrimaryAndCleanupErrorsAndStops()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        session.DirectoryExists(string.Empty).Returns(true);
        IOException primary = new("write failed");
        IOException cleanup = new("cleanup failed");
        session.CreateFileAsync("one", Arg.Any<ImmutableArray<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ProjectWriteEffect("one", false, ProjectWriteStatus.Uncertain, primary, cleanup));
        var writer = CreateWriter(session);

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("one", "two"), [], CancellationToken.None);

        result.Effects[1].Error.Should().BeSameAs(primary);
        result.Effects[1].CleanupError.Should().BeSameAs(cleanup);
        result.Effects[2].Status.Should().Be(ProjectWriteStatus.NotAttempted);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_UnexpectedInternalError_IsNotConvertedToFilesystemFailure()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        session.When(value => value.CheckFileAbsent("file")).Do(_ => throw new InvalidOperationException("bug"));
        var writer = CreateWriter(session);

        Func<Task> action = () => writer.WriteAsync(@"C:\project", Files("file"), [], CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("bug");
    }

    [Fact]
    public async Task WriteAsync_UnexpectedRootCleanupFailure_RetainsAbsoluteDestinationInPartialResult()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        InvalidOperationException failure = new("cleanup bug");
        failure.Data[typeof(ProjectWriteEffect)] = new ProjectWriteEffect(
            string.Empty, true, ProjectWriteStatus.Uncertain, new IOException("creation failed"), failure);
        session.When(value => value.CreateDirectory(string.Empty, Arg.Any<CancellationToken>())).Do(_ => throw failure);
        var writer = CreateWriter(session);

        Exception? thrown = await Record.ExceptionAsync(() => writer.WriteAsync(@"C:\project", Files("file"), [], CancellationToken.None));

        thrown.Should().BeSameAs(failure);
        ProjectWriteEffect effect = thrown!.Data[typeof(ProjectWriteEffect)].Should().BeOfType<ProjectWriteEffect>().Subject;
        ProjectWriteResult result = thrown.Data[typeof(ProjectWriteResult)].Should().BeOfType<ProjectWriteResult>().Subject;
        effect.Path.Should().Be(@"C:\project");
        result.Effects[0].Should().BeSameAs(effect);
        result.Effects[0].Status.Should().Be(ProjectWriteStatus.Uncertain);
        result.Effects[0].CleanupError.Should().BeSameAs(failure);
        result.Effects[1].Status.Should().Be(ProjectWriteStatus.NotAttempted);
    }

    [Fact]
    public async Task WriteAsync_NullArguments_RejectsProgrammerErrors()
    {
        Action constructor = () => _ = new ProjectFileWriter(null!);
        var writer = new ProjectFileWriter(new());
        Func<Task> destination = () => writer.WriteAsync(null!, Files(), [], CancellationToken.None);
        Func<Task> files = () => writer.WriteAsync(@"C:\project", null!, [], CancellationToken.None);
        Func<Task> directories = () => writer.WriteAsync(@"C:\project", Files(), null!, CancellationToken.None);

        constructor.Should().Throw<ArgumentNullException>();
        await destination.Should().ThrowAsync<ArgumentException>();
        await files.Should().ThrowAsync<ArgumentNullException>();
        await directories.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WriteAsync_ComponentExceedsFilesystemLimit_RejectsBeforeOpeningDestination()
    {
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        var writer = new ProjectFileWriter(fileSystem);

        Func<Task> action = () => writer.WriteAsync(@"C:\project", Files(new string('a', 256)), [], CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        fileSystem.DidNotReceiveWithAnyArgs().OpenSession(default!, default);
    }

    [Fact]
    public async Task WriteAsync_CanceledAfterFinalFile_KeepsCreatedEffectButReturnsCanceled()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        session.DirectoryExists(string.Empty).Returns(true);
        using CancellationTokenSource cancellation = new();
        session.CreateFileAsync("file", Arg.Any<ImmutableArray<byte>>(), cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return new ProjectWriteEffect("file", false, ProjectWriteStatus.Created);
        });
        var writer = CreateWriter(session);

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("file"), [], cancellation.Token);

        result.Succeeded.Should().BeFalse();
        result.Canceled.Should().BeTrue();
        result.Effects[1].Status.Should().Be(ProjectWriteStatus.Created);
        result.Effects[1].Error.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_CanceledDuringPreflight_LeavesUnattemptedEffectsUnchanged()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        using CancellationTokenSource cancellation = new();
        session.DirectoryExists(string.Empty).Returns(_ =>
        {
            cancellation.Cancel();
            return true;
        });
        var writer = CreateWriter(session);

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("file"), [], cancellation.Token);

        result.Canceled.Should().BeTrue();
        result.Effects[0].Status.Should().Be(ProjectWriteStatus.Preserved);
        result.Effects[1].Status.Should().Be(ProjectWriteStatus.NotAttempted);
        session.DidNotReceive().CheckFileAbsent("file");
    }

    [Fact]
    public async Task WriteAsync_NativeOperationCanceledBeforeCreation_ReportsNotAttempted()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        session.DirectoryExists(string.Empty).Returns(true);
        session.CreateFileAsync("file", Arg.Any<ImmutableArray<byte>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProjectWriteEffect>>(_ => throw new OperationCanceledException());
        var writer = CreateWriter(session);

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("file"), [], CancellationToken.None);

        result.Canceled.Should().BeTrue();
        result.Effects[1].Status.Should().Be(ProjectWriteStatus.NotAttempted);
        result.Effects[1].Error.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_CanceledDuringCreatedFileCleanup_PreservesFailureAndCleanupHistory()
    {
        var session = Substitute.For<WindowsProjectWriteSession>();
        session.DirectoryExists(string.Empty).Returns(true);
        OperationCanceledException error = new();
        session.CreateFileAsync("file", Arg.Any<ImmutableArray<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new ProjectWriteEffect("file", false, ProjectWriteStatus.CleanupPending, error));
        var writer = CreateWriter(session);

        ProjectWriteResult result = await writer.WriteAsync(@"C:\project", Files("file", "later"), [], CancellationToken.None);

        result.Canceled.Should().BeTrue();
        result.Effects[1].Status.Should().Be(ProjectWriteStatus.CleanupPending);
        result.Effects[1].Error.Should().BeSameAs(error);
        result.Effects[2].Status.Should().Be(ProjectWriteStatus.NotAttempted);
    }

    [Fact]
    public async Task WriteAsync_TooManyEntriesIncludingImplicitParents_RejectsBeforeFilesystemAccess()
    {
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        var writer = new ProjectFileWriter(fileSystem);
        var files = Enumerable.Range(0, ProjectFileWriter.MaximumPlanEntries)
            .ToDictionary(index => $"src/file{index}", _ => ImmutableArray<byte>.Empty);

        Func<Task> action = () => writer.WriteAsync(@"C:\project", files, [], CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*entry limit*");
        fileSystem.DidNotReceiveWithAnyArgs().OpenSession(default!, default);
    }

    [Fact]
    public async Task WriteAsync_TooDeepPlan_RejectsBeforeFilesystemAccess()
    {
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        var writer = new ProjectFileWriter(fileSystem);
        string path = string.Join('/', Enumerable.Repeat("nested", ProjectFileWriter.MaximumPathDepth + 1));

        Func<Task> action = () => writer.WriteAsync(@"C:\project", Files(path), [], CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithMessage("*component limit*");
        fileSystem.DidNotReceiveWithAnyArgs().OpenSession(default!, default);
    }

    private static ProjectFileWriter CreateWriter(WindowsProjectWriteSession session)
    {
        session.CreateDirectory(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            new ProjectWriteEffect(call.Arg<string>(), true, ProjectWriteStatus.Created));
        var fileSystem = Substitute.For<WindowsProjectFileSystem>();
        fileSystem.OpenSession(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(session);
        return new(fileSystem);
    }

    private static Dictionary<string, ImmutableArray<byte>> Files(params string[] paths) =>
        paths.ToDictionary(path => path, _ => ImmutableArray.Create<byte>(1, 2, 3));
}
