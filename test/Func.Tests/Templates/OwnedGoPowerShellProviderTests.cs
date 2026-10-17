// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Go;
using Azure.Functions.Cli.Templates.PowerShell;
using Azure.Functions.Cli.Templates.Projects;
using NuGet.Packaging;
using HostGoPreparation = Azure.Functions.Cli.Templates.Go.Shared.GoProjectPreparation;
using HostPowerShellPreparation = Azure.Functions.Cli.Templates.PowerShell.Shared.PowerShellProjectPreparation;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledRemainingProjectTemplateTests
{
    [Fact]
    public void OwnedProviders_HostCompiledHelpers_AreSeparateFromUnchangedPhysicalHelperOracles()
    {
        typeof(HostGoPreparation).Assembly.FullName.Should().Be(typeof(GoNativeProjectPreparation).Assembly.FullName)
            .And.NotBe(typeof(Azure.Functions.Cli.Workloads.Go.GoProjectPreparation).Assembly.FullName);
        typeof(HostPowerShellPreparation).Assembly.FullName.Should().Be(typeof(PowerShellNativeProjectPreparation).Assembly.FullName)
            .And.NotBe(typeof(Azure.Functions.Cli.Workloads.PowerShell.PowerShellProjectPreparation).Assembly.FullName);
        new HostPowerShellPreparation().ReadRequirementsTemplate().Should()
            .Be(new Azure.Functions.Cli.Workloads.PowerShell.PowerShellProjectPreparation().ReadRequirementsTemplate());
    }

    [Fact]
    public void OwnedProviders_NullDependencies_AreProgrammerErrors()
    {
        Action[] calls =
        [
            () => new GoNativeProjectPreparation(null!, new HostGoPreparation()),
            () => new GoNativeProjectPreparation(_files, null!),
            () => new PowerShellNativeProjectPreparation(null!, new HostPowerShellPreparation()),
            () => new PowerShellNativeProjectPreparation(_files, null!),
            () => new GoProjectWorkflowProvider(null!),
            () => new PowerShellProjectWorkflowProvider(null!),
            () => new OwnedProjectPreparationSource(null!),
        ];
        foreach (Action call in calls) call.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Go", "go", 8)]
    [InlineData("Java", "java", 8)]
    [InlineData("PowerShell", "powershell", 7)]
    public void OwnedProviders_ActualNativePack_ContainsExactVersionedSourceManifest(string stack, string alias, int count)
    {
        using PackageArchiveReader outer = new(BuiltArchive(stack));
        using var bytes = new MemoryStream();
        outer.GetStream($"tools/any/templates/{alias}-basic.nupkg").CopyTo(bytes);
        bytes.Position = 0;
        using PackageArchiveReader native = new(bytes);
        using var manifest = JsonDocument.Parse(Read(native, $"provenance/{alias}/project.json"));
        JsonElement root = manifest.RootElement;
        root.GetProperty("packageId").GetString().Should().Be(native.NuspecReader.GetId());
        root.GetProperty("packageVersion").GetString().Should().Be(native.NuspecReader.GetVersion().ToNormalizedString());
        root.GetProperty("identity").GetString().Should().Be($"Azure.Functions.{stack}.Basic");
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        sources.Should().HaveCount(count);
        foreach (JsonElement source in sources)
        {
            using Stream content = native.GetStream(source.GetProperty("path").GetString()!);
            Convert.ToHexString(SHA256.HashData(content)).Should().Be(source.GetProperty("sha256").GetString());
        }
    }

    [Theory]
    [InlineData("Go", "go")]
    [InlineData("PowerShell", "powershell")]
    public async Task OwnedProviders_ActualSourceContext_RejectWrongTargetEarlyCompletionAndUnboundPreview(string stack, string alias)
    {
        using var services = await InstallAsync(stack);
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "qualified-context"));
        using var templater = ProjectTemplater(services, target, alias);
        ResolvedTemplate selected = (await templater.ResolveGroupAsync($"Azure.Functions.{stack}.Basic",
            TemplateType.Project, CancellationToken.None)).Group!.Single();
        var arguments = new TemplateArgumentParser().Parse(new(selected.Entry.Identity, selected.Parameters, new HashSet<string>(), []));
        var native = await selected.InvokeAsync(new(target, arguments.ValuesByCanonicalName, DryRun: true), CancellationToken.None);
        NativeProjectPreparationContext context = new(selected, target, arguments.ValuesByCanonicalName, native);
        INativeProjectPreparation provider = stack == "Go" ? new GoNativeProjectPreparation(_files, new HostGoTool())
            : new PowerShellNativeProjectPreparation(_files, new HostAzLookup("12"));

        var preview = await provider.PreviewAsync(context, CancellationToken.None);
        var early = await provider.CompleteAsync(context, preview, CancellationToken.None);
        var wrongTarget = await provider.PreviewAsync(context with
        {
            TargetDirectory = WorkingDirectory.FromExplicit(Path.Combine(_root, "different-root")),
        }, CancellationToken.None);
        var unbound = await provider.CompleteAsync(context, preview with { }, CancellationToken.None);
        Dictionary<string, string?> changed = new(context.Parameters) { ["NoBundles"] = "true" };
        var changedParameters = await provider.CompleteAsync(context with { Parameters = changed }, preview, CancellationToken.None);

        preview.Succeeded.Should().BeTrue();
        early.Succeeded.Should().BeFalse();
        wrongTarget.Succeeded.Should().BeFalse();
        unbound.Succeeded.Should().BeFalse();
        changedParameters.Succeeded.Should().BeFalse();
        Directory.Exists(target.Info.FullName).Should().BeFalse();
        INativeProjectWorkflowProvider workflow = stack == "Go" ? new GoProjectWorkflowProvider((GoNativeProjectPreparation)provider)
            : new PowerShellProjectWorkflowProvider((PowerShellNativeProjectPreparation)provider);
        workflow.Handles(selected.Entry).Should().BeTrue();
        workflow.Handles(selected.Entry with { Identity = "another-native-project" }).Should().BeFalse();
        workflow.Handles(selected.Entry with { Package = new("Other.Package", "1.0.0") }).Should().BeFalse();
        workflow.Handles(selected.Entry with { Type = TemplateType.Item }).Should().BeFalse();
    }

    private sealed class HostGoTool : HostGoPreparation
    {
        public int Calls { get; private set; }
        public (int ExitCode, string Stderr) Outcome { get; init; }
        public Action? DuringExecution { get; init; }
        public bool ThrowCancellation { get; init; }
        public Exception? Failure { get; init; }

        public override Task<(int ExitCode, string Stderr)> RunGoModTidyAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            File.Exists(Path.Combine(workingDirectory, "go.mod")).Should().BeTrue();
            File.Exists(Path.Combine(workingDirectory, "main.go")).Should().BeTrue();
            File.Exists(Path.Combine(workingDirectory, ".func", "config.json")).Should().BeTrue();
            Calls++;
            if (Failure is not null) return Task.FromException<(int ExitCode, string Stderr)>(Failure);
            DuringExecution?.Invoke();
            if (ThrowCancellation) cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Outcome);
        }
    }

    private sealed class HostAzLookup(string? major) : HostPowerShellPreparation
    {
        public int Lookups { get; private set; }
        public int SourceReads { get; private set; }
        public Action? DuringLookup { get; init; }
        public Exception? Failure { get; init; }

        public override Task<string?> GetLatestAzModuleMajorVersionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Lookups++;
            if (Failure is not null) return Task.FromException<string?>(Failure);
            DuringLookup?.Invoke();
            return Task.FromResult(major);
        }

        public override string ReadRequirementsTemplate()
        {
            SourceReads++;
            return base.ReadRequirementsTemplate();
        }
    }
}
