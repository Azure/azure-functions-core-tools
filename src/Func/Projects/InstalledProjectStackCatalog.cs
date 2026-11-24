// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Resolves installed stack identities without conflating CLI IDs, host runtimes, or display labels.
/// </summary>
internal sealed class InstalledProjectStackCatalog
{
    private readonly Lazy<IReadOnlyList<IProjectStack>> _stacks;

    public InstalledProjectStackCatalog(IEnumerable<IProjectStack> stacks, IEnumerable<WorkloadInfo> workloads)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        ArgumentNullException.ThrowIfNull(workloads);
        List<IProjectStack> installed = [.. stacks];
        List<WorkloadInfo> packages = [.. workloads.Where(w => w.Stack is not null)];
        // A conflict must not prevent unrelated commands (including workload uninstall) from running.
        _stacks = new(() => Validate(installed, packages));
    }

    internal InstalledProjectStackCatalog(IEnumerable<IProjectStack> stacks) : this(stacks, [])
    {
    }

    public IReadOnlyList<IProjectStack> Stacks => _stacks.Value;

    public IProjectStack? FindStack(string stackIdentifier)
    {
        ArgumentNullException.ThrowIfNull(stackIdentifier);
        return Stacks.SingleOrDefault(stack => string.Equals(stack.Descriptor.StackIdentifier, stackIdentifier.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public IProjectStack? ResolveProjectStack(ProjectStackSettings settings, IProjectStack? selected = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        IReadOnlyList<IProjectStack> installed = Stacks;
        if (settings.HasProjectConfig)
        {
            IProjectStack persisted = settings.StackIdentifier is { } id && FindStack(id) is { } stack
                ? stack
                : throw new ProjectStackResolutionException($"{ProjectStackSettings.StackSource} '{settings.StackIdentifier}' does not identify an installed stack.");

            if (selected is not null && selected.Descriptor != persisted.Descriptor)
            {
                throw new ProjectStackResolutionException($"--stack '{selected.Descriptor.StackIdentifier}' conflicts with {ProjectStackSettings.StackSource} '{settings.StackIdentifier}'.");
            }

            return persisted;
        }

        if (settings.WorkerRuntime is not { } runtime)
        {
            return selected;
        }

        bool Matches(IProjectStack stack) => string.Equals(stack.Descriptor.WorkerRuntime, runtime.Trim(), StringComparison.OrdinalIgnoreCase);
        if (selected is not null)
        {
            return Matches(selected) ? selected
                : throw new ProjectStackResolutionException($"--stack '{selected.Descriptor.StackIdentifier}' uses worker runtime '{selected.Descriptor.WorkerRuntime}', which conflicts with {ProjectStackSettings.WorkerRuntimeSource} '{runtime}'.");
        }

        return installed.Where(Matches).ToList() switch
        {
            [var match] => match,
            [] => throw new ProjectStackResolutionException($"{ProjectStackSettings.WorkerRuntimeSource} '{runtime}' does not match an installed stack's worker runtime. Run 'func workload search --stack' to find stack workloads."),
            var matches => throw new ProjectStackResolutionException($"{ProjectStackSettings.WorkerRuntimeSource} '{runtime}' matches multiple stacks: {string.Join(", ", matches.Select(s => s.Descriptor.StackIdentifier))}. Specify the matching CLI stack ID with 'func init --stack <id>'."),
        };
    }

    public static LanguageDescriptor ResolveLanguage(IProjectStack stack, string value, string source)
        => stack.SupportedLanguages.Where(language => language.IsMatch(value)).ToList() switch
        {
            [var match] => match,
            [] => throw new ProjectStackResolutionException($"Language '{value}' from {source} is not supported by the '{stack.Descriptor.StackIdentifier}' stack. Supported IDs: {string.Join(", ", stack.SupportedLanguages.Select(l => l.LanguageIdentifier))}."),
            var matches => throw new ProjectStackResolutionException($"Language '{value}' from {source} is ambiguous in the '{stack.Descriptor.StackIdentifier}' stack: {string.Join(", ", matches.Select(l => l.LanguageIdentifier))}."),
        };

    private static IReadOnlyList<IProjectStack> Validate(List<IProjectStack> installed, IReadOnlyList<WorkloadInfo> packages)
    {
        foreach (IGrouping<string, WorkloadInfo> group in packages.GroupBy(p => p.Stack!.StackIdentifier, StringComparer.OrdinalIgnoreCase))
        {
            string[] owners = [.. group.Select(p => p.PackageId).Distinct(StringComparer.OrdinalIgnoreCase)];
            if (owners.Length > 1)
            {
                throw new ProjectStackResolutionException($"Multiple installed workloads claim the '{group.Key}' stack: {string.Join(", ", owners)}. Run 'func workload list' and uninstall the conflicting provider.");
            }
        }

        HashSet<string> ids = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (IProjectStack stack in installed)
        {
            ArgumentNullException.ThrowIfNull(stack, "stacks");
            StackDescriptor descriptor = stack.Descriptor;
            ValidateName(descriptor.StackIdentifier);
            ValidateName(descriptor.WorkerRuntime);
            ValidateName(descriptor.DisplayName);
            if (!ids.Add(descriptor.StackIdentifier))
            {
                throw new ProjectStackResolutionException($"Multiple installed workloads claim the '{descriptor.StackIdentifier}' stack. Run 'func workload list' and uninstall the conflicting provider.");
            }

            if (stack.SupportedLanguages is not { Count: > 0 } languages)
            {
                throw new ProjectStackResolutionException($"Stack '{descriptor.StackIdentifier}' must declare at least one supported language.");
            }

            HashSet<string> languageIds = [with(StringComparer.OrdinalIgnoreCase)];
            foreach (LanguageDescriptor language in languages)
            {
                ValidateName(language.LanguageIdentifier);
                ValidateName(language.DisplayName);
                foreach (string alias in language.Aliases)
                {
                    ValidateName(alias);
                }

                if (!languageIds.Add(language.LanguageIdentifier))
                {
                    throw new ProjectStackResolutionException($"Stack '{descriptor.StackIdentifier}' declares language '{language.LanguageIdentifier}' more than once.");
                }
            }
        }

        return installed.AsReadOnly();
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, name.Trim(), StringComparison.Ordinal))
        {
            throw new ProjectStackResolutionException($"Invalid name '{name}' in installed stack metadata. Names cannot be blank or padded with whitespace.");
        }
    }
}

internal sealed class ProjectStackResolutionException(string message) : InvalidOperationException(message);
