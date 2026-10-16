// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Projects native help from the same effective aliases used by strict parsing without attaching mutable command options.
/// </summary>
internal sealed class NativeTemplateHelpProjector(ITemplateArgumentParser arguments)
{
    private readonly ITemplateArgumentParser _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));

    public TemplateHelpInfo Project(TemplateCatalogEntry entry, IReadOnlySet<string> reservedAliases, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(reservedAliases);
        cancellationToken.ThrowIfCancellationRequested();
        TemplateCandidateParseResult parsed = _arguments.Parse(new(entry.Identity, entry.Parameters, reservedAliases, []), cancellationToken);
        if (entry.MetadataDiagnostic is not null || parsed.DefinitionDiagnostics.Count > 0)
        {
            throw new InvalidTemplateMetadataException(entry.MetadataDiagnostic ?? string.Join(" ", parsed.DefinitionDiagnostics));
        }

        List<Option> options = [];
        foreach (TemplateArgumentParameter parameter in parsed.Parameters.Where(parameter => !parameter.Definition.IsHidden))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<string> description = [];
            if (!string.IsNullOrWhiteSpace(parameter.Definition.Description))
            {
                description.Add(parameter.Definition.Description);
            }

            description.Add($"Explicit value ({parameter.Definition.DataType}).");
            if (parameter.Definition.IsRequired)
            {
                description.Add("Required.");
            }

            if (parameter.Definition.DefaultValue is { } defaultValue)
            {
                description.Add($"Default: {defaultValue}.");
            }

            if (parameter.Definition.Choices.Count > 0)
            {
                description.Add($"Choices: {string.Join(", ", parameter.Definition.Choices.Select(choice => choice.Value))}.");
            }

            Option<string?> option = new(parameter.LongAlias)
            {
                Description = string.Join(" ", description),
                Arity = ArgumentArity.ExactlyOne,
            };
            if (parameter.ShortAlias is { } shortAlias)
            {
                option.Aliases.Add(shortAlias);
            }

            if (parameter.Definition.Choices.Count > 0)
            {
                option.AcceptOnlyFromAmong([.. parameter.Definition.Choices.Select(choice => choice.Value)]);
            }

            options.Add(option);
        }

        return new(entry.Identity, options.AsReadOnly());
    }
}
