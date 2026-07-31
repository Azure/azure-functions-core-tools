// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Constraints;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Engine component factory for the custom <c>func-extension-bundle</c>
/// constraint. DI-constructed so it can capture the
/// <see cref="IFuncExtensionBundleContextAccessor"/> the CLI populates per
/// invocation, then hand it to each constraint instance the engine creates.
/// </summary>
internal sealed class ExtensionBundleConstraintFactory(IFuncExtensionBundleContextAccessor accessor) : ITemplateConstraintFactory
{
    internal const string ConstraintType = "func-extension-bundle";

    private static readonly Guid _componentId = new("7B9F1C2E-4D5A-4B6C-8E9F-0A1B2C3D4E5F");

    private readonly IFuncExtensionBundleContextAccessor _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));

    /// <inheritdoc />
    public Guid Id => _componentId;

    /// <inheritdoc />
    public string Type => ConstraintType;

    /// <inheritdoc />
    public Task<ITemplateConstraint> CreateTemplateConstraintAsync(
        IEngineEnvironmentSettings environmentSettings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ITemplateConstraint>(new ExtensionBundleConstraint(ConstraintType, _accessor));
    }
}
