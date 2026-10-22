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
    IEnumerable<INativeProjectWorkflowProvider> projects)
{
    private readonly InstalledProjectStackCatalog _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IReadOnlyList<INativeItemWorkflowProvider> _items =
        Array.AsReadOnly<INativeItemWorkflowProvider>([.. items ?? throw new ArgumentNullException(nameof(items))]);
    private readonly IReadOnlyList<INativeProjectWorkflowProvider> _projects =
        Array.AsReadOnly<INativeProjectWorkflowProvider>([.. projects ?? throw new ArgumentNullException(nameof(projects))]);

    public INativeItemWorkflowProvider? FindItem(string stack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        IProjectStack? installed = _stacks.FindStack(stack);
        if (installed is null)
        {
            throw new InvalidOperationException($"Native item workflow stack '{stack}' is not installed.");
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

    public Task<NativeProjectLayoutQualification?> QualifyLayoutAsync(NativeProjectTemplateSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        INativeProjectWorkflowProvider? provider = FindProject(source.Entry);
        return provider is null ? Task.FromResult<NativeProjectLayoutQualification?>(null) : provider.QualifyLayoutAsync(source, cancellationToken);
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
            ? "A native workflow provider changed the command's target, effective name, preview or force policy."
            : null;
    }
}
