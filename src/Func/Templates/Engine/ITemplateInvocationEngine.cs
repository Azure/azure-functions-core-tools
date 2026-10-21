// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Exposes the live native operations used by invocation policy without transferring session ownership.
/// </summary>
internal interface ITemplateInvocationEngine
{
    public IPhysicalFileSystem FileSystem { get; }

    public void ThrowIfDisposed();

    public Task<TemplateConfigurationSnapshot> ReadConfigurationAsync(ITemplateInfo template, CancellationToken cancellationToken);

    public IReadOnlyDictionary<string, object> GetPopulatedParameters(
        ITemplateInfo template, TemplateConfigurationSnapshot configuration, TemplateInvocationRequest request);

    public Task<ITemplateCreationResult> InstantiateAsync(
        ITemplateInfo template, TemplateInvocationRequest request, InputDataSet parameters, bool dryRun, CancellationToken cancellationToken);
}
