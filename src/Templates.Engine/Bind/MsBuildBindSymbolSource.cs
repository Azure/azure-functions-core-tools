// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Components;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Answers <c>msbuild:&lt;Property&gt;</c> bindings (e.g.
/// <c>msbuild:TargetFramework</c>) by reading the property from the project
/// file in the current invocation's working directory. Without it,
/// TFM-conditional symbols in .NET templates silently fall back to their
/// defaults and scaffolds target the wrong framework.
/// </summary>
internal sealed class MsBuildBindSymbolSource(
    IFuncProjectDirectoryAccessor projectDirectoryAccessor,
    IProjectFilePropertyReader propertyReader) : IBindSymbolSource
{
    private const string Prefix = "msbuild";

    private static readonly Guid _componentId = new("2F1E4A6B-7C3D-4E5F-9A0B-1C2D3E4F5A6B");

    private readonly IFuncProjectDirectoryAccessor _projectDirectoryAccessor =
        projectDirectoryAccessor ?? throw new ArgumentNullException(nameof(projectDirectoryAccessor));

    private readonly IProjectFilePropertyReader _propertyReader =
        propertyReader ?? throw new ArgumentNullException(nameof(propertyReader));

    /// <inheritdoc />
    public Guid Id => _componentId;

    /// <inheritdoc />
    public string DisplayName => "Azure Functions CLI MSBuild property bind source";

    /// <inheritdoc />
    public string SourcePrefix => Prefix;

    /// <inheritdoc />
    public bool RequiresPrefixMatch => true;

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public Task<string?> GetBoundValueAsync(
        IEngineEnvironmentSettings settings,
        string bindName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrEmpty(bindName);

        string? projectDirectory = _projectDirectoryAccessor.Current;
        if (string.IsNullOrEmpty(projectDirectory))
        {
            return Task.FromResult<string?>(null);
        }

        string property = bindName.StartsWith(Prefix + ":", StringComparison.OrdinalIgnoreCase)
            ? bindName[(Prefix.Length + 1)..]
            : bindName;

        return Task.FromResult(_propertyReader.TryReadProperty(projectDirectory, property));
    }
}
