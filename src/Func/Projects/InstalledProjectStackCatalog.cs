// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes stack capabilities contributed by installed workloads without selecting an initializer.
/// </summary>
internal sealed class InstalledProjectStackCatalog(IEnumerable<IProjectStack> stacks)
{
    public IReadOnlyList<IProjectStack> Stacks { get; } = ValidateStacks(stacks);

    /// <summary>
    /// Finds a stack by canonical ID or worker runtime alias, or returns null for an unknown value.
    /// </summary>
    public IProjectStack? FindStack(string stack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        string candidate = stack.Trim();
        return Stacks.SingleOrDefault(s =>
            string.Equals(s.Stack, candidate, StringComparison.OrdinalIgnoreCase)
            || s.WorkerRuntimeAliases.Contains(candidate, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a language name or alias to its canonical spelling, or returns null for an unknown value.
    /// </summary>
    public string? GetCanonicalLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        string candidate = language.Trim();
        foreach (IProjectStack stack in Stacks)
        {
            foreach (string canonical in stack.SupportedLanguages)
            {
                if (string.Equals(canonical, candidate, StringComparison.OrdinalIgnoreCase)
                    || stack.SupportedLanguageAliases.Any(entry =>
                        string.Equals(entry.Key, canonical, StringComparison.OrdinalIgnoreCase)
                        && entry.Value.Contains(candidate, StringComparer.OrdinalIgnoreCase)))
                {
                    return canonical;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets every stack supporting a language name or alias, retaining registration order.
    /// </summary>
    public IReadOnlyList<IProjectStack> GetStacksForLanguage(string language)
    {
        string? canonical = GetCanonicalLanguage(language);
        return canonical is null
            ? []
            : Stacks.Where(s => s.SupportedLanguages.Contains(canonical, StringComparer.OrdinalIgnoreCase)).ToList().AsReadOnly();
    }

    private static IReadOnlyList<IProjectStack> ValidateStacks(IEnumerable<IProjectStack> stacks)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        List<IProjectStack> installed = [.. stacks];
        HashSet<string> stackIds = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> stackNames = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> languageNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (IProjectStack stack in installed)
        {
            ArgumentNullException.ThrowIfNull(stack, nameof(stacks));
            ValidateName(stack.Stack);
            ArgumentException.ThrowIfNullOrWhiteSpace(stack.DisplayName, nameof(stacks));
            ArgumentNullException.ThrowIfNull(stack.WorkerRuntimeAliases, nameof(stacks));
            ArgumentNullException.ThrowIfNull(stack.SupportedLanguages, nameof(stacks));
            ArgumentNullException.ThrowIfNull(stack.SupportedLanguageAliases, nameof(stacks));
            if (!stackIds.Add(stack.Stack))
            {
                throw new InvalidOperationException($"Multiple installed workloads claim the '{stack.Stack}' stack.");
            }

            RegisterName(stackNames, stack.Stack, stack.Stack, "stack");
            foreach (string alias in stack.WorkerRuntimeAliases)
            {
                RegisterName(stackNames, alias, stack.Stack, "stack");
            }

            if (stack.SupportedLanguages.Count == 0)
            {
                throw new ArgumentException($"Stack '{stack.Stack}' must support at least one language.", nameof(stacks));
            }

            HashSet<string> languages = new(StringComparer.OrdinalIgnoreCase);
            foreach (string language in stack.SupportedLanguages)
            {
                ValidateName(language);
                if (!languages.Add(language))
                {
                    throw new ArgumentException($"Stack '{stack.Stack}' declares language '{language}' more than once.", nameof(stacks));
                }

                RegisterName(languageNames, language, language, "language");
            }

            foreach (KeyValuePair<string, IReadOnlyList<string>> entry in stack.SupportedLanguageAliases)
            {
                ValidateName(entry.Key);
                if (!languages.Contains(entry.Key))
                {
                    throw new ArgumentException(
                        $"Stack '{stack.Stack}' declares aliases for unsupported language '{entry.Key}'.", nameof(stacks));
                }

                ArgumentNullException.ThrowIfNull(entry.Value, nameof(stacks));
                foreach (string alias in entry.Value)
                {
                    RegisterName(languageNames, alias, entry.Key, "language");
                }
            }
        }

        return installed.AsReadOnly();
    }

    private static void RegisterName(Dictionary<string, string> names, string name, string canonical, string kind)
    {
        ValidateName(name);
        if (names.TryGetValue(name, out string? owner) && !string.Equals(owner, canonical, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {kind} name or alias '{name}' belongs to both '{owner}' and '{canonical}'.");
        }

        names.TryAdd(name, canonical);
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, "stacks");
        if (!string.Equals(name, name.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException($"Stack metadata name '{name}' must not contain leading or trailing whitespace.", "stacks");
        }
    }
}
