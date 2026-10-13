// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge.Template;
using NuGet.Versioning;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplaterTests : IDisposable
{
    private readonly string _root;
    private readonly string _hive;

    public TemplaterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "func-templater-tests-" + Guid.NewGuid().ToString("N"));
        _hive = Path.Combine(_root, "hive");
        Directory.CreateDirectory(_hive);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp hive
        }
    }

    [Fact]
    public async Task NativeEngine_InstalledItem_PreviewIsReadOnlyAndCreationUsesSameTemplate()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        var output = Path.Combine(_root, "generated");
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(output)), _hive);
        IReadOnlyList<ITemplateInfo> templates = await session.PackageManager.GetTemplatesAsync(CancellationToken.None);
        var selected = templates.Should().ContainSingle(template => template.Identity == TemplateFixtures.ItemTemplateIdentity).Subject;
        TemplateCreator creator = new(session.Settings);

        var preview = await creator.InstantiateAsync(selected, "Welcome", "Welcome", output, new Dictionary<string, string?>(), dryRun: true, cancellationToken: CancellationToken.None);

        preview.Status.Should().Be(CreationResultStatus.Success);
        var change = preview.CreationEffects!.FileChanges.Should().ContainSingle().Subject;
        Path.GetFullPath(Path.Combine(output, change.TargetRelativePath)).Should().Be(Path.Combine(output, "Welcome.txt"));
        Directory.Exists(output).Should().BeFalse();

        var created = await creator.InstantiateAsync(selected, "Welcome", "Welcome", output, new Dictionary<string, string?>(), cancellationToken: CancellationToken.None);

        created.Status.Should().Be(CreationResultStatus.Success);
        File.ReadAllText(Path.Combine(output, "Welcome.txt")).Should().Be("Welcome content");
    }

    [Fact]
    public async Task ItemJourney_ListSelectPreviewCreate_UsesFuncOwnedPath()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]);
        using Templater templater = CreateTemplater(snapshot);
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "journey-output"));
        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        var resolution = await templater.CreateGroupAsync(entries, CancellationToken.None);
        var selected = resolution.Group!.Should().ContainSingle().Subject;
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?> { ["Greeting"] = "welcome" }, "Journey", DryRun: true);

        var preview = await selected.InvokeAsync(request, CancellationToken.None);

        resolution.Status.Should().Be(TemplateResolutionStatus.Success);
        selected.Entry.Should().BeSameAs(entries[0]);
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        preview.Changes.Should().ContainSingle().Which.Path.Should().EndWith("Journey.txt");
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        var created = await selected.InvokeAsync(request with { DryRun = false }, CancellationToken.None);

        created.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(Path.Combine(output.Info.FullName, "Journey.txt")).Should().Be("Journey welcome");
    }

    [Fact]
    public async Task ResolveGroupAsync_RestrictedTemplate_DoesNotExposeInvocableItem()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync("FUNC-TESTS-JOURNEY", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        result.Group.Should().BeEmpty();
        result.Diagnostics.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Restricted);
    }

    [Fact]
    public async Task ResolveGroupAsync_WrongType_DoesNotExposeGroup()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.WrongType);
        result.Group.Should().BeNull();
    }

    [Fact]
    public async Task ResolveGroupAsync_SharedShortNameAcrossGroups_ReportsAmbiguity()
    {
        var first = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "first"));
        var second = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "second"));
        var config = Path.Combine(second, "item", ".template.config", "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace(TemplateFixtures.ItemTemplateIdentity, "Func.Tests.OtherItem", StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(first, _hive);
        await TemplateFixtures.InstallAsync(second, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateShortName, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.AmbiguousGroup);
        result.Group.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_RawDuplicateConstraintTypes_FailsBeforeResolution()
    {
        var package = TemplateFixtures.WriteConstraintPackage(_root, """{"stack":{"type":"func-workload","args":"node"}}""");
        await TemplateFixtures.InstallAsync(package, _hive);
        var config = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"type\":\"func-workload\"", "\"type\":\"func-workload\",\"type\":\"func-workload\"", StringComparison.Ordinal));
        using Templater templater = CreateTemplater();

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        var resolved = await templater.CreateGroupAsync(entries, CancellationToken.None);

        entries.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Failed);
        resolved.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        resolved.Group.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ConfigurationChangedAfterSelection_FailsWithoutWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var config = Path.Combine(package, "item", ".template.config", "template.json");
        File.AppendAllText(config, " ");
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "changed-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Changed"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.TemplateChanged);
        result.Diagnostic.Should().Contain("changed");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_DisposedFlow_ThrowsWithoutWrites()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "disposed-output"));
        templater.Dispose();

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Disposed"), CancellationToken.None);

        await action.Should().ThrowAsync<ObjectDisposedException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_CanceledFlow_ThrowsWithoutWrites()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canceled-output"));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Canceled"), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_UnknownCanonicalParameter_FailsWithoutWrites()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "invalid-parameter-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["unknown"] = "value" }, "Invalid"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        result.Diagnostic.Should().Contain("canonical");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_FileConflict_RequiresExplicitForce()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "conflict-output"));
        Directory.CreateDirectory(output.Info.FullName);
        var target = Path.Combine(output.Info.FullName, "Conflict.txt");
        File.WriteAllText(target, "original");
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "Conflict");

        var refused = await selected.InvokeAsync(request, CancellationToken.None);

        refused.Status.Should().Be(TemplateInvocationStatus.FileConflict);
        refused.Changes.Should().ContainSingle();
        File.ReadAllText(target).Should().Be("original");

        var preview = await selected.InvokeAsync(request with { DryRun = true }, CancellationToken.None);
        preview.Status.Should().Be(TemplateInvocationStatus.Success);
        preview.Changes.Should().ContainSingle().Which.Path.Should().EndWith("Conflict.txt");
        preview.Diagnostic.Should().Contain("requires explicit");
        File.ReadAllText(target).Should().Be("original");

        var forced = await selected.InvokeAsync(request with { Force = true }, CancellationToken.None);

        forced.Status.Should().Be(TemplateInvocationStatus.Success);
        File.ReadAllText(target).Should().Be("Conflict content");
    }

    [Fact]
    public async Task InvokeAsync_UnsupportedPostAction_FailsWithoutWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var config = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"sourceName\": \"ItemFunction\"", "\"sourceName\": \"ItemFunction\", \"postActions\": [{\"actionId\":\"3A7C4B45-1F5D-4A30-959A-51B88E82B5D2\",\"description\":\"fixture\"}]", StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "post-action-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Action"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        result.Diagnostic.Should().Contain("post-actions");
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_SupplementalConfiguration_DoesNotHideUnvalidatedRequirements()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var configDirectory = Path.Combine(package, "item", ".template.config");
        var config = Path.Combine(configDirectory, "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"sourceName\": \"ItemFunction\"", "\"sourceName\": \"ItemFunction\", \"additionalConfigFiles\": [\"extra.template.json\"]", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(configDirectory, "extra.template.json"), """{"constraints":{"stack":{"type":"func-workload","args":"node"}}}""");
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        result.Group.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvedTemplate_ParameterMetadata_CannotBeMutated()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));
        var selected = (await templater.ResolveGroupAsync("func-tests-journey", TemplateType.Item, CancellationToken.None)).Group![0];

        Action changeParameter = () => ((IList<TemplateParameterDefinition>)selected.Parameters)[0] = selected.Parameters[0] with { CanonicalName = "Changed" };

        changeParameter.Should().Throw<NotSupportedException>();
        selected.Parameters.Should().ContainSingle(parameter => parameter.CanonicalName == "Greeting");
    }

    [Fact]
    public async Task TemplateGroup_Filters_PreserveSelectedCapability()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));
        var group = (await templater.ResolveGroupAsync("func-tests-journey", TemplateType.Item, CancellationToken.None)).Group!;

        var filtered = group.FilterByLanguage("csharp").FilterByArguments(new HashSet<string> { "Func.Tests.Journey" }).FilterToHighestPrecedence();

        filtered.Should().ContainSingle().Which.Should().BeSameAs(group[0]);
        group.Should().ContainSingle();
        group.FilterByLanguage("Python").Should().BeEmpty();
        group.FilterByArguments(new HashSet<string>()).FilterToHighestPrecedence().Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveGroupAsync_UnknownReference_ReportsNotFound()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync("missing", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.NotFound);
        result.Group.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_DeletedConfiguration_FailsWithoutWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        File.Delete(Path.Combine(package, "item", ".template.config", "template.json"));
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "deleted-config-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Missing"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_LinkedOutputChild_RejectsExternalWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var item = Path.Combine(package, "item");
        Directory.CreateDirectory(Path.Combine(item, "link"));
        File.Move(Path.Combine(item, "ItemFunction.txt"), Path.Combine(item, "link", "ItemFunction.txt"));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "linked-output"));
        var outside = Path.Combine(_root, "outside-output");
        Directory.CreateDirectory(output.Info.FullName);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(output.Info.FullName, "link"), outside);

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Linked"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        File.Exists(Path.Combine(outside, "Linked.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvokeAsync_PermissionDeniedConfiguration_ReturnsFailureWithoutWrites(int deniedRead)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        var session = new DenyingConfigurationSession(new(WorkingDirectory.FromExplicit(_root)), _hive);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "denied-config-output"));
        session.DeniedRead = deniedRead;

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Denied"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListAsync_NuGetPackageTypes_EnforceHostSharing(bool shared)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteSharedJourneyPackage(_root, shared), _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));

        var result = await templater.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(shared ? TemplateResolutionStatus.Unavailable : TemplateResolutionStatus.Success);
        result.Group.Should().HaveCount(shared ? 0 : 1);
        result.Diagnostics.Should().HaveCount(shared ? 1 : 0);
        if (shared)
        {
            result.Diagnostics[0].ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Failed);
        }
    }

    [Fact]
    public async Task InvokeAsync_LinkIntroducedAfterPreview_RejectsExternalWrites()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var item = Path.Combine(package, "item");
        Directory.CreateDirectory(Path.Combine(item, "link"));
        File.Move(Path.Combine(item, "ItemFunction.txt"), Path.Combine(item, "link", "ItemFunction.txt"));
        await TemplateFixtures.InstallAsync(package, _hive);
        var session = new DenyingConfigurationSession(new(WorkingDirectory.FromExplicit(_root)), _hive);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "late-linked-output"));
        var outside = Path.Combine(_root, "late-outside-output");
        Directory.CreateDirectory(output.Info.FullName);
        Directory.CreateDirectory(outside);
        session.AfterPreview = () => Directory.CreateSymbolicLink(Path.Combine(output.Info.FullName, "link"), outside);

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "LateLinked"), CancellationToken.None);

        result.Status.Should().Be(TemplateInvocationStatus.Failed);
        File.Exists(Path.Combine(outside, "LateLinked.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ListAsync_InvalidPackageTypeMetadata_PreservesHealthyCatalogEntries()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "healthy")), _hive);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteSharedJourneyPackage(Path.Combine(_root, "malformed"), packageTypeVersion: "invalid-version"), _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle(entry => entry.Identity == TemplateFixtures.ItemTemplateIdentity && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Eligible);
        entries.Should().ContainSingle(entry => entry.Identity == "Func.Tests.Journey" && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Failed);
    }

    [Fact]
    public async Task ResolveGroupAsync_MixedTypeShortName_PreservesWrongTypeDiagnostic()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var project = Path.Combine(package, "project", ".template.config", "template.json");
        File.WriteAllText(project, File.ReadAllText(project).Replace(TemplateFixtures.ProjectTemplateShortName, TemplateFixtures.ItemTemplateShortName, StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateShortName, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Success);
        result.Group.Should().ContainSingle().Which.Entry.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
        result.Diagnostics.Should().ContainSingle().Which.Type.Should().Be(TemplateType.Project);
        result.Group!.Diagnostics.Should().BeEquivalentTo(result.Diagnostics);
    }

    [Theory]
    [InlineData(TemplateFixtures.OtherTypeTemplateIdentity)]
    [InlineData(TemplateFixtures.UntypedTemplateIdentity)]
    public async Task ResolveGroupAsync_MalformedType_ReportsAuthoringDiagnostic(string identity)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(identity, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.InvalidTemplateType);
        result.Diagnostic.Should().Contain(identity);
        result.Group.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListAsync_UnreadableCandidate_PreservesHealthyCatalogEntries(bool ioFailure)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteJourneyPackage(_root), _hive);
        var session = new DenyingConfigurationSession(new(WorkingDirectory.FromExplicit(_root)), _hive) { DeniedIdentity = "Func.Tests.Journey", DeniedWithIo = ioFailure };
        using Templater templater = new(session);

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle(entry => entry.Identity == TemplateFixtures.ItemTemplateIdentity && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Eligible);
        entries.Should().ContainSingle(entry => entry.Identity == "Func.Tests.Journey" && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Failed);
    }

    [Fact]
    public async Task ResolveGroupAsync_MalformedIdentityBeforeValidAlias_ReportsAuthoringDiagnostic()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var item = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(item, File.ReadAllText(item).Replace(TemplateFixtures.ItemTemplateShortName, TemplateFixtures.OtherTypeTemplateIdentity, StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.OtherTypeTemplateIdentity, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.InvalidTemplateType);
        result.Group.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveGroupAsync_RejectedGroups_DoNotCreateEligibleAmbiguity(bool allUnavailable)
    {
        var basic = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "basic-family"));
        if (allUnavailable)
        {
            var config = Path.Combine(basic, "item", ".template.config", "template.json");
            File.WriteAllText(config, File.ReadAllText(config).Replace("\"sourceName\": \"ItemFunction\"", "\"sourceName\": \"ItemFunction\", \"constraints\":{\"stack\":{\"type\":\"func-workload\",\"args\":\"python\"}}", StringComparison.Ordinal));
        }

        var restricted = TemplateFixtures.WriteJourneyPackage(Path.Combine(_root, "restricted-family"));
        var restrictedConfig = Path.Combine(restricted, "item", ".template.config", "template.json");
        File.WriteAllText(restrictedConfig, File.ReadAllText(restrictedConfig).Replace("func-tests-journey", TemplateFixtures.ItemTemplateShortName, StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(basic, _hive);
        await TemplateFixtures.InstallAsync(restricted, _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateShortName, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(allUnavailable ? TemplateResolutionStatus.Unavailable : TemplateResolutionStatus.Success);
        result.Group.Should().HaveCount(allUnavailable ? 0 : 1);
        result.Diagnostics.Should().HaveCount(allUnavailable ? 2 : 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListAsync_MultipleItemsInArchive_ReadsPackageMetadataOnce(bool malformed)
    {
        var package = TemplateFixtures.WriteSharedJourneyPackage(_root, shared: false, packageTypeVersion: malformed ? "invalid-version" : null, multiple: true);
        await TemplateFixtures.InstallAsync(package, _hive);
        var session = new CountingPackageSession(new(WorkingDirectory.FromExplicit(_root)), _hive);
        using Templater templater = new(session);

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().HaveCount(2);
        session.PackageReads.Should().Be(1);
        if (malformed)
        {
            entries.Should().OnlyContain(entry => entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Failed);
        }

        var freshSession = new CountingPackageSession(new(WorkingDirectory.FromExplicit(_root)), _hive);
        using Templater fresh = new(freshSession);
        await fresh.ListAsync(TemplateType.Item, CancellationToken.None);
        freshSession.PackageReads.Should().Be(1);
    }

    [Fact]
    public async Task ListAsync_PermissiveFuncArgs_RemainEligible()
    {
        var package = TemplateFixtures.WriteConstraintPackage(_root, """{"stack":{"type":"func-workload","args":[/* fixture */"node",]}}""");
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.Eligible);
    }

    [Fact]
    public async Task ResolveGroupAsync_CaseDifferentIdentity_DoesNotPreemptShortName()
    {
        var basic = TemplateFixtures.WriteBasicPackage(Path.Combine(_root, "identity-package"));
        var alias = TemplateFixtures.WriteJourneyPackage(Path.Combine(_root, "alias-package"));
        var aliasConfig = Path.Combine(alias, "item", ".template.config", "template.json");
        var reference = TemplateFixtures.ItemTemplateIdentity.ToLowerInvariant();
        File.WriteAllText(aliasConfig, File.ReadAllText(aliasConfig).Replace("func-tests-journey", reference, StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(basic, _hive);
        await TemplateFixtures.InstallAsync(alias, _hive);
        using Templater templater = CreateTemplater(new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));

        var result = await templater.ResolveGroupAsync(reference, TemplateType.Item, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Success);
        result.Group.Should().ContainSingle().Which.Entry.Identity.Should().Be("Func.Tests.Journey");

        var exact = await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None);
        exact.Group.Should().ContainSingle().Which.Entry.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
        var aliasCase = await templater.ResolveGroupAsync("FUNC-TESTS-ITEM", TemplateType.Item, CancellationToken.None);
        aliasCase.Group.Should().ContainSingle().Which.Entry.Identity.Should().Be(TemplateFixtures.ItemTemplateIdentity);
    }

    [Fact]
    public async Task ResolveGroupAsync_ProjectInItemOnlyFlow_DoesNotGrantInvocation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        var result = await templater.ResolveGroupAsync(TemplateFixtures.ProjectTemplateIdentity, TemplateType.Project, CancellationToken.None);

        result.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        result.Group.Should().BeEmpty();
        var entries = await templater.ListAsync(TemplateType.Project, CancellationToken.None);
        var picked = await templater.CreateGroupAsync(entries, CancellationToken.None);
        picked.Status.Should().Be(TemplateResolutionStatus.Unavailable);
        picked.Group.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvokeAsync_EngineReturnsAfterCancellation_PropagatesCancellation(bool cancelPreview)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using CancellationTokenSource cancellation = new();
        var session = new CancelingEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, cancellation, cancelPreview);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "engine-canceled-output"));

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Canceled", DryRun: cancelPreview), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_PreCanceledUnknownParameter_PropagatesCancellation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "canceled-unknown-output"));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["unknown"] = "value" }, "Canceled"), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(CreationResultStatus.InvalidParamValues, "InvalidArguments")]
    [InlineData(CreationResultStatus.MissingMandatoryParam, "MissingRequiredParameters")]
    [InlineData(CreationResultStatus.CreateFailed, "EngineFailure")]
    [InlineData(CreationResultStatus.DestructiveChangesDetected, "FileConflict")]
    [InlineData(CreationResultStatus.NotFound, "TemplateUnavailable")]
    [InlineData(CreationResultStatus.TemplateIssueDetected, "InvalidTemplate")]
    [InlineData(CreationResultStatus.CondtionsEvaluationMismatch, "InvalidTemplate")]
    public async Task InvokeAsync_NativeFailure_PreservesStructuredCategory(CreationResultStatus nativeStatus, string expected)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        var session = new FailingEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, nativeStatus);
        using Templater templater = new(session);
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "native-failure-output"));

        var result = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Failure"), CancellationToken.None);

        result.Status.ToString().Should().Be(expected);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_PreflightFailures_PreserveStructuredCategories()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "typed-failure-output"));
        Directory.CreateDirectory(output.Info.FullName);
        File.WriteAllText(Path.Combine(output.Info.FullName, "Typed.txt"), "original");
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "Typed");

        var invalid = await selected.InvokeAsync(request with { Parameters = new Dictionary<string, string?> { ["unknown"] = "value" } }, CancellationToken.None);
        var conflict = await selected.InvokeAsync(request, CancellationToken.None);
        File.AppendAllText(Path.Combine(package, "item", ".template.config", "template.json"), " ");
        var changed = await selected.InvokeAsync(request with { Force = true }, CancellationToken.None);

        invalid.Status.ToString().Should().Be("InvalidArguments");
        conflict.Status.ToString().Should().Be("FileConflict");
        changed.Status.ToString().Should().Be("TemplateChanged");
        File.ReadAllText(Path.Combine(output.Info.FullName, "Typed.txt")).Should().Be("original");
    }

    [Fact]
    public async Task InvokeAsync_NativeCancelledStatus_PropagatesCancellation()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = new(new FailingEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, CreationResultStatus.Cancelled));
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "native-cancelled-output"));

        Func<Task> action = () => selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Cancelled"), CancellationToken.None);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_RealParameterValidation_PreservesInvalidAndMissingCategories()
    {
        var package = TemplateFixtures.WriteBasicPackage(_root);
        var config = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(config, File.ReadAllText(config).Replace("\"sourceName\": \"ItemFunction\"", "\"sourceName\": \"ItemFunction\", \"symbols\":{\"Mode\":{\"type\":\"parameter\",\"datatype\":\"choice\",\"isRequired\":true,\"choices\":[{\"choice\":\"safe\"}]}}", StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, _hive);
        using Templater templater = CreateTemplater();
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "invalid-values-output"));

        var invalid = await selected.InvokeAsync(new(output, new Dictionary<string, string?> { ["Mode"] = "unknown" }, "Invalid"), CancellationToken.None);
        var missing = await selected.InvokeAsync(new(output, new Dictionary<string, string?>(), "Missing"), CancellationToken.None);

        invalid.Status.Should().Be(TemplateInvocationStatus.InvalidArguments);
        missing.Status.Should().Be(TemplateInvocationStatus.MissingRequiredParameters);
        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Theory]
    [InlineData(CreationResultStatus.CreateFailed)]
    [InlineData(CreationResultStatus.Cancelled)]
    public async Task InvokeAsync_CreationFailureAfterSuccessfulPreview_PreservesCategory(CreationResultStatus nativeStatus)
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = new(new FailingEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, nativeStatus, true));
        var selected = (await templater.ResolveGroupAsync(TemplateFixtures.ItemTemplateIdentity, TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "creation-failure-output"));
        var request = new TemplateInvocationRequest(output, new Dictionary<string, string?>(), "Failure");

        if (nativeStatus == CreationResultStatus.Cancelled)
        {
            Func<Task> action = () => selected.InvokeAsync(request, CancellationToken.None);
            await action.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            var result = await selected.InvokeAsync(request, CancellationToken.None);
            result.Status.Should().Be(TemplateInvocationStatus.EngineFailure);
            result.Changes.Should().ContainSingle();
        }

        Directory.Exists(output.Info.FullName).Should().BeFalse();
    }

    [Fact]
    public async Task CreateGroupAsync_ForeignFlowEntry_RejectsUncheckedReplacement()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater first = CreateTemplater();
        using Templater second = CreateTemplater();
        var entries = await first.ListAsync(TemplateType.Item, CancellationToken.None);

        Func<Task> action = () => second.CreateGroupAsync(entries, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>().WithParameterName("entries");
    }

    [Fact]
    public async Task ListAsync_Item_ReturnsItemTemplateEntries()
    {
        string packageDirectory = TemplateFixtures.WriteBasicPackage(_root);
        await TemplateFixtures.InstallAsync(packageDirectory, _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle().Which.Should().BeEquivalentTo(new TemplateCatalogEntry(
            Identity: TemplateFixtures.ItemTemplateIdentity,
            Name: "Func Tests Item",
            Description: null,
            Type: TemplateType.Item,
            ShortNames: [TemplateFixtures.ItemTemplateShortName],
            GroupIdentity: null,
            Language: null,
            Precedence: 0,
            Package: new TemplatePackageInfo(packageDirectory, Version: null),
            IsHidden: false,
            Parameters: [],
            MetadataDiagnostic: null) { ConstraintEvaluation = TemplateConstraintEvaluation.Eligible });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListAsync_FuncWorkload_UsesCapturedUsability(bool usable)
    {
        string package = TemplateFixtures.WriteConstraintPackage(_root, """{"stack":{"type":"func-workload","args":"node"}}""");
        await TemplateFixtures.InstallAsync(package, _hive);
        var snapshot = new TemplateConstraintEvaluationSnapshot([new("node-package", NuGetVersion.Parse("1.0"), ["node"], usable)]);
        using Templater templater = new(new TemplateEngineSession(new(WorkingDirectory.FromExplicit(_root)), _hive, snapshot));

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        var entry = entries.Should().ContainSingle().Subject;
        entry.ConstraintEvaluation.Status.Should().Be(usable ? TemplateConstraintEvaluationStatus.Eligible : TemplateConstraintEvaluationStatus.Restricted);
        entry.ConstraintEvaluation.Diagnostics.Should().HaveCount(usable ? 0 : 1);
    }

    [Fact]
    public async Task ListAsync_UnknownConstraint_RemainsVisibleAndUnavailable()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteConstraintPackage(_root, """{"future":{"type":"future-constraint","args":{}}}"""), _hive);
        using Templater templater = CreateTemplater();

        var entries = await templater.ListAsync(TemplateType.Item, CancellationToken.None);

        entries.Should().ContainSingle().Which.ConstraintEvaluation.Status.Should().Be(TemplateConstraintEvaluationStatus.NotEvaluated);
    }

    [Fact]
    public async Task ListAsync_Project_ReturnsProjectTemplateEntries()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> entries = await templater.ListAsync(TemplateType.Project, CancellationToken.None);

        TemplateCatalogEntry entry = entries.Should().ContainSingle().Subject;
        entry.Identity.Should().Be(TemplateFixtures.ProjectTemplateIdentity);
        entry.Type.Should().Be(TemplateType.Project);
    }

    [Fact]
    public async Task ListAsync_TemplatesWithoutItemOrProjectType_AreNotListed()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteBasicPackage(_root), _hive);

        // The engine installs both, so only their type keeps them out of the catalog.
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.OtherTypeTemplateIdentity);
        await TemplateFixtures.GetInstalledTemplateAsync(_hive, TemplateFixtures.UntypedTemplateIdentity);
        using Templater templater = CreateTemplater();

        IReadOnlyList<TemplateCatalogEntry> items = await templater.ListAsync(TemplateType.Item, CancellationToken.None);
        IReadOnlyList<TemplateCatalogEntry> projects = await templater.ListAsync(TemplateType.Project, CancellationToken.None);

        items.Concat(projects).Select(entry => entry.Identity).Should().BeEquivalentTo(
            [TemplateFixtures.ItemTemplateIdentity, TemplateFixtures.ProjectTemplateIdentity]);
    }

    [Fact]
    public async Task ListAsync_FuncHostMetadata_AppliesParameterAliases()
    {
        IReadOnlyList<TemplateCatalogEntry> entries = await ListMetadataTemplatesAsync();

        TemplateCatalogEntry entry = Entry(entries, TemplateFixtures.MetadataTemplateIdentity);
        entry.Language.Should().Be("TypeScript");
        entry.MetadataDiagnostic.Should().BeNull();
        entry.Parameters.Select(parameter => (parameter.CanonicalName, parameter.LongName, parameter.ShortName)).Should().Equal(
            ("Level", "log-level", "l"),
            ("Namespace", "Namespace", null));
    }

    [Fact]
    public async Task ListAsync_HiddenTemplate_IsListedAsHidden()
    {
        IReadOnlyList<TemplateCatalogEntry> entries = await ListMetadataTemplatesAsync();

        Entry(entries, TemplateFixtures.HiddenTemplateIdentity).IsHidden.Should().BeTrue();
        Entry(entries, TemplateFixtures.DotnetHostOnlyTemplateIdentity).IsHidden
            .Should().BeFalse("dotnetcli.host.json only applies to dotnet new");
    }

    [Fact]
    public async Task ListAsync_InvalidFuncHostMetadata_ReportsDiagnosticOnEntry()
    {
        IReadOnlyList<TemplateCatalogEntry> entries = await ListMetadataTemplatesAsync();

        TemplateCatalogEntry entry = Entry(entries, TemplateFixtures.InvalidHostTemplateIdentity);
        entry.MetadataDiagnostic.Should().Contain("func.host.json");
        entry.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_FromEngineCache_ReturnsSameEntriesAsFromScan()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), _hive);
        IReadOnlyList<TemplateCatalogEntry> scanned = await ListWithNewTemplaterAsync(TemplateType.Item);

        IReadOnlyList<TemplateCatalogEntry> cached = await ListWithNewTemplaterAsync(TemplateType.Item);

        scanned.Select(entry => entry.Identity).Should().BeEquivalentTo(
        [
            TemplateFixtures.MetadataTemplateIdentity,
            TemplateFixtures.InvalidHostTemplateIdentity,
            TemplateFixtures.DotnetHostOnlyTemplateIdentity,
            TemplateFixtures.HiddenTemplateIdentity,
        ]);
        cached.Should().BeEquivalentTo(scanned);
    }

    [Fact]
    public async Task ListAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using Templater templater = CreateTemplater();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = () => templater.ListAsync(TemplateType.Item, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ListAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        Templater templater = CreateTemplater();
        templater.Dispose();

        Func<Task> act = () => templater.ListAsync(TemplateType.Item, CancellationToken.None);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Constructor_NullSession_Throws()
    {
        Action act = () => _ = new Templater(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("session");
    }

    private async Task<IReadOnlyList<TemplateCatalogEntry>> ListMetadataTemplatesAsync()
    {
        await TemplateFixtures.InstallAsync(TemplateFixtures.WriteMetadataPackage(_root), _hive);
        return await ListWithNewTemplaterAsync(TemplateType.Item);
    }

    private async Task<IReadOnlyList<TemplateCatalogEntry>> ListWithNewTemplaterAsync(TemplateType type)
    {
        using Templater templater = CreateTemplater();
        return await templater.ListAsync(type, CancellationToken.None);
    }

    private static TemplateCatalogEntry Entry(IReadOnlyList<TemplateCatalogEntry> entries, string identity)
        => entries.Should().ContainSingle(candidate => candidate.Identity == identity).Subject;

    private Templater CreateTemplater(TemplateConstraintEvaluationSnapshot? snapshot = null)
        => new(new TemplateEngineSession(new TemplateEngineContext(WorkingDirectory.FromExplicit(_root)), _hive, snapshot));

    private sealed class FailingEngineSession(TemplateEngineContext context, string hive, CreationResultStatus nativeStatus, bool failCreation = false) : TemplateEngineSession(context, hive)
    {
        protected override Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request, IReadOnlyDictionary<string, string?> parameters, bool dryRun, CancellationToken cancellationToken)
        {
            if (dryRun && failCreation)
            {
                return base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
            }

            var result = Substitute.For<ITemplateCreationResult>();
            result.Status.Returns(nativeStatus);
            return Task.FromResult(result);
        }
    }

    private sealed class CancelingEngineSession(TemplateEngineContext context, string hive, CancellationTokenSource cancellation, bool cancelPreview) : TemplateEngineSession(context, hive)
    {
        protected override async Task<ITemplateCreationResult> InstantiateAsync(ITemplateInfo template, TemplateInvocationRequest request, IReadOnlyDictionary<string, string?> parameters, bool dryRun, CancellationToken cancellationToken)
        {
            if (dryRun == cancelPreview)
            {
                await cancellation.CancelAsync();
                var result = Substitute.For<ITemplateCreationResult>();
                result.Status.Returns(CreationResultStatus.Success);
                return result;
            }

            return await base.InstantiateAsync(template, request, parameters, dryRun, cancellationToken);
        }
    }

    private sealed class CountingPackageSession(TemplateEngineContext context, string hive) : TemplateEngineSession(context, hive)
    {
        public int PackageReads { get; private set; }

        public override Task<IReadOnlyCollection<string>> ReadPackageTypesAsync(ITemplatePackage? package, CancellationToken cancellationToken)
        {
            PackageReads++;
            return base.ReadPackageTypesAsync(package, cancellationToken);
        }
    }

    private sealed class DenyingConfigurationSession(TemplateEngineContext context, string hive) : TemplateEngineSession(context, hive)
    {
        private int _readsAfterSelection;

        public int? DeniedRead { get; set; }

        public Action? AfterPreview { get; set; }

        public string? DeniedIdentity { get; set; }

        public bool DeniedWithIo { get; set; }

        public override Task<TemplateConfigurationSnapshot> ReadConfigurationAsync(ITemplateInfo template, CancellationToken cancellationToken)
        {
            if (template.Identity == DeniedIdentity)
            {
                if (DeniedWithIo)
                {
                    throw new IOException("Fixture candidate read failed.");
                }

                throw new UnauthorizedAccessException("Fixture candidate access denied.");
            }

            if (DeniedRead is not null || AfterPreview is not null)
            {
                _readsAfterSelection++;
                if (_readsAfterSelection == DeniedRead)
                {
                    throw new UnauthorizedAccessException("Fixture configuration access denied.");
                }

                if (_readsAfterSelection == 2)
                {
                    AfterPreview?.Invoke();
                }
            }

            return base.ReadConfigurationAsync(template, cancellationToken);
        }
    }
}
