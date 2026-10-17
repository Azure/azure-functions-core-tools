// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Projects;
using Azure.Functions.Cli.Templates.PowerShell.Shared;

namespace Azure.Functions.Cli.Templates.PowerShell;

/// <summary>
/// Freezes owned optional dependency content during preview and creates requirements only after native project configuration.
/// </summary>
internal sealed class PowerShellNativeProjectPreparation(
    IFileSystem fileSystem, PowerShellProjectPreparation preparation) : INativeProjectPreparation
{
    internal const string RequirementsPath = "content/project/.template.config/requirements.psd1";
    internal static readonly OwnedProjectPreparationDefinition Definition = new(
        "powershell", "PowerShell", "Azure.Functions.Templates.PowerShell.Basic", "Azure.Functions.PowerShell.Basic",
        "provenance/powershell/project.json", ["content/project/.template.config/template.json",
            "content/project/.template.config/func.host.json", "content/project/host.json", "content/project/profile.ps1",
            "content/project/local.settings.json", "content/project/.gitignore", RequirementsPath]);
    private readonly IFileSystem _files = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly PowerShellProjectPreparation _preparation = preparation ?? throw new ArgumentNullException(nameof(preparation));
    private readonly OwnedProjectPreparationSource _sources = new(fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)));
    private readonly ConditionalWeakTable<NativeProjectPreparationPreview, BoundPreview> _previews = [];

    public async Task<NativeProjectPreparationPreview> PreviewAsync(NativeProjectPreparationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            QualifiedProjectPreparationSource source = await _sources.QualifyAsync(context, Definition, cancellationToken);
            if (!TryOptions(context, out bool managed, out string? runtime))
            {
                return Invalid("PowerShell preparation requires canonical ManagedDependencies and RuntimeVersion (7.4 or 7.6) values.");
            }
            string hash = Convert.ToHexStringLower(SHA256.HashData(source.Sources[RequirementsPath].Span));
            PowerShellManagedDependencyContent prepared = await _preparation.PrepareManagedDependenciesAsync(managed, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (managed && (prepared.Content is null
                || prepared.SourceIdentity != PowerShellProjectPreparation.RequirementsSourceIdentity
                || hash != prepared.SourceSha256))
            {
                return Invalid("The selected requirements source does not match the CLI's linked owned source.");
            }
            BoundPreview bound = new(context, source.ManifestSha256, managed, runtime!, hash, prepared,
                new Dictionary<string, string?>(context.Parameters));
            NativeTemplateEffectPlan plan = Plan(context, bound, prepared.Content);
            RequirementsCapability capability = new(_files, _sources, bound, prepared.Content);
            NativeTemplateEffectResult checkedPlan = await new NativeTemplateEffectWorkflow()
                .ExecuteAsync(plan, capability, dryRun: true, confirmed: false, cancellationToken);
            if (!checkedPlan.Succeeded) return Invalid(string.Join(" ", checkedPlan.Diagnostics));
            NativeProjectPreparationPreview preview = new(true,
                [new(NativeProjectPreparationOperation.PowerShellManagedDependencies, !managed)],
                prepared.Diagnostics,
                plan);
            _previews.Add(preview, bound);
            return preview;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Invalid($"Cannot qualify PowerShell preparation: {exception.Message}");
        }
    }

    public async Task<NativeProjectPreparationResult> CompleteAsync(NativeProjectPreparationContext context,
        NativeProjectPreparationPreview preview, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!_previews.TryGetValue(preview, out BoundPreview? bound) || !preview.Succeeded
                || !ReferenceEquals(context.SelectedTemplate, bound.Context.SelectedTemplate)
                || context.TargetDirectory.Info.FullName != bound.Context.TargetDirectory.Info.FullName
                || context.Parameters.Count != bound.Parameters.Count
                || context.Parameters.Any(pair => !bound.Parameters.TryGetValue(pair.Key, out string? original) || original != pair.Value)
                || !TryOptions(context, out bool managed, out string? runtime) || managed != bound.Managed || runtime != bound.Runtime)
            {
                return new(false, ["PowerShell preparation does not match its source-qualified preview."]);
            }
            QualifiedProjectPreparationSource source = await _sources.QualifyAsync(context, Definition, cancellationToken);
            if (source.ManifestSha256 != bound.ManifestSha256)
            {
                return new(false, ["PowerShell preparation provenance changed after preview; generated files were retained."]);
            }
            string? created = await _sources.ValidateCreatedAsync(context, Definition,
                ["host.json", "profile.ps1", "local.settings.json"], cancellationToken);
            if (created is not null) return new(false, [created]);
            string? settings = await _files.ReadAllTextIfExistsAsync(
                Path.Combine(context.TargetDirectory.Info.FullName, "local.settings.json"), cancellationToken);
            if (settings is null) return new(false, ["Generated PowerShell settings disappeared before preparation."]);
            using var local = JsonDocument.Parse(settings);
            JsonElement values = OwnedProjectPreparationSource.Property(local.RootElement, "Values");
            if (OwnedProjectPreparationSource.Text(values, "FUNCTIONS_WORKER_RUNTIME_VERSION") != bound.Runtime
                || OwnedProjectPreparationSource.Text(values, "FUNCTIONS_WORKER_RUNTIME") != "powershell")
            {
                return new(false, ["Generated PowerShell runtime does not match the qualified preparation options."]);
            }
            if (!bound.Managed) return new(true, ["Skipped PowerShell managed dependencies."]);
            if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_preparation.ReadRequirementsTemplate())))
                != bound.RequirementsSha256)
            {
                return new(false, ["The owned requirements source changed after optional Az module preparation."]);
            }
            NativeTemplateEffectPlan plan = Plan(context, bound, bound.Prepared.Content);
            NativeTemplateEffectResult result = await new NativeTemplateEffectWorkflow().ExecuteAsync(
                plan, new RequirementsCapability(_files, _sources, bound, bound.Prepared.Content), dryRun: false, confirmed: true, cancellationToken);
            return new(result.Succeeded, [.. bound.Prepared.Diagnostics, .. result.Diagnostics], result);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(false, [$"PowerShell preparation could not complete; generated files were retained. {exception.Message}"]);
        }
    }

    private static bool TryOptions(NativeProjectPreparationContext context, out bool managed, out string? runtime)
    {
        managed = false;
        context.Parameters.TryGetValue("RuntimeVersion", out runtime);
        return runtime is "7.4" or "7.6" && context.Parameters.TryGetValue("ManagedDependencies", out string? value)
            && bool.TryParse(value, out managed);
    }

    private static NativeTemplateEffectPlan Plan(NativeProjectPreparationContext context, BoundPreview bound, string? content)
        => new(context.TargetDirectory, content is null ? [] :
            [new TemplateCreateFileEffect(new(Definition.Identity, PowerShellProjectPreparation.RequirementsSourceIdentity,
                bound.RequirementsSha256), Path.Combine(context.TargetDirectory.Info.FullName, "requirements.psd1"), content)]);

    private static NativeProjectPreparationPreview Invalid(string diagnostic) => new(false, [], [diagnostic]);

    private sealed record BoundPreview(NativeProjectPreparationContext Context, string ManifestSha256,
        bool Managed, string Runtime, string RequirementsSha256, PowerShellManagedDependencyContent Prepared,
        IReadOnlyDictionary<string, string?> Parameters);

    private sealed class RequirementsCapability(IFileSystem files, OwnedProjectPreparationSource sources,
        BoundPreview bound, string? expectedContent) : INativeTemplateEffectCapability
    {
        public async Task<IReadOnlyList<string>> ValidateAsync(NativeTemplateEffectPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (plan.OutputDirectory.Info.FullName != bound.Context.TargetDirectory.Info.FullName
                || plan.Effects.Count != (bound.Managed ? 1 : 0))
            {
                return ["PowerShell requires its exact project boundary and conditional single-file effect plan."];
            }
            foreach (TemplateEffect effect in plan.Effects)
            {
                IReadOnlyList<string> diagnostics = await ValidateEffectAsync(effect, cancellationToken);
                if (diagnostics.Count > 0) return diagnostics;
            }
            return [];
        }

        public async Task<TemplateEffectExecutionResult> ExecuteAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            IReadOnlyList<string> diagnostics = await ValidateEffectAsync(effect, cancellationToken);
            if (diagnostics.Count > 0) return new(false, diagnostics);
            var create = (TemplateCreateFileEffect)effect;
            try
            {
                await files.CreateAllTextAsync(create.TargetPath, create.Content, cancellationToken);
                return new(true, []);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new(false, [$"PowerShell requirements create-only write failed; user files were not replaced. {exception.Message}"]);
            }
        }

        private async Task<IReadOnlyList<string>> ValidateEffectAsync(TemplateEffect effect, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (effect is not TemplateCreateFileEffect create || create.Source.TemplateIdentity != Definition.Identity
                || create.Source.Identity != PowerShellProjectPreparation.RequirementsSourceIdentity
                || create.Source.Sha256 != bound.RequirementsSha256 || create.Content != expectedContent
                || create.TargetPath != Path.Combine(bound.Context.TargetDirectory.Info.FullName, "requirements.psd1"))
            {
                return ["PowerShell requirements effect is not bound to its selected template, owned source and prepared content."];
            }
            QualifiedProjectPreparationSource current = await sources.QualifyAsync(bound.Context, Definition, cancellationToken);
            if (current.ManifestSha256 != bound.ManifestSha256)
            {
                return ["PowerShell requirements provenance changed after preview."];
            }
            return files.FileExists(create.TargetPath) || files.DirectoryExists(create.TargetPath)
                ? ["PowerShell requirements.psd1 destination is occupied; existing user files were preserved."] : [];
        }
    }
}
