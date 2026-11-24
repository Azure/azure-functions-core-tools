// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.PostActions;
using Azure.Functions.Cli.Workloads;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Provides func-owned TemplateEngine components.
/// </summary>
/// <param name="components">The core CLI component registrations.</param>
/// <param name="workloadComponents">The components registered by workloads.</param>
/// <param name="interaction">The service used for workload processor diagnostics.</param>
internal sealed class FuncTemplateComponentsProvider(
    IEnumerable<ICoreTemplateComponent> components,
    IEnumerable<WorkloadTemplateComponentRegistration> workloadComponents,
    IInteractionService interaction) : IFuncTemplateComponentProvider
{
    private readonly IEnumerable<ICoreTemplateComponent> _components = components
        ?? throw new ArgumentNullException(nameof(components));
    private readonly IEnumerable<WorkloadTemplateComponentRegistration> _workloadComponents = workloadComponents
        ?? throw new ArgumentNullException(nameof(workloadComponents));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    /// <inheritdoc />
    public IReadOnlyList<ICoreTemplateComponent> Get()
    {
        ICoreTemplateComponent[] core =
        [
            ..Microsoft.TemplateEngine.Edge.Components.AllComponents
                .Select(component => new CoreTemplateComponent(component.Type, component.Instance)),
            ..Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components.AllComponents
                .Select(component => new CoreTemplateComponent(component.Type, component.Instance)),
            .._components,
        ];
        HashSet<Guid> coreComponentIds = [.. core.Select(component => component.Instance.Id)];

        List<(RuntimeWorkloadInfo Workload, IPostActionProcessor Processor)> processors = [];
        foreach (WorkloadTemplateComponentRegistration registration in _workloadComponents)
        {
            if (coreComponentIds.Contains(registration.Component.Id))
            {
                // TODO: log a verbose message indicating that this component is already part of the core components.
                continue;
            }

            if (registration.Component is IPostActionProcessor processor)
            {
                // TODO: split out workload component aggregation logic if needed.
                processors.Add((registration.Workload, processor));
            }
        }

        return
        [
            ..core,
            ..processors.GroupBy(registration => registration.Processor.Id).Select(group =>
                (ICoreTemplateComponent)new CoreTemplateComponent(
                    typeof(ICorePostActionProcessor),
                    new WorkloadPostActionProcessor(group.Key, [.. group], _interaction))),
        ];
    }

    private sealed class CoreTemplateComponent(Type interfaceType, IIdentifiedComponent instance) : ICoreTemplateComponent
    {
        /// <inheritdoc />
        public Type InterfaceType { get; } = interfaceType ?? throw new ArgumentNullException(nameof(interfaceType));

        /// <inheritdoc />
        public IIdentifiedComponent Instance { get; } = instance ?? throw new ArgumentNullException(nameof(instance));
    }
}
