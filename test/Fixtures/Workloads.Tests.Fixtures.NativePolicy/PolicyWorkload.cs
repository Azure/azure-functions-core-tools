// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Native;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

[assembly: CliWorkload<Azure.Functions.Cli.Workloads.Tests.Fixtures.NativePolicy.PolicyWorkload>()]

namespace Azure.Functions.Cli.Workloads.Tests.Fixtures.NativePolicy;

public sealed class PolicyWorkload : Workload
{
    public override string DisplayName => "Native policy fixture";
    public override string Description => "Separately compiled owner policy and source lifetime controls.";

    public override void Configure(FunctionsCliBuilder builder)
    {
        builder.Services.AddSingleton<IProjectStack, PolicyStack>();
        builder.Services.AddSingleton<IFixtureFiles, FixtureFiles>();
        builder.AddNativeTemplatePolicy<FixturePolicy>();
    }
}

internal sealed class PolicyStack : IProjectStack
{
    public string Stack => "fixture";
    public IReadOnlyList<string> SupportedLanguages => ["Fixture"];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } =
        new Dictionary<string, IReadOnlyList<string>> { ["Fixture"] = ["fx"] };
}

internal sealed class FixturePolicy(IFixtureFiles files) : NativeTemplatePolicy
{
    private readonly IFixtureFiles _files = files ?? throw new ArgumentNullException(nameof(files));

    public string Mode { get; set; } = "valid";
    public NativeTemplateSource? LastSource { get; private set; }
    public NativeTemplateInvocationContext? LastInvocation { get; private set; }
    public NativeTemplateCapturedOutput? LastCapture { get; private set; }
    public Dictionary<string, string?> ReturnedDefaults { get; } = new(StringComparer.Ordinal);
    public NativeTemplateCapturedOutput? LastPreparationPreview { get; private set; }
    public NativeTemplateCapturedOutput? LastPreparationCompletion { get; private set; }
    public byte[]? PreparationBytes { get; private set; }
    public List<string> ReturnedPreparationDiagnostics { get; } = [];
    public NativeTemplateCreatedProject? LastCreatedProject { get; private set; }
    public byte[]? ConfigurationBytes { get; private set; }
    public int PreparationExecutions { get; private set; }
    public int PreparationCompletions { get; private set; }
    public NativeTemplateSource? FactorySource { get; private set; }
    public INativeTemplateProjectPreparation? CapabilityOverride { get; set; }
    public Func<NativeTemplateCreatedProject, NativeTemplateProjectPreparationPlan, CancellationToken,
        Task<NativeTemplateProjectPreparationResult>>? CompleteHook { get; set; }
    public override string Stack => "fixture";

    public override bool Handles(NativeTemplateSource selected)
    {
        LastSource = selected;
        return Mode != "unhandled"
            && selected.PackageId is "Fixture.Policy.Templates" or "Fixture.Policy.External" && selected.PackageVersion == "2.0.0";
    }

    public override NativeTemplatePolicyStages GetRequestedStages(NativeTemplateSource selected)
        => Mode == "unknown-stage" ? (NativeTemplatePolicyStages)128 : Mode is "no-stages" or "preparation-none" ? NativeTemplatePolicyStages.None
            : selected.IsProject || selected.Identity is "Fixture.Compile" or "Fixture.Editor"
                ? NativeTemplatePolicyStages.Invocation | NativeTemplatePolicyStages.CapturedOutput | NativeTemplatePolicyStages.Effects
                : NativeTemplatePolicyStages.None;

    public override bool RequiresProjectPreparation(NativeTemplateSource selected)
        => selected.IsProject && Mode.StartsWith("preparation-", StringComparison.Ordinal)
            && !(Mode == "preparation-drift" && LastPreparationPreview is not null);

    public override INativeTemplateProjectPreparation CreateProjectPreparation(NativeTemplateSource selected)
    {
        FactorySource = selected;
        return Mode switch
        {
            "preparation-factory-null" => null!,
            "preparation-factory-unsupported" => base.CreateProjectPreparation(selected),
            _ => CapabilityOverride ?? new FixtureProjectPreparation(this),
        };
    }

    private async Task<NativeTemplateProjectPreparationPlan> PreviewPreparationAsync(
        NativeTemplateCapturedOutput captured, CancellationToken cancellationToken)
    {
        LastPreparationPreview = captured;
        await captured.RevalidateAsync(cancellationToken);
        PreparationBytes = (await captured.ReadFileAsync("Created.proj", cancellationToken)).ToArray();
        if (Mode == "preparation-unsupported")
        {
            throw new NotSupportedException("Fixture workload preparation is unsupported.");
        }
        if (Mode == "preparation-null")
        {
            return null!;
        }
        if (!captured.Invocation.DryRun || !captured.Invocation.CanonicalValues.TryGetValue("SkipNpmInstall", out string? value)
            || !bool.TryParse(value, out bool skip))
        {
            return new(false, false, ["Owner requires its canonical SkipNpmInstall value and actual preview."]);
        }
        ReturnedPreparationDiagnostics.Clear();
        if (Mode == "preparation-diagnostics")
        {
            ReturnedPreparationDiagnostics.Add("Owner preparation diagnostics.");
        }
        return new(Mode != "preparation-refused", skip, ReturnedPreparationDiagnostics);
    }

    private async Task<NativeTemplateProjectPreparationResult> CompletePreparationAsync(
        NativeTemplateCreatedProject created, NativeTemplateProjectPreparationPlan preview, CancellationToken cancellationToken)
    {
        PreparationCompletions++;
        LastCreatedProject = created;
        NativeTemplateCapturedOutput captured = created.Output;
        LastPreparationCompletion = captured;
        await created.RevalidateAsync(cancellationToken);
        ConfigurationBytes = (await created.ReadConfigurationAsync(cancellationToken)).ToArray();
        using var configuration = JsonDocument.Parse(ConfigurationBytes);
        if (configuration.RootElement.GetProperty("stack").GetProperty("runtime").GetString() != Stack
            || configuration.RootElement.GetProperty("stack").GetProperty("language").GetString() != "Fixture"
            || created.ConfigurationPath != Path.Combine(captured.Invocation.OutputDirectory.Info.FullName, ".func", "config.json"))
        {
            return new(false, ["The owner requires the actual finalized canonical configuration."]);
        }
        if (!(await captured.ReadFileAsync("Created.proj", cancellationToken)).Span.SequenceEqual(PreparationBytes))
        {
            return new(false, ["Actual created bytes differ from the preparation preview."]);
        }
        ReturnedPreparationDiagnostics.Add("Mutation after returning the copied plan.");
        if (Mode == "preparation-completion-null")
        {
            return null!;
        }
        if (Mode == "preparation-completion-refused")
        {
            return new(false, ["Owner refused preparation completion."]);
        }
        if (Mode == "preparation-completion-unsupported")
        {
            throw new NotSupportedException("Fixture completion is unsupported.");
        }
        if (Mode == "preparation-tamper")
        {
            await _files.WriteAllTextAsync(Path.Combine(captured.Invocation.OutputDirectory.Info.FullName, "Created.proj"),
                "changed during completion qualification", cancellationToken);
        }
        if (captured.Invocation.DryRun)
        {
            return new(false, ["Completion must use its actual created invocation."]);
        }
        if (preview.Skipped)
        {
            return new(true, ["Owner explicitly skipped preparation."]);
        }
        PreparationExecutions++;
        return CompleteHook is { } complete ? await complete(created, preview, cancellationToken)
            : new(true, ["Original workload preparation stderr."], true);
    }

    private sealed class FixtureProjectPreparation(FixturePolicy policy) : INativeTemplateProjectPreparation
    {
        public Task<NativeTemplateProjectPreparationPlan> PreviewAsync(NativeTemplateCapturedOutput captured, CancellationToken cancellationToken)
            => policy.PreviewPreparationAsync(captured, cancellationToken);

        public Task<NativeTemplateProjectPreparationResult> CompleteAsync(
            NativeTemplateCreatedProject created, NativeTemplateProjectPreparationPlan preview, CancellationToken cancellationToken)
            => policy.CompletePreparationAsync(created, preview, cancellationToken);
    }

    public override async Task<IReadOnlyList<string>> ValidateProjectAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSource = selected;
        if (Mode == "refused")
        {
            return ["Owner refused this project."];
        }
        ReadOnlyMemory<byte> raw = await selected.ReadPackageFileAsync("content/project/.template.config/template.json", cancellationToken);
        using StreamReader reader = new(new MemoryStream(raw.ToArray()));
        using var configuration = JsonDocument.Parse(await reader.ReadToEndAsync(cancellationToken));
        if (configuration.RootElement.GetProperty("sourceName").GetString() != "Owner.App"
            || configuration.RootElement.GetProperty("primaryOutputs")[0].GetProperty("path").GetString() != "Owner.App.proj"
            || configuration.RootElement.GetProperty("sources")[0].GetProperty("modifiers")[0].GetProperty("rename")
                .GetProperty("_ownerignore").GetString() != ".ownerignore")
        {
            return ["The owner refused its project layout or content convention."];
        }
        return [];
    }

    public override Task<NativeTemplateProjectAnchor> DescribeProjectLayoutAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Mode == "missing-declaration" ? null!
            : new NativeTemplateProjectAnchor(Mode == "wrong-index" ? 1 : 0, Mode == "foreign-stack" ? "foreign" : Stack, "Fixture"));
    }

    public override async Task<NativeTemplateInvocationQualification> QualifyInvocationAsync(
        NativeTemplateInvocationContext invocation, CancellationToken cancellationToken)
    {
        LastInvocation = invocation;
        LastSource = invocation.Source;
        await invocation.RevalidateAsync(cancellationToken);
        if (Mode == "missing-invocation")
        {
            return null!;
        }
        if (Mode == "invocation-diagnostics")
        {
            return new(true, ["Owner invocation diagnostics must refuse even when succeeded is true."], new Dictionary<string, string?>(), []);
        }
        if (invocation.ExplicitParameterNames.Contains("Framework") && invocation.CanonicalValues["Framework"] != "net10.0")
        {
            return new(false, ["The owner requires Framework=net10.0."], new Dictionary<string, string?>(), []);
        }
        ReturnedDefaults.Clear();
        if (Mode != "no-defaults" && !invocation.ExplicitParameterNames.Contains("Framework") && invocation.CanonicalValues.ContainsKey("Framework"))
        {
            ReturnedDefaults["Framework"] = "net10.0";
        }
        if (Mode == "explicit-overwrite")
        {
            ReturnedDefaults["Framework"] = "net9.0";
        }
        if (Mode == "replace-greeting")
        {
            ReturnedDefaults["Greeting"] = "replaced";
        }
        NativeTemplateProjectBinding? binding = invocation.Source.Identity == "Fixture.Compile"
            ? new(Path.Combine(invocation.ProjectDirectory.Info.FullName, "Fixture.fsproj"), "net10.0", "Fixture") : null;
        return new(true, [], ReturnedDefaults, [], binding);
    }

    public override async Task<NativeTemplateCaptureQualification> QualifyCapturedOutputAsync(
        NativeTemplateCapturedOutput captured, CancellationToken cancellationToken)
    {
        LastCapture = captured;
        LastInvocation = captured.Invocation;
        LastSource = captured.Invocation.Source;
        await captured.RevalidateAsync(cancellationToken);
        if (Mode == "missing-capture")
        {
            return null!;
        }
        if (Mode == "capture-diagnostics")
        {
            return new(true, ["Owner capture diagnostics must refuse even when succeeded is true."]);
        }
        if (Mode == "capture-refused")
        {
            return new(false, ["Owner refused the actual capture."]);
        }
        NativeTemplateCompileRepair? repair = null;
        if (captured.Invocation.Source.Identity == "Fixture.Compile" && Mode != "missing-repair")
        {
            string project = Path.Combine(captured.Invocation.ProjectDirectory.Info.FullName, "Fixture.fsproj");
            NativeTemplateCapturedFile[] generated = [.. captured.Files.Where(file => Path.GetExtension(file.TargetRelativePath) == ".fs")
                .Select(file => Mode switch
                {
                    "bad-generated-hash" => file with { Sha256 = new('A', 64) },
                    "bad-original-hash" => file with { Source = file.Source with { Sha256 = new('A', 64) } },
                    "foreign-source" => file with { Source = file.Source with { RelativePath = "content/layout.json" } },
                    _ => file,
                })];
            repair = new(project, await _files.ComputeSha256Async(project, cancellationToken),
                [Path.Combine(captured.Invocation.ProjectDirectory.Info.FullName, "Bootstrap.fs")],
                generated);
        }
        return new(true, [], repair);
    }

    public override Task<IReadOnlyList<string>> QualifyPackageReferenceAsync(NativeTemplateCapturedOutput captured,
        NativeTemplateSourceStamp source, string projectPath, string packageId, string version, string projectFileExtension,
        bool continueOnError, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public override Task<IReadOnlyList<string>> QualifyRestoreAsync(NativeTemplateCapturedOutput captured,
        NativeTemplateSourceStamp source, string projectPath, bool continueOnError, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public override Task<IReadOnlyList<string>> QualifyCompileRepairAsync(NativeTemplateCapturedOutput captured,
        NativeTemplateSourceStamp source, NativeTemplateCompileRepair repair, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(Path.GetFileName(repair.EvaluatedCompileOrder[^1]) == "Bootstrap.fs"
            ? [] : ["The fixture owner requires its terminal Bootstrap.fs anchor."]);
    }

    public override Task<IReadOnlyList<string>> QualifyCreateFileAsync(NativeTemplateCapturedOutput captured,
        NativeTemplateSourceStamp source, string targetPath, string generatedContent, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(Path.GetFileName(targetPath) == "owner.settings" && generatedContent == "source-qualified content"
            ? [] : ["The fixture owner refused this create-only target."]);
    }

    public override async Task<NativeItemPolicyDescription> DescribeItemAsync(NativeTemplateSource selected, CancellationToken cancellationToken)
    {
        LastSource = selected;
        NativeTemplateSourceStamp validation = await PinAsync(selected, "content/layout.json", cancellationToken);
        if (selected.Identity == "Fixture.Helper")
        {
            return new(true, validation, null, null, [], null, []);
        }
        NativeTemplateAppendMapping? mapping = selected.Identity == "Fixture.Item" ? new(validation,
            await PinAsync(selected, "content/item/function_app.txt", cancellationToken), "function_app.txt", "function_app.txt",
            "Fixture.Helper", Hash(await selected.ReadPackageFileAsync("content/helper/.template.config/template.json", cancellationToken)),
            new("Fixture.Helper", "content/helper/snippet.txt", Hash(await selected.ReadPackageFileAsync("content/helper/snippet.txt", cancellationToken))),
            "snippet.txt") : null;
        return new(false, validation, "Handler", null, [], mapping, []);
    }

    public override INativeTemplateAppendCapability CreateAppendCapability(NativeTemplateSource selected, WorkingDirectory invocationOutputDirectory)
        => new FixtureAppendCapability(selected, _files);

    private static async Task<NativeTemplateSourceStamp> PinAsync(NativeTemplateSource source, string path, CancellationToken cancellationToken)
        => new(source.Identity, path, Hash(await source.ReadPackageFileAsync(path, cancellationToken)));

    private static string Hash(ReadOnlyMemory<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes.Span));
}

internal sealed class FixtureAppendCapability(NativeTemplateSource source, IFixtureFiles files) : INativeTemplateAppendCapability
{
    public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateAppendRequest request, CancellationToken cancellationToken)
    {
        await source.RevalidateAsync(cancellationToken);
        string digest = await files.ComputeSha256Async(request.TargetPath, cancellationToken);
        return digest.Equals(request.ExpectedTargetSha256, StringComparison.OrdinalIgnoreCase) ? [] : ["Append target changed."];
    }

    public async Task<NativeTemplateAppendResult> ExecuteAsync(NativeTemplateAppendRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> diagnostics = await ValidateAsync(request, cancellationToken);
        if (diagnostics.Count > 0)
        {
            return new(false, diagnostics);
        }
        string original = await files.ReadAllTextIfExistsAsync(request.TargetPath, cancellationToken)
            ?? throw new FileNotFoundException("The fixture append target disappeared.", request.TargetPath);
        await files.WriteAllTextAsync(request.TargetPath, original + request.GeneratedSnippet, cancellationToken);
        return new(true, []);
    }
}

internal interface IFixtureFiles
{
    public Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken);
    public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken);
    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);
}

internal sealed class FixtureFiles : IFixtureFiles
{
    public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
        => Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)));

    public async Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
        => File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
        => File.WriteAllTextAsync(path, content, cancellationToken);
}
