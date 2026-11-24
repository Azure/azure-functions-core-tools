// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes a stack language. Only the identifier and explicit aliases are accepted as input.
/// </summary>
public sealed record LanguageDescriptor
{
    public LanguageDescriptor(string languageIdentifier, string displayName, params string[] aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(aliases);
        LanguageIdentifier = languageIdentifier;
        DisplayName = displayName;
        Aliases = Array.AsReadOnly<string>([.. aliases]);
    }

    public string LanguageIdentifier { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> Aliases { get; }

    public bool Equals(LanguageDescriptor? other) => other is not null && StringComparer.OrdinalIgnoreCase.Equals(LanguageIdentifier, other.LanguageIdentifier);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(LanguageIdentifier);

    public bool IsMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim();
        return string.Equals(LanguageIdentifier, candidate, StringComparison.OrdinalIgnoreCase)
            || Aliases.Contains(candidate, StringComparer.OrdinalIgnoreCase);
    }
}
