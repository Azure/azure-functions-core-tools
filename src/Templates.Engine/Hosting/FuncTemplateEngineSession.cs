// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Lazily builds the engine environment and package manager against the func
/// hive and keeps them alive until the CLI shuts down. Construction is
/// deferred to the first access so commands that never touch templates pay
/// nothing.
/// </summary>
internal sealed class FuncTemplateEngineSession : IFuncTemplateEngineSession, IDisposable
{
    private readonly Lazy<EngineState> _state;
    private bool _disposed;

    public FuncTemplateEngineSession(ITemplateEngineHost host, IPathInfo paths)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(paths);

        _state = new Lazy<EngineState>(() => Create(host, paths));
    }

    /// <inheritdoc />
    public IEngineEnvironmentSettings Settings => State.Settings;

    /// <inheritdoc />
    public TemplatePackageManager PackageManager => State.PackageManager;

    private EngineState State
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _state.Value;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_state.IsValueCreated)
        {
            _state.Value.PackageManager.Dispose();
            _state.Value.Settings.Dispose();
        }
    }

    private static EngineState Create(ITemplateEngineHost host, IPathInfo paths)
    {
        EngineEnvironmentSettings settings = new(host, pathInfo: paths);
        TemplatePackageManager packageManager = new(settings);
        return new EngineState(settings, packageManager);
    }

    private sealed record EngineState(EngineEnvironmentSettings Settings, TemplatePackageManager PackageManager);
}
