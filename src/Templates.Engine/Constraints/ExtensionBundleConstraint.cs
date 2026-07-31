// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions.Constraints;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Evaluates a template-declared extension-bundle requirement
/// (<c>{ id, version }</c>) against the project's resolved bundle. When the
/// requirement is unmet the result is restricted with a call-to-action naming
/// the host.json change the user must make.
/// </summary>
internal sealed class ExtensionBundleConstraint(string type, IFuncExtensionBundleContextAccessor accessor) : ITemplateConstraint
{
    private readonly string _type = type ?? throw new ArgumentNullException(nameof(type));
    private readonly IFuncExtensionBundleContextAccessor _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));

    /// <inheritdoc />
    public string Type => _type;

    /// <inheritdoc />
    public string DisplayName => "Azure Functions extension bundle";

    /// <inheritdoc />
    public TemplateConstraintResult Evaluate(string? args)
    {
        (string? id, string? range) = ParseArgs(args);
        string bundleId = string.IsNullOrWhiteSpace(id) ? "Microsoft.Azure.Functions.ExtensionBundle" : id!;

        if (string.IsNullOrWhiteSpace(range))
        {
            return TemplateConstraintResult.CreateRestricted(
                this,
                "The func-extension-bundle constraint is missing its 'version' range.",
                "Fix the template's constraint declaration.");
        }

        FuncExtensionBundleContext? current = _accessor.Current;
        if (current is null)
        {
            return TemplateConstraintResult.CreateRestricted(
                this,
                $"This template requires extension bundle '{bundleId}' {range}, but this project has no extension bundle configured.",
                $"Add an \"extensionBundle\" with id '{bundleId}' and a version within {range} to your host.json.");
        }

        if (!string.Equals(current.BundleId, bundleId, StringComparison.OrdinalIgnoreCase))
        {
            return TemplateConstraintResult.CreateRestricted(
                this,
                $"This template requires extension bundle '{bundleId}' {range}, but this project uses bundle '{current.BundleId}'.",
                $"Switch your host.json extensionBundle id to '{bundleId}' with a version within {range}.");
        }

        if (!BundleVersionRange.TryParse(range, out BundleVersionRange parsedRange))
        {
            return TemplateConstraintResult.CreateRestricted(
                this,
                $"The extension-bundle version range '{range}' could not be parsed.",
                "Fix the template's constraint declaration.");
        }

        if (parsedRange.Satisfies(current.BundleVersion))
        {
            return TemplateConstraintResult.CreateAllowed(this);
        }

        return TemplateConstraintResult.CreateRestricted(
            this,
            $"This template requires extension bundle '{bundleId}' {range}, but this project has version {current.BundleVersion}.",
            $"Update your host.json extensionBundle version to a value within {range}.");
    }

    private static (string? Id, string? Range) ParseArgs(string? args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(args);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            string? id = root.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;
            string? range = root.TryGetProperty("version", out JsonElement versionElement) && versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString()
                : null;
            return (id, range);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
