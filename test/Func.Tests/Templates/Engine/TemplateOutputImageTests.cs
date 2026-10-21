// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateOutputImageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-memory-output-image-").FullName;

    [Fact]
    public async Task RenderImageAsync_NativeRegularOutput_PreservesRealTargetAndLogicalName()
    {
        var hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), hive);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive);
        ITemplateInvocationEngine engine = session;
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None))
            .Single(template => template.Identity == TemplateFixtures.ItemTemplateIdentity);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "LogicalApp"));
        Directory.CreateDirectory(output.Info.FullName);
        var target = Path.Combine(output.Info.FullName, "LogicalApp.txt");
        File.WriteAllText(target, "original");
        using TemplateOutputImage image = new(engine.FileSystem, output.Info.FullName, CancellationToken.None);
        var fileSystem = session.Settings.Host.FileSystem;

        var native = await engine.RenderImageAsync(template, new(output, new Dictionary<string, string?>()),
            new InputDataSet(template), image, CancellationToken.None);

        native.Status.Should().Be(CreationResultStatus.Success);
        image.Files.Should().Equal("LogicalApp.txt");
        using var reader = new StreamReader(image.OpenCaptured("LogicalApp.txt"));
        (await reader.ReadToEndAsync(CancellationToken.None)).Should().Be("LogicalApp content");
        File.ReadAllText(target).Should().Be("original");
        session.Settings.Host.FileSystem.Should().BeSameAs(fileSystem);
        Action mutate = () => image.CreateFile(target);
        mutate.Should().Throw<InvalidOperationException>().WithMessage("*cannot be changed*");
    }

    [Fact]
    public void Seal_OpenStream_RefusesUntilNativeStreamCompletes()
    {
        var basis = Substitute.For<IPhysicalFileSystem>();
        using TemplateOutputImage image = new(basis, _root, CancellationToken.None);
        using var output = image.CreateFile(Path.Combine(_root, "file.txt"));
        Action seal = image.Seal;

        seal.Should().Throw<InvalidOperationException>().WithMessage("*stream open*");
        output.WriteByte(42);
        output.Dispose();
        image.Seal();

        using var captured = image.OpenCaptured("file.txt");
        captured.ReadByte().Should().Be(42);
        captured.CanWrite.Should().BeFalse();
    }

    [Fact]
    public void BeginCapture_ConcurrentOrMismatchedOwner_DoesNotReplaceTheActiveImage()
    {
        var basis = Substitute.For<IPhysicalFileSystem>();
        var router = new TemplateInvocationFileSystem(basis);
        using TemplateOutputImage first = new(basis, _root, CancellationToken.None);
        using TemplateOutputImage other = new(basis, _root, CancellationToken.None);
        router.BeginCapture(first);

        Action beginOther = () => router.BeginCapture(other);
        Action endOther = () => router.EndCapture(other);

        beginOther.Should().Throw<InvalidOperationException>();
        endOther.Should().Throw<InvalidOperationException>();
        using (var output = router.CreateFile(Path.Combine(_root, "file.txt")))
        {
            output.WriteByte(42);
        }
        first.Files.Should().Equal("file.txt");
        other.Files.Should().BeEmpty();
        router.EndCapture(first);
        basis.DidNotReceiveWithAnyArgs().CreateFile(default!);
    }

    [Fact]
    public async Task CreateFile_PreCanceled_DoesNotProduceCapturedOrRealOutput()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        var basis = Substitute.For<IPhysicalFileSystem>();
        using TemplateOutputImage image = new(basis, _root, cancellation.Token);

        Action create = () => image.CreateFile(Path.Combine(_root, "file.txt"));

        create.Should().Throw<OperationCanceledException>();
        image.Files.Should().BeEmpty();
        basis.DidNotReceiveWithAnyArgs().CreateFile(default!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_NullDependency_Throws(bool missingBasis)
    {
        Action create = () => _ = new TemplateOutputImage(
            missingBasis ? null! : Substitute.For<IPhysicalFileSystem>(), missingBasis ? _root : null!, CancellationToken.None);

        create.Should().Throw<ArgumentNullException>().WithParameterName(missingBasis ? "basis" : "output");
    }

    [Fact]
    public void Dispose_LaterReads_DoNotAppearAsAnEmptySuccessfulImage()
    {
        var image = new TemplateOutputImage(Substitute.For<IPhysicalFileSystem>(), _root, CancellationToken.None);
        image.Dispose();

        Action files = () => _ = image.Files;
        Action directories = () => _ = image.Directories;
        Action file = () => image.HasFile(Path.Combine(_root, "file.txt"));

        files.Should().Throw<ObjectDisposedException>();
        directories.Should().Throw<ObjectDisposedException>();
        file.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_FailureOrCancellationAfterFirstInstall_ReportsCompletedRealFiles(bool cancel)
    {
        var hive = Path.Combine(_root, "hive");
        var package = TemplateFixtures.WriteBasicPackage(_root);
        File.WriteAllText(Path.Combine(package, "item", "Other.txt"), "OTHER");
        await TemplateFixtures.InstallAsync(package, hive);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive);
        using CancellationTokenSource cancellation = new();
        var output = WorkingDirectory.FromExplicit(Directory.CreateDirectory(Path.Combine(_root, "output")).FullName);
        List<string> moved = [];
        int incoming = 0;
        var fileSystem = new RecordingCommitFileSystem(new PhysicalFileSystem())
        {
            AfterMove = path =>
            {
                moved.Add(path);
                if (cancel)
                {
                    cancellation.Cancel();
                }
            },
            AfterIncomingWrite = _ =>
            {
                if (!cancel && ++incoming == 2)
                {
                    throw new IOException("Fixture second incoming write failed.");
                }
            },
        };
        using Templater templater = TemplateFixtures.CreateTemplater(session, fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), cancellation.Token);

        result.Status.Should().Be(cancel ? TemplateInvocationStatus.Failed : TemplateInvocationStatus.EngineFailure);
        result.HasCommittedOutput.Should().BeTrue();
        moved.Should().ContainSingle();
        var change = result.Changes.Should().ContainSingle().Subject;
        Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path)).Should().Be(moved[0]);
        File.ReadAllText(moved[0]).Should().Be(Path.GetFileName(moved[0]) == "Created.txt" ? "Created content" : "OTHER");
        Directory.GetFiles(output.Info.FullName).Should().Equal(moved[0]);
        Directory.GetDirectories(output.Info.FullName).Should().BeEmpty("owned incoming directories are cleaned without rolling back completed files");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_SourceOrForcedTargetChangesAfterCapture_IsNotOverwritten(bool changeSource)
    {
        var hive = Path.Combine(_root, "hive");
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, hive);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive);
        var output = WorkingDirectory.FromExplicit(Directory.CreateDirectory(Path.Combine(_root, "output")).FullName);
        string target = Path.Combine(output.Info.FullName, "Created.txt");
        File.WriteAllText(target, "original");
        var fileSystem = new RecordingCommitFileSystem(new PhysicalFileSystem())
        {
            AfterIncomingWrite = _ =>
            {
                if (changeSource)
                {
                    File.AppendAllText(Path.Combine(package, "item", ".template.config", "template.json"), " ");
                }
                else
                {
                    File.WriteAllText(target, "new occupant bytes");
                }
            },
        };
        using Templater templater = TemplateFixtures.CreateTemplater(session, fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created", Force: true), CancellationToken.None);

        result.Status.Should().Be(changeSource ? TemplateInvocationStatus.TemplateChanged : TemplateInvocationStatus.FileConflict);
        result.HasCommittedOutput.Should().BeFalse();
        result.Changes.Should().BeEmpty();
        File.ReadAllText(target).Should().Be(changeSource ? "original" : "new occupant bytes");
        Directory.GetDirectories(output.Info.FullName).Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_CanceledAfterIncomingCopy_DoesNotInstallPrivateBytes()
    {
        var hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), hive);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive);
        using CancellationTokenSource cancellation = new();
        var output = WorkingDirectory.FromExplicit(Directory.CreateDirectory(Path.Combine(_root, "output")).FullName);
        var fileSystem = new RecordingCommitFileSystem(new PhysicalFileSystem())
        {
            AfterIncomingWrite = _ => cancellation.Cancel(),
        };
        using Templater templater = TemplateFixtures.CreateTemplater(session, fileSystem);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];

        Func<Task> invoke = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), cancellation.Token);

        await invoke.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFileSystemEntries(output.Info.FullName).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenderImageAsync_WrongFilesystemOrOutput_RejectsTheImage(bool wrongFilesystem)
    {
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Path.Combine(_root, "hive"));
        ITemplateInvocationEngine engine = session;
        var template = Substitute.For<Microsoft.TemplateEngine.Abstractions.ITemplateInfo>();
        template.ParameterDefinitions.Returns(ParameterDefinitionSet.Empty);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        using TemplateOutputImage image = new(wrongFilesystem ? Substitute.For<IPhysicalFileSystem>() : engine.FileSystem,
            wrongFilesystem ? output.Info.FullName : Path.Combine(_root, "other-output"), CancellationToken.None);

        Func<Task> render = () => engine.RenderImageAsync(template, new(output, new Dictionary<string, string?>()),
            new InputDataSet(template), image, CancellationToken.None);

        await render.Should().ThrowAsync<ArgumentException>().WithParameterName("image");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task CaptureStream_CommandCancellation_StopsWritesToAnAlreadyOpenStream()
    {
        using CancellationTokenSource cancellation = new();
        var basis = Substitute.For<IPhysicalFileSystem>();
        using TemplateOutputImage image = new(basis, _root, cancellation.Token);
        using Stream output = image.CreateFile(Path.Combine(_root, "file.txt"));
        await cancellation.CancelAsync();

        Action write = () => output.WriteByte(42);

        write.Should().Throw<OperationCanceledException>();
        basis.DidNotReceiveWithAnyArgs().CreateFile(default!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_ActualFileWouldOccupyDirectory_RefusesEvenWithForce(bool force)
    {
        var hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), hive);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive);
        var output = WorkingDirectory.FromExplicit(Directory.CreateDirectory(Path.Combine(_root, "output")).FullName);
        string target = Directory.CreateDirectory(Path.Combine(output.Info.FullName, "Created.txt")).FullName;
        using Templater templater = TemplateFixtures.CreateTemplater(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created", Force: force), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        result.Diagnostic.Should().Contain("directory occupies native output file");
        result.Changes.Should().BeEmpty();
        Directory.Exists(target).Should().BeTrue();
        File.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_RootPlaceholder_RetainsItsActualDirectoryEffect()
    {
        var package = Path.Combine(_root, "root-directory-template");
        Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        File.WriteAllText(Path.Combine(package, "-.-"), "");
        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"),
            """{"identity":"RootDirectory","name":"Root directory","shortName":"root-directory","tags":{"type":"item"}}""");
        var hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(package, hive);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive);
        using Templater templater = TemplateFixtures.CreateTemplater(session);
        var selected = (await templater.ResolveGroupAsync("RootDirectory", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Root"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        var change = result.Changes.Should().ContainSingle().Subject;
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(output.Info.FullName, change.Path))).Should().Be(output.Info.FullName);
        change.Kind.Should().Be(TemplateFileChangeKind.Create);
        Directory.Exists(output.Info.FullName).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(output.Info.FullName).Should().BeEmpty();
    }

    [Fact]
    public void CreateFile_AlternateStreamSyntax_UsesTheHostPlatformsFilenameRules()
    {
        var basis = Substitute.For<IPhysicalFileSystem>();
        using TemplateOutputImage image = new(basis, _root, CancellationToken.None);
        string target = Path.Combine(_root, "file:stream");
        if (Path.DirectorySeparatorChar == '\\')
        {
            Action capture = () => image.CreateFile(target);
            capture.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*invalid on this platform*");
            image.Files.Should().BeEmpty();
        }
        else
        {
            using (Stream output = image.CreateFile(target))
            {
                output.WriteByte(42);
            }
            image.Seal();
            image.Files.Should().Equal("file:stream");
        }
        basis.DidNotReceiveWithAnyArgs().CreateFile(default!);
    }

    [Fact]
    public void GetLastWriteTimeUtc_PrivateOutput_DoesNotInventAPhysicalTimestamp()
    {
        var basis = Substitute.For<IPhysicalFileSystem>();
        var router = new TemplateInvocationFileSystem(basis);
        using TemplateOutputImage image = new(basis, _root, CancellationToken.None);
        router.BeginCapture(image);
        string path = Path.Combine(_root, "file.txt");
        using (Stream output = router.CreateFile(path))
        {
            output.WriteByte(42);
        }

        Action timestamp = () => router.GetLastWriteTimeUtc(path);

        timestamp.Should().Throw<NotSupportedException>().WithMessage("*timestamps are unavailable*");
        router.EndCapture(image);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task InvokeAsync_ExistingPlaceholderDirectory_IsRetainedWithoutDestructiveForce(bool rootPlaceholder, bool force)
    {
        var package = Path.Combine(_root, "retained-directory-template");
        Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        string sourceDirectory = rootPlaceholder ? package : Directory.CreateDirectory(Path.Combine(package, "empty")).FullName;
        File.WriteAllText(Path.Combine(sourceDirectory, "-.-"), "");
        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"),
            """{"identity":"RetainedDirectory","name":"Retained directory","shortName":"retained-directory","tags":{"type":"item"}}""");
        var hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(package, hive);
        var output = WorkingDirectory.FromExplicit(Directory.CreateDirectory(Path.Combine(_root, "output")).FullName);
        string retained = rootPlaceholder ? output.Info.FullName : Directory.CreateDirectory(Path.Combine(output.Info.FullName, "empty")).FullName;
        string? userFile = rootPlaceholder ? null : Path.Combine(retained, "keep.txt");
        if (userFile is not null)
        {
            File.WriteAllText(userFile, "user contents");
        }
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive);
        var template = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        var nativePreview = await new TemplateCreator(session.Settings).InstantiateAsync(template, "Retained", "output", output.Info.FullName,
            new Dictionary<string, string?>(), forceCreation: true, dryRun: true, cancellationToken: CancellationToken.None);
        nativePreview.Status.Should().Be(CreationResultStatus.Success);
        nativePreview.CreationEffects!.FileChanges.Should().ContainSingle().Which.ChangeKind.Should().Be(Microsoft.TemplateEngine.Abstractions.ChangeKind.Overwrite);
        using Templater templater = TemplateFixtures.CreateTemplater(session);
        var selected = (await templater.ResolveGroupAsync("RetainedDirectory", TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Retained", Force: force), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        result.Changes.Should().BeEmpty("retaining an existing directory is not an output overwrite");
        result.HasCommittedOutput.Should().BeFalse();
        Directory.Exists(retained).Should().BeTrue();
        if (userFile is not null)
        {
            File.ReadAllText(userFile).Should().Be("user contents");
            Directory.GetFiles(retained).Should().Equal(userFile);
        }
        else
        {
            Directory.EnumerateFileSystemEntries(retained).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("create-file")]
    [InlineData("create-directory")]
    [InlineData("copy")]
    [InlineData("write-text")]
    [InlineData("delete-file")]
    [InlineData("delete-directory")]
    [InlineData("attributes")]
    [InlineData("timestamp")]
    [InlineData("watch")]
    public void CapturedFilesystem_ForeignMutation_NeverReachesTheRealFilesystem(string operation)
    {
        var host = new FuncTemplateEngineHost(new(WorkingDirectory.FromExplicit(_root)));
        string outside = Path.Combine(_root, "owned-outside.txt");
        File.WriteAllText(outside, "original");
        using TemplateOutputImage image = new(host.FileSystem, Path.Combine(_root, "output"), CancellationToken.None);
        var router = host.InvocationFileSystem;
        router.BeginCapture(image);
        try
        {
            Action mutate = operation switch
            {
                "create-file" => () => router.CreateFile(outside).Dispose(),
                "create-directory" => () => router.CreateDirectory(outside),
                "copy" => () => router.FileCopy(outside, outside, overwrite: true),
                "write-text" => () => router.WriteAllText(outside, "changed"),
                "delete-file" => () => router.FileDelete(outside),
                "delete-directory" => () => router.DirectoryDelete(outside, recursive: true),
                "attributes" => () => router.SetFileAttributes(outside, FileAttributes.Normal),
                "timestamp" => () => router.SetLastWriteTimeUtc(outside, DateTime.UnixEpoch),
                "watch" => () => router.WatchFileChanges(outside, (_, _) => { }).Dispose(),
                _ => throw new ArgumentOutOfRangeException(nameof(operation)),
            };

            mutate.Should().Throw<InvalidTemplateMetadataException>();
            File.ReadAllText(outside).Should().Be("original");
            image.Files.Should().BeEmpty();
        }
        finally
        {
            router.EndCapture(image);
        }
    }

    [Fact]
    public async Task InvokeAsync_ActualParentTraversal_DoesNotInstallOutsideTheRequestedRoot()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        string config = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"sourceName\": \"ItemFunction\"",
            "\"sourceName\": \"ItemFunction\", \"sources\":[{\"source\":\"./\",\"target\":\"../owned-outside\"}]", StringComparison.Ordinal));
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(package, hive);
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), hive);
        using Templater templater = TemplateFixtures.CreateTemplater(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("escapes the requested output");
        result.Changes.Should().BeEmpty();
        result.HasCommittedOutput.Should().BeFalse();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
        Directory.Exists(Path.Combine(_root, "owned-outside")).Should().BeFalse();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
