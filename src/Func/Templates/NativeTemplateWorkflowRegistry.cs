// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Selects explicitly composed CLI workflow providers using the installed stack catalog as the ownership authority.
/// </summary>
internal sealed class NativeTemplateWorkflowRegistry(
    InstalledProjectStackCatalog stacks,
    IEnumerable<INativeItemWorkflowProvider> items,
    IEnumerable<INativeProjectWorkflowProvider> projects,
    NativeTemplateWorkloadCapture? capture = null,
    IEnumerable<INativeItemValidationProvider>? validations = null)
{
    private readonly InstalledProjectStackCatalog _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IReadOnlyList<INativeItemWorkflowProvider> _items =
        Array.AsReadOnly<INativeItemWorkflowProvider>([.. items ?? throw new ArgumentNullException(nameof(items))]);
    private readonly IReadOnlyList<INativeProjectWorkflowProvider> _projects =
        Array.AsReadOnly<INativeProjectWorkflowProvider>([.. projects ?? throw new ArgumentNullException(nameof(projects))]);
    private readonly IReadOnlyList<INativeItemValidationProvider> _validations =
        Array.AsReadOnly<INativeItemValidationProvider>([.. validations ?? []]);

    public INativeItemWorkflowProvider? FindItem(string stack, TemplateCatalogEntry? entry = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        IProjectStack? installed = _stacks.FindStack(stack);
        if (installed is null)
        {
            throw new InvalidOperationException($"Native item workflow stack '{stack}' is not installed.");
        }

        if (entry is not null && UsesNativeDefinition(entry, installed.Stack))
        {
            return null;
        }

        INativeItemWorkflowProvider[] matches = [.. _items.Where(provider =>
            string.Equals(provider.Stack, installed.Stack, StringComparison.OrdinalIgnoreCase))];
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException($"Multiple native item workflow providers claim stack '{installed.Stack}'."),
        };
    }

    public INativeProjectWorkflowProvider? FindProject(TemplateCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.NativeGeneration is { } native)
        {
            UsesNativeDefinition(entry, native.Stack);
            return null;
        }

        INativeProjectWorkflowProvider[] matches = [.. _projects.Where(provider =>
            _stacks.FindStack(provider.Stack) is not null && provider.Handles(entry))];
        if (matches.Length > 1)
        {
            throw new InvalidOperationException($"Multiple native project workflow providers claim '{entry.Identity}'.");
        }

        if (matches.Length == 0)
        {
            if (entry.ProjectConfigurations is { Count: > 0 })
            {
                throw new InvalidTemplateMetadataException($"No CLI-owned project workflow qualifies '{entry.Identity}' for its declared stack.");
            }

            return null;
        }

        INativeProjectWorkflowProvider selected = matches[0];
        if (_stacks.FindStack(selected.Stack) is not { } installed
            || !string.Equals(selected.Stack, installed.Stack, StringComparison.OrdinalIgnoreCase)
            || (entry.ProjectConfigurations is { } declarations
                && declarations.Any(declaration => !string.Equals(declaration.Stack, installed.Stack, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException($"Native project provider for '{entry.Identity}' does not match an installed canonical stack.");
        }

        return selected;
    }

    internal INativeItemCompatibility? FindNativeCompatibility(string stack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        IProjectStack installed = _stacks.FindStack(stack)
            ?? throw new InvalidOperationException($"Native validation stack '{stack}' is not installed.");
        INativeItemValidationProvider[] matches = [.. _validations.Where(provider =>
            string.Equals(provider.Stack, installed.Stack, StringComparison.OrdinalIgnoreCase))];
        return matches.Length switch
        {
            0 => null,
            1 => matches[0].Compatibility,
            _ => throw new InvalidOperationException($"Multiple native input validators claim stack '{installed.Stack}'."),
        };
    }

    internal bool UsesNativeDefinition(TemplateCatalogEntry entry, string stack)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (entry.NativeGeneration is not { } native)
        {
            return false;
        }

        IProjectStack? installed = _stacks.FindStack(stack);
        NativeTemplateWorkloadRegistration? owner = installed is null ? null : capture?.GetStackOrigin(installed);
        if (installed is null || owner is null || native.Package.Origin is not { } origin
            || !string.Equals(native.Stack, installed.Stack, StringComparison.Ordinal)
            || !installed.SupportedLanguages.Contains(native.Language, StringComparer.Ordinal)
            || entry.Language != native.Language
            || !string.Equals(origin.PackageId, owner.PackageId, StringComparison.OrdinalIgnoreCase)
            || origin.Version != owner.PackageVersion
            || !owner.TemplatePackages.Contains(native.Package)
            || (entry.ProjectConfigurations is { } declarations
                && declarations.Any(declaration => declaration.Stack != installed.Stack || declaration.Language != native.Language)))
        {
            throw new InvalidTemplateMetadataException(
                "Native generation does not match the selected installed stack, canonical language and verified physical workload asset.");
        }

        return true;
    }

    public Task<DotNetProjectLayoutQualification?> QualifyLayoutAsync(NativeProjectTemplateSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        INativeProjectWorkflowProvider? provider = FindProject(source.Entry);
        return provider is null ? Task.FromResult<DotNetProjectLayoutQualification?>(null) : provider.QualifyLayoutAsync(source, cancellationToken);
    }

    internal static string? InvocationDiagnostic(TemplateInvocationRequest original, TemplateInvocationRequest prepared,
        TemplateCandidateParseResult? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(prepared);
        if (arguments is not null && arguments.ExplicitParameterNames.Any(name =>
            !arguments.ValuesByCanonicalName.TryGetValue(name, out string? value)
            || !prepared.Parameters.TryGetValue(name, out string? supplied) || value != supplied))
        {
            return "A native workflow provider changed or discarded an explicit canonical template value.";
        }

        return original.OutputDirectory != prepared.OutputDirectory || original.Name != prepared.Name
            || original.DryRun != prepared.DryRun || original.Force != prepared.Force
            || original.PreserveExistingFiles != prepared.PreserveExistingFiles
            || original.PreviewAfterDirectoryCleanup != prepared.PreviewAfterDirectoryCleanup
            ? "A native workflow provider changed the command's target, effective name, preview or force policy."
            : null;
    }
}
