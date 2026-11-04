// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Containers;

internal sealed class ContainerArtifactOrchestrator(
    IEnumerable<IContainerArtifactProvider> providers,
    ContainerArtifactWriter writer)
{
    private readonly IReadOnlyList<IContainerArtifactProvider> _providers =
        (providers ?? throw new ArgumentNullException(nameof(providers))).ToList();
    private readonly ContainerArtifactWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <summary>
    /// Selects the single workload provider for a resolved project and writes its complete artifact set.
    /// </summary>
    /// <exception cref="ContainerArtifactGenerationException">
    /// No provider or multiple providers claim the project and capability, or the generated artifact set is invalid.
    /// </exception>
    public async Task<IReadOnlyList<ContainerArtifact>> GenerateAsync(
        FunctionsProject project,
        ContainerArtifactCapability capability,
        bool projectInitializationSkipped,
        IReadOnlyDictionary<string, string?> parameters,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(parameters);

        List<IContainerArtifactProvider> matches = [.. _providers.Where(provider => provider.Supports(project, capability))];
        if (matches.Count == 0)
        {
            throw new ContainerArtifactGenerationException(
                $"The '{project.StackDisplayName}' workload does not provide {Display(capability)} container artifacts.");
        }

        if (matches.Count > 1)
        {
            string providerNames = string.Join(", ", matches.Select(provider => provider.GetType().FullName).Order(StringComparer.Ordinal));
            throw new ContainerArtifactGenerationException(
                $"Multiple workload providers claim {Display(capability)} container artifacts for '{project.StackDisplayName}': {providerNames}.");
        }

        ContainerArtifactRequest request = new(
            project.WorkingDirectory,
            project,
            capability,
            projectInitializationSkipped,
            parameters);
        ContainerArtifactResult result;
        try
        {
            result = await matches[0].GenerateAsync(request, cancellationToken);
        }
        catch (ContainerArtifactProviderException ex)
        {
            throw new ContainerArtifactGenerationException(ex.Message, ex);
        }

        await _writer.WriteAsync(project.WorkingDirectory, result.Artifacts, force, cancellationToken);
        return result.Artifacts;
    }

    private static string Display(ContainerArtifactCapability capability) => capability switch
    {
        ContainerArtifactCapability.Docker => "Docker",
        ContainerArtifactCapability.FlexByoc => "Flex BYOC",
        _ => capability.ToString(),
    };
}
