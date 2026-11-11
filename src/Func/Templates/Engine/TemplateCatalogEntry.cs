// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Func-owned description of an installed template and its usable metadata.
/// </summary>
internal sealed record TemplateCatalogEntry
{
    /// <summary>
    /// Creates a catalog entry with snapshots of its aliases, parameters, and parameter choices.
    /// </summary>
    /// <param name="identity">Unique template identity.</param>
    /// <param name="name">Display name.</param>
    /// <param name="description">Description, or <c>null</c> when the template gives none.</param>
    /// <param name="type">Template type the template declares in <c>tags.type</c>.</param>
    /// <param name="shortNames">Non-empty short names that select the template.</param>
    /// <param name="groupIdentity">Identity the template shares with its variants, or <c>null</c> when it's ungrouped.</param>
    /// <param name="language">Language the template declares in <c>tags.language</c>, or <c>null</c> when it declares none.</param>
    /// <param name="precedence">Precedence of the template among its variants.</param>
    /// <param name="package">Package that provides the template, or <c>null</c> when its details aren't available.</param>
    /// <param name="isHidden">
    /// Whether <c>func.host.json</c> hides the template from ordinary listings; defaults to <c>false</c> when that file
    /// cannot be read.
    /// </param>
    /// <param name="parameters">Parameters users can set, or empty when Functions host metadata cannot be read.</param>
    /// <param name="metadataDiagnostic">Why required Functions or template metadata can't be used, or <c>null</c> when it's valid.</param>
    /// <param name="metadataDiagnosticCause">Original metadata failure, when available.</param>
    public TemplateCatalogEntry(
        string identity,
        string name,
        string? description,
        TemplateType type,
        IReadOnlyList<string> shortNames,
        string? groupIdentity,
        string? language,
        int precedence,
        TemplatePackageInfo? package,
        bool isHidden,
        IReadOnlyList<TemplateParameterDefinition> parameters,
        string? metadataDiagnostic,
        Exception? metadataDiagnosticCause = null)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        Type = type;
        ShortNames = CaptureShortNames(shortNames);
        GroupIdentity = groupIdentity;
        Language = language;
        Precedence = precedence;
        Package = package;
        IsHidden = isHidden;
        Parameters = CaptureParameters(parameters);
        MetadataDiagnostic = metadataDiagnostic;
        MetadataDiagnosticCause = metadataDiagnosticCause;
    }

    public string Identity { get; }

    public string Name { get; }

    public string? Description { get; }

    public TemplateType Type { get; }

    public IReadOnlyList<string> ShortNames { get; }

    public string? GroupIdentity { get; }

    public string? Language { get; }

    public int Precedence { get; }

    public TemplatePackageInfo? Package { get; }

    public bool IsHidden { get; }

    public IReadOnlyList<TemplateParameterDefinition> Parameters { get; }

    public string? MetadataDiagnostic { get; }

    public Exception? MetadataDiagnosticCause { get; }

    private static IReadOnlyList<string> CaptureShortNames(IReadOnlyList<string> shortNames)
    {
        ArgumentNullException.ThrowIfNull(shortNames);
        return shortNames.ToArray();
    }

    private static IReadOnlyList<TemplateParameterDefinition> CaptureParameters(
        IReadOnlyList<TemplateParameterDefinition> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        List<TemplateParameterDefinition> snapshot = new(parameters.Count);
        foreach (TemplateParameterDefinition? parameter in parameters)
        {
            if (parameter is null)
            {
                throw new ArgumentException("Template parameters cannot contain null entries.", nameof(parameters));
            }

            ArgumentNullException.ThrowIfNull(parameter.Choices);
            snapshot.Add(parameter with { Choices = parameter.Choices.ToArray() });
        }

        return snapshot.ToArray();
    }
}

/// <summary>
/// Installed template package that provides a template.
/// </summary>
/// <param name="Identifier">Package identifier, such as a NuGet package ID or a folder path.</param>
/// <param name="Version">Package version, or <c>null</c> when the package has none, as with a folder.</param>
internal sealed record TemplatePackageInfo(string Identifier, string? Version);
