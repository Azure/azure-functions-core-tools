// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class NativeTemplatePreparationCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "native-preparation-" + Guid.NewGuid().ToString("N"));

    private string Package => Path.Combine(_root, "package");
    private string Target => Path.Combine(_root, "project");
    private string Configuration => Path.Combine(Package, ".template.config", "template.json");

    [Fact]
    public async Task Capture_PrivateNativeExpansion_ReturnsEvaluatedOutputsBytesAndOriginalContextWithoutLiveWritesOrActions()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync("Controlled.Preparation", TemplateType.Item)).Group![0];
        var request = new TemplateInvocationRequest(WorkingDirectory.FromExplicit(Target),
            new Dictionary<string, string?> { ["Value"] = "Canonical" }, "Named", DryRun: true);

        var prepared = await selected.CaptureNativePreparationAsync(request, expandFiles: true, CancellationToken.None);

        prepared.Failure.Should().BeNull();
        prepared.Snapshot!.Source.TemplateIdentity.Should().Be(selected.Entry.Identity);
        prepared.Snapshot.Source.Identity.Should().Be(selected.ConfigurationSourceIdentity);
        prepared.Snapshot.Source.Sha256.Should().Be(selected.ConfigurationSha256);
        prepared.Snapshot.Effects.CreationResult.PrimaryOutputs.Should().ContainSingle().Which.Path.Should().EndWith("Named.fs");
        prepared.Snapshot.Effects.CreationResult.PostActions.Should().ContainSingle();
        var file = prepared.Snapshot.ExpandedFiles.Should().ContainSingle().Subject;
        file.Key.Should().EndWith("Named.fs");
        Encoding.UTF8.GetString(file.Value.Span).Should().Be($"Named|Canonical|{Target}|dotnet");
        Directory.Exists(Target).Should().BeFalse();

        var ordinary = await selected.InvokeAsync(request, CancellationToken.None);
        ordinary.Status.Should().Be(TemplateInvocationStatus.Failed);
        ordinary.Diagnostic.Should().Contain("qualified dispatcher");
    }

    [Fact]
    public async Task Capture_ReadOnlyPreview_DoesNotNeedEffectiveNameOrExpandFiles()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync("Controlled.Preparation", TemplateType.Item)).Group![0];

        var prepared = await selected.CaptureNativePreparationAsync(new(WorkingDirectory.FromExplicit(Target),
            new Dictionary<string, string?>()), expandFiles: false, CancellationToken.None);

        prepared.Failure.Should().BeNull();
        prepared.Snapshot!.ExpandedFiles.Should().BeEmpty();
        prepared.Snapshot.Effects.CreationResult.PrimaryOutputs.Should().ContainSingle();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("parameters")]
    [InlineData("changed-source")]
    [InlineData("occupied-output")]
    public async Task Capture_UnqualifiedInputsOrOutputs_RejectWithoutMutatingLiveTarget(string invalid)
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync("Controlled.Preparation", TemplateType.Item)).Group![0];
        if (invalid == "changed-source")
        {
            File.AppendAllText(Configuration, Environment.NewLine);
        }

        string occupied = Path.Combine(Target, "Named.fs");
        if (invalid == "occupied-output")
        {
            Directory.CreateDirectory(Target);
            File.WriteAllText(occupied, "user-owned");
        }

        var prepared = await selected.CaptureNativePreparationAsync(new(WorkingDirectory.FromExplicit(Target),
            invalid == "parameters" ? new Dictionary<string, string?> { ["Unknown"] = "value" } : [],
            invalid == "name" ? null : "Named"), expandFiles: true, CancellationToken.None);

        prepared.Snapshot.Should().BeNull();
        prepared.Failure.Should().NotBeNull();
        if (invalid == "occupied-output")
        {
            File.ReadAllText(occupied).Should().Be("user-owned");
        }
        else
        {
            Directory.Exists(Target).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Capture_Cancellation_PropagatesWithoutLiveWrites()
    {
        using var templater = await OpenAsync();
        var selected = (await templater.ResolveGroupAsync("Controlled.Preparation", TemplateType.Item)).Group![0];
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => selected.CaptureNativePreparationAsync(new(WorkingDirectory.FromExplicit(Target),
            new Dictionary<string, string?>(), "Named"), expandFiles: true, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(Target).Should().BeFalse();
    }

    [Fact]
    public void Snapshot_CopiesNativeActionsOutputsMappingsAndBytes()
    {
        Dictionary<string, string> arguments = new() { ["files"] = "0" };
        var action = Substitute.For<IPostAction>();
        action.ActionId.Returns(Guid.Parse("84c0da21-51c8-4541-9940-6ca19af04ee6"));
        action.Args.Returns(arguments);
        var path = Substitute.For<ICreationPath>();
        path.Path.Returns("Named.fs");
        var creation = Substitute.For<ICreationResult>();
        creation.PrimaryOutputs.Returns([path]);
        creation.PostActions.Returns([action]);
        var mapping = Substitute.For<IFileChange2>();
        mapping.SourceRelativePath.Returns("Item.fs");
        mapping.TargetRelativePath.Returns("Named.fs");
        mapping.ChangeKind.Returns(ChangeKind.Create);
        var effects = Substitute.For<ICreationEffects>();
        effects.FileChanges.Returns([mapping]);
        effects.CreationResult.Returns(creation);
        byte[] bytes = [1, 2, 3];
        Dictionary<string, ReadOnlyMemory<byte>> files = new() { ["Named.fs"] = bytes };
        NativeTemplatePreparationSnapshot snapshot = new(new("Selected", "config", new('A', 64)), effects, files);

        bytes[0] = 99;
        files.Clear();
        arguments["files"] = "changed";
        path.Path.Returns("Changed.fs");
        mapping.TargetRelativePath.Returns("Changed.fs");
        creation.PostActions.Returns([]);

        snapshot.ExpandedFiles["Named.fs"].ToArray().Should().Equal(1, 2, 3);
        snapshot.Effects.FileChanges.Should().ContainSingle().Which.TargetRelativePath.Should().Be("Named.fs");
        snapshot.Effects.CreationResult.PrimaryOutputs.Should().ContainSingle().Which.Path.Should().Be("Named.fs");
        snapshot.Effects.CreationResult.PostActions.Should().ContainSingle().Which.Args["files"].Should().Be("0");
    }

    [Theory]
    [InlineData("extra-file")]
    [InlineData("extra-directory")]
    [InlineData("wrong-kind")]
    [InlineData("changed-source")]
    public async Task Capture_UnexpectedPrivateExpansion_RefusesDetachedSnapshot(string invalid)
    {
        using var templater = await OpenAsync((fileSystem, directory, _) =>
        {
            if (invalid == "extra-file") fileSystem.WriteAllText(Path.Combine(directory, "unexpected.txt"), "not-previewed");
            if (invalid == "extra-directory") fileSystem.CreateDirectory(Path.Combine(directory, "unexpected"));
            if (invalid == "wrong-kind")
            {
                fileSystem.CreateDirectory(Path.Combine(directory, "Named.fs"));
            }
            if (invalid == "changed-source") File.AppendAllText(Configuration, Environment.NewLine);
        });
        var selected = (await templater.ResolveGroupAsync("Controlled.Preparation", TemplateType.Item)).Group![0];

        var prepared = await selected.CaptureNativePreparationAsync(new(WorkingDirectory.FromExplicit(Target),
            new Dictionary<string, string?>(), "Named"), expandFiles: true, CancellationToken.None);

        prepared.Snapshot.Should().BeNull();
        prepared.Failure.Should().NotBeNull();
        Directory.Exists(Target).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<Templater> OpenAsync(Action<IPhysicalFileSystem, string, CancellationToken>? afterExpansion = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Configuration)!);
        File.WriteAllText(Configuration, """
            {"identity":"Controlled.Preparation","name":"Controlled preparation","shortName":"controlled-preparation",
             "tags":{"type":"item","language":"F#"},"sourceName":"ItemName","primaryOutputs":[{"path":"ItemName.fs"}],
             "symbols":{"Value":{"type":"parameter","datatype":"string","defaultValue":"default","replaces":"VALUE"},
               "Execution":{"type":"bind","binding":"host:WorkingDirectory","replaces":"EXECUTION"},
               "Stack":{"type":"bind","binding":"host:func:stack","replaces":"STACK"}},
             "postActions":[{"actionId":"f3ba64f5-68a9-4ac4-9cfe-4519e4940a77","args":{"executable":"not-executed"}}]}
            """);
        File.WriteAllText(Path.Combine(Package, "ItemName.fs"), "ItemName|VALUE|EXECUTION|STACK");
        string hive = Path.Combine(_root, "hive");
        await TemplateFixtures.InstallAsync(Package, hive, CancellationToken.None);
        var directory = WorkingDirectory.FromExplicit(Target);
        return TemplateFixtures.CreateTemplater(new CaptureSession(new(directory, new(directory, "dotnet", "F#")), hive, afterExpansion));
    }

    private sealed class CaptureSession(TemplateEngineContext context, string hive, Action<IPhysicalFileSystem, string, CancellationToken>? afterExpansion)
        : TemplateEngineSession(context, hive)
    {
        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request,
            InputDataSet parameters, bool dryRun, CancellationToken cancellationToken)
        {
            var result = await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            if (!dryRun) afterExpansion?.Invoke(Settings.Host.FileSystem, request.OutputDirectory.Info.FullName, cancellationToken);
            return result;
        }
    }
}
