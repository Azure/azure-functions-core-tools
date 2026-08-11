// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Templates;

internal interface INewCommandTemplateSelector
{
    public Task<FunctionTemplateInfo?> SelectAsync(
        NewInvocation invocation,
        IReadOnlyList<FunctionTemplateInfo> templates,
        CancellationToken cancellationToken);
}

internal sealed class NewCommandTemplateSelector(
    IInteractionService interaction,
    TemplatePicker picker) : INewCommandTemplateSelector
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly TemplatePicker _picker = picker ?? throw new ArgumentNullException(nameof(picker));

    public async Task<FunctionTemplateInfo?> SelectAsync(
        NewInvocation invocation,
        IReadOnlyList<FunctionTemplateInfo> templates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(templates);

        if (!string.IsNullOrWhiteSpace(invocation.RequestedTemplate))
        {
            FunctionTemplateInfo? matched = templates.FirstOrDefault(template =>
                string.Equals(template.Id, invocation.RequestedTemplate, StringComparison.OrdinalIgnoreCase));
            if (matched is null)
            {
                _interaction.WriteError(
                    $"Template '{invocation.RequestedTemplate}' was not found for this project's stack.");
                _interaction.WriteLine(line => line
                    .Muted("Run ")
                    .Code("func new --list")
                    .Muted(" to see available templates."));
            }

            return matched;
        }

        if (invocation.NonInteractive || !_interaction.IsInteractive)
        {
            _interaction.WriteError("Missing required option: --template.");
            _interaction.WriteLine(line => line
                .Muted("Pass ")
                .Code("--template <id>")
                .Muted(" or run interactively to pick one. ")
                .Code("func new --list")
                .Muted(" shows available templates."));
            return null;
        }

        return await _picker.PickAsync(templates, cancellationToken);
    }
}