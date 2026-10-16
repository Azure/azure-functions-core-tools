// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Edge.Template;

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
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        created.Changes.Should().BeEquivalentTo(preview.Changes);
        created.Changes.Should().Contain(change => change.Path == directory.TargetRelativePath && change.Kind == "Create");
        File.ReadAllText(Path.Combine(output.Info.FullName, "Created.txt")).Should().Be("CONTENT");
        File.ReadAllBytes(Path.Combine(output.Info.FullName, "Empty.txt")).Should().BeEmpty();
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

    private async Task InstallAsync(string placeholder, bool useDefault = false)
    {
        string package = Path.Combine(_root, "package");
        Directory.CreateDirectory(Path.Combine(package, ".template.config"));
        Directory.CreateDirectory(Path.Combine(package, "src", "functions"));
        File.WriteAllText(Path.Combine(package, "src", "functions", placeholder), string.Empty);
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
                CreatedChanges = [.. native.CreationEffects!.FileChanges];
                AfterCreation?.Invoke();
            }

            return native;
        }
    }
}
