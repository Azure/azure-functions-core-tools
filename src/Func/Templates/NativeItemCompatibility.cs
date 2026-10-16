// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal sealed record NativeTemplateValidationRule(string CanonicalName, TemplateUserPrompt Prompt);

internal sealed record NativeTemplateLegacyContract(TemplateEffectSource Source, TemplatePackageInfo? Package, string? DefaultName,
    TemplateUserPrompt? FunctionNamePrompt, Regex? DefaultFunctionNameValidator, IReadOnlyList<NativeTemplateValidationRule> Rules);

internal sealed record NativeItemCompatibilityResult(string? Name, string Origin, IReadOnlyList<string> Diagnostics)
{
    public bool Succeeded => Name is not null && Diagnostics.Count == 0;
}

internal interface INativeItemCompatibility
{
    public Task<NativeItemCompatibilityResult> ResolveAsync(ResolvedTemplate selected,
        TemplateCandidateParseResult arguments, string? requestedName, bool canPrompt, CancellationToken cancellationToken);
}

/// <summary>
/// Applies explicitly supplied original-catalog validation and item-name defaults without changing native engine name policy.
/// </summary>
internal sealed class NativeItemCompatibility(IInteractionService interaction,
    Func<ResolvedTemplate, NativeTemplateLegacyContract?> contracts,
    Func<ResolvedTemplate, string, CancellationToken, Task<ReadOnlyMemory<byte>?>> readOwnedSource) : INativeItemCompatibility
{
    internal static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(1);

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly Func<ResolvedTemplate, NativeTemplateLegacyContract?> _contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
    private readonly Func<ResolvedTemplate, string, CancellationToken, Task<ReadOnlyMemory<byte>?>> _readOwnedSource =
        readOwnedSource ?? throw new ArgumentNullException(nameof(readOwnedSource));

    public async Task<NativeItemCompatibilityResult> ResolveAsync(ResolvedTemplate selected,
        TemplateCandidateParseResult arguments, string? requestedName, bool canPrompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(arguments);
        TemplateCatalogEntry entry = selected.Entry;
        if (entry.Type != TemplateType.Item || entry.Identity != arguments.TemplateIdentity)
        {
            throw new ArgumentException("Compatibility requires the selected item and its own parsed arguments.", nameof(arguments));
        }

        cancellationToken.ThrowIfCancellationRequested();
        TemplateInvocationResult? configurationFailure = await selected.ValidateConfigurationAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (configurationFailure is not null)
        {
            return new(null, "unresolved", [configurationFailure.Diagnostic ?? "The selected template configuration changed. Rerun selection."]);
        }

        NativeTemplateLegacyContract? contract = _contracts(selected);
        cancellationToken.ThrowIfCancellationRequested();
        if (contract is null)
        {
            return new(null, "unresolved", ["The selected item has no source-qualified legacy validation contract. Nothing was generated."]);
        }

        if (contract.Source is null || contract.Source.TemplateIdentity != entry.Identity || contract.Package != entry.Package
            || string.IsNullOrWhiteSpace(contract.Source.Identity)
            || contract.Source.Sha256 is not { Length: 64 } || !contract.Source.Sha256.All(Uri.IsHexDigit))
        {
            return new(null, "unresolved", ["Legacy compatibility metadata does not match the selected installed source identity, package/version or source hash."]);
        }

        ReadOnlyMemory<byte>? source = await _readOwnedSource(selected, contract.Source.Identity, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (source is null || !string.Equals(Convert.ToHexString(SHA256.HashData(source.Value.Span)),
            contract.Source.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return new(null, "unresolved", ["The owned legacy source is missing or changed. Reload the selected source and retry; nothing was generated."]);
        }

        ArgumentNullException.ThrowIfNull(contract.Rules);
        string? name = requestedName ?? contract.DefaultName ?? entry.DefaultName;
        string origin = requestedName is not null ? "explicit" : "authoredDefault";
        if (name is null)
        {
            if (!canPrompt)
            {
                return new(null, "missing", ["Supply --name; this item has no authored name default and this terminal cannot prompt."]);
            }

            name = await _interaction.PromptForInputAsync("Function name", cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            origin = "prompted";
        }

        List<string> diagnostics = [];
        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add("Supply a nonempty function name.");
        }

        Validate(contract.FunctionNamePrompt, contract.DefaultFunctionNameValidator, name, "name");
        foreach (NativeTemplateValidationRule rule in contract.Rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!arguments.Parameters.Any(parameter => parameter.Definition.CanonicalName == rule.CanonicalName))
            {
                diagnostics.Add($"Original prompt '{rule.Prompt.Id}' has no declared native parameter '{rule.CanonicalName}'.");
            }
            else if (arguments.ValuesByCanonicalName.TryGetValue(rule.CanonicalName, out string? value) && value is not null)
            {
                Validate(rule.Prompt, null, value, rule.CanonicalName);
            }
        }

        return new(name, origin, diagnostics.AsReadOnly());

        void Validate(TemplateUserPrompt? prompt, Regex? fallback, string value, string parameter)
        {
            if (prompt?.DataType.ToLowerInvariant() is "bool" or "int")
            {
                return;
            }

            Regex? pattern = fallback is null ? null : new(fallback.ToString(), fallback.Options, ValidationTimeout);
            if (!string.IsNullOrWhiteSpace(prompt?.ValidatorRegex))
            {
                try
                {
                    pattern = new(prompt.ValidatorRegex, RegexOptions.CultureInvariant, ValidationTimeout);
                }
                catch (ArgumentException exception)
                {
                    diagnostics.Add($"Original validator for '{prompt.Id}' in '{contract.Source.Identity}' is invalid: {exception.Message}");
                    return;
                }
            }

            try
            {
                if (pattern is not null && !pattern.IsMatch(value))
                {
                    diagnostics.Add($"Value '{value}' for '{parameter}' does not match the original pattern ({pattern}) from '{contract.Source.Identity}'.");
                }
            }
            catch (RegexMatchTimeoutException)
            {
                diagnostics.Add($"Original validator for '{parameter}' in '{contract.Source.Identity}' timed out for value '{value}'. Nothing was generated.");
            }
        }
    }
}
