// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Captures binding names from one resolved bundle's local metadata, separately from workload availability.
/// </summary>
internal sealed class TemplateBundleBindingCapabilities
{
    private readonly FrozenSet<string> _bindings;
    private readonly string _bundleId;
    private readonly NuGetVersion _version;
    private readonly string _path;

    public TemplateBundleBindingCapabilities(TemplateEngineBundleContext source, IReadOnlyCollection<string> bindings, string? diagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bindings);
        if (source.Path is null)
        {
            throw new ArgumentException("Binding metadata requires a resolved bundle path.", nameof(source));
        }

        if (bindings.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Binding names must be non-empty.", nameof(bindings));
        }

        if (diagnostic is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
            if (bindings.Count != 0)
            {
                throw new ArgumentException("Unreadable binding metadata cannot supply capabilities.", nameof(bindings));
            }
        }

        _bundleId = source.Id;
        _version = source.ResolvedVersion;
        _path = source.Path;
        _bindings = bindings.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        Diagnostic = diagnostic;
    }

    public string? Diagnostic { get; }

    public bool MatchesSource(TemplateEngineBundleContext bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return string.Equals(_bundleId, bundle.Id, StringComparison.OrdinalIgnoreCase)
            && _version.Equals(bundle.ResolvedVersion)
            && string.Equals(_path, bundle.Path, StringComparison.Ordinal);
    }

    public bool Contains(string binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        return Diagnostic is null && _bindings.Contains(binding);
    }
}
