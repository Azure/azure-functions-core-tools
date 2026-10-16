// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateDirectoryEffectsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("func-directory-effects-").FullName;

    private string Hive => Path.Combine(_root, "hive");

    [Theory]
    [InlineData("-.-", false)]
    [InlineData("empty.directory", false)]
    [InlineData("-.-", true)]
    [InlineData("_._", true)]
    public async Task InvokeAsync_NativePlaceholder_PreservesDirectoryEffectAndEmptyRegularFile(string placeholder, bool useDefault)
    {
        await InstallAsync(placeholder, useDefault);
        using RecordingSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Directories", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "Created", DryRun: true);

        var preview = await selected.InvokeAsync(request, CancellationToken.None);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        var directory = session.PreviewChanges.OfType<IFileChange2>().Should().ContainSingle(change =>
            change.SourceRelativePath.EndsWith("/" + placeholder, StringComparison.Ordinal)).Subject;
        session.CreatedChanges.Select(change => change.TargetRelativePath).Should()
            .BeEquivalentTo(session.PreviewChanges.Select(change => change.TargetRelativePath));
        File.Exists(Path.Combine(output.Info.FullName, directory.TargetRelativePath)).Should().BeFalse();
        Directory.Exists(Path.Combine(output.Info.FullName, directory.TargetRelativePath)).Should().BeTrue();
        File.ReadAllText(Path.Combine(output.Info.FullName, "Created.txt")).Should().Be("CONTENT");
        File.Exists(Path.Combine(output.Info.FullName, "Empty.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(output.Info.FullName, "Empty.txt")).Should().BeFalse();
        File.ReadAllBytes(Path.Combine(output.Info.FullName, "Empty.txt")).Should().BeEmpty();
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.Changes.Should().BeEquivalentTo(preview.Changes);
        created.Changes.Should().Contain(change => change.Path == directory.TargetRelativePath && change.Kind == "Create");
        if (!useDefault)
        {
            File.ReadAllText(Path.Combine(output.Info.FullName, "_._")).Should().Be("ordinary file with an overridden default name");
        }

        Directory.GetFiles(Path.Combine(output.Info.FullName, "src", "functions")).Should().BeEmpty();
        Directory.GetFiles(output.Info.FullName, placeholder, SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task InvokeAsync_ActualEntryMissingOrWrongKind_DoesNotAcceptAnotherEntryKind(bool directory, bool replace)
    {
        await InstallAsync("-.-");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        using RecordingSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive)
        {
            AfterCreation = () =>
            {
                string target = Path.Combine(output.Info.FullName, directory ? Path.Combine("src", "functions") : "Empty.txt");
                if (directory)
                {
                    Directory.Delete(target);
                    if (replace)
                    {
                        File.WriteAllText(target, "not a directory");
                    }
                }
                else
                {
                    File.Delete(target);
                    if (replace)
                    {
                        Directory.CreateDirectory(target);
                    }
                }
            },
        };
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Directories", TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.EngineFailure);
        result.Changes.Should().BeEmpty();
        result.Diagnostic.Should().Be("Template creation did not produce the validated output paths.");
    }

    [Fact]
    public async Task InvokeAsync_EmptyRegularFileWithoutDirectoryEffects_RemainsFile()
    {
        await InstallAsync("-.-", includeDirectory: false);
        using RecordingSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Directories", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "regular-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Success);
        File.Exists(Path.Combine(output.Info.FullName, "Empty.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(output.Info.FullName, "Empty.txt")).Should().BeFalse();
        File.ReadAllBytes(Path.Combine(output.Info.FullName, "Empty.txt")).Should().BeEmpty();
        result.Changes.Should().Contain(change => change.Path.EndsWith("Empty.txt", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("-.-", true)]
    [InlineData("empty.directory", false)]
    public async Task InvokeAsync_CommittedDirectoryBeforeCancellation_RetainsSuccessfulEffects(string placeholder, bool useDefault)
    {
        await InstallAsync(placeholder, useDefault);
        using CancellationTokenSource cancellation = new();
        using RecordingSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive)
        {
            AfterCreation = cancellation.Cancel,
        };
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Directories", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "cancel-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), cancellation.Token);

        cancellation.IsCancellationRequested.Should().BeTrue();
        session.LiveCreationCalls.Should().Be(1);
        Directory.Exists(Path.Combine(output.Info.FullName, "src", "functions")).Should().BeTrue();
        File.ReadAllBytes(Path.Combine(output.Info.FullName, "Empty.txt")).Should().BeEmpty();
        result.Status.Should().Be(TemplateInvocationStatus.Success, result.Diagnostic);
        result.Changes.Select(change => change.Path).Should().BeEquivalentTo(session.CreatedChanges.Select(change => change.TargetRelativePath));
    }

    [Fact]
    public async Task InvokeAsync_CreationEffectsDifferFromNativePreview_DoesNotReportSuccess()
    {
        await InstallAsync("-.-");
        using RecordingSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive)
        {
            ChangeCreatedEffectPaths = true,
        };
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Directories", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "changed-effects-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), CancellationToken.None);

        session.LiveCreationCalls.Should().Be(1);
        Directory.Exists(Path.Combine(output.Info.FullName, "src", "functions")).Should().BeTrue();
        File.ReadAllText(Path.Combine(output.Info.FullName, "Created.txt")).Should().Be("CONTENT");
        result.Status.Should().Be(TemplateInvocationStatus.EngineFailure);
        result.Changes.Should().BeEmpty();
        File.Exists(Path.Combine(output.Info.FullName, "Unplanned.txt")).Should().BeFalse();
    }

    [SymbolicLinkFact]
    public async Task InvokeAsync_PlaceholderParentLinkedBeforeCreation_DoesNotWriteExternally()
    {
        await InstallAsync("-.-");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "linked-output"));
        Directory.CreateDirectory(output.Info.FullName);
        string outside = Directory.CreateDirectory(Path.Combine(_root, "linked-target")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(output.Info.FullName, "src"), outside);
        using RecordingSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Directories", TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("symbolic link or reparse point");
        session.LiveCreationCalls.Should().Be(0);
        Directory.GetFileSystemEntries(outside).Should().BeEmpty();
    }

    [SymbolicLinkFact]
    public async Task InvokeAsync_PlaceholderDirectoryBecomesLink_DoesNotAcceptLinkedEntry()
    {
        await InstallAsync("-.-");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        string outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        using RecordingSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive)
        {
            AfterCreation = () =>
            {
                string target = Path.Combine(output.Info.FullName, "src", "functions");
                Directory.Delete(target);
                Directory.CreateSymbolicLink(target, outside);
            },
        };
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync("Func.Tests.Directories", TemplateType.Item, CancellationToken.None)).Group![0];

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Created"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.EngineFailure);
        result.Changes.Should().BeEmpty();
        Directory.GetFileSystemEntries(outside).Should().BeEmpty();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private async Task InstallAsync(string placeholder, bool useDefault = false, bool includeDirectory = true)
    {
        string package = Path.Combine(_root, "package");
        Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        if (includeDirectory)
        {
            Directory.CreateDirectory(Path.Combine(package, "src", "functions"));
            File.WriteAllText(Path.Combine(package, "src", "functions", placeholder), string.Empty);
        }
        File.WriteAllText(Path.Combine(package, "Empty.txt"), string.Empty);
        File.WriteAllText(Path.Combine(package, "Created.txt"), "CONTENT");
        if (!useDefault)
        {
            File.WriteAllText(Path.Combine(package, "_._"), "ordinary file with an overridden default name");
        }

        File.WriteAllText(Path.Combine(package, ".template.config", "template.json"), $$"""
            {
              "identity": "Func.Tests.Directories",
              "name": "Directory effect fixture",
              "shortName": "func-tests-directories",
              "tags": { "type": "item" },
              "placeholderFilename": {{JsonSerializer.Serialize(useDefault ? null : placeholder)}}
            }
            """);
        await TemplateFixtures.InstallAsync(package, Hive);
    }

    private sealed class RecordingSession(TemplateEngineContext context, string hive) : TemplateEngineSession(context, hive)
    {
        public IReadOnlyList<IFileChange> PreviewChanges { get; private set; } = [];

        public IReadOnlyList<IFileChange> CreatedChanges { get; private set; } = [];

        public Action? AfterCreation { get; init; }

        public bool ChangeCreatedEffectPaths { get; init; }

        public int LiveCreationCalls { get; private set; }

        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
            InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            var native = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            if (dryRun)
            {
                PreviewChanges = [.. native.CreationEffects!.FileChanges];
            }
            else
            {
                LiveCreationCalls++;
                CreatedChanges = [.. native.CreationEffects!.FileChanges];
                AfterCreation?.Invoke();
                if (ChangeCreatedEffectPaths)
                {
                    var change = Substitute.For<IFileChange>();
                    change.TargetRelativePath.Returns("Unplanned.txt");
                    change.ChangeKind.Returns(ChangeKind.Create);
                    IReadOnlyList<IFileChange> changes = [change];
                    var effects = Substitute.For<ICreationEffects>();
                    effects.FileChanges.Returns(changes);
                    var result = Substitute.For<ITemplateCreationResult>();
                    result.Status.Returns(native.Status);
                    result.CreationEffects.Returns(effects);
                    return result;
                }
            }

            return native;
        }
    }
}
