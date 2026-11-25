// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Resolves installed stack identities without conflating CLI IDs, host runtimes, or display labels.
/// </summary>
internal sealed class InstalledProjectStackCatalog
{
    private readonly Lazy<IReadOnlyList<IProjectStack>> _stacks;

    public InstalledProjectStackCatalog(IEnumerable<IProjectStack> stacks, IEnumerable<WorkloadInfo> workloads, ProjectStackRegistrationOwners? owners = null)
    {
        ArgumentNullException.ThrowIfNull(stacks);
        ArgumentNullException.ThrowIfNull(workloads);
        List<IProjectStack> installed = [.. stacks];
        List<WorkloadInfo> packages = [.. workloads.Where(w => w.Stack is not null)];
        // A conflict must not prevent unrelated commands (including workload uninstall) from running.
        _stacks = new(() => Validate(installed, packages, owners));
    }

    internal InstalledProjectStackCatalog(IEnumerable<IProjectStack> stacks) : this(stacks, [])
    {
    }

    public IReadOnlyList<IProjectStack> Stacks => _stacks.Value;

    public IProjectStack? FindStack(string stackIdentifier)
    {
        ArgumentNullException.ThrowIfNull(stackIdentifier);
        return Stacks.SingleOrDefault(stack => string.Equals(stack.Descriptor.Identifier, stackIdentifier.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static LanguageDescriptor ResolveLanguage(IProjectStack stack, string value, string source)
        => stack.SupportedLanguages.Where(language => language.IsMatch(value)).ToList() switch
        {
            [var match] => match,
            [] => throw new ProjectStackResolutionException($"Language '{value}' from {source} is not supported by the '{stack.Descriptor.Identifier}' stack. Supported IDs: {string.Join(", ", stack.SupportedLanguages.Select(l => l.LanguageIdentifier))}."),
            var matches => throw new ProjectStackResolutionException($"Language '{value}' from {source} is ambiguous in the '{stack.Descriptor.Identifier}' stack: {string.Join(", ", matches.Select(l => l.LanguageIdentifier))}."),
        };

    private static IReadOnlyList<IProjectStack> Validate(List<IProjectStack> installed, IReadOnlyList<WorkloadInfo> packages, ProjectStackRegistrationOwners? registrationOwners)
    {
        foreach (IGrouping<string, WorkloadInfo> group in packages.GroupBy(p => p.Stack!.Identifier, StringComparer.OrdinalIgnoreCase))
        {
            string[] owners = [.. group.Select(p => p.PackageId).Distinct(StringComparer.OrdinalIgnoreCase)];
            if (owners.Length > 1)
            {
                throw new ProjectStackResolutionException($"Multiple installed workloads claim the '{group.Key}' stack: {string.Join(", ", owners)}. Run 'func workload list' and uninstall the conflicting provider.");
            }
        }

        if (registrationOwners is not null && registrationOwners.Packages.Count != installed.Count)
        {
            throw new ProjectStackResolutionException("Cannot match the installed project stacks to their service registrations.");
        }

        HashSet<string> ids = [with(StringComparer.OrdinalIgnoreCase)];
        for (int i = 0; i < installed.Count; i++)
        {
            IProjectStack stack = installed[i];
            ArgumentNullException.ThrowIfNull(stack, "stacks");
            StackDescriptor descriptor = stack.Descriptor;
            if (registrationOwners?.Packages[i] is { } owner)
            {
                ValidateOwner(descriptor, owner);
            }

            ValidateName(descriptor.Identifier);
            ValidateName(descriptor.WorkerRuntime);
            ValidateName(descriptor.DisplayName);
            if (!ids.Add(descriptor.Identifier))
            {
                throw new ProjectStackResolutionException($"Multiple installed workloads claim the '{descriptor.Identifier}' stack. Run 'func workload list' and uninstall the conflicting provider.");
            }

            if (stack.SupportedLanguages is not { Count: > 0 } languages)
            {
                throw new ProjectStackResolutionException($"Stack '{descriptor.Identifier}' must declare at least one supported language.");
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
                    throw new ProjectStackResolutionException($"Stack '{descriptor.Identifier}' declares language '{language.LanguageIdentifier}' more than once.");
                }
            }
        }

        return installed.AsReadOnly();
    }

    private static void ValidateOwner(StackDescriptor descriptor, RuntimeWorkloadInfo owner)
    {
        if (owner.Stack is not { } declared)
        {
            throw new ProjectStackResolutionException($"Workload '{owner.PackageId}@{owner.PackageVersion}' registered stack '{descriptor.Identifier}', but its manifest does not declare a stack.");
        }

        // Equality identifies a stack across versions; a registration must preserve this version's canonical spelling and presentation.
        string? mismatch = descriptor.Identifier != declared.Identifier ? nameof(StackDescriptor.Identifier)
            : descriptor.WorkerRuntime != declared.WorkerRuntime ? nameof(StackDescriptor.WorkerRuntime)
            : descriptor.DisplayName != declared.DisplayName ? nameof(StackDescriptor.DisplayName)
            : descriptor.Description != declared.Description ? nameof(StackDescriptor.Description) : null;
        if (mismatch is not null)
        {
            throw new ProjectStackResolutionException($"Workload '{owner.PackageId}@{owner.PackageVersion}' registered stack '{descriptor.Identifier}', but {mismatch} differs from its manifest. Use the installed descriptor for '{declared.Identifier}'.");
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !string.Equals(name, name.Trim(), StringComparison.Ordinal))
        {
            throw new ProjectStackResolutionException($"Invalid name '{name}' in installed stack metadata. Names cannot be blank or padded with whitespace.");
        }
    }
}

internal sealed record ProjectStackRegistrationOwners(IReadOnlyList<RuntimeWorkloadInfo?> Packages);

internal sealed class ProjectStackResolutionException(string message) : InvalidOperationException(message);
