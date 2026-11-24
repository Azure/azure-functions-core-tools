// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Retains workload ownership and disposal responsibility independently of engine component registration.
/// </summary>
/// <param name="workload">The workload that registered the component.</param>
/// <param name="component">The registered workload component.</param>
/// <param name="ownsComponent">Whether this registration disposes the component.</param>
internal sealed class WorkloadTemplateComponentRegistration(
    RuntimeWorkloadInfo workload, ITemplateComponent component, bool ownsComponent) : IAsyncDisposable
{
    /// <summary>
    /// Gets the workload that registered the component.
    /// </summary>
    public RuntimeWorkloadInfo Workload { get; } = workload ?? throw new ArgumentNullException(nameof(workload));

    /// <summary>
    /// Gets the registered workload component.
    /// </summary>
    public ITemplateComponent Component { get; } = component ?? throw new ArgumentNullException(nameof(component));

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (!ownsComponent)
        {
            return ValueTask.CompletedTask;
        }

        if (Component is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        if (Component is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
