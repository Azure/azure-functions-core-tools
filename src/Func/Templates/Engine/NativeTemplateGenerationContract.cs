// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Binds an explicitly native Functions definition to the already verified physical workload archive.
/// </summary>
internal sealed record NativeTemplateGenerationContract(string Stack, string Language, NativeTemplatePackagePin Package)
{
    internal static NativeTemplateGenerationContract? Read(TemplateCatalogEntry entry, TemplateConfigurationSnapshot configuration,
        IReadOnlyCollection<string> packageTypes, NativeTemplatePackagePin? verifiedPackage, TemplateEngineProjectContext? project = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(packageTypes);
        using JsonDocument document = Parse(configuration.Content);
        Require(document.RootElement.ValueKind == JsonValueKind.Object, "Native generation configuration must be an object.");
        JsonProperty[] tags = [.. document.RootElement.EnumerateObject()
            .Where(property => property.Name.Equals("tags", StringComparison.OrdinalIgnoreCase))];
        JsonProperty[] declarations = [.. tags.Where(property => property.Value.ValueKind == JsonValueKind.Object)
            .SelectMany(property => property.Value.EnumerateObject())
            .Where(property => property.Name.Equals("func-generation", StringComparison.OrdinalIgnoreCase))];
        if (declarations.Length == 0)
        {
            return null;
        }

        Require(tags is [{ Name: "tags" }] && declarations is [{ Name: "func-generation" }]
            && declarations[0].Value.ValueKind == JsonValueKind.String && declarations[0].Value.GetString() == "native",
            "Native generation must be one canonical tags.func-generation string with value 'native'.");
        Require(tags[0].Value.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            == tags[0].Value.EnumerateObject().Count(), "Native generation tags must not contain duplicate properties.");
        Require(document.RootElement.TryGetProperty("identity", out JsonElement identity) && identity.ValueKind == JsonValueKind.String
            && identity.GetString() == entry.Identity
            && tags[0].Value.TryGetProperty("language", out JsonElement language) && language.ValueKind == JsonValueKind.String
            && language.GetString() == entry.Language
            && tags[0].Value.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), entry.Type.ToString(), StringComparison.OrdinalIgnoreCase),
            "Native generation identity, type and language must match the exact mounted definition.");
        Require(verifiedPackage?.Origin is not null, "Native generation requires the selected physical workload's verified registration receipt and archive.");
        Require(entry.Package is { } package
            && package.Identifier.Equals(verifiedPackage!.Identifier, StringComparison.OrdinalIgnoreCase)
            && NuGetVersion.TryParse(package.Version, out NuGetVersion? entryVersion)
            && NuGetVersion.TryParse(verifiedPackage.Version, out NuGetVersion? verifiedVersion) && entryVersion == verifiedVersion,
            "Native generation authority must identify the selected package and exact version.");
        Require(packageTypes.Count == 1 && packageTypes.Single().Equals("FuncTemplate", StringComparison.OrdinalIgnoreCase),
            "Native Functions generation requires an exclusive FuncTemplate package.");
        Require(entry.DeclaredInternalRole is null && !string.IsNullOrWhiteSpace(entry.Language),
            "Native generation requires an ordinary template with a canonical Functions language, not an internal role.");
        IReadOnlyList<TemplateConstraintDeclaration> constraints = TemplateConstraintDeclarationReader.Read(configuration.Content, packageTypes);
        Require(constraints.Any(constraint => constraint.Type == "host" && constraint.Arguments is { ValueKind: JsonValueKind.Array } hosts
            && hosts.GetArrayLength() > 0 && hosts.EnumerateArray().All(host => host.ValueKind == JsonValueKind.Object
                && host.TryGetProperty("hostname", out JsonElement hostname) && hostname.ValueKind == JsonValueKind.String
                && hostname.GetString() == "func")), "Native Functions generation requires a func-only host constraint.");
        TemplateConstraintDeclaration[] workloads = [.. constraints.Where(constraint => constraint.Type == "func-workload")];
        Require(workloads is [{ Alternatives.Count: 1 }] && workloads[0].Alternatives[0].Identity is not null,
            "Native Functions generation requires one explicit installed-stack workload constraint.");
        Require(project?.Stack is null || (project.Stack == workloads[0].Alternatives[0].Identity && project.Language == entry.Language),
            "Native generation must match the command's selected canonical stack and language.");
        Require(!document.RootElement.EnumerateObject().Any(property =>
            property.Name.Equals("additionalConfigFiles", StringComparison.OrdinalIgnoreCase)),
            "Additional template configuration files are not qualified for the effect-free native generation contract.");
        if (entry.Type == TemplateType.Item)
        {
            Require(!string.IsNullOrWhiteSpace(configuration.DefaultName),
                "Native function items require an authored defaultName.");
            Require(!document.RootElement.TryGetProperty("postActions", out JsonElement actions)
                || actions.ValueKind == JsonValueKind.Null || (actions.ValueKind == JsonValueKind.Array && actions.GetArrayLength() == 0),
                "Effect-free native function items cannot declare post-actions; use a qualified language effect adapter.");
            Require(entry.Language != "F#", "F# native items require the qualified compile-order adapter; effect-free generation is not sufficient.");
        }

        return new(workloads[0].Alternatives[0].Identity!, entry.Language!, verifiedPackage!);
    }

    private static void Require(bool condition, string diagnostic)
    {
        if (!condition)
        {
            throw new InvalidTemplateMetadataException(diagnostic);
        }
    }

    internal static string? FunctionNameDiagnostic(string? name) => name is null || name.Length is < 1 or > 128
        || !char.IsAsciiLetter(name[0]) || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))
        || name.Equals("host", StringComparison.OrdinalIgnoreCase)
            ? "Supply a function name starting with an ASCII letter, using at most 128 ASCII letters, digits, underscores or hyphens. 'host' is reserved."
            : null;

    internal static string? ItemOutputDiagnostic(IEnumerable<TemplateFileChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return changes.Any(change => change.Path.Replace('\\', '/').Split('/')
            .Any(segment => segment.TrimEnd(' ', '.').Equals(".func", StringComparison.OrdinalIgnoreCase)))
            ? "Native function items cannot write the reserved CLI-owned .func configuration namespace."
            : null;
    }

    private static JsonDocument Parse(string configuration)
    {
        try
        {
            return JsonDocument.Parse(configuration, new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException exception)
        {
            throw new InvalidTemplateMetadataException("Native generation configuration is not valid JSON.", exception);
        }
    }
}
