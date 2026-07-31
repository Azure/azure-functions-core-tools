// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Utils;
using EdgeComponents = Microsoft.TemplateEngine.Edge.Components;
using RunnableProjectsComponents = Microsoft.TemplateEngine.Orchestrator.RunnableProjects.Components;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// The func CLI's <see cref="ITemplateEngineHost"/>: host identifier
/// <c>func</c>, version pinned to the running CLI, engine logging bridged to
/// <c>Microsoft.Extensions.Logging</c>, and a component set fixed at
/// construction (Edge defaults + the RunnableProjects generator + func
/// contributions). Because <c>HostIdentifier</c> is <c>func</c> the engine
/// resolves <c>func.host.json</c> template host files.
/// </summary>
internal sealed class FuncTemplateEngineHost : ITemplateEngineHost
{
    private readonly IReadOnlyList<(Type InterfaceType, IIdentifiedComponent Instance)> _builtInComponents;

    public FuncTemplateEngineHost(
        ILoggerFactory loggerFactory,
        IFuncTemplateEngineVersion version,
        IEnumerable<FuncEngineComponent> funcComponents)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(funcComponents);

        LoggerFactory = loggerFactory;
        Logger = loggerFactory.CreateLogger("TemplateEngine");
        Version = version.Version;
        FileSystem = new PhysicalFileSystem();

        List<(Type InterfaceType, IIdentifiedComponent Instance)> builtIns =
            [.. EdgeComponents.AllComponents, .. RunnableProjectsComponents.AllComponents];
        foreach (FuncEngineComponent component in funcComponents)
        {
            builtIns.Add((component.InterfaceType, component.Instance));
        }

        _builtInComponents = builtIns;
    }

    /// <inheritdoc />
    public IReadOnlyList<(Type InterfaceType, IIdentifiedComponent Instance)> BuiltInComponents => _builtInComponents;

    /// <inheritdoc />
    public IPhysicalFileSystem FileSystem { get; private set; }

    /// <inheritdoc />
    public string HostIdentifier => FuncTemplateEnginePaths.HostIdentifier;

    /// <summary>
    /// Empty: func templates carry <c>func.host.json</c> directly, so no
    /// <c>dotnetcli</c> fallback host file is probed.
    /// </summary>
    public IReadOnlyList<string> FallbackHostTemplateConfigNames { get; } = [];

    /// <inheritdoc />
    public ILogger Logger { get; }

    /// <inheritdoc />
    public ILoggerFactory LoggerFactory { get; }

    /// <inheritdoc />
    public string Version { get; }

    /// <inheritdoc />
    public bool TryGetHostParamDefault(string paramName, out string? value)
    {
        if (string.Equals(paramName, "HostIdentifier", StringComparison.Ordinal))
        {
            value = HostIdentifier;
            return true;
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    public void VirtualizeDirectory(string path) => FileSystem = new InMemoryFileSystem(path, FileSystem);

    /// <summary>
    /// No-op: the logger factory is owned by the DI container, not the host,
    /// so disposing the host must not tear down shared CLI logging.
    /// </summary>
    public void Dispose()
    {
    }

    [Obsolete("Replaced by the DestructiveChangesDetected creation status.")]
    bool ITemplateEngineHost.OnPotentiallyDestructiveChangesDetected(IReadOnlyList<IFileChange> changes, IReadOnlyList<IFileChange> destructiveChanges) => true;
}
