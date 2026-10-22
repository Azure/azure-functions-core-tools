// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class TemplateMountedSchemaCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "func-mounted-composition-" + Guid.NewGuid().ToString("N"));

    private string Hive => Path.Combine(_root, "hive");

    [Fact]
    public async Task OrdinaryRunner_InternalRoleAddedAfterCache_RejectsMountedHelperBeforeCreation()
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        await TemplateFixtures.InstallAsync(package, Hive);
        using (Templater first = CreateTemplater())
        {
            var original = (await first.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
            original.Entry.DeclaredInternalRole.Should().BeNull();
            var baselineOutput = WorkingDirectory.FromExplicit(Path.Combine(_root, "baseline-output"));

            var baseline = await ExecuteOrdinaryAsync(first, baselineOutput);

            baseline.Succeeded.Should().BeTrue();
            baseline.Kind.Should().Be("created");
            File.ReadAllText(Path.Combine(baselineOutput.Info.FullName, "Named.txt")).Should().Be("Named hello");
        }

        var configuration = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(configuration, File.ReadAllText(configuration).Replace(
            "\"type\": \"item\"", "\"type\": \"item\", \"func-role\": \"python-append-helper\"", StringComparison.Ordinal));
        using Templater second = CreateTemplater();
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));

        var result = await ExecuteOrdinaryAsync(second, output);

        result.Succeeded.Should().BeFalse();
        result.Kind.Should().Be("internalTemplate");
        result.SelectedTemplate!.DeclaredInternalRole.Should().Be("python-append-helper");
        Directory.Exists(output.Info.FullName).Should().BeFalse();

        static Task<NewExecutionResult> ExecuteOrdinaryAsync(Templater templater, WorkingDirectory destination)
        {
            var interaction = Substitute.For<IInteractionService>();
            var contexts = Substitute.For<INewExecutionContextResolver>();
            contexts.ResolveAsync(Arg.Any<NewExecutionRequest>(), false, Arg.Any<CancellationToken>())
                .Returns(new NewExecutionContextResult(templater.Context, null));
            var factory = Substitute.For<ITemplaterFactory>();
            factory.CreateAsync(Arg.Any<TemplateEngineContext>(), Arg.Any<TemplateConstraintEvaluationSnapshot>(),
                Arg.Any<CancellationToken>(), Arg.Any<Action<TemplateStoreWaitReason>?>()).Returns(templater);
            TemplateArgumentParser parser = new();
            NewExecutionRunner runner = new(contexts, factory, parser, interaction, new(parser, interaction));
            var request = new NewExecutionRequest(destination, "Func.Tests.Journey", "Named", null, false, true, false, false, false,
                [], new HashSet<string>());
            return runner.ExecuteAsync(request, new([]), CancellationToken.None);
        }
    }

    [Fact]
    public async Task ResolveGroupAsync_DefaultNameChangedAfterCache_RetainsMountedSnapshotAndProjection()
    {
        var package = TemplateFixtures.WriteJourneyPackage(_root);
        var configuration = Path.Combine(package, "item", ".template.config", "template.json");
        File.WriteAllText(configuration, File.ReadAllText(configuration).Replace(
            "\"sourceName\": \"ItemFunction\"", "\"sourceName\": \"ItemFunction\", \"defaultName\": \"CachedName\"", StringComparison.Ordinal));
        await TemplateFixtures.InstallAsync(package, Hive);
        using (Templater first = CreateTemplater())
        {
            var original = (await first.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
            original.Entry.DefaultName.Should().Be("CachedName");
        }

        File.WriteAllText(configuration, File.ReadAllText(configuration).Replace("CachedName", "MountedName", StringComparison.Ordinal));
        using TemplateEngineSession session = new(new(WorkingDirectory.FromExplicit(_root)), Hive,
            new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)]));
        var descriptor = (await session.PackageManager.GetTemplatesAsync(CancellationToken.None)).Single();
        descriptor.DefaultName.Should().Be("CachedName");
        var snapshot = await session.ReadConfigurationAsync(descriptor, CancellationToken.None);
        using Templater second = new(session);

        var current = (await second.ResolveGroupAsync("Func.Tests.Journey", TemplateType.Item, CancellationToken.None)).Group![0];
        var output = WorkingDirectory.FromExplicit(Path.Combine(_root, "output"));
        var created = await current.InvokeAsync(new(output, new Dictionary<string, string?>(), current.Entry.DefaultName), CancellationToken.None);

        snapshot.DefaultName.Should().Be("MountedName");
        current.Entry.DefaultName.Should().Be("MountedName");
        created.Status.Should().Be(TemplateInvocationStatus.Success, created.Diagnostic);
        File.ReadAllText(Path.Combine(output.Info.FullName, "MountedName.txt")).Should().Be("MountedName hello");
        File.Exists(Path.Combine(output.Info.FullName, "CachedName.txt")).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private Templater CreateTemplater()
    {
        var directory = WorkingDirectory.FromExplicit(_root);
        return new(new TemplateEngineSession(new(directory, new(directory, "node", "CSharp")), Hive,
            new([new("node-package", NuGetVersion.Parse("1.0"), ["node"], true)])));
    }
}
