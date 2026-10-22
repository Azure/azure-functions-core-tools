// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Spectre.Console;

namespace Azure.Functions.Cli.Templates;

internal sealed class TemplateExecutionInteraction(ITemplateArgumentParser arguments, IInteractionService interaction)
{
    private readonly ITemplateArgumentParser _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public async Task<T> PickAsync<T>(string title, IReadOnlyList<T> choices, Func<T, string> label, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(label);
        cancellationToken.ThrowIfCancellationRequested();
        var byLabel = choices.ToDictionary(label, StringComparer.Ordinal);
        string selected = await SelectLiteralAsync(title, byLabel.Keys, cancellationToken);
        return byLabel.TryGetValue(selected, out T? value)
            ? value
            : throw new InvalidOperationException("The interaction service returned a choice that was not offered.");
    }

    public async Task<TemplateCandidateParseResult> CompleteAsync(ResolvedTemplate selected, TemplateCandidateParseResult initial,
        IReadOnlySet<string> reservedAliases, IReadOnlyList<string> rawTokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(reservedAliases);
        ArgumentNullException.ThrowIfNull(rawTokens);
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, string?> completed = new(StringComparer.Ordinal);
        foreach (TemplateArgumentParameter parameter in initial.MissingRequiredParameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TemplateParameterDefinition definition = parameter.Definition;
            string label = $"{parameter.LongAlias} ({definition.DataType})";
            string value;
            if (definition.Choices.Count > 0)
            {
                value = await SelectLiteralAsync(label, definition.Choices.Select(choice => choice.Value), cancellationToken);
            }
            else if (definition.DataType.Equals("bool", StringComparison.OrdinalIgnoreCase)
                || definition.DataType.Equals("boolean", StringComparison.OrdinalIgnoreCase))
            {
                value = await SelectLiteralAsync(label, ["true", "false"], cancellationToken);
            }
            else
            {
                value = await _interaction.PromptForInputAsync(Markup.Escape(label), cancellationToken: cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            completed.Add(definition.CanonicalName, value);
        }

        return _arguments.Parse(new(
            selected.Entry.Identity, selected.Parameters, reservedAliases, rawTokens, completed), cancellationToken);
    }

    private async Task<string> SelectLiteralAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string selected = await _interaction.PromptForSelectionAsync(title, choices, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return selected;
    }
}
