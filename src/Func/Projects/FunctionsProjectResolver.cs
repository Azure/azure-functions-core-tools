// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Default aggregate project resolver.
/// </summary>
internal sealed class FunctionsProjectResolver(
    IEnumerable<WorkloadProjectFactoryRegistration> factories, InstalledProjectStackCatalog catalog, IProjectStackConfiguration configuration) : IFunctionsProjectResolver
{
    private readonly IReadOnlyList<WorkloadProjectFactoryRegistration> _factories =
        (factories ?? throw new ArgumentNullException(nameof(factories))).ToList();
    private readonly InstalledProjectStackCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IProjectStackConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public async Task<ProjectResolutionResult> ResolveProjectAsync(ProjectResolutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            ProjectStackSettings settings = context.SelectedStack is null ? _configuration.Read(context.WorkingDirectory.Info) : new();
            IProjectStack? stack = _catalog.ResolveProjectStack(settings, context.SelectedStack);
            LanguageDescriptor? language = stack is not null && settings.Language is { } value
                ? InstalledProjectStackCatalog.ResolveLanguage(stack, value, ProjectStackSettings.LanguageSource) : null;

            var creationContext = new ProjectCreationContext(context.WorkingDirectory);
            IEnumerable<WorkloadProjectFactoryRegistration> eligible = stack is null ? _factories : _factories.Where(f => f.Workload.Stack == stack.Descriptor);
            ProjectResolutionResult result = await DetectAsync(eligible, creationContext, language, cancellationToken);
            if (context.SelectedStack is not null && result is ProjectResolutionResult.NotResolved { IsUnrecognized: true })
            {
                // Adoption can proceed without fingerprints, but not when the only recognized project belongs to a different stack.
                ProjectResolutionResult other = await DetectAsync(_factories.Except(eligible), creationContext, null, cancellationToken);
                return other is ProjectResolutionResult.Resolved resolved
                    ? ProjectResolutionResults.NotResolved($"Selected stack '{context.SelectedStack.Descriptor.StackIdentifier}' conflicts with detected project stack '{resolved.Project.ProjectStack.Descriptor.StackIdentifier}' ({resolved.Message}).")
                    : other;
            }

            return result;
        }
        catch (Exception ex) when (ex is ProjectStackConfigurationException or ProjectStackResolutionException)
        {
            return ProjectResolutionResults.NotResolved(ex.Message);
        }
    }

    private async Task<ProjectResolutionResult> DetectAsync(IEnumerable<WorkloadProjectFactoryRegistration> factories, ProjectCreationContext context,
        LanguageDescriptor? configuredLanguage, CancellationToken cancellationToken)
    {
        ProjectResolutionResult.Resolved? match = null;
        foreach (WorkloadProjectFactoryRegistration registration in factories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectCreationResult result = await registration.Factory.TryCreateProjectAsync(context, cancellationToken);
            if (result is ProjectCreationResult.Failed failed)
            {
                return ProjectResolutionResults.NotResolved(failed.Failure.Message, failed.Failure);
            }
            if (result is not ProjectCreationResult.Created created)
            {
                continue;
            }

            IProjectStack stack = created.Project.ProjectStack;
            if (registration.Workload.Stack != stack.Descriptor)
            {
                return ProjectResolutionResults.NotResolved($"Project factory in '{registration.Workload.PackageId}' returned stack '{stack.Descriptor.StackIdentifier}', which is not the package's declared stack.");
            }

            if (match is not null)
            {
                return ProjectResolutionResults.NotResolved($"The directory was recognized by both '{match.Project.ProjectStack.Descriptor.StackIdentifier}' and '{stack.Descriptor.StackIdentifier}'. Specify the project's stack with 'func init --stack <id>'.");
            }

            if (created.Project.Language is { } detected)
            {
                if (!stack.SupportedLanguages.Contains(detected))
                {
                    return ProjectResolutionResults.NotResolved($"The detected project language '{detected.LanguageIdentifier}' is not supported by stack '{stack.Descriptor.StackIdentifier}'.");
                }

                if (configuredLanguage is not null && detected != configuredLanguage)
                {
                    return ProjectResolutionResults.NotResolved($"{ProjectStackSettings.LanguageSource} '{configuredLanguage.LanguageIdentifier}' conflicts with detected project language '{detected.LanguageIdentifier}'.");
                }
            }

            match = new ProjectResolutionResult.Resolved(created.Project, created.Reason);
        }

        return (ProjectResolutionResult?)match ?? ProjectResolutionResults.Unrecognized(
            "No installed workload recognized this directory as an Azure Functions project. Run 'func setup' to set up your local environment for your chosen stack.");
    }
}
