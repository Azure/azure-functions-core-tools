// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Binds native identity and captured configuration to the owning session's invocation coordinator.
/// </summary>
internal sealed class SelectedTemplateInvoker(
    ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationCoordinator coordinator) : ISelectedTemplateInvoker
{
    private readonly ITemplateInfo _template = template ?? throw new ArgumentNullException(nameof(template));
    private readonly TemplateConfigurationSnapshot _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly TemplateInvocationCoordinator _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    public Task<TemplateInvocationResult> InvokeAsync(TemplateInvocationRequest request, CancellationToken cancellationToken)
    {
        _coordinator.ThrowIfDisposed();
        return _coordinator.InvokeAsync(_template, _configuration, request, cancellationToken);
    }
}
